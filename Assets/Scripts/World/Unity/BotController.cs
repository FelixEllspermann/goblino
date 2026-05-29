// BotController.cs — Economy AI for solo bots (Phase 2). One instance drives every bot owner found in
// BotEconomy. Each tick it: assigns idle farmers to harvest a needed resource, trains farmers up to a
// target, builds Huts when near its population cap, and builds a Barracks once established. EVERY action
// is checked + paid out of that bot's BotEconomy — a bot can never act without the real resources.
// (Scouting = Phase 3; military + attacking = Phase 4.)
//
// Solo-only (gated on WorldStartContext.IsSolo). Bot units are locally authoritative in solo, so the
// controller drives them via the normal Goblin command methods.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    /// <summary>Per-bot economy brain. Wire BuildingPlacer / GoblinSpawner / catalog / tilemaps in the Inspector.</summary>
    public sealed class BotController : MonoBehaviour
    {
        [SerializeField] private BuildingPlacer _placer;
        [SerializeField] private GoblinSpawner _spawner;
        [SerializeField] private BuildingCatalog _catalog;
        [SerializeField] private Tilemap _terrainMap;
        [SerializeField] private Tilemap _decorationMap;
        [SerializeField] private float _tickInterval = 1f;

        [Header("Targets")]
        [SerializeField] private int _farmerTarget = 8;
        [SerializeField] private int _searchRadius = 32;
        [SerializeField] private string _keepName = "Keep_0";
        [SerializeField] private string _hutName = "Huts_0";
        [SerializeField] private string _barracksName = "Barracks_0";
        [SerializeField] private string _dockName = "Docks_0";

        [Header("Vision / Scouting")]
        [SerializeField] private int _visionRadius = 6;
        [SerializeField] private int _keepVisionRadius = 10;

        [Header("Military / Attack plans")]
        [SerializeField] private float _engageRadius = 8f;     // a military unit attacks hostiles within this
        [SerializeField] private float _attackCooldown = 300f; // seconds of downtime before rolling the next plan
                                                               // (and before the very first plan after game start)

        private sealed class BotState
        {
            public Vector2Int Keep;
            public bool KeepKnown;
            public float TrainTimer = -1f;   // -1 = not training
            public Vector2Int BuildSite;
            public bool Building;
            public int AssignCycle;

            // Phase 3: the bot's own exploration of the map (it only "knows" what it has seen).
            public bool[,] Explored;
            public Goblin Scout;
            public bool EnemyFound;
            public readonly HashSet<Vector2Int> DiscoveredEnemyBases = new();

            // Phase 4: military + attack plans.
            public float MilTrainTimer = -1f;
            public bool MilNextArcher;          // alternate club/archer
            public GoblinUnitDefinition PendingMil;
            public bool PlanAttacking;          // currently executing the attack
            public int PlanTargetSize;          // planned army size; 0 = no plan (downtime)
            public float PlanTimer;             // counts up during downtime; a plan rolls at _attackCooldown
            public readonly float[] PlanWeights = { 80f, 15f, 5f };  // small / medium / large roll weights

            // Naval: land connected-components so the bot knows what it can reach on foot, plus boat/ferry state.
            public int[,] Comp;                 // per-cell land-component id (−1 = water/oob); recomputed periodically
            public float CompTimer;             // throttle for component recompute
            public float BoatTrainTimer = -1f;  // -1 = not training a boat
            public float BoardTimer;            // boarding window before the boat departs anyway
        }

        private readonly Dictionary<ulong, BotState> _states = new();
        private BuildingDefinition _hutDef, _barracksDef, _dockDef;
        private GoblinUnitDefinition _farmerDef, _clubDef, _archerDef, _boatDef;
        private WorldGeneratorBootstrap _worldSource;
        private float _t;

        private void Start()
        {
            _worldSource = UnityEngine.Object.FindFirstObjectByType<WorldGeneratorBootstrap>();
            _hutDef = FindBuilding(_hutName);
            _barracksDef = FindBuilding(_barracksName);
            _dockDef = FindBuilding(_dockName);
            // Docks train boats ([0] = Boat) — used for naval scouting / invasion.
            if (_dockDef != null && _dockDef.TrainsUnits != null && _dockDef.TrainsUnits.Length > 0)
                _boatDef = _dockDef.TrainsUnits[0];
            var keepDef = FindBuilding(_keepName);
            if (keepDef != null && keepDef.TrainsUnits != null && keepDef.TrainsUnits.Length > 0)
                _farmerDef = keepDef.TrainsUnits[0];
            // Military unit defs come from the Barracks training list ([0]=Club, [1]=Archer).
            if (_barracksDef != null && _barracksDef.TrainsUnits != null)
            {
                if (_barracksDef.TrainsUnits.Length > 0) _clubDef = _barracksDef.TrainsUnits[0];
                if (_barracksDef.TrainsUnits.Length > 1) _archerDef = _barracksDef.TrainsUnits[1];
            }
        }

        private BuildingDefinition FindBuilding(string n)
        {
            if (_catalog == null) return null;
            foreach (var b in _catalog.Buildings) if (b != null && b.name == n) return b;
            return null;
        }

        private void Update()
        {
            if (!WorldStartContext.IsSolo) return;        // bots are solo-only
            if (_placer == null || _spawner == null) return;
            _t += Time.deltaTime;
            if (_t < _tickInterval) return;
            float dt = _t; _t = 0f;

            foreach (var owner in BotEconomy.Owners)
            {
                if (!_states.TryGetValue(owner, out var st)) { st = new BotState(); _states[owner] = st; }
                RunBot(owner, st, dt);
            }
        }

        private void RunBot(ulong owner, BotState st, float dt)
        {
            // 1. Locate the keep (anchor for everything). If it's gone, the bot is effectively dead.
            if (!st.KeepKnown || !_placer.TryGetBuildingAt(st.Keep, out _))
            {
                if (!_placer.TryFindNearestBuildingByName(_keepName, owner, Vector2Int.zero, out st.Keep))
                    return;
                st.KeepKnown = true;
            }
            Vector3 keepWorld = new(st.Keep.x + 0.5f, st.Keep.y + 0.5f, 0f);

            // 1b. Update the bot's own vision (explored map) and discover enemy bases it can see.
            UpdateVision(owner, st);
            ScanForEnemies(owner, st);
            // 1b'. Keep the land connected-components map fresh (used for reachability / naval decisions).
            var world = _worldSource != null ? _worldSource.CurrentWorld : null;
            if (world != null) EnsureComponents(st, world, dt);
            // 1c. Scout for the enemy until one is found (a dedicated farmer explores; boats cross water).
            if (!st.EnemyFound) Scout(owner, st, dt);
            else st.Scout = null;   // release the scout back to the economy once the enemy is known

            // 2. Census of this bot's units (the scout is excluded from harvest assignment).
            int farmers = 0;
            var idleFarmers = new List<Goblin>();
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.Owner != owner) continue;
                if (g.Kind == "FarmerGoblin")
                {
                    farmers++;
                    if (g.IsIdle && g != st.Scout) idleFarmers.Add(g);
                }
            }

            // 3. Assign idle farmers to harvest (round-robin Wood/Food/Stone for a balanced economy).
            foreach (var f in idleFarmers)
            {
                ResourceKind kind = (st.AssignCycle++ % 3) switch
                {
                    0 => ResourceKind.Wood,
                    1 => ResourceKind.Food,
                    _ => ResourceKind.Stone,
                };
                if (TryFindNode(keepWorld, kind, out var cell) || TryFindNode(keepWorld, null, out cell))
                    f.SetHarvestCommand(cell);
            }

            // 4. Train farmers up to target (one at a time), paid from BotEconomy.
            if (_farmerDef != null && st.TrainTimer < 0f && farmers < _farmerTarget
                && BotEconomy.Get(owner, ResourceKind.Food) >= _farmerDef.FoodCost
                && BotEconomy.Get(owner, ResourceKind.Wood) >= _farmerDef.WoodCost
                && BotEconomy.CanAffordPop(owner, _farmerDef.PopulationCost))
            {
                BotEconomy.Add(owner, ResourceKind.Food, -_farmerDef.FoodCost);
                BotEconomy.Add(owner, ResourceKind.Wood, -_farmerDef.WoodCost);
                BotEconomy.AddUsed(owner, _farmerDef.PopulationCost);  // reserve pop now
                st.TrainTimer = Mathf.Max(0.1f, _farmerDef.SpawnDuration);
            }
            else if (st.TrainTimer >= 0f)
            {
                st.TrainTimer -= dt;
                if (st.TrainTimer <= 0f)
                {
                    st.TrainTimer = -1f;
                    _spawner.SpawnKindAt("FarmerGoblin", keepWorld, owner); // pop already reserved
                }
            }

            // 5. Building: track current construction; clear when finished.
            if (st.Building && !BuildingConstruction.IsUnderConstruction(st.BuildSite)) st.Building = false;

            if (!st.Building)
            {
                // Build a Hut when near the population cap, else a Barracks once we have a workforce.
                bool nearCap = BotEconomy.PopUsed(owner) >= BotEconomy.PopCap(owner) - 2;
                if (nearCap && CanAfford(owner, _hutDef))
                    TryBuild(owner, st, _hutDef);
                else if (!nearCap && farmers >= 5 && !OwnsBuilding(owner, _barracksName) && CanAfford(owner, _barracksDef))
                    TryBuild(owner, st, _barracksDef);
            }

            // 6. Military: train an army from the Barracks, then attack discovered enemies.
            RunMilitary(owner, st, dt);
        }

        // Attack-plan state machine:
        //   Downtime (PlanTargetSize==0): build infrastructure + scout. After _attackCooldown AND once an
        //     enemy has been scouted, roll a plan (small/medium/large) → that becomes the target army size.
        //   Building: train military up to the planned size; when reached + enemy known → launch.
        //   Attacking: send the army at the nearest known enemy until every unit is dead → back to downtime.
        private void RunMilitary(ulong owner, BotState st, float dt)
        {
            bool hasBarracks = _placer.TryFindNearestBuildingByName(_barracksName, owner, st.Keep, out var barracks)
                               && !BuildingConstruction.IsUnderConstruction(barracks);

            int military = 0;
            var army = new List<Goblin>();
            Goblin boat = null;
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.Owner != owner) continue;
                if (g.Kind == "FarmerGoblin") continue;
                if (g.IsBoat) { boat = g; continue; }   // transport, not a combatant
                military++; army.Add(g);
            }
            int aboard = boat != null ? boat.PassengerCount : 0;

            // ── Downtime: just grow + scout; only roll a plan once the enemy has been found. ──
            if (st.PlanTargetSize == 0)
            {
                st.PlanTimer += dt;
                if (st.PlanTimer >= _attackCooldown && st.EnemyFound && st.DiscoveredEnemyBases.Count > 0)
                    RollPlan(owner, st);
                return;
            }

            // ── Building toward the planned army size. ──
            if (!st.PlanAttacking)
            {
                TrainMilitary(owner, st, dt, hasBarracks, barracks, military);
                if (military >= st.PlanTargetSize && st.EnemyFound && st.DiscoveredEnemyBases.Count > 0)
                {
                    st.PlanAttacking = true;
                    Debug.Log($"[Bot {owner}] ATTACK LAUNCHED with {military} units (target {st.PlanTargetSize}).");
                }
                if (!st.PlanAttacking) return;
            }

            // ── Attacking until wiped out (no combatants left AND none still aboard the boat). ──
            if (military == 0 && aboard == 0)
            {
                Debug.Log($"[Bot {owner}] attack force destroyed — entering {_attackCooldown:0}s downtime.");
                st.PlanTargetSize = 0; st.PlanAttacking = false; st.PlanTimer = 0f;
                return;
            }

            int keepComp = CompAt(st, st.Keep);
            var ferry = new List<Goblin>();
            foreach (var g in army)
            {
                var tgt = NearestHostile(g, _engageRadius);
                if (tgt != null) { g.SetAttackCommand(tgt); continue; }   // engage hostile units in range
                int gc = CompAt(st, g.transform.position);
                if (NearestAliveBaseOnComponent(st, g.transform.position, gc, out var bo))
                    g.SetAttackBuildingCommand(bo);                       // base reachable on foot → raze it
                else if (gc == keepComp)
                    ferry.Add(g);                                         // base is across water → ship it over
                // (units stranded on a third landmass with no reachable base just wait)
            }

            // Naval invasion: ferry home-landmass units to the nearest enemy base on another landmass.
            if (ferry.Count > 0)
            {
                Vector3 home = new(st.Keep.x + 0.5f, st.Keep.y + 0.5f, 0f);
                if (NearestAliveBaseOtherComponent(st, home, keepComp, out var tb))
                    RunNaval(owner, st, dt, ferry, new Vector3(tb.x + 0.5f, tb.y + 0.5f, 0f));
            }
        }

        // Nearest alive discovered enemy base whose origin sits on land-component 'comp'.
        private static bool NearestAliveBaseOnComponent(BotState st, Vector3 from, int comp, out Vector2Int origin)
        {
            origin = default;
            if (comp < 0) return false;
            float bestSq = float.MaxValue; bool found = false;
            foreach (var b in st.DiscoveredEnemyBases)
            {
                if (CompAt(st, b) != comp) continue;
                if (!BuildingHP.TryGet(b, out int cur, out _) || cur <= 0) continue;
                float d = (new Vector3(b.x + 0.5f, b.y + 0.5f, 0f) - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; origin = b; found = true; }
            }
            return found;
        }

        // Nearest alive discovered enemy base NOT on 'comp' (a different landmass) — the invasion target.
        private static bool NearestAliveBaseOtherComponent(BotState st, Vector3 from, int comp, out Vector2Int origin)
        {
            origin = default;
            float bestSq = float.MaxValue; bool found = false;
            foreach (var b in st.DiscoveredEnemyBases)
            {
                int c = CompAt(st, b);
                if (c < 0 || c == comp) continue;
                if (!BuildingHP.TryGet(b, out int cur, out _) || cur <= 0) continue;
                float d = (new Vector3(b.x + 0.5f, b.y + 0.5f, 0f) - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; origin = b; found = true; }
            }
            return found;
        }

        // Nearest still-standing discovered enemy building to 'from'.
        private static bool NearestAliveBase(BotState st, Vector3 from, out Vector2Int origin)
        {
            origin = default; float bestSq = float.MaxValue; bool found = false;
            foreach (var b in st.DiscoveredEnemyBases)
            {
                if (!BuildingHP.TryGet(b, out int cur, out _) || cur <= 0) continue;
                float d = (new Vector3(b.x + 0.5f, b.y + 0.5f, 0f) - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; origin = b; found = true; }
            }
            return found;
        }

        // Train military one at a time (alternating Club/Archer) up to the planned size, paid from BotEconomy.
        private void TrainMilitary(ulong owner, BotState st, float dt, bool hasBarracks, Vector2Int barracks, int military)
        {
            if (hasBarracks && st.MilTrainTimer < 0f && military < st.PlanTargetSize)
            {
                var def = (st.MilNextArcher && _archerDef != null) ? _archerDef : _clubDef;
                if (def == null) def = _clubDef ?? _archerDef;
                if (def != null
                    && BotEconomy.Get(owner, ResourceKind.Food) >= def.FoodCost
                    && BotEconomy.Get(owner, ResourceKind.Wood) >= def.WoodCost
                    && BotEconomy.CanAffordPop(owner, def.PopulationCost))
                {
                    BotEconomy.Add(owner, ResourceKind.Food, -def.FoodCost);
                    BotEconomy.Add(owner, ResourceKind.Wood, -def.WoodCost);
                    BotEconomy.AddUsed(owner, def.PopulationCost);
                    st.MilTrainTimer = Mathf.Max(0.1f, def.SpawnDuration);
                    st.PendingMil = def;
                    st.MilNextArcher = !st.MilNextArcher;
                }
            }
            else if (st.MilTrainTimer >= 0f)
            {
                st.MilTrainTimer -= dt;
                if (st.MilTrainTimer <= 0f)
                {
                    st.MilTrainTimer = -1f;
                    Vector3 at = hasBarracks ? new Vector3(barracks.x + 0.5f, barracks.y + 0.5f, 0f)
                                             : new Vector3(st.Keep.x + 0.5f, st.Keep.y + 0.5f, 0f);
                    if (st.PendingMil != null) _spawner.SpawnKindAt(st.PendingMil.SpawnerKindName, at, owner);
                }
            }
        }

        // Roll an attack size via the adaptive weights (small 1-3 / medium 5-8 / large 15-25), then decay
        // the rolled size's weight and boost the others (recently-rolled sizes become less likely).
        private void RollPlan(ulong owner, BotState st)
        {
            var w = st.PlanWeights;
            float total = w[0] + w[1] + w[2];
            float r = Random.value * total;
            int i = r < w[0] ? 0 : (r < w[0] + w[1] ? 1 : 2);
            st.PlanTargetSize = i == 0 ? Random.Range(1, 4) : i == 1 ? Random.Range(5, 9) : Random.Range(15, 26);
            st.PlanAttacking = false;

            float removed = w[i] * 0.6f;
            w[i] = Mathf.Max(2f, w[i] - removed);
            for (int j = 0; j < 3; j++) if (j != i) w[j] += removed * 0.5f;

            string size = i == 0 ? "SMALL" : i == 1 ? "MEDIUM" : "LARGE";
            Debug.Log($"[Bot {owner}] rolled {size} attack plan → target {st.PlanTargetSize} units. " +
                      $"weights now S:{w[0]:0} M:{w[1]:0} L:{w[2]:0}");
        }

        // Nearest hostile, living goblin within radius of g (players, monsters, other bots).
        private static Goblin NearestHostile(Goblin g, float radius)
        {
            Goblin best = null; float bestSq = radius * radius;
            foreach (var o in Goblin.All)
            {
                if (o == null || o.CurrentHp <= 0 || !g.IsHostileTo(o)) continue;
                float d = (o.transform.position - g.transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = o; }
            }
            return best;
        }

        // World-center of the discovered enemy base nearest to 'from'.
        private static bool NearestBase(BotState st, Vector3 from, out Vector3 pos)
        {
            pos = default; float bestSq = float.MaxValue; bool found = false;
            foreach (var b in st.DiscoveredEnemyBases)
            {
                var c = new Vector3(b.x + 0.5f, b.y + 0.5f, 0f);
                float d = (c - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; pos = c; found = true; }
            }
            return found;
        }

        private bool CanAfford(ulong owner, BuildingDefinition def) =>
            def != null
            && BotEconomy.Get(owner, ResourceKind.Wood) >= def.WoodCost
            && BotEconomy.Get(owner, ResourceKind.Stone) >= def.StoneCost;

        private void TryBuild(ulong owner, BotState st, BuildingDefinition def)
        {
            if (!TryFindBuildSite(st.Keep, def.Footprint, out var site)) return;
            BotEconomy.Add(owner, ResourceKind.Wood, -def.WoodCost);
            BotEconomy.Add(owner, ResourceKind.Stone, -def.StoneCost);
            _placer.PlaceForce(def, site, charge: false, requireConstruction: true, owner: owner);
            st.BuildSite = site; st.Building = true;

            // Send the nearest bot farmer to construct it.
            Vector3 sw = new(site.x + 0.5f, site.y + 0.5f, 0f);
            Goblin best = null; float bestSq = float.MaxValue;
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.Owner != owner || g.Kind != "FarmerGoblin") continue;
                float d = (g.transform.position - sw).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = g; }
            }
            best?.SetBuildCommand(site);
        }

        private bool OwnsBuilding(ulong owner, string name)
        {
            foreach (var kv in _placer.AllOccupied)
            {
                if (kv.Value == null || kv.Value.name != name) continue;
                if (_placer.TryGetBuildingOwner(kv.Key, out var o) && o == owner) return true;
            }
            return false;
        }

        // ---------- Phase 3: vision + scouting ----------

        // Mark cells around the bot's units and buildings as explored (the bot's own fog of war).
        private void UpdateVision(ulong owner, BotState st)
        {
            var world = _worldSource != null ? _worldSource.CurrentWorld : null;
            if (world == null) return;
            if (st.Explored == null || st.Explored.GetLength(0) != world.Width || st.Explored.GetLength(1) != world.Height)
                st.Explored = new bool[world.Width, world.Height];

            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.Owner != owner) continue;
                MarkExplored(st.Explored, Mathf.FloorToInt(g.transform.position.x), Mathf.FloorToInt(g.transform.position.y), _visionRadius);
            }
            foreach (var kv in _placer.AllOccupied)
            {
                if (!_placer.TryGetBuildingOwner(kv.Key, out var o) || o != owner) continue;
                int r = (kv.Value != null && kv.Value.name.StartsWith("Keep")) ? _keepVisionRadius : _visionRadius;
                MarkExplored(st.Explored, kv.Key.x, kv.Key.y, r);
            }
        }

        private static void MarkExplored(bool[,] ex, int cx, int cy, int r)
        {
            int w = ex.GetLength(0), h = ex.GetLength(1), r2 = r * r;
            for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(h - 1, cy + r); y++)
            for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(w - 1, cx + r); x++)
            {
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= r2) ex[x, y] = true;
            }
        }

        // Any enemy building whose origin the bot has explored becomes a known target.
        private void ScanForEnemies(ulong owner, BotState st)
        {
            if (st.Explored == null) return;
            int w = st.Explored.GetLength(0), h = st.Explored.GetLength(1);
            foreach (var kv in _placer.AllOccupied)
            {
                if (!_placer.TryGetBuildingOwner(kv.Key, out var o) || o == owner) continue; // own / unknown
                if (!_placer.TryGetBuildingOrigin(kv.Key, out var origin)) origin = kv.Key;
                if (origin.x < 0 || origin.y < 0 || origin.x >= w || origin.y >= h) continue;
                if (st.Explored[origin.x, origin.y]) { st.DiscoveredEnemyBases.Add(origin); st.EnemyFound = true; }
            }
        }

        // Send a dedicated scout to WANDER into unexplored territory — the bot does NOT know where the
        // enemy is; it must stumble onto them. The scout prefers unexplored land it can actually REACH on
        // foot (its own landmass), heading there in short hops (so each A* path stays within budget). If
        // its whole landmass is explored without finding an enemy, the bot is boxed in / on an island, so
        // it falls back to a boat: ferry the scout across water to unexplored land on another landmass.
        private void Scout(ulong owner, BotState st, float dt)
        {
            var world = _worldSource != null ? _worldSource.CurrentWorld : null;
            if (world == null) return;

            // (Re)designate a scout when ours is gone — dead, captured, or aboard a boat (inactive).
            // This is how the bot "notices" its scout died: next tick it picks a fresh farmer and resumes.
            bool justAssigned = false;
            if (st.Scout == null || st.Scout.CurrentHp <= 0 || st.Scout.Owner != owner || st.Scout.IsNeutral
                || !st.Scout.gameObject.activeInHierarchy)
            {
                st.Scout = null;
                Goblin idle = null, any = null;          // prefer an idle farmer; else interrupt any farmer
                foreach (var g in Goblin.All)
                {
                    if (g == null || g.IsNeutral || g.Owner != owner || g.Kind != "FarmerGoblin") continue;
                    if (!g.gameObject.activeInHierarchy) continue;
                    any ??= g;
                    if (g.IsIdle) { idle = g; break; }
                }
                st.Scout = idle ?? any;
                if (st.Scout == null) return;
                justAssigned = true;                     // command it THIS tick (interrupt its current task)
            }
            if (!justAssigned && !st.Scout.IsIdle) return;   // mid-hop → let it travel

            Vector3 from = st.Scout.transform.position;
            int scoutComp = CompAt(st, from);

            // 1. Reachable unexplored land on the scout's own landmass → walk there in short hops.
            if (PickReachableUnexplored(world, st, scoutComp, out var target)
                || NearestUnexploredOnComponent(world, st, scoutComp, from, out target))
            {
                Vector3 delta = target - from;
                const float StepDist = 22f;
                Vector3 step = delta.magnitude <= StepDist ? target : from + delta.normalized * StepDist;
                st.Scout.SetMoveCommand(step);
                return;
            }

            // 2. Boxed in: own landmass fully explored, enemy still unknown → ferry the scout across water
            //    to the nearest unexplored land on another landmass.
            if (NearestUnexploredOtherComponent(world, st, scoutComp, from, out var navTo))
                RunNaval(owner, st, dt, new List<Goblin> { st.Scout }, navTo);
        }

        // A random unexplored, walkable land cell ON the given component (reachable on foot). No spawn knowledge.
        private bool PickReachableUnexplored(WorldData world, BotState st, int comp, out Vector3 target)
        {
            target = default;
            if (comp < 0) return false;
            for (int i = 0; i < 60; i++)
            {
                int x = Random.Range(0, world.Width), y = Random.Range(0, world.Height);
                if (st.Explored != null && st.Explored[x, y]) continue;     // already seen
                if (CompAt(st, new Vector2Int(x, y)) != comp) continue;     // not reachable on foot
                target = new Vector3(x + 0.5f, y + 0.5f, 0f);
                return true;
            }
            return false;
        }

        // Deterministic fallback: nearest unexplored land cell on 'comp' (used when random sampling misses).
        private bool NearestUnexploredOnComponent(WorldData world, BotState st, int comp, Vector3 from, out Vector3 target)
        {
            target = default;
            if (comp < 0 || st.Comp == null) return false;
            int fx = Mathf.FloorToInt(from.x), fy = Mathf.FloorToInt(from.y);
            int w = world.Width, h = world.Height, bestSq = int.MaxValue; bool found = false;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (st.Comp[x, y] != comp) continue;
                if (st.Explored != null && st.Explored[x, y]) continue;
                int dx = x - fx, dy = y - fy, sq = dx * dx + dy * dy;
                if (sq < bestSq) { bestSq = sq; target = new Vector3(x + 0.5f, y + 0.5f, 0f); found = true; }
            }
            return found;
        }

        // Nearest unexplored land cell that is NOT on 'comp' (a different landmass) — a naval-scout target.
        private bool NearestUnexploredOtherComponent(WorldData world, BotState st, int comp, Vector3 from, out Vector3 target)
        {
            target = default;
            if (st.Comp == null) return false;
            int fx = Mathf.FloorToInt(from.x), fy = Mathf.FloorToInt(from.y);
            int w = world.Width, h = world.Height, bestSq = int.MaxValue; bool found = false;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int c = st.Comp[x, y];
                if (c < 0 || c == comp) continue;                          // water or own landmass
                if (st.Explored != null && st.Explored[x, y]) continue;
                int dx = x - fx, dy = y - fy, sq = dx * dx + dy * dy;
                if (sq < bestSq) { bestSq = sq; target = new Vector3(x + 0.5f, y + 0.5f, 0f); found = true; }
            }
            return found;
        }

        // Nearest harvestable decoration tile of the given kind (or any kind if kind == null) within radius.
        private bool TryFindNode(Vector3 from, ResourceKind? kind, out Vector3Int cell)
        {
            cell = default;
            if (_decorationMap == null) return false;
            var origin = _decorationMap.WorldToCell(from);
            int bestSq = int.MaxValue;
            for (int dy = -_searchRadius; dy <= _searchRadius; dy++)
            for (int dx = -_searchRadius; dx <= _searchRadius; dx++)
            {
                var c = new Vector3Int(origin.x + dx, origin.y + dy, 0);
                var t = _decorationMap.GetTile(c);
                if (t == null || !Goblin.IsHarvestable(t.name)) continue;
                if (kind.HasValue && Goblin.KindOf(t.name) != kind.Value) continue;
                int sq = dx * dx + dy * dy;
                if (sq < bestSq) { bestSq = sq; cell = c; }
            }
            return bestSq != int.MaxValue;
        }

        // First buildable origin in expanding rings around the keep (terrain passable, empty, no resource).
        private bool TryFindBuildSite(Vector2Int keep, Vector2Int footprint, out Vector2Int site)
        {
            site = default;
            for (int ring = 2; ring <= 10; ring++)
            for (int dy = -ring; dy <= ring; dy++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;
                var o = new Vector2Int(keep.x + dx, keep.y + dy);
                if (IsBuildable(o, footprint)) { site = o; return true; }
            }
            return false;
        }

        private bool IsBuildable(Vector2Int origin, Vector2Int footprint)
        {
            for (int dy = 0; dy < footprint.y; dy++)
            for (int dx = 0; dx < footprint.x; dx++)
            {
                int x = origin.x + dx, y = origin.y + dy;
                var t = _terrainMap != null ? _terrainMap.GetTile(new Vector3Int(x, y, 0)) : null;
                if (t == null) return false;
                if (t.name == "DeepWater" || t.name == "Cliff" || t.name == "Shore") return false;
                if (_placer.TryGetBuildingAt(new Vector2Int(x, y), out _)) return false;
                var d = _decorationMap != null ? _decorationMap.GetTile(new Vector3Int(x, y, 0)) : null;
                if (d != null && Goblin.IsHarvestable(d.name)) return false;
            }
            return true;
        }

        // ---------- Naval: land connected-components + dock/boat ferry ----------

        // Drive a boat to ferry 'cargo' (units on the home landmass) to 'targetWorld' on another landmass.
        // Builds the dock and trains the boat on demand, one step per tick, paid from BotEconomy.
        private void RunNaval(ulong owner, BotState st, float dt, List<Goblin> cargo, Vector3 targetWorld)
        {
            if (_dockDef == null || _boatDef == null) return;

            // 1. Ensure a finished dock on the home-landmass coast.
            bool dockExists = _placer.TryFindNearestBuildingByName(_dockName, owner, st.Keep, out var dockOrigin);
            bool dockReady = dockExists && !BuildingConstruction.IsUnderConstruction(dockOrigin);
            if (!dockReady)
            {
                if (!dockExists && CanAfford(owner, _dockDef) && TryFindDockSite(st, out var site))
                {
                    BotEconomy.Add(owner, ResourceKind.Wood, -_dockDef.WoodCost);
                    BotEconomy.Add(owner, ResourceKind.Stone, -_dockDef.StoneCost);
                    _placer.PlaceForce(_dockDef, site, charge: false, requireConstruction: true, owner: owner);
                    AssignBuilder(owner, site);
                    Debug.Log($"[Bot {owner}] building a DOCK at {site} (needs a boat to cross water).");
                }
                return;   // wait until the dock is up
            }
            if (!DockRegistry.TryGetWaterCell(dockOrigin, out var waterCell)) return;
            Vector3 dockWater = new(waterCell.x + 0.5f, waterCell.y + 0.5f, 0f);

            // 2. Ensure a boat exists (trained at the dock).
            Goblin boat = FindBoat(owner);
            if (boat == null)
            {
                if (st.BoatTrainTimer < 0f
                    && BotEconomy.Get(owner, ResourceKind.Wood) >= _boatDef.WoodCost
                    && BotEconomy.Get(owner, ResourceKind.Food) >= _boatDef.FoodCost
                    && BotEconomy.CanAffordPop(owner, _boatDef.PopulationCost))
                {
                    BotEconomy.Add(owner, ResourceKind.Wood, -_boatDef.WoodCost);
                    BotEconomy.Add(owner, ResourceKind.Food, -_boatDef.FoodCost);
                    BotEconomy.AddUsed(owner, _boatDef.PopulationCost);
                    st.BoatTrainTimer = Mathf.Max(0.1f, _boatDef.SpawnDuration);
                }
                else if (st.BoatTrainTimer >= 0f)
                {
                    st.BoatTrainTimer -= dt;
                    if (st.BoatTrainTimer <= 0f)
                    {
                        st.BoatTrainTimer = -1f;
                        _spawner.SpawnKindAt(_boatDef.SpawnerKindName, dockWater, owner);
                        Debug.Log($"[Bot {owner}] boat ready at dock {dockOrigin}.");
                    }
                }
                return;   // wait until the boat is built
            }

            // 3. Ferry. Only act while the boat is idle (otherwise it's mid-sail / unloading).
            if (!boat.IsIdle) return;

            // Active, alive cargo still waiting on land (boarded units go inactive + drop out of this list).
            var waiting = cargo.FindAll(c => c != null && c.CurrentHp > 0 && c.gameObject.activeInHierarchy);
            int aboard = boat.PassengerCount;
            bool boatHome = (boat.transform.position - dockWater).sqrMagnitude < 16f;   // within ~4 cells

            if (aboard == 0)
            {
                if (waiting.Count == 0) return;                            // nothing to ferry right now
                if (!boatHome) { boat.SetMoveCommand(dockWater); return; } // bring the empty boat back to load
                BoardSome(waiting, boat);                                  // start loading
                st.BoardTimer = 8f;
                return;
            }

            // Some are aboard: depart when full, the boarding window elapses, or no one's left to load.
            st.BoardTimer -= dt;
            bool full = !boat.BoatHasRoom;
            bool moreToLoad = boatHome && waiting.Count > 0;
            if (full || st.BoardTimer <= 0f || !moreToLoad)
                boat.SetUnloadCommand(targetWorld);                       // sail over + drop them on the far shore
            else
                BoardSome(waiting, boat);                                 // keep loading idle stragglers
        }

        // Order idle cargo units to board the boat until it's full.
        private static void BoardSome(List<Goblin> waiting, Goblin boat)
        {
            foreach (var c in waiting)
            {
                if (!boat.BoatHasRoom) break;
                if (c.IsIdle) c.SetBoardCommand(boat);
            }
        }

        // Recompute land connected-components (4-connected flood fill) so the bot knows which cells are
        // reachable on foot from each other. Throttled — terrain doesn't change, so every few seconds is plenty.
        private void EnsureComponents(BotState st, WorldData world, float dt)
        {
            st.CompTimer -= dt;
            bool sized = st.Comp != null && st.Comp.GetLength(0) == world.Width && st.Comp.GetLength(1) == world.Height;
            if (sized && st.CompTimer > 0f) return;
            st.CompTimer = 5f;
            int w = world.Width, h = world.Height;
            if (!sized) st.Comp = new int[w, h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) st.Comp[x, y] = -1;

            var q = new Queue<Vector2Int>();
            int id = 0;
            for (int sy = 0; sy < h; sy++)
            for (int sx = 0; sx < w; sx++)
            {
                if (st.Comp[sx, sy] != -1 || !IsLandCell(sx, sy)) continue;
                st.Comp[sx, sy] = id; q.Enqueue(new Vector2Int(sx, sy));
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    FloodStep(st, q, c.x + 1, c.y, id, w, h);
                    FloodStep(st, q, c.x - 1, c.y, id, w, h);
                    FloodStep(st, q, c.x, c.y + 1, id, w, h);
                    FloodStep(st, q, c.x, c.y - 1, id, w, h);
                }
                id++;
            }
        }

        private void FloodStep(BotState st, Queue<Vector2Int> q, int x, int y, int id, int w, int h)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            if (st.Comp[x, y] != -1 || !IsLandCell(x, y)) return;
            st.Comp[x, y] = id; q.Enqueue(new Vector2Int(x, y));
        }

        // A walkable land cell (anything goblins can stand on — not deep water, shore or cliff).
        private bool IsLandCell(int x, int y)
        {
            var t = _terrainMap != null ? _terrainMap.GetTile(new Vector3Int(x, y, 0)) : null;
            if (t == null) return false;
            return t.name != "DeepWater" && t.name != "Shore" && t.name != "Cliff";
        }

        private static int CompAt(BotState st, Vector2Int c)
        {
            if (st.Comp == null) return -1;
            int w = st.Comp.GetLength(0), h = st.Comp.GetLength(1);
            if (c.x < 0 || c.y < 0 || c.x >= w || c.y >= h) return -1;
            return st.Comp[c.x, c.y];
        }
        private static int CompAt(BotState st, Vector3 world)
            => CompAt(st, new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y)));

        private static Goblin FindBoat(ulong owner)
        {
            foreach (var g in Goblin.All)
                if (g != null && !g.IsNeutral && g.Owner == owner && g.CurrentHp > 0 && g.IsBoat) return g;
            return null;
        }

        // Send the nearest available bot farmer to construct a building at 'site'.
        private void AssignBuilder(ulong owner, Vector2Int site)
        {
            Vector3 sw = new(site.x + 0.5f, site.y + 0.5f, 0f);
            Goblin best = null; float bestSq = float.MaxValue;
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.Owner != owner || g.Kind != "FarmerGoblin") continue;
                if (!g.gameObject.activeInHierarchy) continue;
                float d = (g.transform.position - sw).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = g; }
            }
            best?.SetBuildCommand(site);
        }

        // Buildable coastal cell on the keep's landmass (adjacent to water so the dock gets its pier).
        private bool TryFindDockSite(BotState st, out Vector2Int site)
        {
            site = default;
            int keepComp = CompAt(st, st.Keep);
            var fp = _dockDef.Footprint;
            for (int ring = 1; ring <= 18; ring++)
            for (int dy = -ring; dy <= ring; dy++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;
                var o = new Vector2Int(st.Keep.x + dx, st.Keep.y + dy);
                if (CompAt(st, o) != keepComp) continue;       // must be on our landmass
                if (!IsBuildable(o, fp)) continue;
                if (!HasAdjacentWater(o, fp)) continue;
                site = o; return true;
            }
            return false;
        }

        // True if any cell bordering the footprint is water (deep water or shore).
        private bool HasAdjacentWater(Vector2Int origin, Vector2Int fp)
        {
            for (int dy = -1; dy <= fp.y; dy++)
            for (int dx = -1; dx <= fp.x; dx++)
            {
                if (dx >= 0 && dx < fp.x && dy >= 0 && dy < fp.y) continue;  // interior cell
                var t = _terrainMap != null ? _terrainMap.GetTile(new Vector3Int(origin.x + dx, origin.y + dy, 0)) : null;
                if (t != null && (t.name == "DeepWater" || t.name == "Shore")) return true;
            }
            return false;
        }
    }
}

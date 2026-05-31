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
        [SerializeField] private int _farmerTarget = 14;
        [SerializeField] private int _searchRadius = 18;   // cells from keep a bot will harvest within (was 32 → farmers trekked across the map)
        [SerializeField] private string _keepName = "Keep_0";
        [SerializeField] private string _hutName = "Huts_0";
        [SerializeField] private string _barracksName = "Barracks_0";
        [SerializeField] private string _dockName = "Docks_0";
        [SerializeField] private string _workshopName = "Workshop";
        [SerializeField] private string _wheatName = "Wheatfield";
        [Tooltip("How many wheat fields the bot will build for a steady food supply.")]
        [SerializeField] private int _wheatTarget = 2;

        [Header("Vision / Scouting")]
        [SerializeField] private int _visionRadius = 6;
        [SerializeField] private int _keepVisionRadius = 10;
        [Tooltip("While no enemy is found, send one more scout every this many seconds (up to the max).")]
        [SerializeField] private float _scoutEscalateInterval = 180f;
        [SerializeField] private int _maxScouts = 4;

        [Header("Defense")]
        [Tooltip("Hostile units within this radius of the keep (or near a unit) trigger a defense squad. Ignores the bot's fog — it always feels threats this close.")]
        [SerializeField] private float _defenseRadius = 30f;
        [Tooltip("How many combat units the bot tries to field when defending.")]
        [SerializeField] private int _defenseSquadSize = 4;
        [Tooltip("How many combat units the bot fields to proactively raid a neutral monster it has spotted.")]
        [SerializeField] private int _raidSquadSize = 3;

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
            public BuildingDefinition BuildDef;   // def of the in-progress build (for completion bookkeeping)
            public bool Building;
            public float BuildElapsed;       // seconds since current build started (watchdog)
            public int WheatBuilt;           // completed wheat fields (they convert to decorations, so CountOwned can't track them)
            public int AssignCycle;
            public float StatusLogTimer;     // throttle for the periodic status log

            // Phase 3: the bot's own exploration of the map (it only "knows" what it has seen).
            public bool[,] Explored;
            public readonly List<Goblin> Scouts = new();   // active scouts; count escalates while no enemy found
            public int ScoutDesired = 1;                   // how many scouts to run right now
            public float ScoutEscalateTimer;               // counts up; +1 scout every _scoutEscalateInterval
            public float ScoutPull;                        // 0..1 — how strongly scouts are drawn toward enemy spawns (ramps up over time)
            public bool EnemyFound;
            public readonly HashSet<Vector2Int> DiscoveredEnemyBases = new();

            // Phase 4: military + attack plans.
            public float MilTrainTimer = -1f;
            public GoblinUnitDefinition PendingMil;
            public bool PlanAttacking;          // currently executing the attack
            public bool Defending;              // currently reacting to a nearby threat (for log transitions)
            public bool Raiding;                // currently hunting a spotted neutral monster (for log transitions)
            public int PlanTargetSize;          // planned army size; 0 = no plan (downtime)
            public float PlanTimer;             // counts up during downtime; a plan rolls at _attackCooldown
            public readonly float[] PlanWeights = { 80f, 15f, 5f };  // small / medium / large roll weights

            // Naval: land connected-components so the bot knows what it can reach on foot, plus boat/ferry state.
            public int[,] Comp;                 // per-cell land-component id (−1 = water/oob); recomputed periodically
            public float CompTimer;             // throttle for component recompute
            public float BoatTrainTimer = -1f;  // -1 = not training a boat
            public float BoardTimer;            // boarding window before the boat departs anyway
            // Persistent naval mission so the boat keeps sailing AFTER its cargo boards (boarded units go
            // inactive and stop driving the ferry — without this the boat sits at the dock forever).
            public bool NavalActive;            // a ferry mission is in progress
            public bool NavalInvade;            // true = ferry military to attack; false = ferry one scout to explore
            public Vector3 NavalTarget;         // far-shore world point to unload at
        }

        private readonly Dictionary<ulong, BotState> _states = new();
        private BuildingDefinition _hutDef, _barracksDef, _dockDef, _workshopDef, _wheatDef, _millDef;
        private GoblinUnitDefinition _farmerDef, _clubDef, _archerDef, _boatDef, _spearDef;
        private WorldGeneratorBootstrap _worldSource;
        private float _t;

        private void Start()
        {
            _worldSource = UnityEngine.Object.FindFirstObjectByType<WorldGeneratorBootstrap>();
            _hutDef = FindBuilding(_hutName);
            _barracksDef = FindBuilding(_barracksName);
            _dockDef = FindBuilding(_dockName);
            _workshopDef = FindBuilding(_workshopName);
            _wheatDef = FindBuilding(_wheatName);
            _millDef = FindBuilding("Mill");
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
                if (_barracksDef.TrainsUnits.Length > 2) _spearDef = _barracksDef.TrainsUnits[2];
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
            // 2. Census of this bot's units FIRST (scouting decisions depend on farmer count).
            int farmers = 0;
            var idleFarmers = new List<Goblin>();
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.Owner != owner) continue;
                if (g.Kind == "FarmerGoblin")
                {
                    farmers++;
                    if (g.IsIdle && !st.Scouts.Contains(g)) idleFarmers.Add(g);
                }
            }

            // 1c. Scout for the enemy until found. Scouts are capped to a fraction of the workforce so
            // scouting never starves the economy (the old bug: 4 of 5 farmers scouting → no buildings).
            if (!st.EnemyFound) Scout(owner, st, dt, farmers);
            else if (st.Scouts.Count > 0)   // enemy known → release scouts back to the economy, reset escalation
            {
                LogBot(owner, $"enemy located → releasing {st.Scouts.Count} scouts back to economy.");
                st.Scouts.Clear(); st.ScoutDesired = 1; st.ScoutEscalateTimer = 0f; st.ScoutPull = 0f;
            }
            // Don't assign current scouts to harvest.
            idleFarmers.RemoveAll(f => st.Scouts.Contains(f));

            // 3. Assign idle farmers to harvest. Round-robin over ALL six resource kinds (wood/food/stone
            // weighted heavier since they're the staples, plus gold/iron/crystal ores for upgrades). Falls
            // back to any nearby node if the rolled kind isn't reachable.
            foreach (var f in idleFarmers)
            {
                ResourceKind kind = (st.AssignCycle++ % 8) switch
                {
                    0 or 1 => ResourceKind.Wood,
                    2 or 3 => ResourceKind.Food,
                    4      => ResourceKind.Stone,
                    5      => ResourceKind.Gold,
                    6      => ResourceKind.Iron,
                    _      => ResourceKind.Crystal,
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

            // 5. Building: track current construction; clear when finished. Watchdog: if a build sits
            // unfinished too long (builder died / never arrived), keep a farmer assigned, then give up.
            if (st.Building)
            {
                if (!BuildingConstruction.IsUnderConstruction(st.BuildSite))
                {
                    LogBot(owner, $"finished building {(st.BuildDef != null ? st.BuildDef.name : "?")} at {st.BuildSite}.");
                    if (st.BuildDef == _wheatDef) st.WheatBuilt++;   // completed wheat field (now a harvestable decoration)
                    st.Building = false; st.BuildElapsed = 0f;
                }
                else
                {
                    st.BuildElapsed += dt;
                    EnsureBuilder(owner, st.BuildSite);     // make sure a farmer is actually constructing it
                    if (st.BuildElapsed > 60f)              // stuck → abandon so the bot isn't frozen
                    {
                        LogBot(owner, $"ABANDONING stuck build at {st.BuildSite} after {st.BuildElapsed:0}s.");
                        _placer.RemoveBuilding(st.BuildSite);
                        st.Building = false; st.BuildElapsed = 0f;
                    }
                }
            }

            if (!st.Building)
            {
                // Actively expand: pop room (huts) → barracks → wheat farms (food) → workshop (upgrades)
                // → a 2nd barracks. Each gated on affordability so the bot never stalls on one item.
                int popUsed = BotEconomy.PopUsed(owner), popCap = BotEconomy.PopCap(owner);
                bool nearCap = popUsed >= popCap - 3;
                bool wantRoom = popCap < 90 && popUsed >= popCap * 0.6f;   // grow before hitting the wall
                bool hasBarracks = OwnsBuilding(owner, _barracksName);
                if ((nearCap || wantRoom) && CanAfford(owner, _hutDef))
                    TryBuild(owner, st, _hutDef);                                  // more huts → more pop
                else if (!hasBarracks && farmers >= 4 && CanAfford(owner, _barracksDef))
                    TryBuild(owner, st, _barracksDef);                            // first barracks (early)
                else if (hasBarracks && _wheatDef != null && st.WheatBuilt < _wheatTarget
                         && farmers >= 6 && CanAfford(owner, _wheatDef))
                    TryBuild(owner, st, _wheatDef);                              // wheat farms → steady food
                else if (hasBarracks && _workshopDef != null && !OwnsBuilding(owner, _workshopName)
                         && farmers >= 8 && CanAfford(owner, _workshopDef))
                    TryBuild(owner, st, _workshopDef);                          // workshop → unlock upgrades
                else if (hasBarracks && _millDef != null && !OwnsBuilding(owner, "Mill")
                         && farmers >= 8 && CanAfford(owner, _millDef))
                    TryBuild(owner, st, _millDef);                             // mill → closer resource drop-off
                else if (hasBarracks && CountOwned(owner, _barracksName) < 2 && farmers >= 10
                         && CanAfford(owner, _barracksDef))
                    TryBuild(owner, st, _barracksDef);                            // expand: a 2nd barracks
            }

            // 5b. Buy workshop upgrades whenever affordable (ore is harvested in step 3).
            TryBuyUpgrades(owner, st);

            // 6. Military: train an army from the Barracks, then attack discovered enemies.
            RunMilitary(owner, st, dt);

            // 6b. Drive any active naval ferry mission EVERY tick — independent of cargo. (Boarded units go
            // inactive and can't drive the boat themselves, so the mission must be ticked from here.)
            if (st.NavalActive) DriveNaval(owner, st, dt);

            // 7. Periodic status log so the bot's "thoughts" are visible in the console.
            st.StatusLogTimer += dt;
            if (st.StatusLogTimer >= 15f)
            {
                st.StatusLogTimer = 0f;
                int military = 0;
                foreach (var g in Goblin.All)
                    if (g != null && !g.IsNeutral && g.Owner == owner && g.Kind != "FarmerGoblin" && !g.IsBoat) military++;
                LogBot(owner, $"status — farmers {farmers} (scouts {st.Scouts.Count}), military {military}, " +
                              $"W/F/S {BotEconomy.Get(owner, ResourceKind.Wood)}/{BotEconomy.Get(owner, ResourceKind.Food)}/{BotEconomy.Get(owner, ResourceKind.Stone)} " +
                              $"ore G/I/C {BotEconomy.Get(owner, ResourceKind.Gold)}/{BotEconomy.Get(owner, ResourceKind.Iron)}/{BotEconomy.Get(owner, ResourceKind.Crystal)}, " +
                              $"pop {BotEconomy.PopUsed(owner)}/{BotEconomy.PopCap(owner)}, " +
                              $"barracks {CountOwned(owner, _barracksName)}, huts {CountOwned(owner, _hutName)}, " +
                              $"farms {CountOwned(owner, _wheatName)}, workshop {(OwnsBuilding(owner, _workshopName) ? 1 : 0)}, " +
                              $"enemyFound {st.EnemyFound}, plan {(st.PlanTargetSize == 0 ? "downtime" : st.PlanTargetSize + (st.PlanAttacking ? " ATTACKING" : " building"))}");
            }
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

            // ── DEFENSE: a hostile unit (neutral monster or enemy combatant) near the base → form a squad
            //    and kill it. Overrides the attack plan while the threat is near; trains urgently if needed. ──
            if (NearbyThreat(owner, st, out var threat))
            {
                if (!st.Defending)
                {
                    st.Defending = true;
                    LogBot(owner, $"DEFENDING — {(threat.IsNeutral ? "monster" : "enemy")} near base; rallying {military} + training to {_defenseSquadSize}" + (hasBarracks ? "" : " (NO BARRACKS yet!)"));
                }
                foreach (var g in army) g.SetAttackCommand(threat);    // every combatant converges on it
                if (military < _defenseSquadSize)
                    TrainMilitary(owner, st, dt, hasBarracks, barracks, military, _defenseSquadSize);
                return;
            }
            st.Defending = false;

            // ── Downtime: grow + scout. Proactively RAID a neutral monster we've spotted (field a small
            //    squad and hunt it); otherwise roll the next attack plan once an enemy has been found. ──
            if (st.PlanTargetSize == 0)
            {
                st.PlanTimer += dt;
                if (SeenNeutralMonster(owner, st, out var prey))
                {
                    if (!st.Raiding)
                    {
                        st.Raiding = true;
                        LogBot(owner, $"RAIDING a spotted monster; sending {military}, training to {_raidSquadSize}" + (hasBarracks ? "" : " (NO BARRACKS yet!)"));
                    }
                    foreach (var g in army) g.SetAttackCommand(prey);
                    if (military < _raidSquadSize)
                        TrainMilitary(owner, st, dt, hasBarracks, barracks, military, _raidSquadSize);
                }
                else
                {
                    st.Raiding = false;
                    if (st.PlanTimer >= _attackCooldown && st.EnemyFound && st.DiscoveredEnemyBases.Count > 0)
                        RollPlan(owner, st);
                }
                return;
            }

            // ── Building toward the planned army size. ──
            if (!st.PlanAttacking)
            {
                TrainMilitary(owner, st, dt, hasBarracks, barracks, military, st.PlanTargetSize);
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

            // Naval invasion: if any home-landmass units can't reach a base on foot, start/refresh a ferry
            // mission to the nearest enemy base on another landmass (DriveNaval, ticked from RunBot, runs it).
            if (ferry.Count > 0)
            {
                Vector3 home = new(st.Keep.x + 0.5f, st.Keep.y + 0.5f, 0f);
                if (NearestAliveBaseOtherComponent(st, home, keepComp, out var tb))
                    StartNaval(owner, st, new Vector3(tb.x + 0.5f, tb.y + 0.5f, 0f), invade: true);
            }
        }

        // Begin (or refresh the target of) a naval ferry mission. The actual sailing is driven each tick by
        // DriveNaval so it continues after the cargo boards (boarded units go inactive).
        private void StartNaval(ulong owner, BotState st, Vector3 target, bool invade)
        {
            st.NavalTarget = target;
            st.NavalInvade = invade;
            if (!st.NavalActive)
            {
                st.NavalActive = true;
                LogBot(owner, $"starting naval {(invade ? "invasion" : "scouting")} ferry → {target}.");
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

        // Pick a random available military unit (Club / Archer / Spear). Used for normal army building;
        // monster raids override this to always pick the Speargoblin.
        private GoblinUnitDefinition PickRandomMilitary()
        {
            _milPick.Clear();
            if (_clubDef != null) _milPick.Add(_clubDef);
            if (_archerDef != null) _milPick.Add(_archerDef);
            if (_spearDef != null) _milPick.Add(_spearDef);
            if (_milPick.Count == 0) return null;
            return _milPick[Random.Range(0, _milPick.Count)];
        }
        private readonly List<GoblinUnitDefinition> _milPick = new();

        // Train military one at a time (random Club/Archer/Spear; Speargoblin during raids) up to the
        // planned size, paid from BotEconomy.
        private void TrainMilitary(ulong owner, BotState st, float dt, bool hasBarracks, Vector2Int barracks, int military, int targetSize)
        {
            if (hasBarracks && st.MilTrainTimer < 0f && military < targetSize)
            {
                // Monster raids → train Speargoblins (bonus vs monsters). Otherwise pick a random
                // available military unit (Club / Archer / Spear).
                var def = (st.Raiding && _spearDef != null) ? _spearDef : PickRandomMilitary();
                if (def == null) def = _clubDef ?? _archerDef ?? _spearDef;
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
                    if (st.PendingMil != null)
                    {
                        _spawner.SpawnKindAt(st.PendingMil.SpawnerKindName, at, owner);
                        LogBot(owner, $"trained {st.PendingMil.SpawnerKindName}.");
                    }
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

        // Defense trigger: any HOSTILE unit (neutral monster OR an enemy player's/other bot's combat unit)
        // that is near the keep or right next to one of the bot's units. Returns the nearest one to the keep.
        // Enemy non-combat units (farmers/scouts) don't trigger it; neutral monsters always do.
        private bool NearbyThreat(ulong owner, BotState st, out Goblin threat)
        {
            threat = null;
            Vector3 keep = new(st.Keep.x + 0.5f, st.Keep.y + 0.5f, 0f);
            float keepR2 = _defenseRadius * _defenseRadius;
            float bestSq = float.MaxValue;
            foreach (var m in Goblin.All)
            {
                if (m == null || m.CurrentHp <= 0) continue;
                bool isEnemy = m.IsNeutral || m.Owner != owner;       // not one of our own units
                if (!isEnemy) continue;
                if (!m.IsNeutral && m.AttackDamage <= 0) continue;     // ignore harmless enemy workers/scouts

                float dk = (m.transform.position - keep).sqrMagnitude;
                bool near = dk <= keepR2;
                if (!near)
                {
                    foreach (var g in Goblin.All)   // engaging one of our units?
                    {
                        if (g == null || g.IsNeutral || g.Owner != owner || g.CurrentHp <= 0) continue;
                        if (g.Kind == "FarmerGoblin") continue;
                        if ((m.transform.position - g.transform.position).sqrMagnitude <= 36f) { near = true; break; }
                    }
                }
                if (near && dk < bestSq) { bestSq = dk; threat = m; }
            }
            return threat != null;
        }

        // Nearest neutral monster currently WITHIN SIGHT of the bot — i.e. inside vision range of one of
        // its units (so the bot only raids what it has actually seen, not the whole map). Picks the one
        // nearest the keep. Used for proactive raiding during downtime.
        private bool SeenNeutralMonster(ulong owner, BotState st, out Goblin monster)
        {
            monster = null;
            Vector3 keep = new(st.Keep.x + 0.5f, st.Keep.y + 0.5f, 0f);
            int vr2 = (_visionRadius + 1) * (_visionRadius + 1);
            float bestSq = float.MaxValue;
            foreach (var m in Goblin.All)
            {
                if (m == null || !m.IsNeutral || m.CurrentHp <= 0) continue;
                bool seen = false;
                foreach (var g in Goblin.All)
                {
                    if (g == null || g.IsNeutral || g.Owner != owner || g.CurrentHp <= 0) continue;
                    if ((m.transform.position - g.transform.position).sqrMagnitude <= vr2) { seen = true; break; }
                }
                if (!seen) continue;
                float dk = (m.transform.position - keep).sqrMagnitude;
                if (dk < bestSq) { bestSq = dk; monster = m; }
            }
            return monster != null;
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

        // Buy any workshop upgrade the bot can afford and hasn't bought yet (paid in iron/gold/crystal
        // from its own economy). Mirrors the player's ObjectInspector.OnUpgradeClicked path so the effect
        // applies to all of this bot's units. Requires a completed Workshop.
        private void TryBuyUpgrades(ulong owner, BotState st)
        {
            // Research from every upgrade-providing building the bot owns (Workshop: combat upgrades;
            // Mill: farmer harvest speed + bountiful harvest).
            TryResearchFrom(owner, st, _workshopDef, _workshopName);
            TryResearchFrom(owner, st, _millDef, "Mill");
        }

        // Buy any upgrade provided by a completed building of the given def/name that the bot can afford
        // and hasn't bought yet (paid in iron/gold/crystal). Mirrors the player's upgrade-purchase path.
        private void TryResearchFrom(ulong owner, BotState st, BuildingDefinition def, string buildingName)
        {
            if (def == null || def.ProvidesUpgrades == null) return;
            // The building must exist AND be finished (not still under construction) to research.
            if (!_placer.TryFindNearestBuildingByName(buildingName, owner, st.Keep, out var b)) return;
            if (BuildingConstruction.IsUnderConstruction(b)) return;

            foreach (var up in def.ProvidesUpgrades)
            {
                if (up == null || PlayerUpgrades.IsPurchased(owner, up.Kind)) continue;
                if (BotEconomy.Get(owner, ResourceKind.Iron) < up.IronCost) continue;
                if (BotEconomy.Get(owner, ResourceKind.Gold) < up.GoldCost) continue;
                if (BotEconomy.Get(owner, ResourceKind.Crystal) < up.CrystalCost) continue;

                if (up.IronCost > 0) BotEconomy.Add(owner, ResourceKind.Iron, -up.IronCost);
                if (up.GoldCost > 0) BotEconomy.Add(owner, ResourceKind.Gold, -up.GoldCost);
                if (up.CrystalCost > 0) BotEconomy.Add(owner, ResourceKind.Crystal, -up.CrystalCost);
                PlayerUpgrades.MarkPurchased(owner, up.Kind);
                UpgradeEffects.ApplyToOwnedUnits(owner, up.Kind);
                LogBot(owner, $"researched upgrade {up.Kind}.");
            }
        }

        private void TryBuild(ulong owner, BotState st, BuildingDefinition def)
        {
            if (!TryFindBuildSite(st.Keep, def.Footprint, out var site))
            {
                LogBot(owner, $"wanted to build {def.name} but found no build site near keep.");
                return;
            }
            BotEconomy.Add(owner, ResourceKind.Wood, -def.WoodCost);
            BotEconomy.Add(owner, ResourceKind.Stone, -def.StoneCost);
            _placer.PlaceForce(def, site, charge: false, requireConstruction: true, owner: owner);
            st.BuildSite = site; st.BuildDef = def; st.Building = true; st.BuildElapsed = 0f;
            LogBot(owner, $"building {def.name} at {site} (cost {def.WoodCost}w/{def.StoneCost}s).");
            EnsureBuilder(owner, site);
        }

        // Guarantee a farmer is heading to / constructing the building at 'site'. Skips active scouts and
        // farmers already building it; otherwise assigns the nearest available farmer.
        private void EnsureBuilder(ulong owner, Vector2Int site)
        {
            var scouts = _states.TryGetValue(owner, out var st) ? st.Scouts : null;
            Vector3 sw = new(site.x + 0.5f, site.y + 0.5f, 0f);
            Goblin best = null; float bestSq = float.MaxValue;
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.Owner != owner || g.Kind != "FarmerGoblin") continue;
                if (g.IsBuildingAt(site)) return;          // someone is already on it
                if (scouts != null && scouts.Contains(g)) continue;
                float d = (g.transform.position - sw).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = g; }
            }
            best?.SetBuildCommand(site);
        }

        // Per-bot console logger so the bot's decisions ("thoughts") are visible during play.
        private static void LogBot(ulong owner, string msg) => Debug.Log($"[Bot {owner}] {msg}");

        private bool OwnsBuilding(ulong owner, string name)
        {
            foreach (var kv in _placer.AllOccupied)
            {
                if (kv.Value == null || kv.Value.name != name) continue;
                if (_placer.TryGetBuildingOwner(kv.Key, out var o) && o == owner) return true;
            }
            return false;
        }

        // Count this owner's DISTINCT buildings named `name` (AllOccupied is keyed per-cell, so dedupe by origin).
        private int CountOwned(ulong owner, string name)
        {
            var seen = new HashSet<Vector2Int>();
            foreach (var kv in _placer.AllOccupied)
            {
                if (kv.Value == null || kv.Value.name != name) continue;
                if (!_placer.TryGetBuildingOwner(kv.Key, out var o) || o != owner) continue;
                if (!_placer.TryGetBuildingOrigin(kv.Key, out var origin)) origin = kv.Key;
                seen.Add(origin);
            }
            return seen.Count;
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

        // Scout management. The bot runs a small team of scouts to find the enemy — it does NOT know where
        // they are. Each scout EITHER wanders random reachable unexplored land OR (with probability
        // ScoutPull, which ramps up the longer the enemy stays unfound) heads toward the nearest enemy
        // spawn — so the search drifts into enemy territory over time. Scout count is capped to a fraction
        // of the workforce so scouting can't starve the economy. Dead scouts are noticed + replaced.
        private void Scout(ulong owner, BotState st, float dt, int farmers)
        {
            var world = _worldSource != null ? _worldSource.CurrentWorld : null;
            if (world == null) return;

            // Pull ramps 0 → ~0.9 over ~5 min: early = broad/random, later = drawn at enemy spawns.
            st.ScoutPull = Mathf.Min(0.9f, st.ScoutPull + dt / 300f);

            // Escalate the DESIRED scout count over time, but CAP to ≤ ~1/4 of farmers (min 1) so the
            // economy keeps running (the old bug: 4 of 5 farmers scouting → no harvesting, no buildings).
            st.ScoutEscalateTimer += dt;
            if (st.ScoutDesired < 1) st.ScoutDesired = 1;
            if (st.ScoutEscalateTimer >= _scoutEscalateInterval && st.ScoutDesired < _maxScouts)
            {
                st.ScoutDesired++; st.ScoutEscalateTimer = 0f;
            }
            int effective = Mathf.Clamp(Mathf.Min(st.ScoutDesired, farmers / 4), 1, _maxScouts);

            // Drop scouts that died / were captured / boarded a boat (inactive); trim if the cap dropped.
            st.Scouts.RemoveAll(s => s == null || s.CurrentHp <= 0 || s.Owner != owner || s.IsNeutral
                                     || !s.gameObject.activeInHierarchy);
            while (st.Scouts.Count > effective) st.Scouts.RemoveAt(st.Scouts.Count - 1);

            // Top up to the effective count from available farmers (prefer idle; else interrupt any farmer).
            while (st.Scouts.Count < effective)
            {
                Goblin idle = null, any = null;
                foreach (var g in Goblin.All)
                {
                    if (g == null || g.IsNeutral || g.Owner != owner || g.Kind != "FarmerGoblin") continue;
                    if (!g.gameObject.activeInHierarchy || st.Scouts.Contains(g)) continue;
                    any ??= g;
                    if (g.IsIdle) { idle = g; break; }
                }
                var pick = idle ?? any;
                if (pick == null) break;            // no spare farmers right now
                st.Scouts.Add(pick);
                LogBot(owner, $"sending scout #{st.Scouts.Count} (pull {st.ScoutPull:0.00}).");
                DriveScout(owner, st, dt, pick, st.Scouts.Count == 1);   // command it this tick
            }

            // Drive idle scouts onward (busy ones are still travelling between hops).
            for (int i = 0; i < st.Scouts.Count; i++)
                if (st.Scouts[i].IsIdle) DriveScout(owner, st, dt, st.Scouts[i], i == 0);
        }

        // Move one scout. With probability ScoutPull it heads toward the nearest enemy spawn on its own
        // landmass (drawn into enemy territory over time); otherwise it wanders random reachable unexplored
        // land. Short hops keep each A* path in budget. If boxed in, the PRIMARY scout boats elsewhere.
        private void DriveScout(ulong owner, BotState st, float dt, Goblin scout, bool isPrimary)
        {
            var world = _worldSource != null ? _worldSource.CurrentWorld : null;
            if (world == null) return;
            Vector3 from = scout.transform.position;
            int comp = CompAt(st, from);
            const float StepDist = 22f;

            // Pull toward the nearest enemy spawn on the same landmass.
            if (Random.value < st.ScoutPull && NearestEnemySpawn(world, st, comp, from, out var spawnPos))
            {
                Vector3 d = spawnPos - from;
                Vector3 step = d.magnitude <= StepDist ? spawnPos : from + d.normalized * StepDist;
                scout.SetMoveCommand(step);
                return;
            }

            // Otherwise wander reachable unexplored land on its own landmass.
            if (PickReachableUnexplored(world, st, comp, out var target)
                || NearestUnexploredOnComponent(world, st, comp, from, out target))
            {
                Vector3 delta = target - from;
                Vector3 step = delta.magnitude <= StepDist ? target : from + delta.normalized * StepDist;
                scout.SetMoveCommand(step);
                return;
            }

            // Boxed in: own landmass fully explored → ferry the primary scout across water to explore elsewhere.
            if (isPrimary && NearestUnexploredOtherComponent(world, st, comp, from, out var navTo))
                StartNaval(owner, st, navTo, invade: false);
        }

        // Nearest spawn point that is NOT the bot's own (own = the spawn nearest its keep), restricted to
        // the scout's current landmass (reachable on foot). The scout's "pull" goal.
        private bool NearestEnemySpawn(WorldData world, BotState st, int comp, Vector3 from, out Vector3 pos)
        {
            pos = default;
            if (world.Spawns == null || world.Spawns.Length == 0) return false;

            int ownIdx = -1; float ownBest = float.MaxValue;
            for (int i = 0; i < world.Spawns.Length; i++)
            {
                var s = world.Spawns[i];
                float d = (new Vector2(s.x, s.y) - new Vector2(st.Keep.x, st.Keep.y)).sqrMagnitude;
                if (d < ownBest) { ownBest = d; ownIdx = i; }
            }

            float bestSq = float.MaxValue; bool found = false;
            for (int i = 0; i < world.Spawns.Length; i++)
            {
                if (i == ownIdx) continue;
                var s = world.Spawns[i];
                if (comp >= 0 && CompAt(st, new Vector2Int(s.x, s.y)) != comp) continue;
                float d = (new Vector3(s.x + 0.5f, s.y + 0.5f, 0f) - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; pos = new Vector3(s.x + 0.5f, s.y + 0.5f, 0f); found = true; }
            }
            return found;
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
            for (int ring = 2; ring <= 18; ring++)   // search farther so a cluttered base keeps expanding
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

        // Drive the persistent naval ferry mission (st.NavalActive). Builds the dock + trains the boat on
        // demand, gathers its own cargo each tick (so it keeps working after units board and go inactive),
        // sails to st.NavalTarget, unloads on the far shore, returns for more, and ends when there is
        // nothing left to ferry and the boat is home.
        private void DriveNaval(ulong owner, BotState st, float dt)
        {
            if (_dockDef == null || _boatDef == null) { st.NavalActive = false; return; }

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
                    EnsureBuilder(owner, site);
                    LogBot(owner, $"building a DOCK at {site} (needs a boat to cross water).");
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
                        LogBot(owner, $"boat ready at dock {dockOrigin}.");
                    }
                }
                return;   // wait until the boat is built
            }

            // 3. Ferry. Only act while the boat is idle (otherwise it's mid-sail / unloading).
            if (!boat.IsIdle) return;

            var waiting = NavalCandidates(owner, st);     // re-gathered each tick (boarded units excluded)
            int aboard = boat.PassengerCount;
            bool boatHome = (boat.transform.position - dockWater).sqrMagnitude < 16f;   // within ~4 cells

            if (aboard == 0)
            {
                if (waiting.Count == 0)
                {
                    if (!boatHome) { boat.SetMoveCommand(dockWater); return; }  // park empty boat at home
                    st.NavalActive = false;                                     // delivered everything → done
                    LogBot(owner, "naval ferry complete.");
                    return;
                }
                if (!boatHome) { boat.SetMoveCommand(dockWater); return; }      // fetch the boat to load
                BoardSome(waiting, boat); st.BoardTimer = 8f;                   // start loading
                return;
            }

            // Some are aboard: depart when full, the boarding window elapses, or no one's left to load.
            st.BoardTimer -= dt;
            bool full = !boat.BoatHasRoom;
            bool moreToLoad = boatHome && waiting.Count > 0;
            if (full || st.BoardTimer <= 0f || !moreToLoad)
                boat.SetUnloadCommand(st.NavalTarget);                          // sail over + drop on far shore
            else
                BoardSome(waiting, boat);                                       // keep loading idle stragglers
        }

        // Active home-landmass units eligible to be ferried this tick. Invasion → military with no base
        // reachable on foot; scouting → the current scouts. Boarded (inactive) units are naturally excluded.
        private List<Goblin> NavalCandidates(ulong owner, BotState st)
        {
            var list = new List<Goblin>();
            int keepComp = CompAt(st, st.Keep);
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.Owner != owner || g.CurrentHp <= 0) continue;
                if (!g.gameObject.activeInHierarchy || g.IsBoat) continue;
                if (CompAt(st, g.transform.position) != keepComp) continue;     // only units on our landmass
                if (st.NavalInvade)
                {
                    if (g.Kind == "FarmerGoblin") continue;                     // military only
                    if (NearestAliveBaseOnComponent(st, g.transform.position, keepComp, out _)) continue; // can walk
                }
                else if (!st.Scouts.Contains(g)) continue;                      // scouting → only scouts
                list.Add(g);
            }
            return list;
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

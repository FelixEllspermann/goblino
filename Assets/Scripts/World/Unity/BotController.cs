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

        [Header("Vision / Scouting")]
        [SerializeField] private int _visionRadius = 6;
        [SerializeField] private int _keepVisionRadius = 10;

        [Header("Military")]
        [SerializeField] private int _armyTarget = 6;       // train up to this many military units
        [SerializeField] private int _attackThreshold = 5;  // start attacking once the army reaches this
        [SerializeField] private float _engageRadius = 8f;  // a military unit attacks hostiles within this

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

            // Phase 4: military.
            public float MilTrainTimer = -1f;
            public bool MilNextArcher;   // alternate club/archer
            public GoblinUnitDefinition PendingMil;
            public bool Attacking;
        }

        private readonly Dictionary<ulong, BotState> _states = new();
        private BuildingDefinition _hutDef, _barracksDef;
        private GoblinUnitDefinition _farmerDef, _clubDef, _archerDef;
        private WorldGeneratorBootstrap _worldSource;
        private float _t;

        private void Start()
        {
            _worldSource = UnityEngine.Object.FindFirstObjectByType<WorldGeneratorBootstrap>();
            _hutDef = FindBuilding(_hutName);
            _barracksDef = FindBuilding(_barracksName);
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
            // 1c. Scout for the enemy until one is found (a dedicated farmer explores other spawns).
            if (!st.EnemyFound) Scout(owner, st);
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

        private void RunMilitary(ulong owner, BotState st, float dt)
        {
            bool hasBarracks = _placer.TryFindNearestBuildingByName(_barracksName, owner, st.Keep, out var barracks)
                               && !BuildingConstruction.IsUnderConstruction(barracks);

            // Census current military (non-farmer combatants).
            int military = 0;
            var army = new List<Goblin>();
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.Owner != owner) continue;
                if (g.Kind != "FarmerGoblin") { military++; army.Add(g); }
            }

            // Train the army (one at a time, alternating Club/Archer), paid from BotEconomy.
            if (hasBarracks && st.MilTrainTimer < 0f && military < _armyTarget)
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

            // Attack decision: commit once the army is big enough and an enemy base is known.
            if (!st.Attacking && military >= _attackThreshold && st.EnemyFound && st.DiscoveredEnemyBases.Count > 0)
                st.Attacking = true;
            if (st.Attacking && military == 0) st.Attacking = false;   // wiped out → regroup

            if (!st.Attacking) return;
            foreach (var g in army)
            {
                var tgt = NearestHostile(g, _engageRadius);
                if (tgt != null) g.SetAttackCommand(tgt);                 // engage anything hostile in range
                else if (g.IsIdle && NearestBase(st, g.transform.position, out var basePos))
                    g.SetMoveCommand(basePos);                            // else march to the known enemy base
            }
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

        // Send a dedicated scout toward the nearest not-yet-explored spawn point to find the enemy.
        private void Scout(ulong owner, BotState st)
        {
            var world = _worldSource != null ? _worldSource.CurrentWorld : null;
            if (world == null || world.Spawns == null) return;

            if (st.Scout == null || st.Scout.CurrentHp <= 0 || st.Scout.Owner != owner || st.Scout.IsNeutral)
            {
                st.Scout = null;
                foreach (var g in Goblin.All)
                {
                    if (g == null || g.IsNeutral || g.Owner != owner || g.Kind != "FarmerGoblin") continue;
                    st.Scout = g; break;
                }
                if (st.Scout == null) return;
            }
            if (!st.Scout.IsIdle) return;   // still travelling

            var sc = new Vector2Int(Mathf.FloorToInt(st.Scout.transform.position.x), Mathf.FloorToInt(st.Scout.transform.position.y));
            int best = -1, bestSq = int.MaxValue;
            int w = st.Explored != null ? st.Explored.GetLength(0) : 0, h = st.Explored != null ? st.Explored.GetLength(1) : 0;
            for (int i = 0; i < world.Spawns.Length; i++)
            {
                var sp = world.Spawns[i];
                bool explored = st.Explored != null && sp.x >= 0 && sp.y >= 0 && sp.x < w && sp.y < h && st.Explored[sp.x, sp.y];
                if (explored) continue;
                int dx = sp.x - sc.x, dy = sp.y - sc.y, d = dx * dx + dy * dy;
                if (d < bestSq) { bestSq = d; best = i; }
            }
            if (best >= 0)
            {
                var sp = world.Spawns[best];
                st.Scout.SetMoveCommand(new Vector3(sp.x + 0.5f, sp.y + 0.5f, 0f));
            }
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
    }
}

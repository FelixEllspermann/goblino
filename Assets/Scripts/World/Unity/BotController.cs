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

        private sealed class BotState
        {
            public Vector2Int Keep;
            public bool KeepKnown;
            public float TrainTimer = -1f;   // -1 = not training
            public Vector2Int BuildSite;
            public bool Building;
            public int AssignCycle;
        }

        private readonly Dictionary<ulong, BotState> _states = new();
        private BuildingDefinition _hutDef, _barracksDef;
        private GoblinUnitDefinition _farmerDef;
        private float _t;

        private void Start()
        {
            _hutDef = FindBuilding(_hutName);
            _barracksDef = FindBuilding(_barracksName);
            var keepDef = FindBuilding(_keepName);
            if (keepDef != null && keepDef.TrainsUnits != null && keepDef.TrainsUnits.Length > 0)
                _farmerDef = keepDef.TrainsUnits[0];
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

            // 2. Census of this bot's units.
            int farmers = 0;
            var idleFarmers = new List<Goblin>();
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.Owner != owner) continue;
                if (g.Kind == "FarmerGoblin")
                {
                    farmers++;
                    if (g.IsIdle) idleFarmers.Add(g);
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

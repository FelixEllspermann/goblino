// =============================================================================
// MainBaseSetup.cs  —  RTSCL.World.Unity
//
// Reacts to the world generator producing a new WorldData and bootstraps the
// per-player starting state: wipes previous-game statics, seeds resources,
// places each player's Keep (charge=false, requireConstruction=false), and
// spawns their starting Farmer Goblins around the keep's footprint.
//
// Detection pattern: polls WorldGeneratorBootstrap.CurrentWorld in Update and
// fires OnNewWorld the first frame the reference changes — no event subscription
// needed, and the dependency direction stays World → Unity (not the other way).
//
// Solo vs. Multiplayer: WorldStartContext.PendingSlots is null in solo mode.
// When null, a single team is placed at spawn[0] with owner=0UL. When non-null
// (MP path set by GameStartLoader), one team is placed per slot entry.
//
// To adjust starting resources: change the Add calls in OnNewWorld.
// To add more unit types at start: call SpawnAroundFootprint again in SpawnTeamAt.
// =============================================================================
using UnityEngine;
using UnityEngine.Tilemaps;
using RTSCL.World;

namespace RTSCL.World.Unity
{
    public sealed class MainBaseSetup : MonoBehaviour
    {
        [SerializeField] private WorldGeneratorBootstrap _worldSource;
        [SerializeField] private BuildingPlacer _buildingPlacer;
        [SerializeField] private BuildingCatalog _catalog;
        [SerializeField] private GoblinSpawner _goblinSpawner;
        [SerializeField] private Tilemap _terrainMap;
        [Tooltip("Decoration tilemap — a completed Wheatfield building is converted to a harvestable field tile here.")]
        [SerializeField] private Tilemap _decorationMap;
        [Tooltip("Harvestable wheat-field decoration tile painted where a Wheatfield building finishes (e.g. Wheatfield_0).")]
        [SerializeField] private UnityEngine.Tilemaps.TileBase _wheatTile;
        [Tooltip("Building asset name that becomes a harvestable field when built.")]
        [SerializeField] private string _wheatBuildingName = "Wheatfield";
        [Tooltip("Optional: spawns neutral monsters after the player teams.")]
        [SerializeField] private MonsterSpawner _monsterSpawner;
        [Tooltip("Optional: tracks win/lose by building ownership.")]
        [SerializeField] private MatchManager _matchManager;

        [Header("Main Base")]
        [SerializeField] private string _mainBuildingName = "Keep_0";
        [SerializeField] private int _startingGoblins = 5;
        [SerializeField] private GoblinUnitDefinition _startingUnitDef;
        [Tooltip("Test-only: spawn N Club Goblins farther out from the keep so you can test combat at game start. Set to 0 to disable.")]
        [SerializeField] private int _testStartingClubs = 0;
        [Tooltip("Population cost charged per test Club (defaults to ClubGoblin's PopulationCost = 3)")]
        [SerializeField] private int _testClubPopCost = 3;

        // Cache the last-seen world so Update can detect changes without an event subscription.
        private WorldData _knownWorld;

        // Convert a finished Wheatfield building into a harvestable field tile (build → then farm it).
        private void OnEnable()  => BuildingConstruction.OnCompleted += OnBuildingCompleted;
        private void OnDisable() => BuildingConstruction.OnCompleted -= OnBuildingCompleted;

        private void OnBuildingCompleted(Vector2Int origin)
        {
            if (_buildingPlacer == null) return;
            if (!_buildingPlacer.TryGetBuildingAt(origin, out var def) || def == null) return;
            if (def.name != _wheatBuildingName) return;
            // Bake the Mill's Bountiful Harvest bonus into THIS field based on its builder's upgrades, so
            // the +100 % food is per-player (the cell yields 1000 instead of 500 only if its owner has it).
            ulong owner = _buildingPlacer.TryGetBuildingOwner(origin, out var o) ? o : 0UL;
            var cell = new Vector3Int(origin.x, origin.y, 0);
            if (PlayerUpgrades.IsPurchased(owner, UpgradeKind.MillBountifulHarvest))
                WheatYield.MarkBoosted(cell);
            // Replace the built field shell with a harvestable wheat-field decoration on its cell.
            _buildingPlacer.RemoveBuilding(origin);
            if (_decorationMap != null && _wheatTile != null)
                _decorationMap.SetTile(cell, _wheatTile);
        }

        private void Update()
        {
            if (_worldSource == null) return;
            var w = _worldSource.CurrentWorld;
            // React on the first frame where CurrentWorld changes (new world generated or loaded).
            if (w == null || w == _knownWorld) return;
            _knownWorld = w;
            OnNewWorld(w);
        }

        // Full world-reset + team-spawn sequence. Order matters:
        //   1. Destroy all goblins/buildings from the previous game.
        //   2. Reset all static game-state registries.
        //   3. Wire NetCommandApplier bridge refs so net commands can mutate game state.
        //   4. Seed starting resources.
        //   5. Resolve spawn points and place each player's team.
        private void OnNewWorld(WorldData world)
        {
            // 1. Wipe state from previous game
            if (_goblinSpawner != null) _goblinSpawner.ClearAllGoblins();
            if (_buildingPlacer != null) _buildingPlacer.ClearAllPlaced();
            ResourceBank.Reset();
            TreeHP.Clear();
            GoblinProduction.Clear();
            PopulationManager.Reset();

            // Net-state setup for this world.
            GoblinNetRegistry.Reset();
            PlayerUpgrades.Reset();
            TrainingSpeed.Clear();
            SightRange.Clear();
            WallRegistry.Clear();
            HarvestReservations.Clear();
            RallyPoints.Clear();
            BotEconomy.Reset();
            DockRegistry.Clear();
            WheatYield.Clear();
            Time.timeScale = 1f;   // un-pause in case we returned from a game-over overlay
            // Solo: give bot factions visible colors (player stays white via IsLocalOwner path).
            if (WorldStartContext.IsSolo)
                WorldStartContext.GetPlayerColor = SoloBotColor;
            NetworkCatalog.PopulateFromCatalog(_catalog);
            // Wire refs so NetCommandApplier can call PlaceForce / SpawnByKindAroundFootprint
            // without a direct reference to these MonoBehaviours (assembly-boundary constraint).
            NetCommandApplier.Placer = _buildingPlacer;
            NetCommandApplier.Spawner = _goblinSpawner;
            WorldGrid.Width = world.Width;
            WorldGrid.Height = world.Height;

            // Starting baseline resources for the local player.
            ResourceBank.Add(ResourceKind.Wood, 100);
            ResourceBank.Add(ResourceKind.Food, 100);

            if (world.Spawns == null || world.Spawns.Length == 0) return;

            var def = FindBuildingDefinition(_mainBuildingName);
            if (def == null || _buildingPlacer == null)
            {
                Debug.LogWarning($"[MainBaseSetup] Building '{_mainBuildingName}' not in catalog.");
                // Fallback: no keep, spawn farmers around spawn[0]
                if (_goblinSpawner != null)
                {
                    var mainCell = world.Spawns[0];
                    var c = new Vector3(mainCell.x + 0.5f, mainCell.y + 0.5f, 0f);
                    _goblinSpawner.SpawnGroupAt(c, _startingGoblins);
                }
                return;
            }

            var slots = WorldStartContext.PendingSlots;
            if (slots == null || slots.Length == 0)
            {
                // Solo path: single team at spawn[0], owner=0UL (treated as local everywhere).
                var s0 = world.Spawns[0];
                SpawnTeamAt(def, new Vector2Int(s0.x, s0.y), 0UL, addPopulation: true);

                // Spawn AI bots at the remaining spawn points (owner ids 1..N), each with the same
                // starting team and its own seeded BotEconomy (100 wood + 100 food).
                int bots = Mathf.Clamp(WorldStartContext.SoloBotCount, 0, world.Spawns.Length - 1);
                for (int b = 1; b <= bots; b++)
                {
                    ulong botOwner = (ulong)b;
                    var sb = world.Spawns[b];
                    SpawnTeamAt(def, new Vector2Int(sb.x, sb.y), botOwner, addPopulation: false);
                    BotEconomy.Seed(botOwner, 100, 100);
                    int popPerUnit = _startingUnitDef != null ? _startingUnitDef.PopulationCost : 1;
                    BotEconomy.AddUsed(botOwner, _startingGoblins * popPerUnit);
                }

                // Start win/lose tracking for the player (0) + the spawned bots (1..N).
                if (_matchManager != null)
                {
                    var owners = new System.Collections.Generic.List<ulong> { 0UL };
                    for (int b = 1; b <= bots; b++) owners.Add((ulong)b);
                    _matchManager.Begin(owners);
                }

                SpawnMonsters(world);
                return;
            }

            // MP path: one team per slot. addPopulation=true only for the local player so
            // remote teams don't inflate the local pop-cap counter.
            ulong local = WorldStartContext.LocalPlayer;
            for (int i = 0; i < slots.Length; i++)
            {
                int idx = slots[i].spawnIndex;
                if (idx < 0 || idx >= world.Spawns.Length)
                {
                    Debug.LogWarning($"[MainBaseSetup] Slot {i} spawnIndex={idx} out of range (Spawns.Length={world.Spawns.Length}); skipping");
                    continue;
                }
                bool isLocal = slots[i].steamId == local;
                var s = world.Spawns[idx];
                SpawnTeamAt(def, new Vector2Int(s.x, s.y), slots[i].steamId, addPopulation: isLocal);
            }
            SpawnMonsters(world);
        }

        // Faction colors for solo bot factions (player = owner 0 stays white via the IsLocalOwner path).
        private static Color SoloBotColor(ulong owner) => owner switch
        {
            1UL => new Color(0.9f, 0.3f, 0.3f),   // red
            2UL => new Color(0.9f, 0.85f, 0.3f),  // yellow
            3UL => new Color(0.4f, 0.8f, 0.4f),   // green
            _   => Color.gray,
        };

        // Spawn neutral monsters after the player teams (so spawn order — hence NetIds — is identical
        // across clients). Deterministic from the world seed.
        private void SpawnMonsters(WorldData world)
        {
            if (_monsterSpawner != null)
                _monsterSpawner.SpawnAll(world, (uint)world.Seed);
        }

        /// <summary>Place one player's Keep + starting units at spawnCell.
        /// addPopulation should be true only for the local player so remote teams
        /// don't count against the local pop-cap.</summary>
        private void SpawnTeamAt(BuildingDefinition def, Vector2Int spawnCell, ulong owner, bool addPopulation)
        {
            // Offset so the keep is centered on the spawn cell
            var keepOrigin = new Vector2Int(
                spawnCell.x - def.Footprint.x / 2,
                spawnCell.y - def.Footprint.y / 2);
            // charge=false (free starting keep), requireConstruction=false (pre-built).
            _buildingPlacer.PlaceForce(def, keepOrigin, charge: false, requireConstruction: false, owner: owner);

            if (_goblinSpawner == null) return;

            // Spawn N starting Farmer Goblins evenly distributed around the keep
            _goblinSpawner.SpawnAroundFootprint(keepOrigin, def.Footprint, _startingGoblins, "FarmerGoblin", owner);

            // Test-spawn N Club Goblins one ring further out so you can test combat
            // immediately. Uses a padded virtual-footprint trick: treat keep+farmer-ring
            // as one big footprint, then Clubs spawn at ring 1 of that — i.e., 2 cells
            // out from the real keep, past the Farmers at 1 cell out.
            if (_testStartingClubs > 0)
            {
                var paddedOrigin = new Vector2Int(keepOrigin.x - 1, keepOrigin.y - 1);
                var paddedFootprint = new Vector2Int(def.Footprint.x + 2, def.Footprint.y + 2);
                _goblinSpawner.SpawnAroundFootprint(paddedOrigin, paddedFootprint, _testStartingClubs, "ClubGoblin", owner);
            }

            // Charge population for the local player's starting units.
            if (addPopulation)
            {
                int popPerUnit = _startingUnitDef != null ? _startingUnitDef.PopulationCost : 1;
                PopulationManager.AddUsed(_startingGoblins * popPerUnit);
                if (_testStartingClubs > 0)
                    PopulationManager.AddUsed(_testStartingClubs * _testClubPopCost);
            }
        }

        // Linear scan of BuildingCatalog.Buildings for a definition by asset name.
        // Called once per world; performance is not a concern.
        private BuildingDefinition FindBuildingDefinition(string name)
        {
            if (_catalog == null) return null;
            foreach (var b in _catalog.Buildings)
                if (b != null && b.name == name) return b;
            return null;
        }
    }
}

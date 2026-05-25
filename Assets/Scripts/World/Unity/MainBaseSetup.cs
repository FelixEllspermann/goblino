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

        [Header("Main Base")]
        [SerializeField] private string _mainBuildingName = "Keep_0";
        [SerializeField] private int _startingGoblins = 5;
        [SerializeField] private GoblinUnitDefinition _startingUnitDef;
        [Tooltip("Test-only: spawn N Club Goblins farther out from the keep so you can test combat at game start. Set to 0 to disable.")]
        [SerializeField] private int _testStartingClubs = 2;
        [Tooltip("Population cost charged per test Club (defaults to ClubGoblin's PopulationCost = 3)")]
        [SerializeField] private int _testClubPopCost = 3;

        // Subscribe to "world ready" by polling on Update — keeps things decoupled.
        private WorldData _knownWorld;

        private void Update()
        {
            if (_worldSource == null) return;
            var w = _worldSource.CurrentWorld;
            if (w == null || w == _knownWorld) return;
            _knownWorld = w;
            OnNewWorld(w);
        }

        private void OnNewWorld(WorldData world)
        {
            // 1. Wipe state from previous game
            if (_goblinSpawner != null) _goblinSpawner.ClearAllGoblins();
            if (_buildingPlacer != null) _buildingPlacer.ClearAllPlaced();
            ResourceBank.Reset();
            TreeHP.Clear();
            GoblinProduction.Clear();
            PopulationManager.Reset();

            if (world.Spawns == null || world.Spawns.Length == 0) return;

            // 2. Place the main building at spawn[0]
            var mainCell = world.Spawns[0];
            var def = FindBuildingDefinition(_mainBuildingName);
            if (def != null && _buildingPlacer != null)
            {
                // Offset so the keep is centered on the spawn cell
                var keepOrigin = new Vector2Int(
                    mainCell.x - def.Footprint.x / 2,
                    mainCell.y - def.Footprint.y / 2);
                _buildingPlacer.PlaceForce(def, keepOrigin, charge: false, requireConstruction: false);

                // 3. Spawn N starting Farmer Goblins evenly distributed around the keep
                if (_goblinSpawner != null)
                {
                    _goblinSpawner.SpawnAroundFootprint(keepOrigin, def.Footprint, _startingGoblins, "FarmerGoblin");
                    int popPerUnit = _startingUnitDef != null ? _startingUnitDef.PopulationCost : 1;
                    PopulationManager.AddUsed(_startingGoblins * popPerUnit);

                    // 4. Test-spawn N Club Goblins one ring further out so you can test combat
                    //    immediately. Uses a padded virtual-footprint trick: treat keep+farmer-ring
                    //    as one big footprint, then Clubs spawn at ring 1 of that — i.e., 2 cells
                    //    out from the real keep, past the Farmers at 1 cell out.
                    if (_testStartingClubs > 0)
                    {
                        var paddedOrigin = new Vector2Int(keepOrigin.x - 1, keepOrigin.y - 1);
                        var paddedFootprint = new Vector2Int(def.Footprint.x + 2, def.Footprint.y + 2);
                        _goblinSpawner.SpawnAroundFootprint(paddedOrigin, paddedFootprint, _testStartingClubs, "ClubGoblin");
                        PopulationManager.AddUsed(_testStartingClubs * _testClubPopCost);
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[MainBaseSetup] Building '{_mainBuildingName}' not in catalog.");
                // Fallback: no keep, spawn around mainCell
                if (_goblinSpawner != null)
                {
                    var c = new Vector3(mainCell.x + 0.5f, mainCell.y + 0.5f, 0f);
                    _goblinSpawner.SpawnGroupAt(c, _startingGoblins);
                }
            }
        }

        private BuildingDefinition FindBuildingDefinition(string name)
        {
            if (_catalog == null) return null;
            foreach (var b in _catalog.Buildings)
                if (b != null && b.name == name) return b;
            return null;
        }
    }
}

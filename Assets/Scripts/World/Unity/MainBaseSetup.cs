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

            if (world.Spawns == null || world.Spawns.Length == 0) return;

            // 2. Place the main building at spawn[0]
            var mainCell = world.Spawns[0];
            var def = FindBuildingDefinition(_mainBuildingName);
            if (def != null && _buildingPlacer != null)
            {
                // Offset so the keep is centered on the spawn cell
                var origin = new Vector2Int(
                    mainCell.x - def.Footprint.x / 2,
                    mainCell.y - def.Footprint.y / 2);
                _buildingPlacer.PlaceForce(def, origin);
            }
            else
            {
                Debug.LogWarning($"[MainBaseSetup] Building '{_mainBuildingName}' not in catalog.");
            }

            // 3. Spawn N starting goblins around the keep
            if (_goblinSpawner != null)
            {
                var center = new Vector3(mainCell.x + 0.5f, mainCell.y + 0.5f, 0f);
                _goblinSpawner.SpawnGroupAt(center, _startingGoblins);
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

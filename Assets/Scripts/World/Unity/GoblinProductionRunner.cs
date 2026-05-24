using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Ticks GoblinProduction each frame and spawns finished units near their keep.</summary>
    public sealed class GoblinProductionRunner : MonoBehaviour
    {
        [SerializeField] private BuildingPlacer _placer;
        [SerializeField] private GoblinSpawner _spawner;

        private void Update()
        {
            var done = GoblinProduction.Tick(Time.deltaTime);
            if (done == null) return;
            if (_placer == null || _spawner == null) return;

            foreach (var (origin, def) in done)
            {
                if (!_placer.TryGetBuildingAt(origin, out var building) || building == null) continue;
                // Spawn one unit on the closest passable cell around the keep footprint
                _spawner.SpawnByKindAroundFootprint(def.SpawnerKindName, origin, building.Footprint);
            }
        }
    }
}

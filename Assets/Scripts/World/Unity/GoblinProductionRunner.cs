// GoblinProductionRunner.cs — MonoBehaviour bridge between the static GoblinProduction timer store
// and the scene's GoblinSpawner. Attach to any persistent GameObject in SampleScene (e.g. GameManager).
// Wires: BuildingPlacer (to look up footprint + owner) and GoblinSpawner (to instantiate the unit).
// To redirect spawn logic or add post-spawn effects: extend the foreach block in Update().
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>
    /// Ticks GoblinProduction each frame and spawns finished units adjacent to their producing building.
    /// Requires BuildingPlacer and GoblinSpawner references in the Inspector.
    /// </summary>
    public sealed class GoblinProductionRunner : MonoBehaviour
    {
        [SerializeField] private BuildingPlacer _placer;
        [SerializeField] private GoblinSpawner _spawner;

        private void Update()
        {
            // Tick all active training jobs; returns non-null only when at least one finishes this frame.
            var done = GoblinProduction.Tick(Time.deltaTime);
            if (done == null) return;
            if (_placer == null || _spawner == null) return;

            foreach (var (origin, def, reservedIndex) in done)
            {
                // Look up the building footprint so the spawner knows how wide the structure is.
                if (!_placer.TryGetBuildingAt(origin, out var building) || building == null) continue;
                // Fall back to 0 (local/solo player) if the building has no registered owner.
                ulong owner = _placer.TryGetBuildingOwner(origin, out ulong o) ? o : 0UL;
                // Spawn one unit on the closest passable cell around the keep footprint.
                _spawner.SpawnByKindAroundFootprint(def.SpawnerKindName, origin, building.Footprint, owner, reservedIndex);
            }
        }
    }
}

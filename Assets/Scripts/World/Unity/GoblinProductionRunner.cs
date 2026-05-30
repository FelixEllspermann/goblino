// GoblinProductionRunner.cs — MonoBehaviour bridge between the static GoblinProduction timer store
// and the scene's GoblinSpawner. Attach to any persistent GameObject in SampleScene (e.g. GameManager).
// Wires: BuildingPlacer (to look up footprint + owner) and GoblinSpawner (to instantiate the unit).
// To redirect spawn logic or add post-spawn effects: extend the foreach block in Update().
using System.Collections.Generic;
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
                // Docks spawn boats on their water cell; everything else spawns at the CENTER of the
                // building footprint (e.g. middle of the 2×2 Keep), then walks out to the rally point.
                Goblin unit;
                if (DockRegistry.TryGetWaterCell(origin, out var waterCell))
                    unit = _spawner.SpawnKindAt(def.SpawnerKindName, new Vector3(waterCell.x + 0.5f, waterCell.y + 0.5f, 0f), owner);
                else
                {
                    Vector3 center = new(origin.x + building.Footprint.x * 0.5f, origin.y + building.Footprint.y * 0.5f, 0f);
                    unit = _spawner.SpawnKindAt(def.SpawnerKindName, center, owner, reservedIndex);
                }

                // Send the new unit to the building's rally point. Only the owner issues the move
                // (it broadcasts a normal CmdMove, so remotes mirror it). Spread so units don't stack.
                // Boats (water units) stay at the dock's water cell — don't rally them onto land.
                bool isLocal = owner == WorldStartContext.LocalPlayer || owner == 0UL;
                if (unit != null && isLocal && !def.WaterUnit && RallyPoints.TryGet(origin, out var rally))
                {
                    Vector3 dest = NearestFreeCell(rally, unit);
                    NetCommandIssuer.IssueMove(new List<Goblin> { unit }, dest);
                }
            }
        }

        // Spiral out from the rally cell; return the first cell-center with no OTHER live goblin within
        // ~0.6 units, so rallied units fan out instead of stacking. Falls back to the rally point.
        private static Vector3 NearestFreeCell(Vector3 rally, Goblin self)
        {
            int rx = Mathf.FloorToInt(rally.x), ry = Mathf.FloorToInt(rally.y);
            for (int ring = 0; ring <= 6; ring++)
            for (int dy = -ring; dy <= ring; dy++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue; // ring edge only
                var c = new Vector3(rx + dx + 0.5f, ry + dy + 0.5f, 0f);
                bool occupied = false;
                foreach (var g in Goblin.All)
                {
                    if (g == null || g == self) continue;
                    if ((g.transform.position - c).sqrMagnitude < 0.36f) { occupied = true; break; }
                }
                if (!occupied) return c;
            }
            return rally;
        }
    }
}

// ResourceCluster.cs — Data types describing resource deposits on the generated map.
// ResourcePlanner produces a list of ResourceClusters stored in WorldData.Resources.
// The Unity-side WorldPainter / MainBaseSetup reads these to place resource objects in the scene.
// To add a new resource type: extend ResourceType, then update ResourcePlanner and the Unity painter.

using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    /// <summary>Distinguishes what a resource cluster provides to the economy.</summary>
    public enum ResourceType
    {
        Stone,  // Provides building material (wood in current UI naming)
        Food,   // Provides food for unit training; only placed on Grassland / Forest tiles
    }

    /// <summary>An immutable cluster of same-type resource tiles placed by ResourcePlanner.
    /// Center is the seed tile; Cells contains every tile in the cluster (including Center).</summary>
    public readonly struct ResourceCluster
    {
        public readonly ResourceType Type;
        public readonly int2 Center;
        public readonly IReadOnlyList<int2> Cells;

        public ResourceCluster(ResourceType type, int2 center, IReadOnlyList<int2> cells)
        {
            Type = type;
            Center = center;
            Cells = cells;
        }
    }
}

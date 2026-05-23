using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public enum ResourceType
    {
        Stone,
        Food,
    }

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

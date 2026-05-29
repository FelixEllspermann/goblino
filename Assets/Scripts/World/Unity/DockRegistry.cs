// DockRegistry.cs — Maps each placed Dock (by its land-cell origin) to the adjacent WATER cell where
// its Docks_1 sprite sits and where boats it trains spawn. Cleared on a new world.
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-dock water-side cell (boat spawn point). Keyed by the dock's land origin.</summary>
    public static class DockRegistry
    {
        private static readonly Dictionary<Vector2Int, Vector2Int> _waterCell = new();

        public static void Set(Vector2Int origin, Vector2Int waterCell) => _waterCell[origin] = waterCell;
        public static bool TryGetWaterCell(Vector2Int origin, out Vector2Int waterCell) => _waterCell.TryGetValue(origin, out waterCell);
        public static void Remove(Vector2Int origin) => _waterCell.Remove(origin);
        public static void Clear() => _waterCell.Clear();
    }
}

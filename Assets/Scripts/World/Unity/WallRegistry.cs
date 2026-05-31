// WallRegistry.cs — global set of wall cells. Two jobs:
//   1. Pathing: Goblin.IsCellPassable treats a wall cell as un-walkable for land units.
//   2. Auto-tiling: each WallSegment recomputes its sprite from its 4 wall neighbours; placing/removing
//      a wall refreshes itself + its neighbours via RefreshAround.
// Reset on new world via MainBaseSetup.OnNewWorld → Clear().
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-cell registry of placed wall segments (movement-blocking + auto-tiling source).</summary>
    public static class WallRegistry
    {
        private static readonly Dictionary<Vector2Int, WallSegment> _walls = new();

        /// <summary>True if a wall occupies cell (x, y). Used by Goblin pathing to block land units.</summary>
        public static bool IsWall(int x, int y) => _walls.ContainsKey(new Vector2Int(x, y));

        public static void Register(Vector2Int cell, WallSegment seg) => _walls[cell] = seg;
        public static void Unregister(Vector2Int cell) => _walls.Remove(cell);

        /// <summary>Recompute the sprite of the wall at <paramref name="cell"/> and each of its 4 neighbours
        /// (their connectivity changed). Called when a wall is placed or removed.</summary>
        public static void RefreshAround(Vector2Int cell)
        {
            Recompute(cell);
            Recompute(new Vector2Int(cell.x, cell.y + 1));
            Recompute(new Vector2Int(cell.x, cell.y - 1));
            Recompute(new Vector2Int(cell.x + 1, cell.y));
            Recompute(new Vector2Int(cell.x - 1, cell.y));
        }

        private static void Recompute(Vector2Int cell)
        {
            if (_walls.TryGetValue(cell, out var seg) && seg != null) seg.RecomputeSprite();
        }

        /// <summary>Scale the max HP of every wall owned by <paramref name="owner"/> (Wall-HP upgrade,
        /// retroactive to walls already placed). Owner is read from each wall's BuildingOwner.</summary>
        public static void ApplyHpMultiplierForOwner(ulong owner, float factor)
        {
            foreach (var kv in _walls)
            {
                if (kv.Value == null) continue;
                var bo = kv.Value.GetComponent<BuildingOwner>();
                if (bo != null && bo.Owner == owner) BuildingHP.ScaleMax(kv.Key, factor);
            }
        }

        /// <summary>Wipe all wall cells. Called when a new world is generated.</summary>
        public static void Clear() => _walls.Clear();
    }
}

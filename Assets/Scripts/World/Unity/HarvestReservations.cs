using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Owner-local registry that assigns each harvesting goblin a distinct standing
    /// cell adjacent to a shared resource node, so farmers fan out instead of stacking.
    /// Reservations never affect resource totals (owner-authoritative); cross-client
    /// divergence is purely cosmetic.</summary>
    public static class HarvestReservations
    {
        private static readonly Dictionary<Goblin, (Vector3Int node, Vector2Int cell)> _byGoblin = new();
        private static readonly Dictionary<Vector3Int, HashSet<Vector2Int>> _takenByNode = new();

        private static readonly Vector3Int[] Offsets =
        {
            new( 1, 0, 0), new(-1, 0, 0), new( 0, 1, 0), new( 0,-1, 0),
            new( 1, 1, 0), new( 1,-1, 0), new(-1, 1, 0), new(-1,-1, 0),
        };

        /// <summary>Reserve the nearest free passable adjacent cell of <paramref name="node"/> for
        /// <paramref name="g"/>. Releases any prior reservation held by g first. Falls back to the
        /// closest passable adjacent cell (stacking) and finally the node center if none is passable.</summary>
        public static Vector2Int Reserve(Vector3Int node, Goblin g, Func<int, int, bool> passable, Vector3 from)
        {
            Release(g);

            if (!_takenByNode.TryGetValue(node, out var taken))
            {
                taken = new HashSet<Vector2Int>();
                _takenByNode[node] = taken;
            }

            Vector2Int bestFree = default; float bestFreeDist = float.MaxValue; bool foundFree = false;
            Vector2Int bestAny = default;  float bestAnyDist  = float.MaxValue; bool foundAny = false;

            foreach (var off in Offsets)
            {
                var nc = node + off;
                if (passable != null && !passable(nc.x, nc.y)) continue;
                var cell = new Vector2Int(nc.x, nc.y);
                float d = (new Vector3(nc.x + 0.5f, nc.y + 0.5f, 0f) - from).sqrMagnitude;
                if (d < bestAnyDist) { bestAnyDist = d; bestAny = cell; foundAny = true; }
                if (!taken.Contains(cell) && d < bestFreeDist) { bestFreeDist = d; bestFree = cell; foundFree = true; }
            }

            Vector2Int chosen;
            if (foundFree)      chosen = bestFree;
            else if (foundAny)  chosen = bestAny;          // all free taken → stack on closest passable
            else                chosen = new Vector2Int(node.x, node.y); // nothing passable → node center

            taken.Add(chosen);
            _byGoblin[g] = (node, chosen);
            return chosen;
        }

        /// <summary>Free whatever cell g currently holds.</summary>
        public static void Release(Goblin g)
        {
            if (g == null) return;
            if (!_byGoblin.TryGetValue(g, out var held)) return;
            _byGoblin.Remove(g);
            if (_takenByNode.TryGetValue(held.node, out var taken))
            {
                taken.Remove(held.cell);
                if (taken.Count == 0) _takenByNode.Remove(held.node);
            }
        }

        /// <summary>Wipe all reservations (new world).</summary>
        public static void Clear()
        {
            _byGoblin.Clear();
            _takenByNode.Clear();
        }
    }
}

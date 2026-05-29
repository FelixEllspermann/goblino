// =============================================================================
// HarvestReservations.cs  —  RTSCL.World.Unity
//
// Owner-local registry that fans multiple harvesters out around a shared
// resource node by assigning each a distinct adjacent standing cell.
// This is a cosmetic/pathing aid only — resource accounting is always
// authoritative on the owner client and never involves this registry.
//
// Ownership: runs on every client but only the local-owner client issues
// harvest commands; remote goblins simply path to the same tile-cell they
// received via CmdHarvest (they call Reserve too, but divergence is harmless
// because it only affects where they stand, not how much they collect).
//
// Clear() must be called by MainBaseSetup.OnNewWorld to wipe stale entries
// between games. Release() must be called whenever a goblin stops harvesting
// (new command, node depleted, death) to free its cell for others.
// =============================================================================
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
        // Maps each goblin to the (node tile-cell, reserved standing cell) it currently holds.
        private static readonly Dictionary<Goblin, (Vector3Int node, Vector2Int cell)> _byGoblin = new();
        // Maps each node tile-cell to the set of standing cells already reserved by other goblins.
        private static readonly Dictionary<Vector3Int, HashSet<Vector2Int>> _takenByNode = new();

        // 8-directional adjacency, tried in this order during Reserve to pick the nearest free spot.
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

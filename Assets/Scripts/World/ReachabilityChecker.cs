// ReachabilityChecker.cs — Final validation gate in the world generation pipeline.
// Flood-fills the passable land from spawn[0] and confirms every other spawn and every
// resource cluster center is reached. If not, WorldGenerator discards the map and retries.
// Note: Shore is passable here (unlike SpawnPlanner) so clusters near the coast are not rejected.

using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    /// <summary>Validates that all spawns and resource cluster centers are reachable from
    /// spawn[0] via 4-connected passable tiles. Used as a retry gate in WorldGenerator.</summary>
    public static class ReachabilityChecker
    {
        // Shore is passable here so coastal resource clusters are not incorrectly rejected.
        // (SpawnPlanner uses a stricter definition that excludes Shore for safety buffers.)
        private static bool IsPassable(Biome b) =>
            b != Biome.DeepWater && b != Biome.Cliff;

        /// <summary>Returns true when every spawn (index 1+) and every cluster center
        /// is connected to spawn[0] over passable tiles. Always returns true for single-spawn maps.</summary>
        public static bool AllReachable(Biome[,] biomes, int w, int h,
                                         int2[] spawns,
                                         IReadOnlyList<ResourceCluster> clusters)
        {
            if (spawns.Length == 0) return true;
            var reached = FloodFill(biomes, w, h, spawns[0]);
            for (int i = 1; i < spawns.Length; i++)
                if (!reached[spawns[i].x, spawns[i].y]) return false;
            foreach (var c in clusters)
                if (!reached[c.Center.x, c.Center.y]) return false;
            return true;
        }

        /// <summary>4-connected iterative flood fill starting at <paramref name="start"/>.
        /// Returns a boolean reachability grid — true means the tile was reached.
        /// Returns an all-false grid if the start tile itself is impassable.</summary>
        private static bool[,] FloodFill(Biome[,] biomes, int w, int h, int2 start)
        {
            var reached = new bool[w, h];
            if (!IsPassable(biomes[start.x, start.y])) return reached;
            var stack = new Stack<int2>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p.x < 0 || p.x >= w || p.y < 0 || p.y >= h) continue;
                if (reached[p.x, p.y]) continue;
                if (!IsPassable(biomes[p.x, p.y])) continue;
                reached[p.x, p.y] = true;
                stack.Push(new int2(p.x + 1, p.y));
                stack.Push(new int2(p.x - 1, p.y));
                stack.Push(new int2(p.x, p.y + 1));
                stack.Push(new int2(p.x, p.y - 1));
            }
            return reached;
        }
    }
}

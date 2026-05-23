using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public static class ReachabilityChecker
    {
        private static bool IsPassable(Biome b) =>
            b != Biome.DeepWater && b != Biome.Cliff;

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

using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public static class ContinentShaper
    {
        public static void ApplyFalloff(float[,] elevation, int w, int h, float falloffStrength)
        {
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float nx = (x / (float)(w - 1)) * 2f - 1f;
                float ny = (y / (float)(h - 1)) * 2f - 1f;
                float d2 = nx * nx + ny * ny;
                float falloff = math.saturate(1f - d2 * falloffStrength);
                elevation[x, y] *= falloff;
            }
        }

        private static bool IsLand(Biome b) =>
            b != Biome.DeepWater && b != Biome.Shore;

        public static void RemoveMiniIslands(Biome[,] biomes, int w, int h, int threshold)
        {
            var visited = new bool[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (visited[x, y]) continue;
                if (!IsLand(biomes[x, y])) { visited[x, y] = true; continue; }

                var component = FloodFill(biomes, visited, w, h, x, y, IsLand);
                if (component.Count < threshold)
                    foreach (var c in component)
                        biomes[c.x, c.y] = Biome.DeepWater;
            }
        }

        public static void FillMiniLakes(Biome[,] biomes, int w, int h, int threshold)
        {
            var visited = new bool[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (visited[x, y]) continue;
                if (biomes[x, y] != Biome.DeepWater) { visited[x, y] = true; continue; }

                var component = FloodFill(biomes, visited, w, h, x, y,
                                          b => b == Biome.DeepWater);
                if (component.Count >= threshold) continue;

                // Touches edge → it's the ocean, do not fill
                bool touchesEdge = false;
                foreach (var c in component)
                    if (c.x == 0 || c.y == 0 || c.x == w - 1 || c.y == h - 1)
                    { touchesEdge = true; break; }
                if (touchesEdge) continue;

                foreach (var c in component)
                    biomes[c.x, c.y] = Biome.Grassland;
            }
        }

        private static List<int2> FloodFill(Biome[,] biomes, bool[,] visited,
                                            int w, int h, int startX, int startY,
                                            System.Func<Biome, bool> predicate)
        {
            var result = new List<int2>();
            var stack = new Stack<int2>();
            stack.Push(new int2(startX, startY));
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p.x < 0 || p.x >= w || p.y < 0 || p.y >= h) continue;
                if (visited[p.x, p.y]) continue;
                if (!predicate(biomes[p.x, p.y])) continue;
                visited[p.x, p.y] = true;
                result.Add(p);
                stack.Push(new int2(p.x + 1, p.y));
                stack.Push(new int2(p.x - 1, p.y));
                stack.Push(new int2(p.x, p.y + 1));
                stack.Push(new int2(p.x, p.y - 1));
            }
            return result;
        }
    }
}

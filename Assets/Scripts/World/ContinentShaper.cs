// ContinentShaper.cs — Post-processes the raw elevation field and biome grid to produce
// a natural-looking continent shape. Two steps run in order:
//   1. ApplyFalloff  — radial vignette on raw elevation, driving map edges toward water.
//   2. RemoveMiniIslands / FillMiniLakes — flood-fill cleanup after biome classification.
// Tune FalloffStrength, MiniIslandRemovalThreshold, LakeFillThreshold in WorldGenConfig.

using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    /// <summary>Static helpers that shape the continental outline of a generated map.</summary>
    public static class ContinentShaper
    {
        /// <summary>Multiplies each elevation value by a radial falloff factor so map edges
        /// are pushed below the water threshold, creating a surrounded-by-ocean feel.
        /// <paramref name="falloffStrength"/> > 1 makes the continent smaller; tune in WorldGenConfig.</summary>
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

        // Shore is treated as water for island-removal purposes so coastal fringes don't anchor tiny islands.
        private static bool IsLand(Biome b) =>
            b != Biome.DeepWater && b != Biome.Shore;

        /// <summary>Flood-fills every land-connected component; any component smaller than
        /// <paramref name="threshold"/> tiles is converted to DeepWater (mini-island removal).</summary>
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

        /// <summary>Flood-fills enclosed water pockets smaller than <paramref name="threshold"/> tiles
        /// and converts them to Grassland. Water pools that touch the map edge are left intact
        /// (they are part of the ocean, not landlocked lakes).</summary>
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

        /// <summary>Iterative 4-connected flood fill (stack-based to avoid recursion limits).
        /// Collects all contiguous tiles matching <paramref name="predicate"/> into a list
        /// and marks them visited so the outer scan does not re-process them.</summary>
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

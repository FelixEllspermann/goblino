using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public static class SpawnPlanner
    {
        private static bool IsImpassable(Biome b) =>
            b == Biome.DeepWater || b == Biome.Cliff || b == Biome.Shore;

        private static bool IsEligibleStart(Biome b) =>
            b == Biome.Grassland || b == Biome.Forest;

        public static int2[] PlaceSpawns(Biome[,] biomes, int w, int h, int playerCount,
                                          int buffer, int candidatesPerSpawn,
                                          ref Random rng)
        {
            var candidates = new List<int2>();
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (!IsEligibleStart(biomes[x, y])) continue;
                if (!HasBuffer(biomes, w, h, x, y, buffer)) continue;
                candidates.Add(new int2(x, y));
            }

            if (candidates.Count == 0)
                return new int2[0];

            // Reservoir-like sample: pick up to playerCount*candidatesPerSpawn from candidates
            int sampleCount = math.min(playerCount * candidatesPerSpawn, candidates.Count);
            var sample = new List<int2>(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                int idx = rng.NextInt(0, candidates.Count);
                sample.Add(candidates[idx]);
            }

            // Greedy farthest-point sampling
            var chosen = new List<int2>(playerCount);
            chosen.Add(sample[rng.NextInt(0, sample.Count)]);
            while (chosen.Count < playerCount && sample.Count > 0)
            {
                int2 best = sample[0];
                int bestMinDist = -1;
                foreach (var cand in sample)
                {
                    int minDist = int.MaxValue;
                    foreach (var c in chosen)
                    {
                        int dx = cand.x - c.x;
                        int dy = cand.y - c.y;
                        int d = dx * dx + dy * dy;
                        if (d < minDist) minDist = d;
                    }
                    if (minDist > bestMinDist) { bestMinDist = minDist; best = cand; }
                }
                chosen.Add(best);
            }
            return chosen.ToArray();
        }

        private static bool HasBuffer(Biome[,] biomes, int w, int h,
                                      int x, int y, int buffer)
        {
            if (buffer <= 0) return true;
            for (int dx = -buffer; dx <= buffer; dx++)
            for (int dy = -buffer; dy <= buffer; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) return false;
                if (IsImpassable(biomes[nx, ny])) return false;
            }
            return true;
        }
    }
}

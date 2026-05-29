// SpawnPlanner.cs — Selects player starting positions on the generated biome grid.
// Strategy: collect all eligible (Grassland/Forest) tiles with an impassable-free buffer zone,
// take a random sample from them, then use greedy farthest-point selection to spread spawns apart.
// Tune spawn quality via SpawnBufferToImpassable and CandidatesPerSpawn in WorldGenConfig.

using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    /// <summary>Places <c>playerCount</c> spawn points that are maximally spread across
    /// eligible land tiles, guaranteeing a clear buffer around each starting position.</summary>
    public static class SpawnPlanner
    {
        // Shore is treated as impassable so spawns can't be placed right on the waterline.
        private static bool IsImpassable(Biome b) =>
            b == Biome.DeepWater || b == Biome.Cliff || b == Biome.Shore;

        // Only Grassland/Forest are considered valid starting biomes (trees + space to build).
        private static bool IsEligibleStart(Biome b) =>
            b == Biome.Grassland || b == Biome.Forest;

        /// <summary>Returns up to <paramref name="playerCount"/> spawn positions; may return fewer
        /// (even empty) if the map has insufficient eligible area — WorldGenerator treats this as a
        /// failed attempt and retries with a different seed.</summary>
        /// <param name="buffer">Minimum impassable-free radius around each candidate tile.</param>
        /// <param name="candidatesPerSpawn">How many random candidates to evaluate per player slot.</param>
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

            // Greedy farthest-point sampling: each new spawn maximises the minimum
            // squared distance to any already-chosen spawn, spreading players apart.
            var chosen = new List<int2>(playerCount);
            chosen.Add(sample[rng.NextInt(0, sample.Count)]);
            while (chosen.Count < playerCount && sample.Count > 0)
            {
                int2 best = sample[0];
                int bestMinDist = -1;
                foreach (var cand in sample)
                {
                    // Find the closest already-chosen spawn to this candidate.
                    int minDist = int.MaxValue;
                    foreach (var c in chosen)
                    {
                        int dx = cand.x - c.x;
                        int dy = cand.y - c.y;
                        int d = dx * dx + dy * dy;  // squared distance — no sqrt needed for comparison
                        if (d < minDist) minDist = d;
                    }
                    // Pick the candidate whose nearest neighbour is farthest away.
                    if (minDist > bestMinDist) { bestMinDist = minDist; best = cand; }
                }
                chosen.Add(best);
            }
            return chosen.ToArray();
        }

        /// <summary>Returns true only when every tile within the square [x±buffer, y±buffer]
        /// is in-bounds and passable — ensures the spawn has open space to place a Keep and units.</summary>
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

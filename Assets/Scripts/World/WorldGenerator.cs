using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public sealed class WorldGenerator
    {
        public WorldData Generate(int seed, WorldGenConfig cfg)
        {
            uint baseSeed = unchecked((uint)seed);
            if (baseSeed == 0) baseSeed = 1u;

            for (int attempt = 0; attempt < cfg.MaxRegenerationAttempts; attempt++)
            {
                uint trySeed = baseSeed + (uint)attempt;
                var result = TryGenerate(trySeed, cfg);
                if (result != null) return result;
            }
            throw new InvalidOperationException(
                $"WorldGenerator failed reachability after {cfg.MaxRegenerationAttempts} attempts " +
                $"(base seed {baseSeed}). Tune WorldGenConfig.");
        }

        private static WorldData TryGenerate(uint seed, WorldGenConfig cfg)
        {
            int w = cfg.Width, h = cfg.Height;

            // 1. Noise channels
            var elevField   = new NoiseField(seed, channel: 0, cfg.ElevationScale);
            var moistField  = new NoiseField(seed, channel: 1, cfg.MoistureScale);
            var tempField   = new NoiseField(seed, channel: 2, cfg.TemperatureScale);

            var elevation   = new float[w, h];
            var moisture    = new float[w, h];
            var temperature = new float[w, h];

            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                elevation[x, y]   = elevField.Sample(x, y);
                moisture[x, y]    = moistField.Sample(x, y);
                float t = tempField.Sample(x, y) * 0.6f;
                float latBias = math.abs(((float)y / h) - 0.5f) * 2f * 0.4f;
                temperature[x, y] = math.saturate(t + latBias);
            }

            // 2. Continental falloff
            ContinentShaper.ApplyFalloff(elevation, w, h, cfg.FalloffStrength);

            // 3. First-pass classification (without TropicalCoast water-proximity)
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                biomes[x, y] = BiomeClassifier.Classify(
                    elevation[x, y], moisture[x, y], temperature[x, y],
                    hasNearbyWater: false, cfg);

            // 4. Second pass — upgrade hot+moist land near water to TropicalCoast
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (biomes[x, y] != Biome.Grassland && biomes[x, y] != Biome.Forest) continue;
                if (temperature[x, y] <= cfg.TropicalMin) continue;
                if (HasWaterWithinManhattan(biomes, w, h, x, y, radius: 2))
                    biomes[x, y] = Biome.TropicalCoast;
            }

            // 5. Post-processing
            ContinentShaper.RemoveMiniIslands(biomes, w, h, cfg.MiniIslandRemovalThreshold);
            ContinentShaper.FillMiniLakes(biomes, w, h, cfg.LakeFillThreshold);

            // 6. Spawn placement
            var rng = new Random(seed);
            // burn a few values to decorrelate from noise offsets
            rng.NextUInt(); rng.NextUInt();
            var spawns = SpawnPlanner.PlaceSpawns(biomes, w, h, cfg.PlayerCount,
                                                    cfg.SpawnBufferToImpassable,
                                                    cfg.CandidatesPerSpawn, ref rng);
            if (spawns.Length < cfg.PlayerCount) return null;

            // 7. Reserve spawn areas (set decoration-mask later in DecorationPlacer)

            // 8. Resource clusters
            var clusters = ResourcePlanner.PlaceClusters(biomes, w, h, spawns, cfg, ref rng);

            // 9. Reachability gate
            if (!ReachabilityChecker.AllReachable(biomes, w, h, spawns, clusters))
                return null;

            return new WorldData(w, h, biomes, spawns, clusters, seed);
        }

        private static bool HasWaterWithinManhattan(Biome[,] biomes, int w, int h,
                                                     int cx, int cy, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (math.abs(dx) + math.abs(dy) > radius) continue;
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (biomes[nx, ny] == Biome.DeepWater || biomes[nx, ny] == Biome.Shore)
                    return true;
            }
            return false;
        }
    }
}

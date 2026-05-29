// Tests for WorldGenerator (RTSCL.World assembly) — the top-level pipeline that combines
// NoiseField, ContinentShaper, BiomeClassifier, SpawnPlanner, ResourcePlanner, and
// ReachabilityChecker into a complete WorldData result.
// Integration-level tests: assert properties of the full output rather than individual subsystems.
using System.Collections.Generic;
using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class WorldGeneratorTests
    {
        // The entire pipeline must be deterministic: the same seed must produce a bit-identical biome grid.
        // This is the core multiplayer invariant — all clients must converge on the same map.
        [Test]
        public void Generate_SameSeed_ProducesIdenticalBiomes()
        {
            var cfg = new WorldGenConfig { Width = 64, Height = 64, PlayerCount = 2 };
            var w1 = new WorldGenerator().Generate(seed: 12345, cfg);
            var w2 = new WorldGenerator().Generate(seed: 12345, cfg);
            for (int x = 0; x < cfg.Width; x++)
            for (int y = 0; y < cfg.Height; y++)
                Assert.AreEqual(w1.Biomes[x, y], w2.Biomes[x, y],
                                $"divergence at ({x},{y})");
        }

        // Different seeds must produce meaningfully different worlds (not a constant map).
        // Threshold of 100 diverging cells is conservative — real maps diverge in thousands.
        [Test]
        public void Generate_DifferentSeed_ProducesDifferentBiomes()
        {
            var cfg = new WorldGenConfig { Width = 64, Height = 64, PlayerCount = 2 };
            var w1 = new WorldGenerator().Generate(seed: 1, cfg);
            var w2 = new WorldGenerator().Generate(seed: 2, cfg);

            int differing = 0;
            for (int x = 0; x < cfg.Width; x++)
            for (int y = 0; y < cfg.Height; y++)
                if (w1.Biomes[x, y] != w2.Biomes[x, y]) differing++;
            Assert.Greater(differing, 100, "two different seeds should diverge meaningfully");
        }

        // WorldData.Spawns must contain exactly as many entries as PlayerCount requests.
        [Test]
        public void Generate_ProducesRequestedSpawnCount()
        {
            var cfg = new WorldGenConfig { Width = 96, Height = 96, PlayerCount = 4 };
            var w = new WorldGenerator().Generate(seed: 999, cfg);
            Assert.AreEqual(4, w.Spawns.Length);
        }

        // The generator must reject maps where any spawn or resource is unreachable from the others;
        // a valid generated world must pass the ReachabilityChecker's flood-fill.
        [Test]
        public void Generate_AllSpawnsReachableFromEachOther()
        {
            var cfg = new WorldGenConfig { Width = 96, Height = 96, PlayerCount = 4 };
            var w = new WorldGenerator().Generate(seed: 555, cfg);
            Assert.IsTrue(ReachabilityChecker.AllReachable(w.Biomes, w.Width, w.Height,
                                                            w.Spawns, w.Resources));
        }

        // The continental falloff shaper must produce both a meaningful ocean and meaningful landmass,
        // confirming that neither the entire map became water nor all-land.
        [Test]
        public void Generate_ProducesAtLeastSomeWaterAndSomeLand()
        {
            var cfg = new WorldGenConfig { Width = 96, Height = 96, PlayerCount = 2 };
            var w = new WorldGenerator().Generate(seed: 7, cfg);
            int water = 0, land = 0;
            for (int x = 0; x < w.Width; x++)
            for (int y = 0; y < w.Height; y++)
            {
                if (w.Biomes[x, y] == Biome.DeepWater) water++;
                else land++;
            }
            Assert.Greater(water, 100, "expected meaningful coastline");
            Assert.Greater(land, 100, "expected meaningful land");
        }
    }
}

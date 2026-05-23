using System.Collections.Generic;
using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class WorldGeneratorTests
    {
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

        [Test]
        public void Generate_ProducesRequestedSpawnCount()
        {
            var cfg = new WorldGenConfig { Width = 96, Height = 96, PlayerCount = 4 };
            var w = new WorldGenerator().Generate(seed: 999, cfg);
            Assert.AreEqual(4, w.Spawns.Length);
        }

        [Test]
        public void Generate_AllSpawnsReachableFromEachOther()
        {
            var cfg = new WorldGenConfig { Width = 96, Height = 96, PlayerCount = 4 };
            var w = new WorldGenerator().Generate(seed: 555, cfg);
            Assert.IsTrue(ReachabilityChecker.AllReachable(w.Biomes, w.Width, w.Height,
                                                            w.Spawns, w.Resources));
        }

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

using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class ContinentShaperTests
    {
        [Test]
        public void Falloff_CenterUnchanged_EdgesPushedToZero()
        {
            int w = 64, h = 64;
            var elevation = new float[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                elevation[x, y] = 1f;

            ContinentShaper.ApplyFalloff(elevation, w, h, falloffStrength: 1.2f);

            // Center should still be ~1
            Assert.Greater(elevation[w / 2, h / 2], 0.95f);
            // Corners should be ~0
            Assert.Less(elevation[0, 0], 0.1f);
            Assert.Less(elevation[w - 1, h - 1], 0.1f);
        }

        [Test]
        public void RemoveMiniIslands_SmallIslandBecomesWater()
        {
            int w = 16, h = 16;
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                biomes[x, y] = Biome.DeepWater;

            // Place a 3-cell "island" at (5,5),(5,6),(6,5) — under threshold 10
            biomes[5, 5] = Biome.Grassland;
            biomes[5, 6] = Biome.Grassland;
            biomes[6, 5] = Biome.Grassland;

            ContinentShaper.RemoveMiniIslands(biomes, w, h, threshold: 10);

            Assert.AreEqual(Biome.DeepWater, biomes[5, 5]);
            Assert.AreEqual(Biome.DeepWater, biomes[5, 6]);
            Assert.AreEqual(Biome.DeepWater, biomes[6, 5]);
        }

        [Test]
        public void FillMiniLakes_SmallEnclosedWaterBecomesGrassland()
        {
            int w = 16, h = 16;
            var biomes = new Biome[w, h];
            // Fill with land
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                biomes[x, y] = Biome.Grassland;
            // Carve a 2-cell lake in the middle
            biomes[8, 8] = Biome.DeepWater;
            biomes[8, 9] = Biome.DeepWater;

            ContinentShaper.FillMiniLakes(biomes, w, h, threshold: 5);

            Assert.AreEqual(Biome.Grassland, biomes[8, 8]);
            Assert.AreEqual(Biome.Grassland, biomes[8, 9]);
        }

        [Test]
        public void FillMiniLakes_OceanNotFilled()
        {
            int w = 8, h = 8;
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                biomes[x, y] = Biome.DeepWater;
            biomes[3, 3] = Biome.Grassland;

            // The ocean touches the map edge — must NOT be filled even if "small"
            ContinentShaper.FillMiniLakes(biomes, w, h, threshold: 999);

            Assert.AreEqual(Biome.DeepWater, biomes[0, 0]);
        }
    }
}

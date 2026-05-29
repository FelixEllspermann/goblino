// Tests for ContinentShaper (RTSCL.World assembly).
// Covers the three post-processing passes: radial falloff (keeps map interior as land, forces
// corners to water), mini-island removal (flood-fill below threshold → water), and
// mini-lake filling (enclosed water regions below threshold → nearest land biome).
using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class ContinentShaperTests
    {
        // ApplyFalloff must preserve high elevation at the map center while crushing corners to near-zero.
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

        // A connected land region smaller than the threshold must be flood-filled back to DeepWater,
        // preventing unplayable micro-islands in the ocean.
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

        // A small enclosed water pocket that does not touch the map edge must be filled to land,
        // eliminating landlocked puddles that would block unit movement.
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

        // Water connected to the map border is the open ocean and must never be filled,
        // even with an arbitrarily large threshold.
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

using System.Collections.Generic;
using NUnit.Framework;
using RTSCL.World;
using Unity.Mathematics;

namespace RTSCL.World.Tests
{
    public class ReachabilityCheckerTests
    {
        [Test]
        public void AllReachable_OnOpenMap_ReturnsTrue()
        {
            int w = 16, h = 16;
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++) biomes[x, y] = Biome.Grassland;

            var spawns = new[] { new int2(2, 2), new int2(13, 13) };
            var clusters = new List<ResourceCluster>
            {
                new ResourceCluster(ResourceType.Stone, new int2(8, 8), new[] { new int2(8, 8) })
            };

            Assert.IsTrue(ReachabilityChecker.AllReachable(biomes, w, h, spawns, clusters));
        }

        [Test]
        public void SplitByWaterWall_ReturnsFalse()
        {
            int w = 16, h = 16;
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++) biomes[x, y] = Biome.Grassland;
            // Vertical water wall at x=8
            for (int y = 0; y < h; y++) biomes[8, y] = Biome.DeepWater;

            var spawns = new[] { new int2(2, 2), new int2(13, 13) };
            Assert.IsFalse(ReachabilityChecker.AllReachable(biomes, w, h, spawns,
                                                            new List<ResourceCluster>()));
        }

        [Test]
        public void ResourceOnCliff_ReturnsFalse()
        {
            int w = 8, h = 8;
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++) biomes[x, y] = Biome.Grassland;

            var spawns = new[] { new int2(0, 0) };
            // The cluster center is on a cliff cell, which is impassable
            biomes[4, 4] = Biome.Cliff;
            var clusters = new List<ResourceCluster>
            {
                new ResourceCluster(ResourceType.Stone, new int2(4, 4), new[] { new int2(4, 4) })
            };
            Assert.IsFalse(ReachabilityChecker.AllReachable(biomes, w, h, spawns, clusters));
        }
    }
}

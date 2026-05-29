// Tests for ResourcePlanner (RTSCL.World assembly).
// Verifies that resource cluster placement honours the configured per-spawn and free-roam counts,
// produces both Stone and Food types, and is fully deterministic given identical RNG seeds.
using System.Collections.Generic;
using NUnit.Framework;
using RTSCL.World;
using Unity.Mathematics;

namespace RTSCL.World.Tests
{
    public class ResourcePlannerTests
    {
        private static Biome[,] MakeGrassland(int w, int h)
        {
            var b = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                b[x, y] = Biome.Grassland;
            return b;
        }

        // Total cluster count must equal (spawns × (stone + food) per spawn) + free-roam count.
        // Config pins both per-spawn and free-roam values to fixed numbers for an exact assertion.
        [Test]
        public void PlaceClusters_ProducesPerSpawnAndFreeRoam()
        {
            var biomes = MakeGrassland(64, 64);
            var spawns = new[] { new int2(16, 16), new int2(48, 48) };
            var cfg = new WorldGenConfig
            {
                StoneClustersPerSpawn = 2,
                FoodClustersPerSpawn = 2,
                FreeRoamClusterCountMin = 6,
                FreeRoamClusterCountMax = 6,
                ClusterSizeMin = 3, ClusterSizeMax = 3,
                ClusterRadiusMin = 6, ClusterRadiusMax = 10,
            };
            var rng = new Random(11);
            var clusters = ResourcePlanner.PlaceClusters(biomes, 64, 64, spawns, cfg, ref rng);
            // 2 spawns * (2 stone + 2 food) = 8, plus 6 free-roam = 14
            Assert.AreEqual(14, clusters.Count);
        }

        // Both Stone and Food ResourceTypes must appear in the output so the economy has both axes.
        [Test]
        public void PlaceClusters_StoneAndFoodBothRepresented()
        {
            var biomes = MakeGrassland(64, 64);
            var spawns = new[] { new int2(32, 32) };
            var rng = new Random(5);
            var clusters = ResourcePlanner.PlaceClusters(biomes, 64, 64, spawns,
                                                          new WorldGenConfig(), ref rng);
            bool hasStone = false, hasFood = false;
            foreach (var c in clusters)
            {
                if (c.Type == ResourceType.Stone) hasStone = true;
                if (c.Type == ResourceType.Food) hasFood = true;
            }
            Assert.IsTrue(hasStone);
            Assert.IsTrue(hasFood);
        }

        // Identical RNG seeds must produce clusters in the same order with identical types and centres —
        // required for multiplayer map parity across all clients.
        [Test]
        public void PlaceClusters_Determinism()
        {
            var biomes = MakeGrassland(48, 48);
            var spawns = new[] { new int2(10, 10), new int2(35, 35) };
            var cfg = new WorldGenConfig();
            var rng1 = new Random(2026);
            var rng2 = new Random(2026);
            var a = ResourcePlanner.PlaceClusters(biomes, 48, 48, spawns, cfg, ref rng1);
            var b = ResourcePlanner.PlaceClusters(biomes, 48, 48, spawns, cfg, ref rng2);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Type, b[i].Type);
                Assert.AreEqual(a[i].Center.x, b[i].Center.x);
                Assert.AreEqual(a[i].Center.y, b[i].Center.y);
            }
        }
    }
}

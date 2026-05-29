// Tests for SpawnPlanner (RTSCL.World assembly).
// Verifies that player spawn points are: placed in the requested quantity, located only on eligible
// land biomes (Grassland or Forest), reproducible from the same RNG seed, and separated from water
// by the requested buffer distance.
using NUnit.Framework;
using RTSCL.World;
using Unity.Mathematics;

namespace RTSCL.World.Tests
{
    public class SpawnPlannerTests
    {
        private static Biome[,] MakeAllGrassland(int w, int h)
        {
            var b = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                b[x, y] = Biome.Grassland;
            return b;
        }

        // PlaceSpawns must return exactly as many spawn points as there are players.
        [Test]
        public void PlaceSpawns_OnUniformGrass_ReturnsRequestedCount()
        {
            var biomes = MakeAllGrassland(64, 64);
            var rng = new Random(42);
            var spawns = SpawnPlanner.PlaceSpawns(biomes, 64, 64, playerCount: 4,
                                                  buffer: 0, candidatesPerSpawn: 100, ref rng);
            Assert.AreEqual(4, spawns.Length);
        }

        // Every returned spawn cell must fall on a passable land biome (Grassland or Forest).
        [Test]
        public void PlaceSpawns_AllSpawnsOnEligibleCells()
        {
            var biomes = MakeAllGrassland(32, 32);
            var rng = new Random(7);
            var spawns = SpawnPlanner.PlaceSpawns(biomes, 32, 32, playerCount: 3,
                                                  buffer: 0, candidatesPerSpawn: 100, ref rng);
            foreach (var s in spawns)
            {
                Assert.That(biomes[s.x, s.y], Is.EqualTo(Biome.Grassland)
                                                    .Or.EqualTo(Biome.Forest));
            }
        }

        // Identical RNG seeds must produce identical spawn positions — required for multiplayer map parity.
        [Test]
        public void PlaceSpawns_Determinism()
        {
            var biomes = MakeAllGrassland(48, 48);
            var rng1 = new Random(123);
            var rng2 = new Random(123);
            var a = SpawnPlanner.PlaceSpawns(biomes, 48, 48, 2, 0, 50, ref rng1);
            var b = SpawnPlanner.PlaceSpawns(biomes, 48, 48, 2, 0, 50, ref rng2);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].x, b[i].x);
                Assert.AreEqual(a[i].y, b[i].y);
            }
        }

        // The buffer parameter must exclude any cell within 'buffer' cells of a water tile so that
        // keeps cannot be placed right at the water's edge.
        [Test]
        public void PlaceSpawns_BufferKeepsAwayFromWater()
        {
            int w = 32, h = 32;
            var biomes = MakeAllGrassland(w, h);
            // Make left column water
            for (int y = 0; y < h; y++) biomes[0, y] = Biome.DeepWater;

            var rng = new Random(99);
            var spawns = SpawnPlanner.PlaceSpawns(biomes, w, h, playerCount: 2,
                                                  buffer: 4, candidatesPerSpawn: 100, ref rng);
            foreach (var s in spawns)
                Assert.GreaterOrEqual(s.x, 4, "spawn must be >=4 cells from water column");
        }
    }
}

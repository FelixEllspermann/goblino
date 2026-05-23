using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class BiomeClassifierTests
    {
        private static WorldGenConfig DefaultConfig() => new WorldGenConfig();

        [Test]
        public void LowElevation_ReturnsDeepWater()
        {
            var b = BiomeClassifier.Classify(0.10f, m: 0.5f, t: 0.5f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.DeepWater, b);
        }

        [Test]
        public void ShoreRange_ReturnsShore()
        {
            var b = BiomeClassifier.Classify(0.35f, m: 0.5f, t: 0.5f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Shore, b);
        }

        [Test]
        public void HighElevation_ReturnsCliff()
        {
            var b = BiomeClassifier.Classify(0.90f, m: 0.5f, t: 0.5f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Cliff, b);
        }

        [Test]
        public void LowTemperature_ReturnsSnow()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.5f, t: 0.10f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Snow, b);
        }

        [Test]
        public void HotAndDry_ReturnsDesert()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.20f, t: 0.80f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Desert, b);
        }

        [Test]
        public void HotMoistNearWater_ReturnsTropicalCoast()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.60f, t: 0.80f, hasNearbyWater: true,
                                             DefaultConfig());
            Assert.AreEqual(Biome.TropicalCoast, b);
        }

        [Test]
        public void HotMoistFarFromWater_FallsBackToGrasslandOrForest()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.50f, t: 0.80f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreNotEqual(Biome.TropicalCoast, b);
        }

        [Test]
        public void HighMoisture_ReturnsForest()
        {
            var b = BiomeClassifier.Classify(0.60f, m: 0.80f, t: 0.50f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Forest, b);
        }

        [Test]
        public void MidMoisture_ReturnsGrassland()
        {
            var b = BiomeClassifier.Classify(0.60f, m: 0.50f, t: 0.50f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Grassland, b);
        }

        [Test]
        public void LowMoisture_ReturnsDryGrass()
        {
            var b = BiomeClassifier.Classify(0.60f, m: 0.10f, t: 0.50f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.DryGrass, b);
        }
    }
}

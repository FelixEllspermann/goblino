// Tests for BiomeClassifier (RTSCL.World assembly).
// Each test exercises a single branch of the elevation/moisture/temperature decision tree,
// confirming that the thresholds defined in WorldGenConfig map to the expected Biome enum value.
// All tests use the default WorldGenConfig to reflect production thresholds.
using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class BiomeClassifierTests
    {
        private static WorldGenConfig DefaultConfig() => new WorldGenConfig();

        // Very low elevation (0.10) must always produce DeepWater regardless of moisture/temperature.
        [Test]
        public void LowElevation_ReturnsDeepWater()
        {
            var b = BiomeClassifier.Classify(0.10f, m: 0.5f, t: 0.5f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.DeepWater, b);
        }

        // Elevation in the transitional shore band must yield Shore (the coastal walkable strip).
        [Test]
        public void ShoreRange_ReturnsShore()
        {
            // Pick the midpoint of the config's shore band so the test tracks the configured thresholds
            // rather than a brittle literal (the band shifts when DeepWaterMax/ShoreMax are tuned).
            var cfg = DefaultConfig();
            float e = (cfg.DeepWaterMax + cfg.ShoreMax) * 0.5f;
            var b = BiomeClassifier.Classify(e, m: 0.5f, t: 0.5f, hasNearbyWater: false, cfg);
            Assert.AreEqual(Biome.Shore, b);
        }

        // Very high elevation (0.90) must produce Cliff — an impassable terrain type.
        [Test]
        public void HighElevation_ReturnsCliff()
        {
            var b = BiomeClassifier.Classify(0.90f, m: 0.5f, t: 0.5f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Cliff, b);
        }

        // Low temperature (0.10) at mid elevation overrides moisture and must produce Snow.
        [Test]
        public void LowTemperature_ReturnsSnow()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.5f, t: 0.10f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Snow, b);
        }

        // High temperature + low moisture at mid elevation must produce Desert.
        [Test]
        public void HotAndDry_ReturnsDesert()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.20f, t: 0.80f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Desert, b);
        }

        // TropicalCoast requires hot+moist conditions AND an adjacent water cell (hasNearbyWater=true).
        [Test]
        public void HotMoistNearWater_ReturnsTropicalCoast()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.60f, t: 0.80f, hasNearbyWater: true,
                                             DefaultConfig());
            Assert.AreEqual(Biome.TropicalCoast, b);
        }

        // Without nearby water the TropicalCoast condition must not fire; cell falls back to a land biome.
        [Test]
        public void HotMoistFarFromWater_FallsBackToGrasslandOrForest()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.50f, t: 0.80f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreNotEqual(Biome.TropicalCoast, b);
        }

        // High moisture at mid elevation/temperature must produce Forest.
        [Test]
        public void HighMoisture_ReturnsForest()
        {
            var b = BiomeClassifier.Classify(0.60f, m: 0.80f, t: 0.50f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Forest, b);
        }

        // Mid moisture at mid elevation/temperature must produce the default Grassland biome.
        [Test]
        public void MidMoisture_ReturnsGrassland()
        {
            var b = BiomeClassifier.Classify(0.60f, m: 0.50f, t: 0.50f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Grassland, b);
        }

        // Low moisture at mid elevation/temperature must produce DryGrass (sparse vegetation).
        [Test]
        public void LowMoisture_ReturnsDryGrass()
        {
            var b = BiomeClassifier.Classify(0.60f, m: 0.10f, t: 0.50f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.DryGrass, b);
        }
    }
}

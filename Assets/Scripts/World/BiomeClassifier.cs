// BiomeClassifier.cs — Pure mapping from noise values to a Biome label.
// Called twice per tile by WorldGenerator: first without water-proximity data (first pass),
// then the second pass upgrades hot+moist tiles near water to TropicalCoast externally.
// To add a new biome: add a threshold field to WorldGenConfig and a branch here in priority order.

namespace RTSCL.World
{
    /// <summary>Stateless classifier that maps (elevation, moisture, temperature) noise values
    /// to a <see cref="Biome"/>, driven entirely by thresholds in <see cref="WorldGenConfig"/>.</summary>
    public static class BiomeClassifier
    {
        /// <summary>Returns the biome for a single tile given its noise values.
        /// Rules are checked in priority order: water/shore first, then extremes (cliff, snow),
        /// then climate-driven biomes. TropicalCoast requires the caller to supply
        /// <paramref name="hasNearbyWater"/> = true (WorldGenerator handles this in a second pass).</summary>
        /// <param name="e">Elevation in [0..1] (post-falloff).</param>
        /// <param name="m">Moisture in [0..1].</param>
        /// <param name="t">Temperature in [0..1].</param>
        /// <param name="hasNearbyWater">True if a water tile exists within Manhattan radius 2.</param>
        public static Biome Classify(float e, float m, float t, bool hasNearbyWater,
                                     WorldGenConfig c)
        {
            if (e < c.DeepWaterMax) return Biome.DeepWater;      // Below sea level
            if (e < c.ShoreMax)     return Biome.Shore;           // Shallow coastal fringe
            if (e > c.CliffMin)     return Biome.Cliff;           // Too steep to traverse
            if (t < c.SnowMax)      return Biome.Snow;            // Cold high ground
            if (t > c.TropicalMin && m < c.DesertMoistureMax) return Biome.Desert;
            if (t > c.TropicalMin && m >= c.DesertMoistureMax && hasNearbyWater)
                return Biome.TropicalCoast;                        // Hot + moist + near water
            if (m > c.ForestMoistureMin)     return Biome.Forest;
            if (m > c.GrasslandMoistureMin)  return Biome.Grassland;
            return Biome.DryGrass;                                 // Fallback: arid passable land
        }
    }
}

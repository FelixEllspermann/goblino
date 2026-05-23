namespace RTSCL.World
{
    public static class BiomeClassifier
    {
        public static Biome Classify(float e, float m, float t, bool hasNearbyWater,
                                     WorldGenConfig c)
        {
            if (e < c.DeepWaterMax) return Biome.DeepWater;
            if (e < c.ShoreMax) return Biome.Shore;
            if (e > c.CliffMin) return Biome.Cliff;
            if (t < c.SnowMax) return Biome.Snow;
            if (t > c.TropicalMin && m < c.DesertMoistureMax) return Biome.Desert;
            if (t > c.TropicalMin && m >= c.DesertMoistureMax && hasNearbyWater)
                return Biome.TropicalCoast;
            if (m > c.ForestMoistureMin) return Biome.Forest;
            if (m > c.GrasslandMoistureMin) return Biome.Grassland;
            return Biome.DryGrass;
        }
    }
}

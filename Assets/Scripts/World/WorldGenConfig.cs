using System;

namespace RTSCL.World
{
    [Serializable]
    public sealed class WorldGenConfig
    {
        // Map dimensions
        public int Width = 128;
        public int Height = 128;
        public int PlayerCount = 2;

        // Noise scales (larger = smoother / more zoomed-in features)
        public float ElevationScale = 40f;
        public float MoistureScale = 30f;
        public float TemperatureScale = 60f;

        // Continent shaping
        public float FalloffStrength = 1.2f;

        // Biome thresholds (elevation_adjusted)
        public float DeepWaterMax = 0.30f;
        public float ShoreMax = 0.40f;
        public float CliffMin = 0.85f;

        // Biome thresholds (temperature & moisture)
        public float SnowMax = 0.25f;
        public float TropicalMin = 0.75f;
        public float DesertMoistureMax = 0.30f;
        public float ForestMoistureMin = 0.65f;
        public float GrasslandMoistureMin = 0.35f;

        // Post-processing
        public int MiniIslandRemovalThreshold = 10;
        public int LakeFillThreshold = 5;

        // Spawn placement
        public int SpawnBufferToImpassable = 4;
        public int SpawnReservedAreaSize = 5;
        public int CandidatesPerSpawn = 100;

        // Resource clusters
        public int StoneClustersPerSpawn = 2;
        public int FoodClustersPerSpawn = 2;
        public int FreeRoamClusterCountMin = 6;
        public int FreeRoamClusterCountMax = 10;
        public int ClusterSizeMin = 3;
        public int ClusterSizeMax = 5;
        public int ClusterRadiusMin = 6;
        public int ClusterRadiusMax = 12;

        // Reachability retry
        public int MaxRegenerationAttempts = 5;
    }
}

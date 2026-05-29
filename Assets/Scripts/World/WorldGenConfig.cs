// WorldGenConfig.cs — All tuneable parameters for the world generator, grouped by subsystem.
// This class is [Serializable] so Unity can expose it on a MonoBehaviour inspector field
// (e.g. the WorldGeneratorBootstrap component). Defaults produce a playable 128×128 map for 4 players.
// Most values have a direct 1:1 mapping to a specific pipeline step — see WorldGenerator.TryGenerate
// and the relevant subsystem class for usage.

using System;

namespace RTSCL.World
{
    /// <summary>Plain data object holding all generation parameters consumed by WorldGenerator.
    /// Adjust fields here (or via inspector) to change map size, biome distribution,
    /// spawn spacing, resource density, or retry budget.</summary>
    [Serializable]
    public sealed class WorldGenConfig
    {
        // ── Map dimensions ──────────────────────────────────────────────────────────
        public int Width = 128;       // Tile columns
        public int Height = 128;      // Tile rows
        public int PlayerCount = 4;   // Number of spawn points to place (1–4 supported)

        // ── Noise scales (larger value = smoother / more zoomed-in features) ───────
        public float ElevationScale = 40f;
        public float MoistureScale = 30f;
        public float TemperatureScale = 60f;

        // ── Continent shaping ────────────────────────────────────────────────────────
        // Higher FalloffStrength → smaller landmass, more ocean border.
        // Tuned low (0.6) for a large connected continent rather than scattered islands.
        public float FalloffStrength = 0.6f;

        // ── Biome thresholds (post-falloff elevation, normalised [0..1]) ────────────
        // Low water cutoffs → more land, less ocean (favours mainland over islands).
        public float DeepWaterMax = 0.22f;   // elevation < this → DeepWater
        public float ShoreMax = 0.32f;       // elevation in [DeepWaterMax, ShoreMax) → Shore
        public float CliffMin = 0.85f;       // elevation > this → Cliff

        // ── Biome thresholds (temperature & moisture, both normalised [0..1]) ───────
        public float SnowMax = 0.25f;              // temperature < this → Snow
        public float TropicalMin = 0.75f;          // temperature > this enables Desert / TropicalCoast
        public float DesertMoistureMax = 0.30f;    // moisture < this (hot) → Desert
        public float ForestMoistureMin = 0.65f;    // moisture > this → Forest
        public float GrasslandMoistureMin = 0.35f; // moisture in [GrasslandMoistureMin, ForestMoistureMin) → Grassland

        // ── Post-processing (island/lake cleanup) ────────────────────────────────────
        public int MiniIslandRemovalThreshold = 30; // Land components with fewer tiles are erased (high → few stray islands)
        public int LakeFillThreshold = 12;          // Enclosed water pockets with fewer tiles are filled (high → fewer inland lakes)

        // ── Spawn placement ──────────────────────────────────────────────────────────
        public int SpawnBufferToImpassable = 4;  // Min clear-tile radius required around a spawn candidate
        public int SpawnReservedAreaSize = 5;    // Decoration-exclusion radius (used by future DecorationPlacer)
        public int CandidatesPerSpawn = 100;     // Random samples drawn before farthest-point selection

        // ── Resource clusters ────────────────────────────────────────────────────────
        public int StoneClustersPerSpawn = 2;    // Stone clusters placed near each spawn
        public int FoodClustersPerSpawn = 2;     // Food clusters placed near each spawn
        public int FreeRoamClusterCountMin = 6;  // Additional clusters scattered across the map (min)
        public int FreeRoamClusterCountMax = 10; // Additional clusters scattered across the map (max)
        public int ClusterSizeMin = 3;           // Tiles per cluster (min)
        public int ClusterSizeMax = 5;           // Tiles per cluster (max)
        public int ClusterRadiusMin = 6;         // Distance from spawn for per-spawn clusters (min, in tiles)
        public int ClusterRadiusMax = 12;        // Distance from spawn for per-spawn clusters (max, in tiles)

        // ── Reachability retry ───────────────────────────────────────────────────────
        // Increase if generation often throws for complex configs; each retry uses seed+1.
        public int MaxRegenerationAttempts = 5;
    }
}

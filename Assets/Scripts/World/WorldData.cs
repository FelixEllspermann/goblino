// WorldData.cs — Immutable snapshot of a fully-generated map, returned by WorldGenerator.Generate.
// Consumed by the Unity layer (MainBaseSetup / WorldPainter) to paint tiles, place buildings,
// and spawn starting units. All fields are set once at construction — WorldData is never mutated.

using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    /// <summary>Complete, read-only output of a single world-generation run.
    /// Passed from WorldGenerator → WorldStartContext → MainBaseSetup for scene construction.</summary>
    public sealed class WorldData
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Biome[,] Biomes;                    // [x, y] indexed tile biome grid
        public readonly int2[] Spawns;                      // Per-player starting positions; index == player slot
        public readonly IReadOnlyList<ResourceCluster> Resources; // All resource deposits on the map
        public readonly uint Seed;                          // Exact seed used; stored so multiplayer clients can reproduce the same map

        public WorldData(int width, int height, Biome[,] biomes, int2[] spawns,
                         IReadOnlyList<ResourceCluster> resources, uint seed)
        {
            Width = width;
            Height = height;
            Biomes = biomes;
            Spawns = spawns;
            Resources = resources;
            Seed = seed;
        }

        /// <summary>Returns true when (x, y) falls within the map boundaries.</summary>
        public bool InBounds(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height;

        /// <summary>Convenience accessor; does not bounds-check — call InBounds first if needed.</summary>
        public Biome BiomeAt(int x, int y) => Biomes[x, y];
    }
}

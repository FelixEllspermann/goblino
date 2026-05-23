using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public sealed class WorldData
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Biome[,] Biomes;
        public readonly int2[] Spawns;
        public readonly IReadOnlyList<ResourceCluster> Resources;
        public readonly uint Seed;

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

        public bool InBounds(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height;

        public Biome BiomeAt(int x, int y) => Biomes[x, y];
    }
}

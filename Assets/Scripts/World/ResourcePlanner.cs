using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public static class ResourcePlanner
    {
        private static bool IsPassable(Biome b) =>
            b != Biome.DeepWater && b != Biome.Cliff && b != Biome.Shore;

        public static List<ResourceCluster> PlaceClusters(Biome[,] biomes, int w, int h,
                                                          int2[] spawns, WorldGenConfig cfg,
                                                          ref Random rng)
        {
            var result = new List<ResourceCluster>();
            var occupied = new bool[w, h];

            foreach (var spawn in spawns)
            {
                for (int i = 0; i < cfg.StoneClustersPerSpawn; i++)
                    TryPlaceClusterAround(biomes, w, h, spawn, ResourceType.Stone,
                                          cfg, occupied, ref rng, result);
                for (int i = 0; i < cfg.FoodClustersPerSpawn; i++)
                    TryPlaceClusterAround(biomes, w, h, spawn, ResourceType.Food,
                                          cfg, occupied, ref rng, result);
            }

            int freeCount = rng.NextInt(cfg.FreeRoamClusterCountMin,
                                        cfg.FreeRoamClusterCountMax + 1);
            for (int i = 0; i < freeCount; i++)
            {
                var type = rng.NextBool() ? ResourceType.Stone : ResourceType.Food;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    int x = rng.NextInt(0, w);
                    int y = rng.NextInt(0, h);
                    if (!IsPassable(biomes[x, y])) continue;
                    if (occupied[x, y]) continue;
                    PlaceCluster(biomes, w, h, new int2(x, y), type, cfg, occupied,
                                  ref rng, result);
                    break;
                }
            }
            return result;
        }

        private static void TryPlaceClusterAround(Biome[,] biomes, int w, int h, int2 spawn,
                                                  ResourceType type, WorldGenConfig cfg,
                                                  bool[,] occupied, ref Random rng,
                                                  List<ResourceCluster> result)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                int radius = rng.NextInt(cfg.ClusterRadiusMin, cfg.ClusterRadiusMax + 1);
                float angle = rng.NextFloat(0f, math.PI * 2f);
                int x = spawn.x + (int)(math.cos(angle) * radius);
                int y = spawn.y + (int)(math.sin(angle) * radius);
                if (x < 0 || x >= w || y < 0 || y >= h) continue;
                if (!IsPassable(biomes[x, y])) continue;
                if (occupied[x, y]) continue;
                if (type == ResourceType.Food && biomes[x, y] != Biome.Grassland
                                              && biomes[x, y] != Biome.Forest) continue;
                PlaceCluster(biomes, w, h, new int2(x, y), type, cfg, occupied,
                              ref rng, result);
                return;
            }
        }

        private static void PlaceCluster(Biome[,] biomes, int w, int h, int2 center,
                                          ResourceType type, WorldGenConfig cfg,
                                          bool[,] occupied, ref Random rng,
                                          List<ResourceCluster> result)
        {
            int size = rng.NextInt(cfg.ClusterSizeMin, cfg.ClusterSizeMax + 1);
            var cells = new List<int2>();
            cells.Add(center);
            occupied[center.x, center.y] = true;
            int placed = 1;
            int attempt = 0;
            while (placed < size && attempt < size * 8)
            {
                attempt++;
                int dx = rng.NextInt(-1, 2);
                int dy = rng.NextInt(-1, 2);
                int nx = center.x + dx;
                int ny = center.y + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (!IsPassable(biomes[nx, ny])) continue;
                if (occupied[nx, ny]) continue;
                cells.Add(new int2(nx, ny));
                occupied[nx, ny] = true;
                placed++;
            }
            result.Add(new ResourceCluster(type, center, cells));
        }
    }
}

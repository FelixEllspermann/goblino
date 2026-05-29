using System;
using System.Collections.Generic;
using RTSCL.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;
using Random = Unity.Mathematics.Random;

namespace RTSCL.World.Unity
{
    public sealed class DecorationPlacer
    {
        [Serializable]
        public sealed class DecorationConfig
        {
            [Header("Cosmetic scatter (non-harvestable only)")]
            public float DesertDensity = 0.10f;
            public TileBase[] DesertTiles;       // Cactus_*, Tumbleweed_*
            public float DryGrassDensity = 0.05f;
            public TileBase[] DryGrassTiles;     // Tumbleweed_*

            [Header("Trees / Forests (biome-aware)")]
            public TileBase[] DeciduousTreeTiles; // Trees_*
            public TileBase[] PineForestTiles;    // PineTrees_*, WinterTrees_*
            public TileBase[] CoconutForestTiles; // CoconutTrees_*
            public TileBase[] DeadTreeTiles;      // DeadTrees_*
            public int ForestCount = 22;
            public int ForestSizeMin = 15;
            public int ForestSizeMax = 40;
            public int ForestRadius = 5;
            public float ScatteredTreeDensity = 0.01f;

            [Header("Stone")]
            public TileBase[] RockTiles;          // Rocks_*
            public int StoneClusterCount = 12;
            public int StoneSizeMin = 3;
            public int StoneSizeMax = 8;
            public int StoneRadius = 3;

            [Header("Wheat")]
            public TileBase WheatTile;            // Wheatfield_0

            [Header("Ore Deposits")]
            public TileBase GoldOreTile;
            public int GoldDeposits = 6;
            public TileBase IronOreTile;
            public int IronDeposits = 6;
            public TileBase CrystalOreTile;
            public int CrystalDeposits = 3;

            [Header("Spawn")]
            public int SpawnReservedRadius = 2;
            public int SafeSpawnRadius = 25;
            public int SafeSpawnWheat = 5;
        }

        private readonly Tilemap _decorationMap;
        private readonly DecorationConfig _cfg;

        public DecorationPlacer(Tilemap decorationMap, DecorationConfig cfg)
        {
            _decorationMap = decorationMap;
            _cfg = cfg;
        }

        public void Place(WorldData world, uint seed)
        {
            _decorationMap.ClearAllTiles();
            var rng = new Random(seed == 0 ? 1u : seed);
            var reserved = BuildReservedMask(world);

            // Fixed pass order (deterministic for a given seed → MP-consistent).
            PlaceForests(world, reserved, ref rng);
            ScatteredTrees(world, reserved, ref rng);

            for (int i = 0; i < _cfg.StoneClusterCount; i++)
                PlaceStoneCluster(world, reserved, ref rng);

            PlaceOre(world, _cfg.GoldOreTile, _cfg.GoldDeposits, reserved, ref rng);
            PlaceOre(world, _cfg.IronOreTile, _cfg.IronDeposits, reserved, ref rng);
            PlaceOre(world, _cfg.CrystalOreTile, _cfg.CrystalDeposits, reserved, ref rng);

            if (world.Spawns != null)
                foreach (var s in world.Spawns)
                    GuaranteeSpawn(world, s, reserved, ref rng);

            // Cosmetic scatter LAST so it only fills empty, unreserved cells.
            CosmeticScatter(world, reserved, ref rng);

            _decorationMap.RefreshAllTiles();
        }

        // ---------- Biome helpers ----------

        private static bool IsPassableBiome(Biome b) =>
            b != Biome.DeepWater && b != Biome.Cliff && b != Biome.Shore;

        private TileBase[] ForestTilesFor(Biome b) => b switch
        {
            Biome.Forest        => _cfg.DeciduousTreeTiles,
            Biome.Grassland     => _cfg.DeciduousTreeTiles,
            Biome.Snow          => _cfg.PineForestTiles,
            Biome.TropicalCoast => _cfg.CoconutForestTiles,
            Biome.DryGrass      => _cfg.DeadTreeTiles,
            _                   => null,
        };

        private bool BiomeSupportsTrees(Biome b)
        {
            var t = ForestTilesFor(b);
            return t != null && t.Length > 0;
        }

        // ---------- Forests ----------

        private void PlaceForests(WorldData world, bool[,] reserved, ref Random rng)
        {
            int placed = 0, attempts = 0, maxAttempts = Mathf.Max(1, _cfg.ForestCount) * 30;
            while (placed < _cfg.ForestCount && attempts < maxAttempts)
            {
                attempts++;
                int cx = rng.NextInt(0, world.Width);
                int cy = rng.NextInt(0, world.Height);
                if (reserved[cx, cy]) continue;
                var biome = world.BiomeAt(cx, cy);
                var tiles = ForestTilesFor(biome);
                if (tiles == null || tiles.Length == 0) continue;

                // Grow only into cells whose biome maps to the SAME tree array (keeps a forest one species).
                GrowCluster(world, cx, cy, tiles, _cfg.ForestSizeMin, _cfg.ForestSizeMax, _cfg.ForestRadius,
                            reserved, ref rng, (x, y) => ForestTilesFor(world.BiomeAt(x, y)) == tiles);
                placed++;
            }
        }

        private void ScatteredTrees(WorldData world, bool[,] reserved, ref Random rng)
        {
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (reserved[x, y]) continue;
                if (_decorationMap.GetTile(new Vector3Int(x, y, 0)) != null) continue;
                var biome = world.BiomeAt(x, y);
                if (!BiomeSupportsTrees(biome)) continue;
                if (rng.NextFloat() > _cfg.ScatteredTreeDensity) continue;
                var tiles = ForestTilesFor(biome);
                _decorationMap.SetTile(new Vector3Int(x, y, 0), tiles[rng.NextInt(0, tiles.Length)]);
                reserved[x, y] = true;
            }
        }

        // ---------- Stone ----------

        private void PlaceStoneCluster(WorldData world, bool[,] reserved, ref Random rng)
        {
            if (_cfg.RockTiles == null || _cfg.RockTiles.Length == 0) return;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                int cx = rng.NextInt(0, world.Width);
                int cy = rng.NextInt(0, world.Height);
                if (reserved[cx, cy]) continue;
                if (!IsPassableBiome(world.BiomeAt(cx, cy))) continue;
                GrowCluster(world, cx, cy, _cfg.RockTiles, _cfg.StoneSizeMin, _cfg.StoneSizeMax, _cfg.StoneRadius,
                            reserved, ref rng, (x, y) => IsPassableBiome(world.BiomeAt(x, y)));
                return;
            }
        }

        // ---------- Generic cluster growth ----------

        private void GrowCluster(WorldData world, int cx, int cy, TileBase[] tiles,
                                 int sizeMin, int sizeMax, int radius,
                                 bool[,] reserved, ref Random rng, Func<int, int, bool> cellOk)
        {
            int target = rng.NextInt(sizeMin, sizeMax + 1);
            int placed = 0, attempts = 0, maxAttempts = target * 12;
            while (placed < target && attempts < maxAttempts)
            {
                attempts++;
                int x = cx + rng.NextInt(-radius, radius + 1);
                int y = cy + rng.NextInt(-radius, radius + 1);
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height) continue;
                if (reserved[x, y]) continue;
                var cell = new Vector3Int(x, y, 0);
                if (_decorationMap.GetTile(cell) != null) continue;
                if (!cellOk(x, y)) continue;
                _decorationMap.SetTile(cell, tiles[rng.NextInt(0, tiles.Length)]);
                reserved[x, y] = true;
                placed++;
            }
        }

        // ---------- Ore (single deposits) ----------

        private void PlaceOre(WorldData world, TileBase tile, int count, bool[,] reserved, ref Random rng)
        {
            if (tile == null || count <= 0) return;
            int placed = 0, attempts = 0, maxAttempts = count * 40;
            while (placed < count && attempts < maxAttempts)
            {
                attempts++;
                int x = rng.NextInt(0, world.Width);
                int y = rng.NextInt(0, world.Height);
                if (reserved[x, y]) continue;
                if (!IsPassableBiome(world.BiomeAt(x, y))) continue;
                var cell = new Vector3Int(x, y, 0);
                if (_decorationMap.GetTile(cell) != null) continue;
                _decorationMap.SetTile(cell, tile);
                reserved[x, y] = true;
                placed++;
            }
        }

        // ---------- Safe-spawn guarantee ----------

        private void GuaranteeSpawn(WorldData world, int2 spawn, bool[,] reserved, ref Random rng)
        {
            int r = _cfg.SafeSpawnRadius;

            // 1 forest (spawn biome's trees, or deciduous fallback so wood is always reachable).
            var forestTiles = ForestTilesFor(world.BiomeAt(spawn.x, spawn.y)) ?? _cfg.DeciduousTreeTiles;
            if (forestTiles != null && forestTiles.Length > 0 &&
                TryFindCellInRadius(world, spawn, r, reserved, ref rng, out int fx, out int fy))
            {
                GrowCluster(world, fx, fy, forestTiles, _cfg.ForestSizeMin, _cfg.ForestSizeMax, _cfg.ForestRadius,
                            reserved, ref rng, (x, y) => IsPassableBiome(world.BiomeAt(x, y)));
            }

            // 1 stone cluster.
            if (_cfg.RockTiles != null && _cfg.RockTiles.Length > 0 &&
                TryFindCellInRadius(world, spawn, r, reserved, ref rng, out int sx, out int sy))
            {
                GrowCluster(world, sx, sy, _cfg.RockTiles, _cfg.StoneSizeMin, _cfg.StoneSizeMax, _cfg.StoneRadius,
                            reserved, ref rng, (x, y) => IsPassableBiome(world.BiomeAt(x, y)));
            }

            // 1 of each ore.
            PlaceSingleInRadius(world, spawn, r, _cfg.GoldOreTile, reserved, ref rng);
            PlaceSingleInRadius(world, spawn, r, _cfg.IronOreTile, reserved, ref rng);
            PlaceSingleInRadius(world, spawn, r, _cfg.CrystalOreTile, reserved, ref rng);

            // 5 wheat.
            for (int i = 0; i < _cfg.SafeSpawnWheat; i++)
                PlaceSingleInRadius(world, spawn, r, _cfg.WheatTile, reserved, ref rng);
        }

        private void PlaceSingleInRadius(WorldData world, int2 center, int radius, TileBase tile,
                                         bool[,] reserved, ref Random rng)
        {
            if (tile == null) return;
            if (TryFindCellInRadius(world, center, radius, reserved, ref rng, out int x, out int y))
            {
                _decorationMap.SetTile(new Vector3Int(x, y, 0), tile);
                reserved[x, y] = true;
            }
            else
            {
                Debug.LogWarning($"[DecorationPlacer] Could not place a guaranteed {tile.name} near spawn ({center.x},{center.y})");
            }
        }

        private bool TryFindCellInRadius(WorldData world, int2 center, int radius, bool[,] reserved,
                                         ref Random rng, out int outX, out int outY)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                int x = center.x + rng.NextInt(-radius, radius + 1);
                int y = center.y + rng.NextInt(-radius, radius + 1);
                if (x < 0 || x >= world.Width || y < 0 || y >= world.Height) continue;
                if (reserved[x, y]) continue;
                if (!IsPassableBiome(world.BiomeAt(x, y))) continue;
                if (_decorationMap.GetTile(new Vector3Int(x, y, 0)) != null) continue;
                outX = x; outY = y; return true;
            }
            outX = 0; outY = 0; return false;
        }

        // ---------- Cosmetic scatter (non-harvestable) ----------

        private void CosmeticScatter(WorldData world, bool[,] reserved, ref Random rng)
        {
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (reserved[x, y]) continue;
                if (_decorationMap.GetTile(new Vector3Int(x, y, 0)) != null) continue;
                var biome = world.BiomeAt(x, y);
                float density; TileBase[] pool;
                if (biome == Biome.Desert) { density = _cfg.DesertDensity; pool = _cfg.DesertTiles; }
                else if (biome == Biome.DryGrass) { density = _cfg.DryGrassDensity; pool = _cfg.DryGrassTiles; }
                else continue;
                if (pool == null || pool.Length == 0) continue;
                if (rng.NextFloat() > density) continue;
                _decorationMap.SetTile(new Vector3Int(x, y, 0), pool[rng.NextInt(0, pool.Length)]);
            }
        }

        // ---------- Reserved mask ----------

        private bool[,] BuildReservedMask(WorldData world)
        {
            var mask = new bool[world.Width, world.Height];
            int r = _cfg.SpawnReservedRadius;
            if (world.Spawns != null)
            foreach (var s in world.Spawns)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int nx = s.x + dx, ny = s.y + dy;
                    if (nx >= 0 && nx < world.Width && ny >= 0 && ny < world.Height)
                        mask[nx, ny] = true;
                }
            }
            if (world.Resources != null)
                foreach (var cluster in world.Resources)
                    foreach (var cell in cluster.Cells)
                        if (cell.x >= 0 && cell.x < world.Width && cell.y >= 0 && cell.y < world.Height)
                            mask[cell.x, cell.y] = true;
            return mask;
        }
    }
}

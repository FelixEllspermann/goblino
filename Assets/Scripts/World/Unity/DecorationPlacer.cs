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
            // Per-biome decoration probabilities (0..1) and weighted sprite pools.
            public float ForestDensity = 0.25f;
            public TileBase[] ForestTiles;       // Trees_*.asset

            public float GrasslandDensity = 0.08f;
            public TileBase[] GrasslandTiles;    // mix of Trees, Wheatfield, Rocks, DeadTrees

            public float DryGrassDensity = 0.12f;
            public TileBase[] DryGrassTiles;     // DeadTrees, Rocks, Tumbleweed

            public float DesertDensity = 0.10f;
            public TileBase[] DesertTiles;       // Cactus, Tumbleweed, Rocks

            public float SnowDensity = 0.20f;
            public TileBase[] SnowTiles;         // PineTrees, WinterTrees, WinterDeadTrees, Rocks

            public float TropicalDensity = 0.18f;
            public TileBase[] TropicalTiles;     // CoconutTrees, Rocks

            public float ShoreDensity = 0.03f;
            public TileBase[] ShoreTiles;        // Rocks only

            public float CliffDensity = 0.05f;
            public TileBase[] CliffTiles;        // Rocks only

            public int SpawnReservedRadius = 2;  // total reserved area = (2r+1)²

            [Header("Ore Deposits")]
            public TileBase GoldOreTile;
            public int GoldDeposits = 6;
            public TileBase IronOreTile;
            public int IronDeposits = 6;
            public TileBase CrystalOreTile;
            public int CrystalDeposits = 3;
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

            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (reserved[x, y]) continue;
                var biome = world.BiomeAt(x, y);
                (float density, TileBase[] pool) = PoolFor(biome);
                if (pool == null || pool.Length == 0) continue;
                if (rng.NextFloat() > density) continue;

                var tile = pool[rng.NextInt(0, pool.Length)];
                _decorationMap.SetTile(new Vector3Int(x, y, 0), tile);
            }

            // Sparse ore deposits (after the per-biome decoration pass so the existing
            // decoration output is unchanged for a given seed; ore appends to the rng stream).
            PlaceOre(world, _cfg.GoldOreTile, _cfg.GoldDeposits, reserved, ref rng);
            PlaceOre(world, _cfg.IronOreTile, _cfg.IronDeposits, reserved, ref rng);
            PlaceOre(world, _cfg.CrystalOreTile, _cfg.CrystalDeposits, reserved, ref rng);

            _decorationMap.RefreshAllTiles();
        }

        private void PlaceOre(WorldData world, TileBase tile, int count, bool[,] reserved, ref Random rng)
        {
            if (tile == null || count <= 0) return;
            int placed = 0;
            int attempts = 0;
            int maxAttempts = count * 40;
            while (placed < count && attempts < maxAttempts)
            {
                attempts++;
                int x = rng.NextInt(0, world.Width);
                int y = rng.NextInt(0, world.Height);
                if (reserved[x, y]) continue;
                var biome = world.BiomeAt(x, y);
                if (biome == Biome.DeepWater || biome == Biome.Cliff || biome == Biome.Shore) continue;
                var cell = new Vector3Int(x, y, 0);
                if (_decorationMap.GetTile(cell) != null) continue; // don't overwrite a tree/rock
                _decorationMap.SetTile(cell, tile);
                reserved[x, y] = true; // prevent another ore landing on the same cell
                placed++;
            }
        }

        private (float, TileBase[]) PoolFor(Biome b) => b switch
        {
            Biome.Forest        => (_cfg.ForestDensity,    _cfg.ForestTiles),
            Biome.Grassland     => (_cfg.GrasslandDensity, _cfg.GrasslandTiles),
            Biome.DryGrass      => (_cfg.DryGrassDensity,  _cfg.DryGrassTiles),
            Biome.Desert        => (_cfg.DesertDensity,    _cfg.DesertTiles),
            Biome.Snow          => (_cfg.SnowDensity,      _cfg.SnowTiles),
            Biome.TropicalCoast => (_cfg.TropicalDensity,  _cfg.TropicalTiles),
            Biome.Shore         => (_cfg.ShoreDensity,     _cfg.ShoreTiles),
            Biome.Cliff         => (_cfg.CliffDensity,     _cfg.CliffTiles),
            _ => (0f, null),
        };

        private bool[,] BuildReservedMask(WorldData world)
        {
            var mask = new bool[world.Width, world.Height];
            int r = _cfg.SpawnReservedRadius;
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
            // Also mask resource cluster cells so decorations don't overlap them
            foreach (var cluster in world.Resources)
                foreach (var cell in cluster.Cells)
                    mask[cell.x, cell.y] = true;
            return mask;
        }
    }
}

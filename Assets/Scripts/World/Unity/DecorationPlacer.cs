// DecorationPlacer.cs — Seed-deterministic placement of all map decorations onto the decoration Tilemap.
// Called once per world generation by WorldGeneratorBootstrap.RegenerateWithSeed().
// Because placement uses Unity.Mathematics.Random with a fixed seed, every client that runs the same seed
// produces an IDENTICAL decoration layout — no per-decoration sync is needed over the network.
//
// Placement pass order (must stay fixed to remain deterministic with a given seed):
//   1. Forests (biome-matched species clusters)
//   2. Scattered individual trees
//   3. Stone clusters
//   4. Ore deposits (Gold → Iron → Crystal)
//   5. Guaranteed-spawn resources (one forest, one stone, one of each ore, N wheat near each spawn)
//   6. Cosmetic scatter (cactus, tumbleweed — non-harvestable, fills empty unreserved cells last)
//
// "Reserved" cells: the bool[,] mask prevents decorations from overlapping spawn keep footprints
// and resource clusters already placed by the world generator (WorldData.Resources).
//
// To tune decoration density/counts: adjust DecorationConfig fields in the Inspector on WorldGeneratorBootstrap.
// To add a new decoration type: add TileBase field(s) to DecorationConfig and a placement pass below.
using System;
using System.Collections.Generic;
using RTSCL.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;
using Random = Unity.Mathematics.Random;

namespace RTSCL.World.Unity
{
    /// <summary>Stateless helper that populates the decoration Tilemap from a WorldData + seed.
    /// Construct it, call Place(), discard it — all state is local to the call.</summary>
    public sealed class DecorationPlacer
    {
        /// <summary>Serializable config bag exposed on WorldGeneratorBootstrap in the Inspector.
        /// All density/count/radius knobs live here — never hardcoded below.</summary>
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
            public int StoneClusterCount = 20;
            public int StoneSizeMin = 4;
            public int StoneSizeMax = 10;
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
            /// <summary>Cells cleared around each spawn point so the keep can be placed without overlap.</summary>
            public int SpawnReservedRadius = 2;
            /// <summary>Radius within which guaranteed-spawn resources are seeded near each spawn point.
            /// Kept small so the starting resources are close to the keep and (via reachable-cell search)
            /// always on the same walkable landmass.</summary>
            public int SafeSpawnRadius = 12;
            /// <summary>Number of wheat tiles guaranteed near each spawn.</summary>
            public int SafeSpawnWheat = 5;
        }

        private readonly Tilemap _decorationMap;
        private readonly DecorationConfig _cfg;

        /// <param name="decorationMap">The Tilemap to paint decorations onto (not the terrain map).</param>
        /// <param name="cfg">Config bag from the Inspector on WorldGeneratorBootstrap.</param>
        public DecorationPlacer(Tilemap decorationMap, DecorationConfig cfg)
        {
            _decorationMap = decorationMap;
            _cfg = cfg;
        }

        /// <summary>Main entry point. Clears the decoration Tilemap and re-paints all decoration passes
        /// in a fixed deterministic order using the provided seed.</summary>
        /// <param name="world">WorldData produced by WorldGenerator (provides biomes, size, spawns, resources).</param>
        /// <param name="seed">Generation seed. Must match across all clients for identical layouts.
        /// Seed 0 is remapped to 1 (Unity.Mathematics.Random rejects 0).</param>
        public void Place(WorldData world, uint seed)
        {
            _decorationMap.ClearAllTiles();
            // Remap 0 → 1: Unity.Mathematics.Random requires a non-zero seed.
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

        /// <summary>Returns true for biomes that can receive decorations (excludes water, cliffs, shore).</summary>
        private static bool IsPassableBiome(Biome b) =>
            b != Biome.DeepWater && b != Biome.Cliff && b != Biome.Shore;

        /// <summary>Maps a biome to its appropriate tree tile array.
        /// Returns null for biomes that don't support trees (Desert, DeepWater, etc.).
        /// Forests are kept single-species by comparing the returned array reference in PlaceForests.</summary>
        private TileBase[] ForestTilesFor(Biome b) => b switch
        {
            Biome.Forest        => _cfg.DeciduousTreeTiles,
            Biome.Grassland     => _cfg.DeciduousTreeTiles,
            Biome.Snow          => _cfg.PineForestTiles,
            Biome.TropicalCoast => _cfg.CoconutForestTiles,
            Biome.DryGrass      => _cfg.DeadTreeTiles,
            _                   => null,
        };

        /// <summary>Returns true if ForestTilesFor() yields a non-empty array for this biome.</summary>
        private bool BiomeSupportsTrees(Biome b)
        {
            var t = ForestTilesFor(b);
            return t != null && t.Length > 0;
        }

        // ---------- Forests ----------

        /// <summary>Places up to ForestCount forest clusters, each grown via GrowCluster.
        /// Each cluster picks a random start cell, determines species from that cell's biome,
        /// then only grows into neighboring cells with the same species mapping (single-species forest).</summary>
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

        /// <summary>Fills remaining empty, unreserved, tree-supporting cells at ScatteredTreeDensity probability.
        /// Runs after PlaceForests so it avoids re-filling cluster cells.</summary>
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

        /// <summary>Attempts to place one stone cluster at a random passable, unreserved location.
        /// Retries up to 30 times before giving up (avoids infinite loops on small/full maps).</summary>
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

        /// <summary>Grows a cluster of tiles around (cx, cy) up to a random size in [sizeMin, sizeMax].
        /// Each candidate cell is chosen within ±radius of the center; only placed if unreserved,
        /// empty, in bounds, and satisfying the cellOk predicate.
        /// The same tile array is used for forests and stone clusters — species/type is determined by the caller.</summary>
        /// <param name="cellOk">Predicate that returns true if a cell is eligible for this cluster type.</param>
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

        /// <summary>Scatters 'count' single-cell ore deposits across the map at random passable, empty locations.
        /// Each deposit is a single tile. To increase deposit size, add a GrowCluster call here instead.</summary>
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

        /// <summary>Ensures every spawn point has at least one forest, one stone cluster, one of each ore type,
        /// and SafeSpawnWheat wheat tiles within SafeSpawnRadius. Called after global passes so guaranteed
        /// resources always appear even on seed/map combinations where global scattering missed a spawn area.
        /// Falls back to deciduous trees if the spawn biome doesn't support trees.</summary>
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

        /// <summary>Places a single tile within radius of center. Logs a warning if no empty cell is found
        /// after 200 attempts (usually means the spawn area is very crowded).</summary>
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

        /// <summary>Finds an empty, unreserved cell that is REACHABLE from <paramref name="center"/> on foot
        /// (BFS over passable, water-free cells) within Chebyshev <paramref name="radius"/>. This guarantees
        /// guaranteed-spawn resources land on the same walkable landmass as the keep — never on an isolated
        /// passable cell across water, and never randomly missed. Picks a random eligible cell for spread.
        /// Returns false only if no reachable empty cell exists in range (very rare near a valid spawn).</summary>
        private bool TryFindCellInRadius(WorldData world, int2 center, int radius, bool[,] reserved,
                                         ref Random rng, out int outX, out int outY)
        {
            outX = 0; outY = 0;
            if (center.x < 0 || center.x >= world.Width || center.y < 0 || center.y >= world.Height) return false;

            var candidates = new List<int2>();
            var visited = new bool[world.Width, world.Height];
            var queue = new Queue<int2>();
            visited[center.x, center.y] = true;
            queue.Enqueue(center);

            while (queue.Count > 0)
            {
                var p = queue.Dequeue();

                // Eligible target = walkable, empty, and not reserved (keep footprint / planned resources).
                if (!reserved[p.x, p.y]
                    && IsPassableBiome(world.BiomeAt(p.x, p.y))
                    && _decorationMap.GetTile(new Vector3Int(p.x, p.y, 0)) == null)
                    candidates.Add(p);

                // Expand to 4-connected neighbours that stay walkable and within the search box.
                TryEnqueue(world, visited, queue, center, radius, p.x + 1, p.y);
                TryEnqueue(world, visited, queue, center, radius, p.x - 1, p.y);
                TryEnqueue(world, visited, queue, center, radius, p.x, p.y + 1);
                TryEnqueue(world, visited, queue, center, radius, p.x, p.y - 1);
            }

            if (candidates.Count == 0) return false;
            var c = candidates[rng.NextInt(0, candidates.Count)];
            outX = c.x; outY = c.y;
            return true;
        }

        /// <summary>BFS helper: enqueue (x,y) if in-bounds, unvisited, walkable, and within Chebyshev
        /// <paramref name="radius"/> of <paramref name="center"/>. Traversal ignores the reserved mask
        /// (so it can path THROUGH the keep area) — only candidacy checks reserved.</summary>
        private static void TryEnqueue(WorldData world, bool[,] visited, Queue<int2> queue,
                                       int2 center, int radius, int x, int y)
        {
            if (x < 0 || x >= world.Width || y < 0 || y >= world.Height) return;
            if (visited[x, y]) return;
            if (math.abs(x - center.x) > radius || math.abs(y - center.y) > radius) return;
            if (!IsPassableBiome(world.BiomeAt(x, y))) return;
            visited[x, y] = true;
            queue.Enqueue(new int2(x, y));
        }

        // ---------- Cosmetic scatter (non-harvestable) ----------

        /// <summary>Fills empty, unreserved Desert and DryGrass cells with visual-only tiles (cactus, tumbleweed).
        /// Runs last so it never overwrites harvestable resources. These tiles have no gameplay function.</summary>
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

        /// <summary>Builds the initial reserved cell mask before any decoration is placed.
        /// Reserved = spawn keep footprints (±SpawnReservedRadius) + resource cluster cells from WorldData.Resources.
        /// Decorations are never placed on reserved cells, preserving spawn safety and pre-planned resources.</summary>
        private bool[,] BuildReservedMask(WorldData world)
        {
            var mask = new bool[world.Width, world.Height];
            int r = _cfg.SpawnReservedRadius;
            // Mark keep footprint areas around each spawn point.
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
            // Mark cells already occupied by world-generator resource clusters.
            if (world.Resources != null)
                foreach (var cluster in world.Resources)
                    foreach (var cell in cluster.Cells)
                        if (cell.x >= 0 && cell.x < world.Width && cell.y >= 0 && cell.y < world.Height)
                            mask[cell.x, cell.y] = true;
            return mask;
        }
    }
}

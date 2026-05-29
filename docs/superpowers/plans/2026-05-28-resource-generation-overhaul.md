# Resource Generation Overhaul (Sub-project A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-cell random harvestable scatter with biome-aware tree forests, random stone clusters (3–8), single ore deposits, and a guaranteed safe-spawn zone (radius 25: 1× each ore + 5× wheat + 1 forest + 1 stone cluster); cosmetics (cactus/tumbleweed) stay as low-density scatter.

**Architecture:** Rewrite `DecorationPlacer.Place` into fixed-order seeded passes (forests → scattered trees → stone clusters → ore → safe-spawn guarantees → cosmetic-last). Biome→tree-tile mapping keeps trees biome-appropriate. New `DecorationConfig` fields hold the dedicated tile arrays + counts, wired in the scene; harvestables are stripped from the old cosmetic pools.

**Tech Stack:** Unity 6 / C# / Tilemaps / RTSCL.World.Unity asmdef

**Spec:** `docs/superpowers/specs/2026-05-28-resource-generation-overhaul-design.md`

---

## File Structure

### Modified
| File | Change |
|---|---|
| `Assets/Scripts/World/Unity/DecorationPlacer.cs` | New `DecorationConfig` shape; `Place` rewritten into passes; biome-aware forest/tree/cluster/guarantee helpers |
| `Assets/Scenes/SampleScene.unity` | Wire new tile arrays + counts into `_decorationConfig`; cosmetic pools = cactus/tumbleweed only |

Available tile families (under `Assets/Generated/Tiles/Decorations/`): `Trees_*`, `PineTrees_*`, `WinterTrees_*`, `WinterDeadTrees_*`, `CoconutTrees_*`, `DeadTrees_*`, `Rocks_*`, `Wheatfield_*`, `Cactus_*`, `Tumbleweed_*`, `GoldOre_0`, `IronOre_0`, `CrystalOre_0`.

---

## Task 1: Rewrite DecorationPlacer

**Files:**
- Modify (full replace): `Assets/Scripts/World/Unity/DecorationPlacer.cs`

- [ ] **Step 1: Replace the whole file**

Write `Assets/Scripts/World/Unity/DecorationPlacer.cs`:

```csharp
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
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `Unity_ReadConsole` Types=["Error"]. Expect 0. (The removed config fields — ForestTiles/GrasslandTiles/SnowTiles/TropicalTiles/ShoreTiles/CliffTiles + their densities — just drop from serialization; the scene re-wire is Task 2.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/DecorationPlacer.cs
git commit -m "feat(worldgen): DecorationPlacer rewrite — biome-aware forests, stone clusters, safe-spawn guarantees"
```

---

## Task 2: Scene wiring — tile arrays + counts

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

Via Unity MCP. Populate the new `_decorationConfig` tile arrays from the tile families, set cosmetic pools to cactus/tumbleweed, set counts.

- [ ] **Step 1: Wire arrays + counts via Unity MCP**

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var wgb = Object.FindFirstObjectByType<RTSCL.World.Unity.WorldGeneratorBootstrap>();
        if (wgb == null) { result.LogError("WorldGeneratorBootstrap not found"); return; }

        TileBase[] ByPrefix(params string[] prefixes)
        {
            var list = new List<TileBase>();
            foreach (var guid in AssetDatabase.FindAssets("t:Tile", new[] { "Assets/Generated/Tiles/Decorations" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var t = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (t == null) continue;
                foreach (var p in prefixes)
                    if (t.name.StartsWith(p)) { list.Add(t); break; }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list.ToArray();
        }
        TileBase Single(string exact)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Tile", new[] { "Assets/Generated/Tiles/Decorations" }))
            {
                var t = AssetDatabase.LoadAssetAtPath<Tile>(AssetDatabase.GUIDToAssetPath(guid));
                if (t != null && t.name == exact) return t;
            }
            return null;
        }

        var deciduous = ByPrefix("Trees_");
        var pine = ByPrefix("PineTrees_", "WinterTrees_");
        var coconut = ByPrefix("CoconutTrees_");
        var dead = ByPrefix("DeadTrees_");
        var rocks = ByPrefix("Rocks_");
        var wheat = Single("Wheatfield_0");
        var desertCosmetic = ByPrefix("Cactus_", "Tumbleweed_");
        var dryCosmetic = ByPrefix("Tumbleweed_");

        result.Log($"counts: deciduous={deciduous.Length} pine={pine.Length} coconut={coconut.Length} dead={dead.Length} rocks={rocks.Length} wheat={(wheat==null?"NULL":"ok")} desertCos={desertCosmetic.Length}");

        var so = new SerializedObject(wgb);
        var cfg = so.FindProperty("_decorationConfig");
        if (cfg == null) { result.LogError("_decorationConfig not found"); return; }

        void SetArray(string rel, TileBase[] items)
        {
            var p = cfg.FindPropertyRelative(rel);
            p.ClearArray();
            for (int i = 0; i < items.Length; i++)
            {
                p.InsertArrayElementAtIndex(i);
                p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }
        void SetInt(string rel, int v) => cfg.FindPropertyRelative(rel).intValue = v;
        void SetFloat(string rel, float v) => cfg.FindPropertyRelative(rel).floatValue = v;
        void SetObj(string rel, Object v) => cfg.FindPropertyRelative(rel).objectReferenceValue = v;

        SetArray("DeciduousTreeTiles", deciduous);
        SetArray("PineForestTiles", pine);
        SetArray("CoconutForestTiles", coconut);
        SetArray("DeadTreeTiles", dead);
        SetArray("RockTiles", rocks);
        SetObj("WheatTile", wheat);
        SetArray("DesertTiles", desertCosmetic);
        SetArray("DryGrassTiles", dryCosmetic);

        SetInt("ForestCount", 22);
        SetInt("ForestSizeMin", 15);
        SetInt("ForestSizeMax", 40);
        SetInt("ForestRadius", 5);
        SetFloat("ScatteredTreeDensity", 0.01f);
        SetInt("StoneClusterCount", 12);
        SetInt("StoneSizeMin", 3);
        SetInt("StoneSizeMax", 8);
        SetInt("StoneRadius", 3);
        SetFloat("DesertDensity", 0.10f);
        SetFloat("DryGrassDensity", 0.05f);
        SetInt("SpawnReservedRadius", 2);
        SetInt("SafeSpawnRadius", 25);
        SetInt("SafeSpawnWheat", 5);
        // Ore tile refs + counts persist from Phase 2 (same field names).

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("DecorationConfig wired");
    }
}
```

- [ ] **Step 2: Verify console — expect the counts log (deciduous/pine/coconut/dead/rocks all > 0, wheat ok) + "DecorationConfig wired", 0 errors**

`Unity_ReadConsole` Types=["Error"]. Expect 0. If any tile array logs 0, report which family is missing.

- [ ] **Step 3: Verify ore tile refs survived the config reshape**

Run a quick check that GoldOreTile/IronOreTile/CrystalOreTile are still set (their field names are unchanged so the references should persist):

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var wgb = Object.FindFirstObjectByType<RTSCL.World.Unity.WorldGeneratorBootstrap>();
        var so = new SerializedObject(wgb);
        var cfg = so.FindProperty("_decorationConfig");
        string g = cfg.FindPropertyRelative("GoldOreTile").objectReferenceValue?.name ?? "NULL";
        string i = cfg.FindPropertyRelative("IronOreTile").objectReferenceValue?.name ?? "NULL";
        string c = cfg.FindPropertyRelative("CrystalOreTile").objectReferenceValue?.name ?? "NULL";
        result.Log($"Ore tiles: gold={g} iron={i} crystal={c}");
    }
}
```

If any is NULL, re-wire them: GoldOreTile→GoldOre_0, IronOreTile→IronOre_0, CrystalOreTile→CrystalOre_0 (load from `Assets/Generated/Tiles/Decorations/`), set + save.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "feat(worldgen): wire DecorationConfig tile arrays + counts (forests/stone/wheat/cosmetic)"
```

---

## Task 3: Final compile + smoke verification

**Files:** none changed.

- [ ] **Step 1: Full refresh + console check via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

`Unity_ReadConsole` Types=["Error"] + FilterText="CS". Expect 0.

- [ ] **Step 2: Run RTSCL.World.Tests**

TestRunnerApi + Temp file callback. Expect 36/36 pass (world-gen reachability unaffected; DecorationPlacer is not covered by edit-mode tests).

- [ ] **Step 3: Solo smoke (user manually)**

User runs Play Solo:
- Trees appear as forests (clumps), biome-correct: deciduous in Forest/Grassland, pine in Snow, coconut in Tropical, dead-tree groves in DryGrass, none in Desert.
- Only occasional single trees between forests — no dense random tree carpet.
- Each spawn: within ~25 cells there's 1 gold + 1 iron + 1 crystal, 5 wheat, a forest, a stone cluster.
- Stone clusters (3–8 rocks) scattered out in the world.
- Cactus/tumbleweed scattered cosmetically in desert/drygrass; NO random lone rocks/wheat across the map.
- New seed each solo run → different but rule-consistent layout.

- [ ] **Step 4: No commit unless a fix was needed**

If smoke passes: no further commits. If a tile family was mis-sorted or a guarantee fails, fix + commit.

---

## Plan Self-Review Notes

**Spec coverage:**
- Forests biome-aware + frequent + large (Task 1 `PlaceForests`/`ForestTilesFor`)
- Scattered rare singles (Task 1 `ScatteredTrees`)
- Stone clusters 3–8 (Task 1 `PlaceStoneCluster`/`GrowCluster`)
- Ore single deposits (Task 1 `PlaceOre`)
- Safe-spawn guarantee r=25: 1 each ore + 5 wheat + 1 forest + 1 stone cluster (Task 1 `GuaranteeSpawn`)
- Cosmetic-only scatter, harvestables stripped (Task 1 `CosmeticScatter` + Task 2 pools = cactus/tumbleweed)
- Determinism: single seeded rng, fixed pass order (Task 1 `Place`)
- Tiles wired + counts (Task 2)

**Placeholder scan:** none.

**Type consistency:**
- `DecorationConfig` field names used in `Place`/helpers (Task 1) exactly match the `SetArray`/`SetInt`/`SetObj` relative-property names (Task 2): `DeciduousTreeTiles`, `PineForestTiles`, `CoconutForestTiles`, `DeadTreeTiles`, `RockTiles`, `WheatTile`, `DesertTiles`, `DryGrassTiles`, `ForestCount/SizeMin/SizeMax/Radius`, `ScatteredTreeDensity`, `StoneClusterCount/SizeMin/SizeMax/Radius`, `DesertDensity`, `DryGrassDensity`, `SpawnReservedRadius`, `SafeSpawnRadius`, `SafeSpawnWheat`, ore fields unchanged.
- `Biome` enum values referenced (Forest/Grassland/Snow/TropicalCoast/DryGrass/Desert/DeepWater/Cliff/Shore) all exist.
- `world.Spawns` (int2[]), `world.BiomeAt`, `world.Resources`, `world.Width/Height` — existing API.
- `GrowCluster(... Func<int,int,bool> cellOk)` signature consistent across forest/stone/guarantee callers.

**Determinism note:** cosmetic moved to the LAST pass (vs spec's listed order) so cosmetics never block forests/resources; still a single fixed-order rng stream → MP-consistent. Documented in `Place`.

**Compile-clean per commit:** Task 1 compiles (removed config fields just drop from serialization; null arrays are guarded). Task 2 is scene-only.

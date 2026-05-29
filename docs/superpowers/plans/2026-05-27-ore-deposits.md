# Ore Deposits (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Gold/Iron/Crystal mineable ore deposits placed sparsely across the world, harvested via the Phase 1 `ResourceKind` system.

**Architecture:** Three new `Tile` assets (named `GoldOre_0`/`IronOre_0`/`CrystalOre_0`, referencing `Resources_0/5/2`). `Goblin` classifies them via name prefix → Gold/Iron/Crystal (HP 200). `DecorationPlacer` gets a sparse seed-deterministic ore-placement pass. `ResourceUI` shows 3 more counters (scene wiring only — data-driven UI auto-builds them).

**Tech Stack:** Unity 6 / C# / Tilemaps / RTSCL.World.Unity asmdef

**Spec:** `docs/superpowers/specs/2026-05-27-ore-deposits-design.md`

---

## File Structure

### New (Tile assets)
| File | Sprite |
|---|---|
| `Assets/Generated/Tiles/Decorations/GoldOre_0.asset` | Resources_0 |
| `Assets/Generated/Tiles/Decorations/IronOre_0.asset` | Resources_5 |
| `Assets/Generated/Tiles/Decorations/CrystalOre_0.asset` | Resources_2 |

### Modified
| File | Change |
|---|---|
| `Goblin.cs` | `IsOreTile`, `IsHarvestable += ore`, `KindOf += 3 ores`, `MaxHpFor` ore=200 |
| `DecorationPlacer.cs` | ore tile fields + counts in `DecorationConfig`; sparse ore-placement pass in `Place` |
| `Assets/Scenes/SampleScene.unity` | wire 3 ore tiles + counts into `_decorationConfig`; append Gold/Iron/Crystal to ResourceUI `_kinds` |

---

## Task 1: Goblin ore classification

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Add IsOreTile + extend IsHarvestable**

Find:

```csharp
        public static bool IsRockTile(string tileName) =>
            tileName.StartsWith("Rocks_");

        public static bool IsHarvestable(string tileName) =>
            IsTreeTile(tileName) || IsWheatfieldTile(tileName) || IsRockTile(tileName);
```

Replace with:

```csharp
        public static bool IsRockTile(string tileName) =>
            tileName.StartsWith("Rocks_");

        public static bool IsOreTile(string tileName) =>
            tileName.StartsWith("GoldOre_") || tileName.StartsWith("IronOre_") || tileName.StartsWith("CrystalOre_");

        public static bool IsHarvestable(string tileName) =>
            IsTreeTile(tileName) || IsWheatfieldTile(tileName) || IsRockTile(tileName) || IsOreTile(tileName);
```

- [ ] **Step 2: Extend KindOf**

Find:

```csharp
        public static ResourceKind KindOf(string tileName)
        {
            if (IsWheatfieldTile(tileName)) return ResourceKind.Food;
            if (IsRockTile(tileName)) return ResourceKind.Stone;
            return ResourceKind.Wood; // trees + default
        }
```

Replace with:

```csharp
        public static ResourceKind KindOf(string tileName)
        {
            if (IsWheatfieldTile(tileName)) return ResourceKind.Food;
            if (IsRockTile(tileName)) return ResourceKind.Stone;
            if (tileName.StartsWith("GoldOre_")) return ResourceKind.Gold;
            if (tileName.StartsWith("IronOre_")) return ResourceKind.Iron;
            if (tileName.StartsWith("CrystalOre_")) return ResourceKind.Crystal;
            return ResourceKind.Wood; // trees + default
        }
```

- [ ] **Step 3: Extend MaxHpFor**

Find:

```csharp
        private static int MaxHpFor(string tileName)
        {
            if (IsWheatfieldTile(tileName)) return 500;
            if (IsRockTile(tileName)) return 100;
            return TreeHP.MaxHP; // trees = 50
        }
```

Replace with:

```csharp
        private static int MaxHpFor(string tileName)
        {
            if (IsWheatfieldTile(tileName)) return 500;
            if (IsRockTile(tileName)) return 100;
            if (IsOreTile(tileName)) return 200;
            return TreeHP.MaxHP; // trees = 50
        }
```

- [ ] **Step 4: Refresh + compile check via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `Unity_ReadConsole` Types=["Error"]. Expect 0.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(ore): Goblin classifies GoldOre/IronOre/CrystalOre tiles (HP 200)"
```

---

## Task 2: Create the 3 ore Tile assets

**Files:**
- Create: `Assets/Generated/Tiles/Decorations/GoldOre_0.asset`, `IronOre_0.asset`, `CrystalOre_0.asset`

Driven via Unity MCP so GUIDs are generated correctly.

- [ ] **Step 1: Create the Tile assets via Unity MCP**

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        Sprite Sub(string name)
        {
            foreach (var s in AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/MiniWorldSprites/Buildings/Wood/Resources.png"))
                if (s is Sprite sp && sp.name == name) return sp;
            return null;
        }

        void MakeTile(string assetName, string spriteName)
        {
            var sprite = Sub(spriteName);
            if (sprite == null) { result.LogError($"Sprite {spriteName} not found"); return; }
            var path = $"Assets/Generated/Tiles/Decorations/{assetName}.asset";
            if (AssetDatabase.LoadAssetAtPath<Tile>(path) != null) { result.Log($"{assetName} already exists"); return; }
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.Sprite;
            AssetDatabase.CreateAsset(tile, path);
            result.Log($"Created {assetName} ← {spriteName}");
        }

        MakeTile("GoldOre_0", "Resources_0");
        MakeTile("IronOre_0", "Resources_5");
        MakeTile("CrystalOre_0", "Resources_2");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        result.Log("Ore tiles done");
    }
}
```

- [ ] **Step 2: Verify assets exist + console clean**

`Unity_ReadConsole` Types=["Error"]. Expect 0. Confirm the 3 `.asset` files exist in `Assets/Generated/Tiles/Decorations/`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Generated/Tiles/Decorations/GoldOre_0.asset" "Assets/Generated/Tiles/Decorations/GoldOre_0.asset.meta" "Assets/Generated/Tiles/Decorations/IronOre_0.asset" "Assets/Generated/Tiles/Decorations/IronOre_0.asset.meta" "Assets/Generated/Tiles/Decorations/CrystalOre_0.asset" "Assets/Generated/Tiles/Decorations/CrystalOre_0.asset.meta"
git commit -m "feat(ore): GoldOre/IronOre/CrystalOre Tile assets (Resources_0/5/2)"
```

---

## Task 3: DecorationPlacer ore-placement pass

**Files:**
- Modify: `Assets/Scripts/World/Unity/DecorationPlacer.cs`

- [ ] **Step 1: Add ore fields to DecorationConfig**

Find (end of the `DecorationConfig` class, after the `ShoreTiles`/`CliffTiles`/`SpawnReservedRadius` fields — locate `public int SpawnReservedRadius = 2;`):

```csharp
            public int SpawnReservedRadius = 2;  // total reserved area = (2r+1)²
```

Right after that line (still inside the `DecorationConfig` class), add:

```csharp

            [Header("Ore Deposits")]
            public TileBase GoldOreTile;
            public int GoldDeposits = 6;
            public TileBase IronOreTile;
            public int IronDeposits = 6;
            public TileBase CrystalOreTile;
            public int CrystalDeposits = 3;
```

- [ ] **Step 2: Run an ore pass at the end of Place**

Find the end of `Place`:

```csharp
                var tile = pool[rng.NextInt(0, pool.Length)];
                _decorationMap.SetTile(new Vector3Int(x, y, 0), tile);
            }
            _decorationMap.RefreshAllTiles();
        }
```

Replace with:

```csharp
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
```

(`Random` here is `Unity.Mathematics.Random`, already aliased at the top of the file as `using Random = Unity.Mathematics.Random;`. `Biome` is in `RTSCL.World`, already imported via `using RTSCL.World;`.)

- [ ] **Step 3: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/DecorationPlacer.cs
git commit -m "feat(ore): DecorationPlacer sparse seed-deterministic ore deposit pass"
```

---

## Task 4: Scene wiring — decoration config tiles + ResourceUI counters

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

Via Unity MCP. Wire the 3 ore tiles into the WorldGenerator's `_decorationConfig`, and append Gold/Iron/Crystal counters to ResourceUI.

- [ ] **Step 1: Wire ore tiles into _decorationConfig via Unity MCP**

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var wgb = Object.FindFirstObjectByType<RTSCL.World.Unity.WorldGeneratorBootstrap>();
        if (wgb == null) { result.LogError("WorldGeneratorBootstrap not found"); return; }

        var gold = AssetDatabase.LoadAssetAtPath<Tile>("Assets/Generated/Tiles/Decorations/GoldOre_0.asset");
        var iron = AssetDatabase.LoadAssetAtPath<Tile>("Assets/Generated/Tiles/Decorations/IronOre_0.asset");
        var crystal = AssetDatabase.LoadAssetAtPath<Tile>("Assets/Generated/Tiles/Decorations/CrystalOre_0.asset");
        if (gold == null || iron == null || crystal == null) { result.LogError("Ore tile asset(s) missing"); return; }

        var so = new SerializedObject(wgb);
        var cfg = so.FindProperty("_decorationConfig");
        cfg.FindPropertyRelative("GoldOreTile").objectReferenceValue = gold;
        cfg.FindPropertyRelative("IronOreTile").objectReferenceValue = iron;
        cfg.FindPropertyRelative("CrystalOreTile").objectReferenceValue = crystal;
        // counts default to 6/6/3 from the field initializers but set explicitly in case the
        // serialized config predates these fields (Unity zero-fills new serialized ints).
        cfg.FindPropertyRelative("GoldDeposits").intValue = 6;
        cfg.FindPropertyRelative("IronDeposits").intValue = 6;
        cfg.FindPropertyRelative("CrystalDeposits").intValue = 3;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("Ore tiles + counts wired into _decorationConfig");
    }
}
```

Note: new serialized fields on an already-serialized config deserialize to zero, so explicitly setting the counts is required (the C# field initializers `= 6` do NOT apply to already-serialized instances).

- [ ] **Step 2: Append Gold/Iron/Crystal to ResourceUI `_kinds` via Unity MCP**

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var rui = Object.FindFirstObjectByType<RTSCL.World.Unity.ResourceUI>();
        if (rui == null) { result.LogError("ResourceUI not found"); return; }

        Sprite Sub(string name)
        {
            foreach (var s in AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/MiniWorldSprites/Buildings/Wood/Resources.png"))
                if (s is Sprite sp && sp.name == name) return sp;
            return null;
        }
        var gold = Sub("Resources_0");
        var iron = Sub("Resources_5");
        var crystal = Sub("Resources_2");
        if (gold == null || iron == null || crystal == null) { result.LogError("Ore sprite(s) missing"); return; }

        var so = new SerializedObject(rui);
        var kinds = so.FindProperty("_kinds");

        // Remove any pre-existing Gold/Iron/Crystal entries first (idempotent re-run).
        for (int i = kinds.arraySize - 1; i >= 0; i--)
        {
            int k = kinds.GetArrayElementAtIndex(i).FindPropertyRelative("Kind").enumValueIndex;
            if (k >= 3) kinds.DeleteArrayElementAtIndex(i); // Gold=3, Iron=4, Crystal=5
        }

        void AddKind(int kindEnum, Sprite icon)
        {
            int idx = kinds.arraySize;
            kinds.InsertArrayElementAtIndex(idx);
            var el = kinds.GetArrayElementAtIndex(idx);
            el.FindPropertyRelative("Kind").enumValueIndex = kindEnum;
            el.FindPropertyRelative("Icon").objectReferenceValue = icon;
        }
        AddKind(3, gold);    // Gold
        AddKind(4, iron);    // Iron
        AddKind(5, crystal); // Crystal

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log($"ResourceUI _kinds now has {kinds.arraySize} entries (Wood/Food/Stone/Gold/Iron/Crystal)");
    }
}
```

- [ ] **Step 3: Verify console — expect both wiring logs, 0 errors**

`Unity_ReadConsole` Types=["Error"]. Expect 0. The second log should report 6 `_kinds` entries.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "feat(ore): wire ore tiles into decoration config + 3 ResourceUI counters"
```

---

## Task 5: Final compile + smoke verification

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

TestRunnerApi + Temp file callback. Expect 36/36 pass.

- [ ] **Step 3: Solo smoke (user manually)**

User runs Play Solo:
- Six counters: Wood, Food, Stone, Gold, Iron, Crystal (with icons).
- Gold/Iron/Crystal deposits scattered on the map (away from spawn).
- Right-click an ore → Farmer mines it (HP 200, slower), carry shows "Carrying: N gold" etc.
- Deposit at keep → matching counter rises.
- Carrying stone + right-click gold → deposit stone first, then mine gold.
- Ore depleted → particle burst, tile gone.
- New solo game → ore re-placed (different cells, same seed → same layout).

- [ ] **Step 4: No commit unless a fix was needed**

If smoke passes: no further commits.

---

## Plan Self-Review Notes

**Spec coverage:**
- 3 ore Tile assets (Task 2)
- Goblin IsOreTile/KindOf/MaxHpFor/IsHarvestable (Task 1)
- DecorationPlacer ore fields + sparse pass (Task 3)
- Scene: decoration-config tile wiring + ResourceUI counters (Task 4)
- Stone/Wood/Food unaffected; ore harvested via generic Phase 1 path

**Placeholder scan:** none.

**Type consistency:**
- Tile names `GoldOre_0`/`IronOre_0`/`CrystalOre_0` — created Task 2; matched by `KindOf`/`IsOreTile` prefixes (Task 1)
- `ResourceKind.Gold=3/Iron=4/Crystal=5` — used in KindOf (Task 1) + scene enum indices (Task 4)
- `DecorationConfig.GoldOreTile/IronOreTile/CrystalOreTile` + `GoldDeposits/IronDeposits/CrystalDeposits` — defined Task 3; wired Task 4
- `PlaceOre(WorldData, TileBase, int, bool[,], ref Random)` — Task 3 internal
- `ResourceUI._kinds` KindIcon entries — Phase 1 structure; appended Task 4

**Ordering / compile-clean:**
- Task 1 (Goblin) compiles alone (ore tiles don't need to exist for code to compile — classification is string-based).
- Task 2 (assets) independent.
- Task 3 (DecorationPlacer) compiles alone (fields + method).
- Task 4 (scene) needs Tasks 2 + 3 done (tiles + config fields exist). Sequenced last before smoke.

**Determinism:** ore pass uses the same seeded `rng` after the decoration loop — same seed → same deposits on every client (MP-safe).

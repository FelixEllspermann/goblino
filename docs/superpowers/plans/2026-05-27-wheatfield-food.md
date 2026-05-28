# Wheatfield Food Harvest + Info-Panel Carry Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Food resource via Wheatfield harvest (1 food/tick, 500 HP per field), big destruction-burst on trees + wheatfields, move carry display from overhead text to ObjectInspector info panel.

**Architecture:** Generalize TreeHP to accept per-call maxHP. Extend Goblin's harvest FSM to branch tile-kind (tree → wood, wheatfield → food). Add CarriedFood inventory with mutual-exclusion vs wood (auto-deposit on type mismatch). Replace GoblinCarryText overhead with an inline "Carrying: X wood/food" line in ObjectInspector's description while a single Farmer is selected.

**Tech Stack:** Unity 6 / C# / Steamworks.NET / RTSCL.World.Unity asmdef

**Spec:** `docs/superpowers/specs/2026-05-27-wheatfield-food-design.md`

---

## File Structure

### Modified

| File | Change |
|---|---|
| `Assets/Scripts/World/Unity/ResourceBank.cs` | Add `Food`, `AddFood`, `OnFoodChanged`. Reset clears Food. |
| `Assets/Scripts/World/Unity/ResourceUI.cs` | Add `_foodLabel` SerializeField + subscriber. |
| `Assets/Scripts/World/Unity/TreeHP.cs` | Add optional `maxHp` param to `Hit` + `GetHP`. |
| `Assets/Scripts/World/Unity/TreeHitEffect.cs` | Add `SpawnBurst` for the destruction effect. |
| `Assets/Scripts/World/Unity/Goblin.cs` | `CarriedFood`, `IsWheatfieldTile`, `IsHarvestable`, `IsTreeStillThere` → `IsHarvestableStillThere`, `HitTree` → `HitHarvestable` (tile-kind branch + destruction burst), mutual-exclusion in `SetHarvestCommand`, deposit both slots, remove `GoblinCarryText.AttachTo`. |
| `Assets/Scripts/World/Unity/GoblinSelectionController.cs` | `TryGetTreeAt` → `TryGetHarvestableAt`, `FindNearbyTrees` → `FindNearbyHarvestables`. |
| `Assets/Scripts/World/Unity/ObjectInspector.cs` | Carry-info appended to single-Farmer description; polled in Update. |
| `Assets/Scenes/SampleScene.unity` | Add Food label UI + wire to ResourceUI._foodLabel. |

### Deleted

| File | Reason |
|---|---|
| `Assets/Scripts/World/Unity/GoblinCarryText.cs` (+ `.meta`) | Replaced by ObjectInspector display. |

---

## Task 1: ResourceBank + ResourceUI Food extension

**Files:**
- Modify: `Assets/Scripts/World/Unity/ResourceBank.cs`
- Modify: `Assets/Scripts/World/Unity/ResourceUI.cs`

- [ ] **Step 1: Extend ResourceBank.cs**

Replace the file content with:

```csharp
using System;

namespace RTSCL.World.Unity
{
    public static class ResourceBank
    {
        public static int Wood { get; private set; }
        public static int Food { get; private set; }

        public static event Action<int> OnWoodChanged;
        public static event Action<int> OnFoodChanged;

        public static void AddWood(int amount)
        {
            Wood += amount;
            OnWoodChanged?.Invoke(Wood);
        }

        public static void AddFood(int amount)
        {
            Food += amount;
            OnFoodChanged?.Invoke(Food);
        }

        public static void Reset()
        {
            Wood = 0;
            Food = 0;
            OnWoodChanged?.Invoke(Wood);
            OnFoodChanged?.Invoke(Food);
        }
    }
}
```

- [ ] **Step 2: Extend ResourceUI.cs**

Replace the file content with:

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    public sealed class ResourceUI : MonoBehaviour
    {
        [SerializeField] private Text _woodLabel;
        [SerializeField] private Text _foodLabel;
        [SerializeField] private Text _populationLabel;

        private void OnEnable()
        {
            ResourceBank.OnWoodChanged += UpdateWood;
            ResourceBank.OnFoodChanged += UpdateFood;
            PopulationManager.OnChanged += UpdatePopulation;
            UpdateWood(ResourceBank.Wood);
            UpdateFood(ResourceBank.Food);
            UpdatePopulation();
        }

        private void OnDisable()
        {
            ResourceBank.OnWoodChanged -= UpdateWood;
            ResourceBank.OnFoodChanged -= UpdateFood;
            PopulationManager.OnChanged -= UpdatePopulation;
        }

        private void UpdateWood(int wood)
        {
            if (_woodLabel != null) _woodLabel.text = wood.ToString();
        }

        private void UpdateFood(int food)
        {
            if (_foodLabel != null) _foodLabel.text = food.ToString();
        }

        private void UpdatePopulation()
        {
            if (_populationLabel != null)
                _populationLabel.text = $"{PopulationManager.Used} / {PopulationManager.Cap}";
        }
    }
}
```

- [ ] **Step 3: Refresh + compile check via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `Unity_ReadConsole` Types=["Error"]. Expect 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/ResourceBank.cs Assets/Scripts/World/Unity/ResourceUI.cs
git commit -m "feat(food): ResourceBank.Food + ResourceUI _foodLabel"
```

---

## Task 2: TreeHP optional maxHp parameter

**Files:**
- Modify: `Assets/Scripts/World/Unity/TreeHP.cs`

- [ ] **Step 1: Add maxHp param to GetHP + Hit**

Replace the file content with:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    public static class TreeHP
    {
        public const int MaxHP = 50;
        private static readonly Dictionary<Vector3Int, int> _hp = new();

        public static int GetHP(Vector3Int cell, int maxHp = MaxHP) =>
            _hp.TryGetValue(cell, out var hp) ? hp : maxHp;

        /// <summary>Returns remaining HP after the hit (0 = dead). `maxHp` defines lazy-init for unseen cells.</summary>
        public static int Hit(Vector3Int cell, int amount = 1, int maxHp = MaxHP)
        {
            int next = Mathf.Max(0, GetHP(cell, maxHp) - amount);
            if (next > 0) _hp[cell] = next;
            else _hp.Remove(cell);
            return next;
        }

        public static void Clear() => _hp.Clear();
    }
}
```

- [ ] **Step 2: Refresh + compile check**

Expect 0 errors. Existing `TreeHP.Hit(cell, 1)` callers continue to work via default param.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/TreeHP.cs
git commit -m "feat(food): TreeHP.Hit accepts optional maxHp for variable harvest pools"
```

---

## Task 3: TreeHitEffect.SpawnBurst

**Files:**
- Modify: `Assets/Scripts/World/Unity/TreeHitEffect.cs`

The existing `Spawn` produces a single squashy flash sprite. `SpawnBurst` spawns 5 of them in a spread for the destruction visual.

- [ ] **Step 1: Add SpawnBurst static method**

Find the existing `Spawn` method. After it (before the `private void Update()`), insert:

```csharp
public static void SpawnBurst(Tilemap decorationMap, Vector3Int cell, Sprite sprite)
{
    if (sprite == null) return;
    var basePos = decorationMap.CellToWorld(cell) + new Vector3(0.5f, 0.5f, 0f);
    const int Count = 5;
    const float Radius = 0.35f;
    for (int i = 0; i < Count; i++)
    {
        float angle = (i / (float)Count) * Mathf.PI * 2f;
        Vector3 offset = new(Mathf.Cos(angle) * Radius, Mathf.Sin(angle) * Radius, 0f);
        var go = new GameObject("TreeHitFx_Burst");
        go.transform.position = basePos + offset;
        var fx = go.AddComponent<TreeHitEffect>();
        fx._renderer = go.AddComponent<SpriteRenderer>();
        fx._renderer.sprite = sprite;
        fx._renderer.sortingOrder = 40;
        fx._renderer.color = new Color(3f, 3f, 3f, 1f);
    }
}
```

- [ ] **Step 2: Refresh + compile check**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/TreeHitEffect.cs
git commit -m "feat(food): TreeHitEffect.SpawnBurst for tree + wheatfield destruction"
```

---

## Task 4: Goblin harvest infrastructure (helpers + rename)

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

This task adds the tile-kind detection helpers and renames `IsTreeStillThere` → `IsHarvestableStillThere` so wheatfields satisfy the "still there" check too. No FSM changes yet — those are in Task 5.

- [ ] **Step 1: Add `IsWheatfieldTile` + `IsHarvestable` after `IsTreeTile`**

Find the existing `IsTreeTile` method (around line 569):

```csharp
public static bool IsTreeTile(string tileName)
{
    return tileName.StartsWith("Trees_")
        || tileName.StartsWith("PineTrees_")
        || tileName.StartsWith("WinterTrees_")
        || tileName.StartsWith("WinterDeadTrees_")
        || tileName.StartsWith("DeadTrees_")
        || tileName.StartsWith("CoconutTrees_");
}
```

Immediately after it, add:

```csharp
public static bool IsWheatfieldTile(string tileName) =>
    tileName.StartsWith("Wheatfield_");

public static bool IsHarvestable(string tileName) =>
    IsTreeTile(tileName) || IsWheatfieldTile(tileName);
```

- [ ] **Step 2: Rename `IsTreeStillThere` to `IsHarvestableStillThere`**

Find (around line 542):

```csharp
private bool IsTreeStillThere(Vector3Int cell)
{
    if (_decorationMap == null) return false;
    var t = _decorationMap.GetTile(cell);
    return t != null && IsTreeTile(t.name);
}
```

Replace with:

```csharp
private bool IsHarvestableStillThere(Vector3Int cell)
{
    if (_decorationMap == null) return false;
    var t = _decorationMap.GetTile(cell);
    return t != null && IsHarvestable(t.name);
}
```

- [ ] **Step 3: Update all call sites in Goblin.cs**

The previous step renamed the method. Update every existing reference. There are 4 call sites in Goblin.cs (in MovingToTree, Harvesting, WalkingToDeposit resume, and FindNextTreeOrIdle).

After the rename, do a search-replace within Goblin.cs:

- `IsTreeStillThere(` → `IsHarvestableStillThere(`

Verify all 4 references updated.

- [ ] **Step 4: Refresh + compile check via Unity MCP**

Expect 0 errors (helpers added + rename is consistent within the file).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(food): Goblin harvest helpers + IsTreeStillThere → IsHarvestableStillThere rename"
```

---

## Task 5: Goblin HitHarvestable + CarriedFood + mutual exclusion

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

The meaty change: dual inventory + tile-kind branching + auto-deposit on mismatch.

- [ ] **Step 1: Add `CarriedFood` property**

Find the existing `CarriedWood` declaration:

```csharp
private const int MaxCarriedWood = 10;
public int CarriedWood { get; private set; }
```

Right after, add:

```csharp
public int CarriedFood { get; private set; }
```

(`MaxCarriedWood = 10` is reused as the cap for food too — spec calls this out.)

- [ ] **Step 2: Rename `HitTree` → `HitHarvestable` and branch on tile-kind**

Find the existing `HitTree(Vector3Int cell)` method (around line 363):

```csharp
private bool HitTree(Vector3Int cell)
{
    var tile = _decorationMap.GetTile(cell) as UnityEngine.Tilemaps.Tile;
    var sprite = tile != null ? tile.sprite : null;

    int remaining = TreeHP.Hit(cell, 1);
    CarriedWood = Mathf.Min(MaxCarriedWood, CarriedWood + WoodPerHit);
    StartHitAnim(cell);
    TreeHitEffect.Spawn(_decorationMap, cell, sprite);

    if (remaining <= 0)
    {
        _decorationMap.SetTile(cell, null);
        return true;
    }
    return false;
}
```

Replace with:

```csharp
private bool HitHarvestable(Vector3Int cell)
{
    var tile = _decorationMap.GetTile(cell) as UnityEngine.Tilemaps.Tile;
    var sprite = tile != null ? tile.sprite : null;
    string tileName = tile != null ? tile.name : "";

    bool isWheatfield = IsWheatfieldTile(tileName);
    int maxHp = isWheatfield ? 500 : TreeHP.MaxHP;
    int remaining = TreeHP.Hit(cell, 1, maxHp);

    if (isWheatfield) CarriedFood = Mathf.Min(MaxCarriedWood, CarriedFood + 1);
    else              CarriedWood = Mathf.Min(MaxCarriedWood, CarriedWood + 1);

    StartHitAnim(cell);
    TreeHitEffect.Spawn(_decorationMap, cell, sprite);

    if (remaining <= 0)
    {
        _decorationMap.SetTile(cell, null);
        TreeHitEffect.SpawnBurst(_decorationMap, cell, sprite);
        return true;
    }
    return false;
}
```

- [ ] **Step 3: Update Harvesting case to use new method + dual cap check**

Find the `case State.Harvesting:` block (around line 256). The body should currently contain:

```csharp
bool destroyed = HitTree(_treeCell);
if (CarriedWood >= MaxCarriedWood || destroyed)
    TryStartDepositRun();
```

Replace those two lines with:

```csharp
bool destroyed = HitHarvestable(_treeCell);
if (CarriedWood >= MaxCarriedWood || CarriedFood >= MaxCarriedWood || destroyed)
    TryStartDepositRun();
```

- [ ] **Step 4: Update WalkingToDeposit case to deposit both slots**

Find the `case State.WalkingToDeposit:` block. The current arrival logic deposits CarriedWood. Replace:

```csharp
ResourceBank.AddWood(CarriedWood);
CarriedWood = 0;
```

With:

```csharp
if (CarriedWood > 0) ResourceBank.AddWood(CarriedWood);
if (CarriedFood > 0) ResourceBank.AddFood(CarriedFood);
CarriedWood = 0;
CarriedFood = 0;
```

- [ ] **Step 5: Mutual-exclusion in `SetHarvestCommand`**

Find the existing method (line ~131):

```csharp
public void SetHarvestCommand(Vector3Int treeCell)
{
    if (_state == State.Dying) return;
    ResetHitAnim();
    _treeCell = treeCell;
    _moveTarget = FindAdjacentStandingSpot(treeCell);
    _state = State.MovingToTree;
    _harvestTimer = 0f;
}
```

Replace with (auto-deposit on type-mismatch):

```csharp
public void SetHarvestCommand(Vector3Int treeCell)
{
    if (_state == State.Dying) return;
    ResetHitAnim();

    // Mutual exclusion: if carrying the opposite resource, deposit first then resume to this cell.
    var tile = _decorationMap != null ? _decorationMap.GetTile(treeCell) : null;
    bool targetIsWheat = tile != null && IsWheatfieldTile(tile.name);
    bool carryingOppositeWood = CarriedWood > 0 && targetIsWheat;
    bool carryingOppositeFood = CarriedFood > 0 && !targetIsWheat;
    if (carryingOppositeWood || carryingOppositeFood)
    {
        _treeCell = treeCell;
        TryStartDepositRun();    // sets _state = WalkingToDeposit and broadcasts move; resume targets _treeCell
        return;
    }

    _treeCell = treeCell;
    _moveTarget = FindAdjacentStandingSpot(treeCell);
    _state = State.MovingToTree;
    _harvestTimer = 0f;
}
```

- [ ] **Step 6: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(food): HitHarvestable + CarriedFood + mutual-exclusion auto-deposit"
```

---

## Task 6: GoblinSelectionController accepts wheatfields

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSelectionController.cs`

- [ ] **Step 1: Rename `TryGetTreeAt` → `TryGetHarvestableAt`**

Find (line ~109):

```csharp
private bool TryGetTreeAt(Vector3 world, out Vector3Int cell)
{
    cell = default;
    if (_decorationMap == null) return false;
    cell = _decorationMap.WorldToCell(world);
    var t = _decorationMap.GetTile(cell);
    return t != null && Goblin.IsTreeTile(t.name);
}
```

Replace with:

```csharp
private bool TryGetHarvestableAt(Vector3 world, out Vector3Int cell)
{
    cell = default;
    if (_decorationMap == null) return false;
    cell = _decorationMap.WorldToCell(world);
    var t = _decorationMap.GetTile(cell);
    return t != null && Goblin.IsHarvestable(t.name);
}
```

- [ ] **Step 2: Update the call site in Update**

Find the call site in `Update` (right-click handling):

```csharp
if (TryGetTreeAt(worldTarget, out var treeCell))
```

Replace with:

```csharp
if (TryGetHarvestableAt(worldTarget, out var treeCell))
```

- [ ] **Step 3: Rename `FindNearbyTrees` → `FindNearbyHarvestables`**

Find (line ~144):

```csharp
private List<Vector3Int> FindNearbyTrees(Vector3Int origin, int maxCount, int radius)
{
    var found = new List<(Vector3Int cell, int distSq)>();
    for (int dy = -radius; dy <= radius; dy++)
    for (int dx = -radius; dx <= radius; dx++)
    {
        var c = new Vector3Int(origin.x + dx, origin.y + dy, 0);
        var t = _decorationMap.GetTile(c);
        if (t == null) continue;
        if (!Goblin.IsTreeTile(t.name)) continue;
        found.Add((c, dx * dx + dy * dy));
    }
    found.Sort((a, b) => a.distSq.CompareTo(b.distSq));
    var result = new List<Vector3Int>(System.Math.Min(maxCount, found.Count));
    for (int i = 0; i < found.Count && i < maxCount; i++) result.Add(found[i].cell);
    return result;
}
```

Replace with:

```csharp
private List<Vector3Int> FindNearbyHarvestables(Vector3Int origin, int maxCount, int radius)
{
    var found = new List<(Vector3Int cell, int distSq)>();
    for (int dy = -radius; dy <= radius; dy++)
    for (int dx = -radius; dx <= radius; dx++)
    {
        var c = new Vector3Int(origin.x + dx, origin.y + dy, 0);
        var t = _decorationMap.GetTile(c);
        if (t == null) continue;
        if (!Goblin.IsHarvestable(t.name)) continue;
        found.Add((c, dx * dx + dy * dy));
    }
    found.Sort((a, b) => a.distSq.CompareTo(b.distSq));
    var result = new List<Vector3Int>(System.Math.Min(maxCount, found.Count));
    for (int i = 0; i < found.Count && i < maxCount; i++) result.Add(found[i].cell);
    return result;
}
```

- [ ] **Step 4: Update the caller in CommandHarvest**

Find (line ~126):

```csharp
var trees = FindNearbyTrees(clickedTree, workers.Count, _harvestSpreadRadius);
```

Replace with:

```csharp
var trees = FindNearbyHarvestables(clickedTree, workers.Count, _harvestSpreadRadius);
```

- [ ] **Step 5: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSelectionController.cs
git commit -m "feat(food): selection controller accepts wheatfields as harvest targets"
```

---

## Task 7: ObjectInspector carry display + delete GoblinCarryText

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs` (remove the AttachTo call)
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs` (poll + describe)
- Delete: `Assets/Scripts/World/Unity/GoblinCarryText.cs` + `.meta`

- [ ] **Step 1: Remove `GoblinCarryText.AttachTo(this);` from Goblin.Init**

In `Assets/Scripts/World/Unity/Goblin.cs`, find:

```csharp
BuildSelectionRing();
GoblinHealthBar.AttachTo(this);
GoblinCarryText.AttachTo(this);
```

Replace with:

```csharp
BuildSelectionRing();
GoblinHealthBar.AttachTo(this);
```

- [ ] **Step 2: Delete GoblinCarryText.cs + meta**

```bash
git rm Assets/Scripts/World/Unity/GoblinCarryText.cs Assets/Scripts/World/Unity/GoblinCarryText.cs.meta
```

- [ ] **Step 3: Add carry state to ObjectInspector**

In `Assets/Scripts/World/Unity/ObjectInspector.cs`, find the existing fields block (after `_selBuildingOwner`). Add two new private fields:

```csharp
private string _lastCarryLine = "";
private string _lastGoblinDescBase = "";
```

`_lastCarryLine` is the most recently appended line ("Carrying: 5 wood" etc.), `_lastGoblinDescBase` is the base description (without the carry line) for rebuilding cheaply.

- [ ] **Step 4: Capture base description in `ShowGoblinSelection`**

Find `ShowGoblinSelection` and the existing `SetHeader(name, desc);` line. Right BEFORE it, store the base:

```csharp
_lastGoblinDescBase = desc;
_lastCarryLine = "";
SetHeader(name, desc);
```

(Replace the existing `SetHeader(name, desc);` with this 3-line block.)

- [ ] **Step 5: Poll carry state in Update**

Find the existing `Update()` method. After the existing `if (_selKind == SelKind.Building) UpdateProgressUI();` line, add:

```csharp
if (_selKind == SelKind.Goblins) UpdateCarryUI();
```

- [ ] **Step 6: Add UpdateCarryUI method**

After the existing `UpdateProgressUI()` method, add:

```csharp
private void UpdateCarryUI()
{
    if (_selectionController == null) return;
    var sel = _selectionController.Selection;
    string newCarry = "";
    if (sel.Count == 1 && sel[0] != null && sel[0].Kind == "FarmerGoblin")
    {
        int wood = sel[0].CarriedWood;
        int food = sel[0].CarriedFood;
        if (wood > 0) newCarry = $"Carrying: {wood} wood";
        else if (food > 0) newCarry = $"Carrying: {food} food";
    }
    if (newCarry == _lastCarryLine) return;
    _lastCarryLine = newCarry;
    string full = string.IsNullOrEmpty(newCarry)
        ? _lastGoblinDescBase
        : _lastGoblinDescBase + "\n" + newCarry;
    if (_descriptionLabel != null) _descriptionLabel.text = full;
}
```

- [ ] **Step 7: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "feat(food): carry display moves from overhead text to ObjectInspector"
```

(The `git rm` from Step 2 is already staged.)

---

## Task 8: Scene UI Food label

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

Driven via Unity MCP to avoid hand-editing YAML.

- [ ] **Step 1: Find existing Wood label hierarchy via Unity MCP**

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        foreach (var go in scene.GetRootGameObjects())
        {
            var rui = go.GetComponentInChildren<RTSCL.World.Unity.ResourceUI>(true);
            if (rui != null)
            {
                result.Log($"Found ResourceUI at {go.name}/{rui.transform.GetSiblingIndex()}");
                var so = new SerializedObject(rui);
                var woodProp = so.FindProperty("_woodLabel");
                var foodProp = so.FindProperty("_foodLabel");
                result.Log($"_woodLabel set: {woodProp.objectReferenceValue != null} | _foodLabel set: {foodProp.objectReferenceValue != null}");
                if (woodProp.objectReferenceValue is Text woodLabel)
                {
                    result.Log($"Wood label name: {woodLabel.name}, parent: {woodLabel.transform.parent?.name}");
                }
                return;
            }
        }
        result.Log("ResourceUI not found");
    }
}
```

Use `EditorSceneManager` (not `SceneManager`) so the scene loads in the editor. Add `using UnityEditor.SceneManagement;` at the top.

- [ ] **Step 2: Duplicate the Wood label as Food label + wire the SerializeField**

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        RTSCL.World.Unity.ResourceUI rui = null;
        foreach (var go in scene.GetRootGameObjects())
        {
            rui = go.GetComponentInChildren<RTSCL.World.Unity.ResourceUI>(true);
            if (rui != null) break;
        }
        if (rui == null) { result.LogError("ResourceUI not found"); return; }

        var so = new SerializedObject(rui);
        var woodProp = so.FindProperty("_woodLabel");
        var foodProp = so.FindProperty("_foodLabel");
        if (woodProp.objectReferenceValue == null) { result.LogError("Wood label not assigned"); return; }
        if (foodProp.objectReferenceValue != null) { result.Log("Food label already wired"); return; }

        var woodLabel = (Text)woodProp.objectReferenceValue;
        var parent = woodLabel.transform.parent;
        var foodGo = Object.Instantiate(woodLabel.gameObject, parent);
        foodGo.name = "FoodLabel";
        // Offset below the wood label by 30 UI px
        var rt = foodGo.GetComponent<RectTransform>();
        var srcRt = woodLabel.GetComponent<RectTransform>();
        rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(0f, -30f);
        var foodText = foodGo.GetComponent<Text>();
        foodText.text = "0";
        foodProp.objectReferenceValue = foodText;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log($"Food label wired at {foodGo.name}");
    }
}
```

- [ ] **Step 3: Refresh + compile check**

Expect 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "feat(food): add Food label UI in SampleScene"
```

---

## Task 9: Final compile + smoke verification

**Files:** none changed.

- [ ] **Step 1: Full refresh + console clear via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

`Unity_ReadConsole` Types=["Error"]. Expect 0.

- [ ] **Step 2: Run RTSCL.World.Tests**

Use the existing TestRunnerApi pattern (e.g., `Temp/wheatfield_test_results.txt`). Expect 36/36 still pass.

- [ ] **Step 3: Solo smoke (user manually)**

User runs Play Solo:
- Build menu shows Hut / Barracks / Workshop
- Wood UI + Food UI both visible (Food = 0 at start)
- Right-click a tree → Farmer chops; carry shows in info panel ("Carrying: 1 wood" etc.)
- At 10 wood: walks to Keep, deposits, Wood UI +10
- Right-click a Wheatfield → Farmer walks there + chops
- "Carrying: 1 food", "2 food"... visible in info panel
- At 10 food: walks to Keep, deposits, Food UI +10
- Tree destruction shows 5-particle burst
- Wheatfield destruction (after 500 hits — or test with reduced HP temporarily) shows the same burst
- Carrying wood + right-click wheatfield → Farmer walks to Keep first, deposits, then walks to wheatfield
- No overhead carry text anywhere

- [ ] **Step 4: No commit unless a fix was needed**

If smoke passes: no further commits.

---

## Plan Self-Review Notes

**Spec coverage:**
- Food bank/UI/event (Task 1)
- TreeHP variable maxHp (Task 2)
- Destruction burst (Task 3)
- IsWheatfieldTile / IsHarvestable helpers (Task 4)
- HitHarvestable branching (Task 5 step 2)
- CarriedFood field (Task 5 step 1)
- Deposit both slots (Task 5 step 4)
- Mutual exclusion (Task 5 step 5)
- Selection accepts wheatfields (Task 6)
- Carry display in info panel (Task 7)
- Delete GoblinCarryText (Task 7 step 2)
- Scene UI wiring (Task 8)

**Placeholder scan:** no TBDs/TODOs/vague items. All code blocks complete.

**Type consistency:**
- `CarriedFood` (public int) used in Task 5 + Task 7
- `MaxCarriedWood = 10` reused as both caps (per spec)
- `IsWheatfieldTile`, `IsHarvestable` (public static) used in Task 4 + Task 5 + Task 6
- `IsHarvestableStillThere` (private bool) rename applied across all 4 call sites in Goblin.cs (Task 4 step 3)
- `HitHarvestable` (private bool) replaces `HitTree` in Task 5; callers updated in step 3
- `TreeHP.Hit(cell, amount, maxHp)` default param keeps existing callers working
- `TryGetHarvestableAt` / `FindNearbyHarvestables` renames done in Task 6 with consistent caller updates
- `_lastCarryLine`, `_lastGoblinDescBase`, `UpdateCarryUI` (private) in ObjectInspector — consistent within Task 7
- ResourceBank.AddFood + OnFoodChanged used in Task 1 only — no downstream dependents past ResourceUI

**Cross-task boundaries:**
- Task 4 (rename) → Task 5 (use new method) — Task 5 calls `IsWheatfieldTile` defined in Task 4. Dependency clean.
- Task 5 (CarriedFood) → Task 7 (poll CarriedFood) — Task 7 reads the public property. Dependency clean.
- Task 7 (delete GoblinCarryText) → Task 5/4 (no dependency on GoblinCarryText) — clean.
- Task 8 (scene) is independent of all code changes — runs anytime.

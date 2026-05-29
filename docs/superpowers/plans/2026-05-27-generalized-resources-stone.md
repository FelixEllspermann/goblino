# Generalized Resources + Stone Mining (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor Wood/Food into a `ResourceKind`-keyed resource system, then make Rocks harvestable for a new Stone resource.

**Architecture:** `ResourceBank` becomes array-backed with `Get(kind)`/`Add(kind,amt)`/`OnChanged(kind,value)` plus Wood/Food convenience accessors (kept so cost code is untouched). `Goblin` carries a single typed slot (`CarriedKind`+`CarriedAmount`). Tiles classify to a `ResourceKind` via `KindOf`. `ResourceUI` builds per-kind counters from a serialized kind→sprite map. Legacy `OnWoodChanged`/`OnFoodChanged` are kept during migration and removed in the final code task so every commit compiles.

**Tech Stack:** Unity 6 / C# / UGUI / RTSCL.World.Unity asmdef

**Spec:** `docs/superpowers/specs/2026-05-27-generalized-resources-stone-design.md`

---

## File Structure

### New
| File | Purpose |
|---|---|
| `Assets/Scripts/World/Unity/ResourceKind.cs` | `enum ResourceKind { Wood, Food, Stone, Gold, Iron, Crystal }` |

### Modified
| File | Change |
|---|---|
| `ResourceBank.cs` | array-backed + Get/Add/OnChanged + Wood/Food convenience (legacy events kept then removed) |
| `Goblin.cs` | CarriedKind/CarriedAmount, IsRockTile, IsHarvestable += rocks, KindOf, MaxHpFor, HitHarvestable rewrite, SetHarvestCommand generalized, deposit via Add |
| `ObjectInspector.cs` | UpdateCarryUI generic; Start → OnChanged |
| `ResourceUI.cs` | data-driven per-kind counters |
| `Assets/Scenes/SampleScene.unity` | ResourceUI countersRoot + kind→sprite (Wood/Food/Stone) wiring |

---

## Task 1: ResourceKind enum

**Files:**
- Create: `Assets/Scripts/World/Unity/ResourceKind.cs`

- [ ] **Step 1: Create the enum**

```csharp
namespace RTSCL.World.Unity
{
    public enum ResourceKind { Wood = 0, Food = 1, Stone = 2, Gold = 3, Iron = 4, Crystal = 5 }
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

Then `Unity_ReadConsole` Types=["Error"]. Expect 0.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/ResourceKind.cs Assets/Scripts/World/Unity/ResourceKind.cs.meta
git commit -m "feat(resource): ResourceKind enum"
```

---

## Task 2: ResourceBank array-backed refactor

**Files:**
- Modify: `Assets/Scripts/World/Unity/ResourceBank.cs`

Keeps `Wood`/`Food`/`AddWood`/`AddFood` AND the legacy `OnWoodChanged`/`OnFoodChanged` events (fired inside `Add`) so all current subscribers keep compiling + working. Adds the generic API.

- [ ] **Step 1: Replace ResourceBank.cs**

```csharp
using System;

namespace RTSCL.World.Unity
{
    public static class ResourceBank
    {
        private static readonly int[] _amounts = new int[6]; // ResourceKind count

        /// <summary>Fires (kind, newValue) on any change.</summary>
        public static event Action<ResourceKind, int> OnChanged;

        // Legacy single-resource events — kept during migration, removed in Task 4.
        public static event Action<int> OnWoodChanged;
        public static event Action<int> OnFoodChanged;

        public static int Get(ResourceKind k) => _amounts[(int)k];

        public static void Add(ResourceKind k, int amount)
        {
            _amounts[(int)k] += amount;
            int v = _amounts[(int)k];
            OnChanged?.Invoke(k, v);
            if (k == ResourceKind.Wood) OnWoodChanged?.Invoke(v);
            else if (k == ResourceKind.Food) OnFoodChanged?.Invoke(v);
        }

        public static void Reset()
        {
            for (int i = 0; i < _amounts.Length; i++) _amounts[i] = 0;
            for (int i = 0; i < _amounts.Length; i++) OnChanged?.Invoke((ResourceKind)i, 0);
            OnWoodChanged?.Invoke(0);
            OnFoodChanged?.Invoke(0);
        }

        // Convenience for existing call sites.
        public static int Wood => Get(ResourceKind.Wood);
        public static int Food => Get(ResourceKind.Food);
        public static void AddWood(int amount) => Add(ResourceKind.Wood, amount);
        public static void AddFood(int amount) => Add(ResourceKind.Food, amount);
    }
}
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors (all existing call sites — BuildingPlacer, ObjectInspector, Goblin, ResourceUI — still resolve via convenience + legacy events).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/ResourceBank.cs
git commit -m "feat(resource): ResourceBank array-backed with Get/Add/OnChanged + legacy shims"
```

---

## Task 3: Goblin generalized carry + Stone classification

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs` (UpdateCarryUI — coupled to the carry rename)

- [ ] **Step 1: Replace the carry fields**

In `Goblin.cs` find:

```csharp
        private const int WoodPerHit = 1;
        private const int MaxCarriedWood = 10;
        public int CarriedWood { get; private set; }
        public int CarriedFood { get; private set; }
```

Replace with:

```csharp
        private const int MaxCarried = 10;
        public ResourceKind CarriedKind { get; private set; }
        public int CarriedAmount { get; private set; }
```

(`WoodPerHit` is removed — yield is a literal `1` in the rewritten HitHarvestable.)

- [ ] **Step 2: Add classification helpers next to IsWheatfieldTile**

Find:

```csharp
        public static bool IsWheatfieldTile(string tileName) =>
            tileName.StartsWith("Wheatfield_");

        public static bool IsHarvestable(string tileName) =>
            IsTreeTile(tileName) || IsWheatfieldTile(tileName);
```

Replace with:

```csharp
        public static bool IsWheatfieldTile(string tileName) =>
            tileName.StartsWith("Wheatfield_");

        public static bool IsRockTile(string tileName) =>
            tileName.StartsWith("Rocks_");

        public static bool IsHarvestable(string tileName) =>
            IsTreeTile(tileName) || IsWheatfieldTile(tileName) || IsRockTile(tileName);

        public static ResourceKind KindOf(string tileName)
        {
            if (IsWheatfieldTile(tileName)) return ResourceKind.Food;
            if (IsRockTile(tileName)) return ResourceKind.Stone;
            return ResourceKind.Wood; // trees + default
        }

        private static int MaxHpFor(string tileName)
        {
            if (IsWheatfieldTile(tileName)) return 500;
            if (IsRockTile(tileName)) return 100;
            return TreeHP.MaxHP; // trees = 50
        }
```

- [ ] **Step 3: Rewrite HitHarvestable**

Find the current `HitHarvestable`:

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

Replace with:

```csharp
        private bool HitHarvestable(Vector3Int cell)
        {
            var tile = _decorationMap.GetTile(cell) as UnityEngine.Tilemaps.Tile;
            var sprite = tile != null ? tile.sprite : null;
            string tileName = tile != null ? tile.name : "";

            int remaining = TreeHP.Hit(cell, 1, MaxHpFor(tileName));
            CarriedKind = KindOf(tileName);
            CarriedAmount = Mathf.Min(MaxCarried, CarriedAmount + 1);

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

- [ ] **Step 4: Update the Harvesting case**

Find:

```csharp
                    if (!IsHarvestableStillThere(_treeCell))
                    {
                        ResetHitAnim();
                        if (CarriedWood > 0) TryStartDepositRun();
                        else FindNextTreeOrIdle();
                        break;
                    }
                    _harvestTimer += Time.deltaTime;
                    if (_harvestTimer >= _chopTickDuration)
                    {
                        _harvestTimer = 0f;
                        bool destroyed = HitHarvestable(_treeCell);
                        if (CarriedWood >= MaxCarriedWood || CarriedFood >= MaxCarriedWood || destroyed)
                            TryStartDepositRun();
                    }
```

Replace with:

```csharp
                    if (!IsHarvestableStillThere(_treeCell))
                    {
                        ResetHitAnim();
                        if (CarriedAmount > 0) TryStartDepositRun();
                        else FindNextTreeOrIdle();
                        break;
                    }
                    _harvestTimer += Time.deltaTime;
                    if (_harvestTimer >= _chopTickDuration)
                    {
                        _harvestTimer = 0f;
                        bool destroyed = HitHarvestable(_treeCell);
                        if (CarriedAmount >= MaxCarried || destroyed)
                            TryStartDepositRun();
                    }
```

- [ ] **Step 5: Update the WalkingToDeposit deposit**

Find:

```csharp
                        // Arrived at keep — deposit.
                        if (CarriedWood > 0) ResourceBank.AddWood(CarriedWood);
                        if (CarriedFood > 0) ResourceBank.AddFood(CarriedFood);
                        CarriedWood = 0;
                        CarriedFood = 0;
```

Replace with:

```csharp
                        // Arrived at keep — deposit.
                        if (CarriedAmount > 0) ResourceBank.Add(CarriedKind, CarriedAmount);
                        CarriedAmount = 0;
```

- [ ] **Step 6: Generalize SetHarvestCommand mutual exclusion**

Find:

```csharp
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
```

Replace with:

```csharp
            // Mutual exclusion: if carrying a different resource kind, deposit first then resume to this cell.
            var tile = _decorationMap != null ? _decorationMap.GetTile(treeCell) : null;
            if (tile != null && CarriedAmount > 0 && KindOf(tile.name) != CarriedKind)
            {
                _treeCell = treeCell;
                TryStartDepositRun();    // sets _state = WalkingToDeposit and broadcasts move; resume targets _treeCell
                return;
            }
```

- [ ] **Step 7: Update ObjectInspector.UpdateCarryUI**

In `ObjectInspector.cs` find:

```csharp
            if (sel.Count == 1 && sel[0] != null && sel[0].Kind == "FarmerGoblin")
            {
                int wood = sel[0].CarriedWood;
                int food = sel[0].CarriedFood;
                if (wood > 0) newCarry = $"Carrying: {wood} wood";
                else if (food > 0) newCarry = $"Carrying: {food} food";
            }
```

Replace with:

```csharp
            if (sel.Count == 1 && sel[0] != null && sel[0].Kind == "FarmerGoblin" && sel[0].CarriedAmount > 0)
            {
                newCarry = $"Carrying: {sel[0].CarriedAmount} {sel[0].CarriedKind.ToString().ToLowerInvariant()}";
            }
```

- [ ] **Step 8: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "feat(resource): Goblin single typed carry slot + Rocks→Stone classification"
```

---

## Task 4: ResourceUI data-driven + event-migration cleanup

**Files:**
- Modify: `Assets/Scripts/World/Unity/ResourceUI.cs`
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs` (Start subscription)
- Modify: `Assets/Scripts/World/Unity/ResourceBank.cs` (remove legacy events)

All event-migration lands in one commit so removing the legacy events is safe.

- [ ] **Step 1: Replace ResourceUI.cs with the data-driven version**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    public sealed class ResourceUI : MonoBehaviour
    {
        [System.Serializable]
        public sealed class KindIcon
        {
            public ResourceKind Kind;
            public Sprite Icon;
        }

        [SerializeField] private RectTransform _countersRoot;
        [SerializeField] private Text _populationLabel;
        [SerializeField] private Font _font;
        [SerializeField] private List<KindIcon> _kinds = new();

        [Header("Layout")]
        [SerializeField] private float _rowHeight = 28f;
        [SerializeField] private float _iconSize = 20f;
        [SerializeField] private int _fontSize = 20;

        private readonly Dictionary<ResourceKind, Text> _texts = new();

        private void OnEnable()
        {
            BuildCounters();
            ResourceBank.OnChanged += OnResourceChanged;
            PopulationManager.OnChanged += UpdatePopulation;
            foreach (var ki in _kinds)
                if (_texts.TryGetValue(ki.Kind, out var t)) t.text = ResourceBank.Get(ki.Kind).ToString();
            UpdatePopulation();
        }

        private void OnDisable()
        {
            ResourceBank.OnChanged -= OnResourceChanged;
            PopulationManager.OnChanged -= UpdatePopulation;
        }

        private void BuildCounters()
        {
            if (_countersRoot == null || _texts.Count > 0) return;
            for (int i = 0; i < _kinds.Count; i++)
            {
                var ki = _kinds[i];
                float y = -i * _rowHeight;

                var iconGo = new GameObject($"{ki.Kind}Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(_countersRoot, false);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = new Vector2(0f, 1f); irt.anchorMax = new Vector2(0f, 1f); irt.pivot = new Vector2(0f, 1f);
                irt.sizeDelta = new Vector2(_iconSize, _iconSize);
                irt.anchoredPosition = new Vector2(0f, y);
                var img = iconGo.GetComponent<Image>();
                img.sprite = ki.Icon; img.preserveAspect = true; img.raycastTarget = false;

                var txtGo = new GameObject($"{ki.Kind}Count", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                txtGo.transform.SetParent(_countersRoot, false);
                var trt = (RectTransform)txtGo.transform;
                trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 1f);
                trt.sizeDelta = new Vector2(90f, _iconSize);
                trt.anchoredPosition = new Vector2(_iconSize + 6f, y);
                var txt = txtGo.GetComponent<Text>();
                txt.font = _font; txt.fontSize = _fontSize; txt.color = Color.white;
                txt.alignment = TextAnchor.MiddleLeft; txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.text = "0";
                _texts[ki.Kind] = txt;
            }
        }

        private void OnResourceChanged(ResourceKind kind, int value)
        {
            if (_texts.TryGetValue(kind, out var t)) t.text = value.ToString();
        }

        private void UpdatePopulation()
        {
            if (_populationLabel != null)
                _populationLabel.text = $"{PopulationManager.Used} / {PopulationManager.Cap}";
        }
    }
}
```

- [ ] **Step 2: Migrate ObjectInspector.Start to OnChanged**

In `ObjectInspector.cs` find:

```csharp
            ResourceBank.OnWoodChanged += _ => Refresh();
            ResourceBank.OnFoodChanged += _ => Refresh();
```

Replace with:

```csharp
            ResourceBank.OnChanged += (_, __) => Refresh();
```

- [ ] **Step 3: Remove legacy events from ResourceBank**

In `ResourceBank.cs` remove the two legacy event declarations and their invocations:

Remove these lines:
```csharp
        // Legacy single-resource events — kept during migration, removed in Task 4.
        public static event Action<int> OnWoodChanged;
        public static event Action<int> OnFoodChanged;
```

In `Add`, remove:
```csharp
            if (k == ResourceKind.Wood) OnWoodChanged?.Invoke(v);
            else if (k == ResourceKind.Food) OnFoodChanged?.Invoke(v);
```

In `Reset`, remove:
```csharp
            OnWoodChanged?.Invoke(0);
            OnFoodChanged?.Invoke(0);
```

The `Add` method keeps the `int v = _amounts[(int)k];` line + `OnChanged?.Invoke(k, v);`.

- [ ] **Step 4: Refresh + compile check via Unity MCP**

Expect 0 errors (no remaining references to the legacy events).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/ResourceUI.cs Assets/Scripts/World/Unity/ObjectInspector.cs Assets/Scripts/World/Unity/ResourceBank.cs
git commit -m "feat(resource): data-driven ResourceUI + migrate subscribers to OnChanged + drop legacy events"
```

---

## Task 5: Scene wiring — ResourceUI counters

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

Via Unity MCP. Repurpose the existing WoodCounter container as `_countersRoot`, delete the old hardcoded labels/icons, wire `_font` + `_kinds` (Wood/Food/Stone). `_populationLabel` reference persists (same field name).

- [ ] **Step 1: Inspect current ResourceUI wiring + WoodCounter children via Unity MCP**

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
        var rui = Object.FindFirstObjectByType<RTSCL.World.Unity.ResourceUI>();
        if (rui == null) { result.LogError("ResourceUI not found"); return; }
        var so = new SerializedObject(rui);
        var pop = so.FindProperty("_populationLabel").objectReferenceValue as Text;
        result.Log($"ResourceUI on {rui.name}; popLabel={(pop==null?"null":pop.name+" parent="+pop.transform.parent.name)}");
        // Find the WoodCounter (old wood label parent) by scanning for a Text named "Count"
        foreach (var t in rui.GetComponentsInChildren<Text>(true))
            result.Log($"Text: {t.name} parent={t.transform.parent.name} font={(t.font==null?"null":t.font.name)}");
        foreach (var img in rui.GetComponentsInChildren<Image>(true))
            result.Log($"Image: {img.name} parent={img.transform.parent.name}");
    }
}
```

Note the WoodCounter container name, a Font from any existing Text, and the population label. (From prior work: the wood label is named "Count" under "WoodCounter"; food label "FoodLabel"; icons "WoodIcon"/"FoodIcon".)

- [ ] **Step 2: Build counters root + wire kinds via Unity MCP**

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UImage = UnityEngine.UI.Image;
using UnityEngine.UI;
using System.Collections.Generic;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var rui = Object.FindFirstObjectByType<RTSCL.World.Unity.ResourceUI>();
        if (rui == null) { result.LogError("ResourceUI not found"); return; }

        // Locate the old WoodCounter container + grab a font from any Text under it.
        Transform woodCounter = null;
        Font font = null;
        foreach (var t in rui.GetComponentsInChildren<Text>(true))
        {
            if (t.transform.parent != null && t.transform.parent.name == "WoodCounter")
            { woodCounter = t.transform.parent; if (font == null) font = t.font; }
            if (font == null) font = t.font;
        }
        if (woodCounter == null) { result.LogError("WoodCounter container not found"); return; }

        // Delete the old hardcoded children (Count, FoodLabel, WoodIcon, FoodIcon).
        for (int i = woodCounter.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(woodCounter.GetChild(i).gameObject);

        // Load icon sprites.
        Sprite Sub(string path, string name)
        {
            foreach (var s in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (s is Sprite sp && sp.name == name) return sp;
            return null;
        }
        var wood = Sub("Assets/MiniWorldSprites/Nature/Trees.png", "Trees_2");
        var food = Sub("Assets/MiniWorldSprites/Nature/Wheatfield.png", "Wheatfield_0");
        var stone = Sub("Assets/MiniWorldSprites/Nature/Rocks.png", "Rocks_6");
        result.Log($"sprites wood={(wood==null?"NULL":"ok")} food={(food==null?"NULL":"ok")} stone={(stone==null?"NULL":"ok")}");

        var so = new SerializedObject(rui);
        so.FindProperty("_countersRoot").objectReferenceValue = woodCounter;
        so.FindProperty("_font").objectReferenceValue = font;

        var kinds = so.FindProperty("_kinds");
        kinds.ClearArray();
        void AddKind(int idx, int kindEnum, Sprite icon)
        {
            kinds.InsertArrayElementAtIndex(idx);
            var el = kinds.GetArrayElementAtIndex(idx);
            el.FindPropertyRelative("Kind").enumValueIndex = kindEnum; // Wood=0, Food=1, Stone=2
            el.FindPropertyRelative("Icon").objectReferenceValue = icon;
        }
        AddKind(0, 0, wood);
        AddKind(1, 1, food);
        AddKind(2, 2, stone);

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("ResourceUI counters root + Wood/Food/Stone kinds wired");
    }
}
```

Note: `enumValueIndex` matches `ResourceKind` declaration order (Wood=0, Food=1, Stone=2), so it equals the enum's int value here.

- [ ] **Step 3: Verify console — expect sprite "ok" logs + wiring success, 0 errors**

`Unity_ReadConsole` Types=["Error"]. Expect 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "feat(resource): ResourceUI scene — counters root + Wood/Food/Stone icon wiring"
```

---

## Task 6: Final compile + smoke verification

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
- Three counters with icons: Wood (tree), Food (wheat), Stone (rock).
- Chop a tree → Wood rises on deposit; wheatfield → Food; rock → Stone.
- Select 1 Farmer carrying → info panel "Carrying: N stone" (or wood/food).
- Carry wood + right-click a rock → Farmer deposits wood first, then mines stone.
- Rock destroyed → particle burst.
- Building/unit costs unchanged (Wood/Food still deducts correctly).

- [ ] **Step 4: No commit unless a fix was needed**

If smoke passes: no further commits.

---

## Plan Self-Review Notes

**Spec coverage:**
- ResourceKind enum (Task 1)
- ResourceBank array-backed + Get/Add/OnChanged + convenience (Task 2; legacy removed Task 4)
- Goblin single typed carry slot + KindOf/MaxHpFor/IsRockTile + HitHarvestable + SetHarvestCommand (Task 3)
- ObjectInspector carry display generic (Task 3) + Start OnChanged (Task 4)
- ResourceUI data-driven counters (Task 4) + scene wiring (Task 5)
- Stone harvest from Rocks (Task 3 classification; Rocks already on map)

**Placeholder scan:** none.

**Type consistency:**
- `ResourceKind` (Wood=0..Crystal=5) — Task 1; used everywhere
- `ResourceBank.Get/Add/OnChanged(ResourceKind,int)` — Task 2; consumed Task 3 (Add), Task 4 (UI/Inspector)
- `Goblin.CarriedKind`(ResourceKind)/`CarriedAmount`(int)/`MaxCarried` — Task 3; read by ObjectInspector (Task 3 step 7)
- `Goblin.KindOf(string)`(public static)/`MaxHpFor`(private static)/`IsRockTile`(public static) — Task 3
- `ResourceUI.KindIcon{Kind,Icon}` + `_countersRoot`/`_font`/`_kinds`/`_populationLabel` — Task 4 code; Task 5 wires `_countersRoot`/`_font`/`_kinds`; `_populationLabel` persists by field-name
- Legacy `OnWoodChanged`/`OnFoodChanged` exist Tasks 2-3, removed Task 4 after all subscribers migrated — no dangling refs

**Compile-clean per commit:**
- Task 2 keeps legacy events + convenience → existing subscribers compile.
- Task 3 touches Goblin + the one ObjectInspector method that reads the renamed carry fields → same commit.
- Task 4 migrates both remaining subscribers (ResourceUI rebuild + ObjectInspector.Start) AND removes legacy events in the same commit → no dangling references.
- Task 5 is scene-only; ResourceUI code already compiles with new serialized fields (unwired just shows nothing until wired).

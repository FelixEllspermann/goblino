# Food Cost for Units + Resource UI Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `FoodCost` to GoblinUnitDefinition. Update Farmer (0 wood / 50 food) and Club (150 wood + 100 food). ObjectInspector enforces both costs on train clicks + greys cards when either resource is insufficient. Add tree/wheatfield sprite icons before the Wood/Food counts in the resource bar.

**Architecture:** Extend the existing single-resource cost path (`WoodCost`) with a parallel `FoodCost` everywhere it's checked or displayed. Cost text on unit cards becomes a format helper. UI icons are pure Inspector-wired SerializeFields on ResourceUI — no runtime logic.

**Tech Stack:** Unity 6 / C# / Steamworks.NET / RTSCL.World.Unity asmdef

**Spec:** `docs/superpowers/specs/2026-05-27-food-cost-icons-design.md`

---

## File Structure

### Modified

| File | Change |
|---|---|
| `Assets/Scripts/World/Unity/GoblinUnitDefinition.cs` | Add `public int FoodCost = 0;` |
| `Assets/Generated/Units/FarmerGoblin.asset` | `WoodCost: 0`, append `FoodCost: 50` |
| `Assets/Generated/Units/ClubGoblin.asset` | Keep `WoodCost: 150`, append `FoodCost: 100` |
| `Assets/Scripts/World/Unity/ObjectInspector.cs` | `CardRefs.FoodCost`, `FormatUnitCost`, `BuildUnitCards` passes formatted cost + sets `FoodCost`, `Refresh` checks both resources, `OnUnitClicked` checks+deducts both, subscribe to `OnFoodChanged`. |
| `Assets/Scripts/World/Unity/ResourceUI.cs` | Add `_woodIcon` + `_foodIcon` SerializeFields. |
| `Assets/Scenes/SampleScene.unity` | Add WoodIcon + FoodIcon Image GameObjects + wire ResourceUI._woodIcon/_foodIcon. |

---

## Task 1: GoblinUnitDefinition.FoodCost

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinUnitDefinition.cs`

- [ ] **Step 1: Add FoodCost field**

Find:

```csharp
public int WoodCost = 50;
public int PopulationCost = 1;
```

Replace with:

```csharp
public int WoodCost = 50;
public int FoodCost = 0;
public int PopulationCost = 1;
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
git add Assets/Scripts/World/Unity/GoblinUnitDefinition.cs
git commit -m "feat(cost): GoblinUnitDefinition.FoodCost field"
```

---

## Task 2: Update Unit Assets (Farmer + Club)

**Files:**
- Modify: `Assets/Generated/Units/FarmerGoblin.asset`
- Modify: `Assets/Generated/Units/ClubGoblin.asset`

Drive via Unity MCP so Inspector serialization is canonical.

- [ ] **Step 1: Patch both assets via Unity MCP**

```csharp
using UnityEditor;
using UnityEngine;
using RTSCL.World.Unity;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var farmer = AssetDatabase.LoadAssetAtPath<GoblinUnitDefinition>("Assets/Generated/Units/FarmerGoblin.asset");
        var club = AssetDatabase.LoadAssetAtPath<GoblinUnitDefinition>("Assets/Generated/Units/ClubGoblin.asset");
        if (farmer == null || club == null) { result.LogError("Unit defs missing"); return; }

        farmer.WoodCost = 0;
        farmer.FoodCost = 50;
        EditorUtility.SetDirty(farmer);

        club.WoodCost = 150;
        club.FoodCost = 100;
        EditorUtility.SetDirty(club);

        AssetDatabase.SaveAssets();
        result.Log($"Farmer wood/food = {farmer.WoodCost}/{farmer.FoodCost}; Club wood/food = {club.WoodCost}/{club.FoodCost}");
    }
}
```

- [ ] **Step 2: Refresh + verify console (expect the log line, 0 errors)**

`Unity_ReadConsole` Types=["Error"]. Expect 0.

- [ ] **Step 3: Commit**

```bash
git add Assets/Generated/Units/FarmerGoblin.asset Assets/Generated/Units/ClubGoblin.asset
git commit -m "feat(cost): Farmer 50 food / Club +100 food asset values"
```

---

## Task 3: ObjectInspector — CardRefs.FoodCost + cost-text formatting

**Files:**
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs`

- [ ] **Step 1: Add FoodCost to CardRefs**

Find:

```csharp
public int WoodCost;
public UpgradeDefinition Upgrade;
```

Replace with:

```csharp
public int WoodCost;
public int FoodCost;
public UpgradeDefinition Upgrade;
```

- [ ] **Step 2: Add FormatUnitCost helper**

Add this private static method near the other helpers (e.g., right after `PrettyKindName` or `DescribeBuilding`):

```csharp
private static string FormatUnitCost(int woodCost, int foodCost)
{
    if (woodCost > 0 && foodCost > 0) return $"{woodCost} Wood, {foodCost} Food";
    if (woodCost > 0) return $"{woodCost} Wood";
    if (foodCost > 0) return $"{foodCost} Food";
    return "Free";
}
```

- [ ] **Step 3: Update BuildUnitCards to pass formatted cost + set FoodCost**

Find:

```csharp
private void BuildUnitCards(GoblinUnitDefinition[] units)
{
    ClearCards();
    if (_unitCardsContainer == null) return;
    _unitCardsContainer.gameObject.SetActive(true);
    foreach (var u in units)
    {
        if (u == null) continue;
        var c = CreateCard(u.DisplayName, u.Icon, u.WoodCost, () => OnUnitClicked(u));
        c.Unit = u;
        _cards.Add(c);
    }
}
```

Replace with:

```csharp
private void BuildUnitCards(GoblinUnitDefinition[] units)
{
    ClearCards();
    if (_unitCardsContainer == null) return;
    _unitCardsContainer.gameObject.SetActive(true);
    foreach (var u in units)
    {
        if (u == null) continue;
        var c = CreateCard(u.DisplayName, u.Icon, FormatUnitCost(u.WoodCost, u.FoodCost), () => OnUnitClicked(u));
        c.Unit = u;
        c.WoodCost = u.WoodCost;
        c.FoodCost = u.FoodCost;
        _cards.Add(c);
    }
}
```

- [ ] **Step 4: Change CreateCard signature to accept cost string**

The current signature:

```csharp
private CardRefs CreateCard(string title, Sprite icon, int woodCost, Action onClick)
```

uses `woodCost` to set the cost text. Change the parameter type from `int woodCost` to `string costText`:

```csharp
private CardRefs CreateCard(string title, Sprite icon, string costText, Action onClick)
```

Inside `CreateCard`, find:

```csharp
costText.text = $"{woodCost} Wood";
```

Replace with:

```csharp
costText.text = ... // (rename local var clash — see below)
```

Wait — there's a local `Text costText` variable inside the method that has the same name as the new parameter. Resolve the naming clash by renaming the local Text variable:

Inside `CreateCard`, find the local declaration (it's the `Text` field added to the "Cost" GameObject):

```csharp
var costText = costGo.AddComponent<Text>();
costText.text = $"{woodCost} Wood";
```

Replace with:

```csharp
var costLabel = costGo.AddComponent<Text>();
costLabel.text = costText;     // costText is now the parameter
costLabel.font = _cardFont;    // copy remaining property assignments from the existing block
costLabel.fontSize = 12;
costLabel.color = new Color(0.85f, 0.75f, 0.45f);
costLabel.alignment = TextAnchor.MiddleLeft;
costLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
```

And at the end of the method where the returned CardRefs is built:

```csharp
return new CardRefs
{
    Root = card,
    Button = btn,
    Bg = bg,
    Icon = iconImg,
    Name = nameText,
    Cost = costText,    // OLD: refers to local Text — now must refer to costLabel
    WoodCost = woodCost,  // OLD: parameter renamed
};
```

Replace with:

```csharp
return new CardRefs
{
    Root = card,
    Button = btn,
    Bg = bg,
    Icon = iconImg,
    Name = nameText,
    Cost = costLabel,
    // WoodCost / FoodCost are filled by the BuildXCards caller
};
```

(Building / Upgrade card builders still need to set `c.WoodCost` after calling CreateCard. That's already true in `BuildBuildingCards` and `BuildUpgradeCards` — but they currently DON'T set WoodCost on the result. The old path piggy-backed on CreateCard's `WoodCost = woodCost;` initializer.)

Verify by reading the existing `BuildBuildingCards` and `BuildUpgradeCards` — they need a `c.WoodCost = def.WoodCost;` (resp. `u.WoodCost`) added after `CreateCard` because CreateCard no longer sets WoodCost.

- [ ] **Step 5: Update BuildBuildingCards to set WoodCost**

Find:

```csharp
private void BuildBuildingCards(List<BuildingDefinition> defs)
{
    ClearCards();
    if (_unitCardsContainer == null) return;
    _unitCardsContainer.gameObject.SetActive(true);
    foreach (var b in defs)
    {
        if (b == null) continue;
        var c = CreateCard(b.DisplayName, b.Sprite, b.WoodCost, () => OnBuildingClicked(b));
        c.Building = b;
        _cards.Add(c);
    }
}
```

Replace with:

```csharp
private void BuildBuildingCards(List<BuildingDefinition> defs)
{
    ClearCards();
    if (_unitCardsContainer == null) return;
    _unitCardsContainer.gameObject.SetActive(true);
    foreach (var b in defs)
    {
        if (b == null) continue;
        var c = CreateCard(b.DisplayName, b.Sprite, $"{b.WoodCost} Wood", () => OnBuildingClicked(b));
        c.Building = b;
        c.WoodCost = b.WoodCost;
        _cards.Add(c);
    }
}
```

- [ ] **Step 6: Update BuildUpgradeCards to set WoodCost**

Find:

```csharp
private void BuildUpgradeCards(UpgradeDefinition[] upgrades)
{
    ClearCards();
    if (_unitCardsContainer == null) return;
    _unitCardsContainer.gameObject.SetActive(true);
    foreach (var u in upgrades)
    {
        if (u == null) continue;
        var c = CreateCard(u.DisplayName, u.Icon, u.WoodCost, () => OnUpgradeClicked(u));
        c.Upgrade = u;
        c.UpgradeKind = u.Kind;
        _cards.Add(c);
    }
}
```

Replace with:

```csharp
private void BuildUpgradeCards(UpgradeDefinition[] upgrades)
{
    ClearCards();
    if (_unitCardsContainer == null) return;
    _unitCardsContainer.gameObject.SetActive(true);
    foreach (var u in upgrades)
    {
        if (u == null) continue;
        var c = CreateCard(u.DisplayName, u.Icon, $"{u.WoodCost} Wood", () => OnUpgradeClicked(u));
        c.Upgrade = u;
        c.UpgradeKind = u.Kind;
        c.WoodCost = u.WoodCost;
        _cards.Add(c);
    }
}
```

- [ ] **Step 7: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "feat(cost): CardRefs.FoodCost + FormatUnitCost helper + per-card cost string"
```

---

## Task 4: ObjectInspector — affordability check + deduct Food

**Files:**
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs`

- [ ] **Step 1: Subscribe to OnFoodChanged**

In `Start()`, find:

```csharp
ResourceBank.OnWoodChanged += _ => Refresh();
```

Right after it, add:

```csharp
ResourceBank.OnFoodChanged += _ => Refresh();
```

- [ ] **Step 2: Update Refresh affordability check**

Find:

```csharp
bool affordable = ResourceBank.Wood >= card.WoodCost;
```

Replace with:

```csharp
bool affordable = ResourceBank.Wood >= card.WoodCost
               && ResourceBank.Food >= card.FoodCost;
```

- [ ] **Step 3: Update OnUnitClicked to check + deduct both**

Find:

```csharp
private void OnUnitClicked(GoblinUnitDefinition unit)
{
    if (_selKind != SelKind.Building || _selDef == null) return;
    if (GoblinProduction.IsBusy(_selOrigin)) return;
    if (ResourceBank.Wood < unit.WoodCost) return;
    if (!PopulationManager.CanAfford(unit.PopulationCost)) return;
    ResourceBank.AddWood(-unit.WoodCost);
    ulong owner = WorldStartContext.LocalPlayer;
    NetCommandIssuer.IssueTrainUnit(_selOrigin, unit, owner);
    Refresh();
}
```

Replace with:

```csharp
private void OnUnitClicked(GoblinUnitDefinition unit)
{
    if (_selKind != SelKind.Building || _selDef == null) return;
    if (GoblinProduction.IsBusy(_selOrigin)) return;
    if (ResourceBank.Wood < unit.WoodCost) return;
    if (ResourceBank.Food < unit.FoodCost) return;
    if (!PopulationManager.CanAfford(unit.PopulationCost)) return;
    if (unit.WoodCost > 0) ResourceBank.AddWood(-unit.WoodCost);
    if (unit.FoodCost > 0) ResourceBank.AddFood(-unit.FoodCost);
    ulong owner = WorldStartContext.LocalPlayer;
    NetCommandIssuer.IssueTrainUnit(_selOrigin, unit, owner);
    Refresh();
}
```

- [ ] **Step 4: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "feat(cost): train-unit affordability + deduct on both Wood and Food"
```

---

## Task 5: ResourceUI icon SerializeFields

**Files:**
- Modify: `Assets/Scripts/World/Unity/ResourceUI.cs`

- [ ] **Step 1: Add icon SerializeFields**

Find:

```csharp
[SerializeField] private Text _woodLabel;
[SerializeField] private Text _foodLabel;
[SerializeField] private Text _populationLabel;
```

Replace with:

```csharp
[SerializeField] private Image _woodIcon;
[SerializeField] private Image _foodIcon;
[SerializeField] private Text _woodLabel;
[SerializeField] private Text _foodLabel;
[SerializeField] private Text _populationLabel;
```

`Image` is in `UnityEngine.UI` which is already imported.

The icons aren't read anywhere — they're just slots that ensure the scene wiring exists. (The implementer can verify wiring later by inspecting `_woodIcon != null` if desired.)

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/ResourceUI.cs
git commit -m "feat(cost): ResourceUI _woodIcon + _foodIcon SerializeFields"
```

---

## Task 6: Scene wiring — WoodIcon + FoodIcon Images

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

Driven via Unity MCP. Create 2 Image GameObjects with sprite refs and wire to ResourceUI.

- [ ] **Step 1: Find sprite GUIDs for Trees_0 and Wheatfield_0 via Unity MCP**

```csharp
using UnityEditor;
using UnityEngine;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var treeSprites = AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/MiniWorldSprites/Nature/Trees.png");
        Sprite trees0 = null;
        foreach (var s in treeSprites)
            if (s is Sprite sp && sp.name == "Trees_0") { trees0 = sp; break; }
        var wheatSprites = AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/MiniWorldSprites/Nature/Wheatfield.png");
        Sprite wheat0 = null;
        foreach (var s in wheatSprites)
            if (s is Sprite sp && sp.name == "Wheatfield_0") { wheat0 = sp; break; }
        result.Log($"Trees_0={(trees0 == null ? "MISSING" : trees0.name)}; Wheatfield_0={(wheat0 == null ? "MISSING" : wheat0.name)}");
    }
}
```

Expect: `Trees_0=Trees_0; Wheatfield_0=Wheatfield_0`. If either is MISSING, fall back to whatever sub-sprite-0 exists in those PNGs (use `result.Log` to enumerate sub-sprite names if needed).

- [ ] **Step 2: Add WoodIcon + FoodIcon GameObjects and wire**

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
        var woodLabelProp = so.FindProperty("_woodLabel");
        var foodLabelProp = so.FindProperty("_foodLabel");
        var woodIconProp = so.FindProperty("_woodIcon");
        var foodIconProp = so.FindProperty("_foodIcon");

        var woodLabel = (Text)woodLabelProp.objectReferenceValue;
        var foodLabel = (Text)foodLabelProp.objectReferenceValue;
        if (woodLabel == null || foodLabel == null) { result.LogError("Wood or Food label not assigned"); return; }

        var treeSprites = AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/MiniWorldSprites/Nature/Trees.png");
        Sprite trees0 = null;
        foreach (var s in treeSprites) if (s is Sprite sp && sp.name == "Trees_0") { trees0 = sp; break; }
        var wheatSprites = AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/MiniWorldSprites/Nature/Wheatfield.png");
        Sprite wheat0 = null;
        foreach (var s in wheatSprites) if (s is Sprite sp && sp.name == "Wheatfield_0") { wheat0 = sp; break; }
        if (trees0 == null || wheat0 == null) { result.LogError("Sprites missing"); return; }

        // WoodIcon
        if (woodIconProp.objectReferenceValue == null)
        {
            var woodIconGo = new GameObject("WoodIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            woodIconGo.transform.SetParent(woodLabel.transform.parent, false);
            var img = woodIconGo.GetComponent<Image>();
            img.sprite = trees0;
            img.preserveAspect = true;
            var rt = (RectTransform)woodIconGo.transform;
            var srcRt = (RectTransform)woodLabel.transform;
            rt.anchorMin = srcRt.anchorMin;
            rt.anchorMax = srcRt.anchorMax;
            rt.pivot = srcRt.pivot;
            rt.sizeDelta = new Vector2(24f, 24f);
            rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(-30f, 0f);
            woodIconProp.objectReferenceValue = img;
            result.Log("WoodIcon created");
        }
        else
        {
            result.Log("WoodIcon already wired");
        }

        // FoodIcon
        if (foodIconProp.objectReferenceValue == null)
        {
            var foodIconGo = new GameObject("FoodIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            foodIconGo.transform.SetParent(foodLabel.transform.parent, false);
            var img = foodIconGo.GetComponent<Image>();
            img.sprite = wheat0;
            img.preserveAspect = true;
            var rt = (RectTransform)foodIconGo.transform;
            var srcRt = (RectTransform)foodLabel.transform;
            rt.anchorMin = srcRt.anchorMin;
            rt.anchorMax = srcRt.anchorMax;
            rt.pivot = srcRt.pivot;
            rt.sizeDelta = new Vector2(24f, 24f);
            rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(-30f, 0f);
            foodIconProp.objectReferenceValue = img;
            result.Log("FoodIcon created");
        }
        else
        {
            result.Log("FoodIcon already wired");
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("Scene saved");
    }
}
```

- [ ] **Step 3: Verify console — expect WoodIcon/FoodIcon creation logs, 0 errors**

`Unity_ReadConsole` Types=["Error"]. Expect 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "feat(cost): wire WoodIcon + FoodIcon in SampleScene ResourceUI"
```

---

## Task 7: Final compile + smoke verification

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

Use the test-runner pattern (TestRunnerApi + Temp file callback). Expect 36/36 still pass.

- [ ] **Step 3: Solo smoke (user manually)**

User runs Play Solo:
- Top bar: 🌳 (Trees_0 sprite) before Wood count; 🌾 (Wheatfield_0 sprite) before Food count.
- Click Keep → Farmer card shows cost "50 Food". Card greyed at game start (Food = 0).
- Right-click a Wheatfield → Farmer harvests; Food UI rises.
- At Food >= 50: Farmer card becomes interactable.
- Click Farmer card → 50 Food deducted, Wood unchanged, Farmer spawns.
- Build a Barracks (existing wood path, 500 wood).
- Click Barracks → Club card shows "150 Wood, 100 Food". Greyed if either insufficient.
- Train Club → 150 Wood AND 100 Food deducted.
- Workshop upgrades still show "X Wood" only (food-cost-on-upgrade out of scope).

- [ ] **Step 4: No commit unless a fix was needed**

If smoke passes: no further commits.

---

## Plan Self-Review Notes

**Spec coverage:**
- GoblinUnitDefinition.FoodCost (Task 1)
- Asset migrations (Task 2)
- CardRefs.FoodCost + cost-text formatter (Task 3)
- Refresh + OnUnitClicked checks both resources + OnFoodChanged subscribe (Task 4)
- ResourceUI icon SerializeFields (Task 5)
- Scene icon GameObjects + wiring (Task 6)

**Placeholder scan:** no TBDs / TODOs / vague items.

**Type consistency:**
- `FoodCost` (int) used as `unit.FoodCost`, `card.FoodCost`, in cost text + affordability — consistent across Tasks 1, 3, 4
- `FormatUnitCost(int, int)` static helper signature — Task 3
- `CreateCard(string, Sprite, string, Action)` new signature with `costText` parameter (string, was int) — Tasks 3 step 4–6 all updated
- `BuildBuildingCards` + `BuildUpgradeCards` both updated to explicitly set `c.WoodCost = ...` since CreateCard no longer assigns it (Task 3 step 5+6)
- `_woodIcon` + `_foodIcon` (Image type) — Task 5 + Task 6 use the same names

**Compile-break window:** Tasks 1, 3, 4, 5 are individually compile-clean — no interim broken state.

**Asset changes:** Task 2 (assets) and Task 6 (scene) are pure-data; can be reordered relative to code tasks without harm. Order chosen: define field → migrate data → wire code → wire scene → smoke.

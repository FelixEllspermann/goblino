# Build Menu Sidebar — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the cramped bottom-bar build menu with a scalable left-edge sidebar (vertical category tabs + scrollable grid), driven by per-building `Category` + `Requires` data, sharing one dynamically-sized tooltip.

**Architecture:** A new runtime-built `BuildMenu` MonoBehaviour owns the build UI (extracted from `ObjectInspector`). `BuildingDefinition` gains a `BuildCategory` enum field and a `Requires` array. `BuildRequirements.IsUnlocked` gates buildings. A shared `UITooltip` singleton replaces the inline tooltip in `ObjectInspector` and is reused by `BuildMenu`. `ObjectInspector` keeps only inspection + train/upgrade cards.

**Tech Stack:** Unity 6000.4.3f1, uGUI (runtime-built UI), C# (`RTSCL.World.Unity` asmdef). All editor wiring via Unity MCP `Unity_RunCommand`.

**Conventions for every task below:**
- After a C# edit, recompile via `Unity_RunCommand`:
  `AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport)` for each changed file, then `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation()`, wait ~13s, then `Unity_ReadConsole` filtered for `error CS`. **Expected: 0 errors.**
- MCP `Unity_RunCommand` scripts must be `internal class CommandScript : IRunCommand` in namespace `Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`.
- **Wiring an asset ref into a scene component: `LoadAssetAtPath` AFTER `OpenScene`**, else the ref goes stale → silently null.
- 42 EditMode tests must stay green at the end (`RTSCL.World.Tests`).

---

## File Structure

| File | New/Modify | Responsibility |
|---|---|---|
| `Assets/Scripts/World/Unity/BuildCategory.cs` | Create | Enum of build categories (tab order). |
| `Assets/Scripts/World/Unity/BuildRequirements.cs` | Create | `IsUnlocked(def, owner)` prereq check. |
| `Assets/Scripts/World/Unity/UITooltip.cs` | Create | Shared, dynamically-sized hover tooltip singleton. |
| `Assets/Scripts/World/Unity/BuildingDefinition.cs` | Modify | Add `Category` + `Requires` fields. |
| `Assets/Scripts/World/Unity/BuildMenu.cs` | Create | Left sidebar: category tabs + scrollable building grid. |
| `Assets/Scripts/World/Unity/ObjectInspector.cs` | Modify | Remove build-menu + inline tooltip; use `UITooltip`. |
| `Assets/Tests/Editor/BuildRequirementsTests.cs` | Create | Unit tests for `BuildRequirements` (pure logic). |
| `Assets/Scenes/SampleScene.unity` | Modify (MCP) | Add `BuildMenu` object; move buildables list; set categories. |
| `Assets/Generated/Buildings/*.asset` | Modify (MCP) | Set each building's `Category`. |

---

## Task 1: BuildCategory enum

**Files:**
- Create: `Assets/Scripts/World/Unity/BuildCategory.cs`

- [ ] **Step 1: Create the enum**

```csharp
// BuildCategory.cs — categories for the build menu sidebar. Declaration order = top-to-bottom tab order.
// Assigned per building on BuildingDefinition.Category; BuildMenu groups buildings by this.
namespace RTSCL.World.Unity
{
    /// <summary>Build-menu category. Order here is the tab order in the sidebar.</summary>
    public enum BuildCategory
    {
        Economy = 0,
        Military = 1,
        Defense = 2,
        Naval = 3,
        Advanced = 4,
    }
}
```

- [ ] **Step 2: Recompile + verify 0 errors** (see conventions header).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildCategory.cs Assets/Scripts/World/Unity/BuildCategory.cs.meta
git commit -m "feat(build): BuildCategory enum (Economy/Military/Defense/Naval/Advanced)"
```

---

## Task 2: BuildingDefinition gains Category + Requires

**Files:**
- Modify: `Assets/Scripts/World/Unity/BuildingDefinition.cs`

- [ ] **Step 1: Add the two fields** after the existing `ProvidesUpgrades` field (end of the class body, before the closing brace):

```csharp
        /// <summary>Build-menu category this building appears under (groups the sidebar tabs).</summary>
        [Tooltip("Which build-menu category/tab this building appears under.")]
        public BuildCategory Category = BuildCategory.Economy;

        /// <summary>Buildings that must be owned + finished before this one can be built.
        /// Empty = always buildable. Drives the build menu's locked/greyed state (tech-tree foundation).</summary>
        [Tooltip("Prerequisite buildings (must be owned + finished). Empty = always buildable.")]
        public BuildingDefinition[] Requires;
```

- [ ] **Step 2: Recompile + verify 0 errors.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildingDefinition.cs
git commit -m "feat(build): BuildingDefinition.Category + Requires fields"
```

---

## Task 3: BuildRequirements helper (with unit tests)

**Files:**
- Create: `Assets/Scripts/World/Unity/BuildRequirements.cs`
- Test: `Assets/Tests/Editor/BuildRequirementsTests.cs`

**Note on testability:** `BuildRequirements.IsUnlocked` needs to know which buildings an owner owns + whether each is finished. Those live in `BuildingPlacer` (instance) + `BuildingConstruction` (static). To keep the helper unit-testable without a live `BuildingPlacer`, inject the ownership check as a delegate: `IsUnlocked(def, Func<BuildingDefinition,bool> ownsCompleted)`. The MonoBehaviour caller (BuildMenu) supplies the real lookup; tests supply a fake.

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/Tests/Editor/BuildRequirementsTests.cs
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RTSCL.World.Unity;

public class BuildRequirementsTests
{
    private static BuildingDefinition Def(string name, params BuildingDefinition[] requires)
    {
        var d = ScriptableObject.CreateInstance<BuildingDefinition>();
        d.name = name;
        d.Requires = requires;
        return d;
    }

    [Test]
    public void NoRequirements_AlwaysUnlocked()
    {
        var d = Def("Hut");
        Assert.IsTrue(BuildRequirements.IsUnlocked(d, _ => false));
    }

    [Test]
    public void MissingPrereq_Locked()
    {
        var barracks = Def("Barracks");
        var workshop = Def("Workshop", barracks);
        Assert.IsFalse(BuildRequirements.IsUnlocked(workshop, owned => owned == barracks ? false : true));
    }

    [Test]
    public void AllPrereqsOwned_Unlocked()
    {
        var barracks = Def("Barracks");
        var workshop = Def("Workshop", barracks);
        Assert.IsTrue(BuildRequirements.IsUnlocked(workshop, owned => owned == barracks));
    }

    [Test]
    public void NullRequires_AlwaysUnlocked()
    {
        var d = Def("Hut");
        d.Requires = null;
        Assert.IsTrue(BuildRequirements.IsUnlocked(d, _ => false));
    }
}
```

- [ ] **Step 2: Run the tests, verify they FAIL** (BuildRequirements doesn't exist yet → compile error / red).
  Run via Unity MCP TestRunnerApi (EditMode) or the Test Runner window. Expected: fails to compile / not found.

- [ ] **Step 3: Implement `BuildRequirements`**

```csharp
// BuildRequirements.cs — tech-tree gate for the build menu. A building is unlocked when all of its
// BuildingDefinition.Requires entries are owned + finished. The ownership check is injected so this stays
// pure-logic + unit-testable; BuildMenu supplies the real BuildingPlacer/BuildingConstruction lookup.
using System;

namespace RTSCL.World.Unity
{
    /// <summary>Decides whether a building is unlocked given which prerequisite buildings are owned+built.</summary>
    public static class BuildRequirements
    {
        /// <summary>True if <paramref name="def"/> has no prerequisites, or every prerequisite passes
        /// <paramref name="ownsCompleted"/> (owned by the player AND finished constructing).</summary>
        public static bool IsUnlocked(BuildingDefinition def, Func<BuildingDefinition, bool> ownsCompleted)
        {
            if (def == null) return false;
            if (def.Requires == null || def.Requires.Length == 0) return true;
            foreach (var req in def.Requires)
            {
                if (req == null) continue;                // null entry = ignore
                if (ownsCompleted == null || !ownsCompleted(req)) return false;
            }
            return true;
        }
    }
}
```

- [ ] **Step 4: Run the tests, verify they PASS** (4/4 green; total suite 46 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildRequirements.cs Assets/Scripts/World/Unity/BuildRequirements.cs.meta Assets/Tests/Editor/BuildRequirementsTests.cs Assets/Tests/Editor/BuildRequirementsTests.cs.meta
git commit -m "feat(build): BuildRequirements.IsUnlocked + tests"
```

---

## Task 4: Shared UITooltip singleton (dynamically sized)

**Files:**
- Create: `Assets/Scripts/World/Unity/UITooltip.cs`

**Context:** Replaces the inline tooltip currently in `ObjectInspector` (fields `_tooltip`, `_tooltipText`, `_tooltipRect`, methods `ShowTooltip`/`HideTooltip`/`EnsureTooltip`/`PositionTooltip`, nested `CardHover`). UITooltip builds its panel lazily on the first `Show`, parents to the top-most Canvas in the scene, and **sizes itself to its text** via a preferred-size calc (not a `ContentSizeFitter`, because the panel follows the mouse and we set `position` directly — we compute width/height from the text's preferred size + padding each Show).

- [ ] **Step 1: Create UITooltip**

```csharp
// UITooltip.cs — one shared hover tooltip for the whole UI (build menu, inspector cards, etc).
// Lazily builds a dark panel on the first Show(), parents it to the top Canvas, sizes it to fit the text
// (preferred size + padding, clamped to a max width with wrapping), and follows the mouse. raycastTarget
// is off so it never eats clicks/hover. Call UITooltip.Show(text) on pointer-enter, UITooltip.Hide() on exit.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    /// <summary>Process-wide hover tooltip. Auto-creates itself; size adapts to the text.</summary>
    public sealed class UITooltip : MonoBehaviour
    {
        private const float MaxWidth = 320f;     // wrap beyond this
        private const float PadX = 12f, PadY = 8f;
        private const float CursorOffset = 16f;

        private static UITooltip _instance;
        private RectTransform _rect;
        private Text _text;
        private Font _font;
        private bool _visible;

        /// <summary>Show the tooltip with <paramref name="text"/>. <paramref name="font"/> is used the first
        /// time the panel is built (callers pass their card font). No-op for empty text.</summary>
        public static void Show(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) { Hide(); return; }
            EnsureInstance(font);
            if (_instance == null) return;
            _instance.ShowImpl(text);
        }

        public static void Hide()
        {
            if (_instance != null) _instance.HideImpl();
        }

        private static void EnsureInstance(Font font)
        {
            if (_instance != null) return;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            var go = new GameObject("UITooltip", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _instance = go.AddComponent<UITooltip>();
            _instance._font = font;
            _instance.Build();
        }

        private void Build()
        {
            _rect = (RectTransform)transform;
            _rect.pivot = new Vector2(0f, 0f);

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);
            bg.raycastTarget = false;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(PadX, PadY); tr.offsetMax = new Vector2(-PadX, -PadY);
            _text = txtGo.AddComponent<Text>();
            _text.font = _font;
            _text.fontSize = 14;
            _text.color = Color.white;
            _text.alignment = TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.raycastTarget = false;

            gameObject.SetActive(false);
        }

        private void ShowImpl(string text)
        {
            _text.text = text;
            // Size to content: measure unconstrained preferred width, clamp to MaxWidth, then measure
            // wrapped preferred height at that width.
            var gen = _text.cachedTextGeneratorForLayout;
            float prefW = _text.preferredWidth;
            float w = Mathf.Min(prefW, MaxWidth - 2f * PadX);
            _text.rectTransform.sizeDelta = new Vector2(w, _text.rectTransform.sizeDelta.y);
            float h = _text.preferredHeight;
            _rect.sizeDelta = new Vector2(w + 2f * PadX, h + 2f * PadY);
            gameObject.SetActive(true);
            _visible = true;
            transform.SetAsLastSibling();
            Position();
        }

        private void HideImpl()
        {
            _visible = false;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_visible) Position();
        }

        private void Position()
        {
            if (Mouse.current == null) return;
            Vector2 m = Mouse.current.position.ReadValue();
            float w = _rect.sizeDelta.x, h = _rect.sizeDelta.y;
            float x = Mathf.Min(m.x + CursorOffset, Screen.width - w - 4f);
            float y = Mathf.Min(m.y + CursorOffset, Screen.height - h - 4f);
            _rect.position = new Vector3(Mathf.Max(4f, x), Mathf.Max(4f, y), 0f);
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
```

- [ ] **Step 2: Recompile + verify 0 errors.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/UITooltip.cs Assets/Scripts/World/Unity/UITooltip.cs.meta
git commit -m "feat(ui): shared UITooltip singleton, sizes to its text"
```

---

## Task 5: Point ObjectInspector at UITooltip; remove its inline tooltip

**Files:**
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs`

**Context:** ObjectInspector currently has: fields `_tooltip`/`_tooltipText`/`_tooltipRect`; nested `CardHover` class with `Owner`/`Tip`; methods `ShowTooltip`/`HideTooltip`/`EnsureTooltip`/`PositionTooltip`; an `Update()` line `if (_tooltip != null && _tooltip.activeSelf) PositionTooltip();`. CardHover calls `Owner.ShowTooltip(Tip)`. We redirect these to `UITooltip` and delete the inline implementation. (The build-menu code is removed in Task 7, not here — keep this task tooltip-only.)

- [ ] **Step 1: Replace the `CardHover` nested class** so it calls UITooltip directly (no `Owner` callback needed). Find:

```csharp
        private sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public ObjectInspector Owner;
            public string Tip;
            public void OnPointerEnter(PointerEventData e) { if (Owner != null) Owner.ShowTooltip(Tip); }
            public void OnPointerExit(PointerEventData e)  { if (Owner != null) Owner.HideTooltip(); }
        }
```

Replace with:

```csharp
        private sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Font Font;
            public string Tip;
            public void OnPointerEnter(PointerEventData e) => UITooltip.Show(Tip, Font);
            public void OnPointerExit(PointerEventData e)  => UITooltip.Hide();
        }
```

- [ ] **Step 2: Update where CardHover is attached** in `CreateCard`. Find:

```csharp
                var hover = card.AddComponent<CardHover>();
                hover.Owner = this;
                hover.Tip = tooltip;
```

Replace with:

```csharp
                var hover = card.AddComponent<CardHover>();
                hover.Font = _cardFont;
                hover.Tip = tooltip;
```

- [ ] **Step 3: Delete the inline tooltip fields.** Find and DELETE:

```csharp
        // Hover tooltip (built lazily once, reused). Shows what a build/upgrade/train card does.
        private GameObject _tooltip;
        private Text _tooltipText;
        private RectTransform _tooltipRect;
```

- [ ] **Step 4: Delete the tooltip methods.** Find and DELETE the whole `// ---------- Hover tooltip ----------` region: methods `ShowTooltip`, `HideTooltip`, `EnsureTooltip`, `PositionTooltip` (everything from the `internal void ShowTooltip(string text)` summary down to the end of `PositionTooltip`).

- [ ] **Step 5: Remove the tooltip line in Update().** Find and DELETE:

```csharp
            if (_tooltip != null && _tooltip.activeSelf) PositionTooltip();
```

- [ ] **Step 6: Recompile + verify 0 errors.** (Build-menu code still present here — that's fine; it still uses `CreateCard`/`CardHover`, which now route to UITooltip.)

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "refactor(ui): ObjectInspector uses shared UITooltip; drop inline tooltip"
```

---

## Task 6: BuildMenu component (left sidebar)

**Files:**
- Create: `Assets/Scripts/World/Unity/BuildMenu.cs`

**Context:** This is the new sidebar. It mirrors the card-building style already in `ObjectInspector.CreateCard` (icon + name + cost-icon row), but builds its own self-contained UI tree at runtime and manages tabs + a scroll grid. It does NOT touch `ObjectInspector`. It reads the same resource icons + card font (serialized on BuildMenu, wired in Task 8). It calls `BuildingPlacer.Select(def)` to start ghost placement (existing public method).

Public/SerializeField surface used by Task 8 wiring:
- `[SerializeField] private GoblinSelectionController _selectionController;`
- `[SerializeField] private BuildingPlacer _placer;`
- `[SerializeField] private List<BuildingDefinition> _buildables = new();`
- `[SerializeField] private Font _font;`
- `[SerializeField] private Sprite _cardSprite;`
- `[SerializeField] private List<ResourceUI.KindIcon> _resourceIcons = new();`

- [ ] **Step 1: Create BuildMenu.cs**

```csharp
// BuildMenu.cs — left-edge build sidebar. Shown only while a Farmer is selected. Vertical category tabs
// (one per non-empty BuildCategory) beside a scrollable grid of that category's buildings. Each card shows
// icon + name + cost icons, greys out when locked (BuildRequirements) or unaffordable, and on click starts
// ghost placement via BuildingPlacer.Select. Built entirely at runtime (no prefab). Uses the shared UITooltip.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    public sealed class BuildMenu : MonoBehaviour
    {
        [SerializeField] private GoblinSelectionController _selectionController;
        [SerializeField] private BuildingPlacer _placer;
        [Tooltip("All buildings a Farmer can construct (moved here from ObjectInspector).")]
        [SerializeField] private List<BuildingDefinition> _buildables = new();
        [SerializeField] private Font _font;
        [SerializeField] private Sprite _cardSprite;
        [Tooltip("Resource icons for cost rows (same sprites as the top resource bar).")]
        [SerializeField] private List<ResourceUI.KindIcon> _resourceIcons = new();

        [Header("Layout")]
        [SerializeField] private float _panelWidth = 360f;
        [SerializeField] private float _panelHeight = 520f;
        [SerializeField] private Vector2 _cellSize = new(150f, 64f);
        [SerializeField] private float _tabWidth = 96f;

        private Canvas _canvas;
        private GameObject _root;          // whole sidebar (toggled on/off)
        private RectTransform _tabColumn;  // holds category tab buttons
        private RectTransform _gridContent; // scroll content holding building cards
        private BuildCategory _activeCategory = BuildCategory.Economy;
        private readonly List<(BuildingDefinition def, GameObject card, CanvasGroup costGroup, Button btn, Image bg)> _cards = new();
        private bool _built;
        private bool _shown;

        private static readonly Color BgEnabled  = new(0.18f, 0.18f, 0.22f, 0.95f);
        private static readonly Color BgDisabled = new(0.10f, 0.10f, 0.12f, 0.7f);

        private void Start()
        {
            _canvas = Object.FindFirstObjectByType<Canvas>();
            if (_selectionController != null)
                _selectionController.OnSelectionChanged += OnSelectionChanged;
            ResourceBank.OnChanged += (_, __) => RefreshAvailability();
            BuildingConstruction.OnCompleted += _ => RefreshAvailability();
            SetShown(false);
        }

        private void OnDestroy()
        {
            if (_selectionController != null)
                _selectionController.OnSelectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            bool hasFarmer = false;
            if (_selectionController != null)
                foreach (var g in _selectionController.Selection)
                    if (g != null && g.Kind == "FarmerGoblin") { hasFarmer = true; break; }
            SetShown(hasFarmer);
        }

        private void SetShown(bool show)
        {
            _shown = show;
            if (show)
            {
                EnsureBuilt();
                BuildTabs();
                BuildGrid();
            }
            if (_root != null) _root.SetActive(show);
        }

        // ---------- UI construction ----------

        private void EnsureBuilt()
        {
            if (_built || _canvas == null) return;
            _built = true;

            _root = new GameObject("BuildMenu", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(_canvas.transform, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = new Vector2(0f, 0.5f); rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(_panelWidth, _panelHeight);
            rt.anchoredPosition = new Vector2(8f, 0f);
            _root.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.92f);

            // Tab column (left).
            var tabGo = new GameObject("Tabs", typeof(RectTransform), typeof(VerticalLayoutGroup));
            tabGo.transform.SetParent(_root.transform, false);
            _tabColumn = (RectTransform)tabGo.transform;
            _tabColumn.anchorMin = new Vector2(0f, 0f); _tabColumn.anchorMax = new Vector2(0f, 1f);
            _tabColumn.pivot = new Vector2(0f, 1f);
            _tabColumn.sizeDelta = new Vector2(_tabWidth, 0f);
            _tabColumn.anchoredPosition = Vector2.zero;
            var tvlg = tabGo.GetComponent<VerticalLayoutGroup>();
            tvlg.padding = new RectOffset(6, 6, 6, 6); tvlg.spacing = 6;
            tvlg.childForceExpandHeight = false; tvlg.childControlHeight = true;
            tvlg.childForceExpandWidth = true; tvlg.childControlWidth = true;

            // Scroll view (right of the tabs) for the building grid.
            var scrollGo = new GameObject("Grid", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(_root.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = new Vector2(_tabWidth + 6f, 6f); srt.offsetMax = new Vector2(-6f, -6f);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            _gridContent = (RectTransform)contentGo.transform;
            _gridContent.anchorMin = new Vector2(0f, 1f); _gridContent.anchorMax = new Vector2(1f, 1f);
            _gridContent.pivot = new Vector2(0.5f, 1f);
            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = _cellSize; grid.spacing = new Vector2(6f, 6f);
            grid.padding = new RectOffset(6, 6, 6, 6);
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _gridContent;
        }

        private void BuildTabs()
        {
            if (_tabColumn == null) return;
            for (int i = _tabColumn.childCount - 1; i >= 0; i--) Destroy(_tabColumn.GetChild(i).gameObject);

            // Ensure the active category is one that actually has buildings; else pick the first non-empty.
            if (BuildablesIn(_activeCategory).Count == 0)
                foreach (BuildCategory c in System.Enum.GetValues(typeof(BuildCategory)))
                    if (BuildablesIn(c).Count > 0) { _activeCategory = c; break; }

            foreach (BuildCategory cat in System.Enum.GetValues(typeof(BuildCategory)))
            {
                if (BuildablesIn(cat).Count == 0) continue;
                var captured = cat;
                var tab = MakeButton(_tabColumn, cat.ToString(),
                                     () => { _activeCategory = captured; BuildGrid(); BuildTabs(); });
                // Highlight the active tab.
                tab.GetComponent<Image>().color = cat == _activeCategory ? BgEnabled : BgDisabled;
            }
        }

        private void BuildGrid()
        {
            if (_gridContent == null) return;
            for (int i = _gridContent.childCount - 1; i >= 0; i--) Destroy(_gridContent.GetChild(i).gameObject);
            _cards.Clear();

            foreach (var def in BuildablesIn(_activeCategory))
                _cards.Add(MakeCard(def));
            RefreshAvailability();
        }

        private List<BuildingDefinition> BuildablesIn(BuildCategory cat)
        {
            var list = new List<BuildingDefinition>();
            if (_buildables == null) return list;
            foreach (var b in _buildables) if (b != null && b.Category == cat) list.Add(b);
            return list;
        }

        // ---------- Cards / buttons ----------

        private Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Tab_{label}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = BgDisabled;
            go.GetComponent<LayoutElement>().preferredHeight = 40;
            var t = new GameObject("Label", typeof(RectTransform), typeof(Text));
            t.transform.SetParent(go.transform, false);
            var trt = (RectTransform)t.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = t.GetComponent<Text>();
            txt.text = label; txt.font = _font; txt.fontSize = 14; txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter; txt.raycastTarget = false;
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(onClick);
            return btn;
        }

        private (BuildingDefinition, GameObject, CanvasGroup, Button, Image) MakeCard(BuildingDefinition def)
        {
            var card = new GameObject($"Card_{def.name}", typeof(RectTransform), typeof(Image), typeof(Button));
            card.transform.SetParent(_gridContent, false);
            var bg = card.GetComponent<Image>();
            bg.sprite = _cardSprite; bg.type = Image.Type.Sliced; bg.color = BgEnabled;
            var btn = card.GetComponent<Button>();
            btn.onClick.AddListener(() => OnCardClicked(def));

            // Hover tooltip.
            var hover = card.AddComponent<CardHover>();
            hover.Font = _font;
            hover.Tip = BuildingTooltip(def);

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6); vlg.spacing = 2;
            vlg.childForceExpandHeight = false; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childControlWidth = true;
            vlg.childAlignment = TextAnchor.UpperLeft;

            // Name.
            var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameGo.transform.SetParent(card.transform, false);
            var nameTxt = nameGo.GetComponent<Text>();
            nameTxt.text = def.DisplayName; nameTxt.font = _font; nameTxt.fontSize = 14;
            nameTxt.color = Color.white; nameTxt.alignment = TextAnchor.MiddleLeft;
            nameTxt.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Cost row (icons + numbers).
            var costGo = new GameObject("Cost", typeof(RectTransform), typeof(CanvasGroup), typeof(HorizontalLayoutGroup));
            costGo.transform.SetParent(card.transform, false);
            var costGroup = costGo.GetComponent<CanvasGroup>();
            var chlg = costGo.GetComponent<HorizontalLayoutGroup>();
            chlg.spacing = 6; chlg.childForceExpandHeight = false; chlg.childForceExpandWidth = false;
            chlg.childControlHeight = true; chlg.childControlWidth = true;
            AddCost(costGo.transform, ResourceKind.Wood, def.WoodCost);
            AddCost(costGo.transform, ResourceKind.Stone, def.StoneCost);

            return (def, card, costGroup, btn, bg);
        }

        private void AddCost(Transform parent, ResourceKind kind, int amount)
        {
            if (amount <= 0) return;
            var sprite = IconFor(kind);
            if (sprite != null)
            {
                var ig = new GameObject($"Icon_{kind}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                ig.transform.SetParent(parent, false);
                ig.GetComponent<Image>().sprite = sprite;
                ig.GetComponent<Image>().preserveAspect = true;
                var le = ig.GetComponent<LayoutElement>(); le.preferredWidth = 18; le.preferredHeight = 18;
            }
            var ng = new GameObject("Amount", typeof(RectTransform), typeof(Text));
            ng.transform.SetParent(parent, false);
            var t = ng.GetComponent<Text>();
            t.text = sprite != null ? amount.ToString() : $"{amount} {kind}";
            t.font = _font; t.fontSize = 13; t.color = new Color(0.92f, 0.88f, 0.7f);
            t.alignment = TextAnchor.MiddleLeft; t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private Sprite IconFor(ResourceKind kind)
        {
            foreach (var ki in _resourceIcons) if (ki != null && ki.Kind == kind) return ki.Icon;
            return null;
        }

        private void OnCardClicked(BuildingDefinition def)
        {
            if (_placer == null || def == null) return;
            ulong owner = WorldStartContext.LocalPlayer;
            if (!BuildRequirements.IsUnlocked(def, OwnsCompleted(owner))) return;
            if (ResourceBank.Wood < def.WoodCost) return;
            if (ResourceBank.Get(ResourceKind.Stone) < def.StoneCost) return;
            _placer.Select(def);
        }

        // ---------- Availability ----------

        private void RefreshAvailability()
        {
            if (!_shown) return;
            ulong owner = WorldStartContext.LocalPlayer;
            var owns = OwnsCompleted(owner);
            foreach (var (def, _, costGroup, btn, bg) in _cards)
            {
                bool unlocked = BuildRequirements.IsUnlocked(def, owns);
                bool affordable = ResourceBank.Wood >= def.WoodCost
                                  && ResourceBank.Get(ResourceKind.Stone) >= def.StoneCost;
                bool enabled = unlocked && affordable;
                btn.interactable = enabled;
                bg.color = enabled ? BgEnabled : BgDisabled;
                if (costGroup != null) costGroup.alpha = enabled ? 1f : 0.5f;
            }
        }

        // Builds a predicate: "does this owner own a finished building of this definition?"
        private System.Func<BuildingDefinition, bool> OwnsCompleted(ulong owner)
        {
            return req =>
            {
                if (_placer == null || req == null) return false;
                foreach (var kv in _placer.AllOccupied)
                {
                    if (kv.Value != req) continue;
                    if (!_placer.TryGetBuildingOwner(kv.Key, out var o) || o != owner) continue;
                    if (!_placer.TryGetBuildingOrigin(kv.Key, out var origin)) origin = kv.Key;
                    if (!BuildingConstruction.IsUnderConstruction(origin)) return true;
                }
                return false;
            };
        }

        private static string BuildingTooltip(BuildingDefinition b)
        {
            if (b == null) return "";
            string name = b.name;
            string role =
                name.StartsWith("Hut")        ? "Raises your population cap so you can field more units." :
                name.StartsWith("Barracks")   ? "Trains military units (Club + Archer)." :
                name.StartsWith("Docks")      ? "Coastal building. Trains transport boats to cross water." :
                name.StartsWith("Workshop")   ? "Researches permanent upgrades (paid in ore)." :
                name.StartsWith("Wheatfield") ? "Once built, becomes a harvestable wheat field (food)." :
                name == "Mill"                ? "Extra resource drop-off point — workers deliver to the nearest Keep or Mill." :
                "Building.";
            string pop = b.PopulationProvided > 0 ? $"\n+{b.PopulationProvided} population cap" : "";
            string req = "";
            if (b.Requires != null && b.Requires.Length > 0)
            {
                var names = new List<string>();
                foreach (var r in b.Requires) if (r != null) names.Add(r.DisplayName);
                if (names.Count > 0) req = "\nRequires: " + string.Join(", ", names);
            }
            return role + pop + req;
        }

        // Shared hover-tooltip forwarder (same shape as ObjectInspector's).
        private sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Font Font;
            public string Tip;
            public void OnPointerEnter(PointerEventData e) => UITooltip.Show(Tip, Font);
            public void OnPointerExit(PointerEventData e)  => UITooltip.Hide();
        }
    }
}
```

- [ ] **Step 2: Recompile + verify 0 errors.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildMenu.cs Assets/Scripts/World/Unity/BuildMenu.cs.meta
git commit -m "feat(ui): BuildMenu left sidebar (category tabs + scrollable building grid)"
```

---

## Task 7: Remove the build menu from ObjectInspector

**Files:**
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs`

**Context:** ObjectInspector still has the stop-gap build menu. Now that BuildMenu owns it, strip it: the `BuildCategories` table, `_buildCategory` field, `CategoryOf`, `BuildCategoryMenu`, `BuildablesIn`, the category-aware `BuildBuildingCards`, and the `_farmerBuildables` serialized field. In `ShowGoblinSelection`, the block that called `BuildCategoryMenu()` should just clear cards (the sidebar handles building now).

- [ ] **Step 1: In `ShowGoblinSelection`, replace the build-menu trigger.** Find:

```csharp
            if (hasFarmer && _farmerBuildables != null && _farmerBuildables.Count > 0)
            {
                _buildCategory = null;        // always reopen at the top-level category list
                BuildCategoryMenu();
            }
            else
                ClearCards();
```

Replace with:

```csharp
            // The build menu now lives in the BuildMenu sidebar; the inspector just clears its cards here.
            ClearCards();
```

- [ ] **Step 2: Delete the category table + `_buildCategory` + `CategoryOf`.** Find and DELETE:

```csharp
        // Build menu categories: the farmer build list is grouped so 6+ buildings aren't crammed in one row.
        // Top level shows category buttons; clicking one shows that category's buildings + a Back card.
        private static readonly (string label, string[] names)[] BuildCategories =
        {
            ("Economy",  new[] { "Huts", "Wheatfield", "Mill", "Workshop" }),
            ("Military", new[] { "Barracks" }),
            ("Naval",    new[] { "Docks" }),
        };
        private string _buildCategory;          // null = showing category buttons; else the open category label

        // Which category a building belongs to (by asset-name prefix). "" if none matched.
        private static string CategoryOf(BuildingDefinition b)
        {
            if (b == null) return "";
            foreach (var (label, names) in BuildCategories)
                foreach (var n in names)
                    if (b.name.StartsWith(n)) return label;
            return "";
        }
```

- [ ] **Step 3: Delete `BuildCategoryMenu`, `BuildablesIn`, and the category `BuildBuildingCards`.** Find and DELETE the three methods (from the `// Top level of the build menu` comment block through the end of the `BuildBuildingCards()` method that takes no args).

- [ ] **Step 4: Delete the `_farmerBuildables` field.** Find and DELETE:

```csharp
        [Header("Worker Build Options")]
        [Tooltip("Buildings a Farmer Goblin can construct when selected")]
        [SerializeField] private List<BuildingDefinition> _farmerBuildables = new();
```

- [ ] **Step 5: Recompile + verify 0 errors.** (If the compiler flags an unused `_resourceIcons`/`IconFor`/`AddCostText`/`BuildingTooltip` in ObjectInspector — those are still used by the train/upgrade cards, so they stay. Only remove what Steps 1–4 name.)

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "refactor(ui): remove build menu from ObjectInspector (moved to BuildMenu sidebar)"
```

---

## Task 8: Scene wiring + per-building categories (MCP)

**Files:**
- Modify (MCP): `Assets/Scenes/SampleScene.unity`, `Assets/Generated/Buildings/*.asset`

- [ ] **Step 1: Set each building's `Category`** via `Unity_RunCommand`:

```csharp
using UnityEngine; using UnityEditor; using System.Text; using RTSCL.World.Unity;
internal class CommandScript : IRunCommand {
  public void Execute(ExecutionResult result) {
    var sb = new StringBuilder();
    void Set(string path, BuildCategory cat) {
      var d = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
      if (d == null) { sb.AppendLine($"MISSING {path}"); return; }
      var so = new SerializedObject(d); so.FindProperty("Category").enumValueIndex = (int)cat;
      so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(d);
      sb.AppendLine($"{d.name} -> {cat}");
    }
    Set("Assets/Generated/Buildings/Huts_0.asset", BuildCategory.Economy);
    Set("Assets/Generated/Buildings/Workshop.asset", BuildCategory.Economy);
    Set("Assets/Generated/Buildings/Wheatfield.asset", BuildCategory.Economy);
    Set("Assets/Generated/Buildings/Mill.asset", BuildCategory.Economy);
    Set("Assets/Generated/Buildings/Barracks_0.asset", BuildCategory.Military);
    Set("Assets/Generated/Buildings/Docks_0.asset", BuildCategory.Naval);
    AssetDatabase.SaveAssets();
    result.Log(sb.ToString());
  }
}
```
Expected log: each building mapped to its category.

- [ ] **Step 2: Create the BuildMenu GameObject + wire it**, copying the buildables + resource icons from ObjectInspector's old serialized data (read BEFORE we lost it: the 6 building assets; the resource icons live on ResourceUI). Run via MCP:

```csharp
using UnityEngine; using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine.SceneManagement;
using System.Text; using System.Collections.Generic; using RTSCL.World.Unity;
internal class CommandScript : IRunCommand {
  public void Execute(ExecutionResult result) {
    var sb = new StringBuilder();
    var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);

    // Reuse the existing GameManager-ish object that holds ObjectInspector, so refs resolve in the same scene.
    var insp = Object.FindFirstObjectByType<ObjectInspector>(FindObjectsInactive.Include);
    var iso = new SerializedObject(insp);
    var font = iso.FindProperty("_cardFont").objectReferenceValue as Font;
    var cardSprite = iso.FindProperty("_cardSprite").objectReferenceValue as Sprite;

    // Buildables: load the 6 building assets AFTER OpenScene.
    string[] paths = {
      "Assets/Generated/Buildings/Huts_0.asset","Assets/Generated/Buildings/Barracks_0.asset",
      "Assets/Generated/Buildings/Workshop.asset","Assets/Generated/Buildings/Docks_0.asset",
      "Assets/Generated/Buildings/Wheatfield.asset","Assets/Generated/Buildings/Mill.asset" };

    // Create / find BuildMenu host.
    var existing = Object.FindFirstObjectByType<BuildMenu>(FindObjectsInactive.Include);
    GameObject host = existing != null ? existing.gameObject : new GameObject("BuildMenu");
    var bm = existing != null ? existing : host.AddComponent<BuildMenu>();
    var bso = new SerializedObject(bm);

    bso.FindProperty("_selectionController").objectReferenceValue = Object.FindFirstObjectByType<GoblinSelectionController>();
    bso.FindProperty("_placer").objectReferenceValue = Object.FindFirstObjectByType<BuildingPlacer>();
    bso.FindProperty("_font").objectReferenceValue = font;
    bso.FindProperty("_cardSprite").objectReferenceValue = cardSprite;

    var blist = bso.FindProperty("_buildables");
    blist.ClearArray();
    for (int i = 0; i < paths.Length; i++) {
      var d = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(paths[i]);
      blist.InsertArrayElementAtIndex(i); blist.GetArrayElementAtIndex(i).objectReferenceValue = d;
    }

    // Resource icons: copy from ResourceUI._kinds.
    var rui = Object.FindFirstObjectByType<ResourceUI>(FindObjectsInactive.Include);
    var rk = new SerializedObject(rui).FindProperty("_kinds");
    var ik = bso.FindProperty("_resourceIcons");
    ik.ClearArray();
    for (int i = 0; i < rk.arraySize; i++) {
      ik.InsertArrayElementAtIndex(i);
      ik.GetArrayElementAtIndex(i).FindPropertyRelative("Kind").enumValueIndex =
        rk.GetArrayElementAtIndex(i).FindPropertyRelative("Kind").enumValueIndex;
      ik.GetArrayElementAtIndex(i).FindPropertyRelative("Icon").objectReferenceValue =
        rk.GetArrayElementAtIndex(i).FindPropertyRelative("Icon").objectReferenceValue;
    }

    bso.ApplyModifiedPropertiesWithoutUndo();
    EditorUtility.SetDirty(bm);
    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);

    // Verify after reopen.
    EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
    var v = new SerializedObject(Object.FindFirstObjectByType<BuildMenu>(FindObjectsInactive.Include));
    sb.AppendLine($"buildables={v.FindProperty("_buildables").arraySize} icons={v.FindProperty("_resourceIcons").arraySize} " +
                  $"sel={v.FindProperty("_selectionController").objectReferenceValue!=null} placer={v.FindProperty("_placer").objectReferenceValue!=null} " +
                  $"font={v.FindProperty("_font").objectReferenceValue!=null} cardSprite={v.FindProperty("_cardSprite").objectReferenceValue!=null}");
    result.Log(sb.ToString());
  }
}
```
Expected: `buildables=6 icons=6 sel=True placer=True font=True cardSprite=True`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scenes/SampleScene.unity Assets/Generated/Buildings/
git commit -m "chore(scene): add BuildMenu sidebar, wire buildables+icons, set building categories"
```

---

## Task 9: Verification probe + docs

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Probe category grouping + IsUnlocked** via MCP:

```csharp
using UnityEngine; using UnityEditor; using System.Text; using RTSCL.World.Unity;
internal class CommandScript : IRunCommand {
  public void Execute(ExecutionResult result) {
    var sb = new StringBuilder();
    string[] paths = {
      "Assets/Generated/Buildings/Huts_0.asset","Assets/Generated/Buildings/Barracks_0.asset",
      "Assets/Generated/Buildings/Workshop.asset","Assets/Generated/Buildings/Docks_0.asset",
      "Assets/Generated/Buildings/Wheatfield.asset","Assets/Generated/Buildings/Mill.asset" };
    foreach (var p in paths) {
      var d = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(p);
      sb.AppendLine($"{d.name}: cat={d.Category} requires={(d.Requires!=null?d.Requires.Length:0)} unlocked(empty)={BuildRequirements.IsUnlocked(d, _=>false)}");
    }
    result.Log(sb.ToString());
  }
}
```
Expected: Huts/Workshop/Wheatfield/Mill = Economy, Barracks = Military, Docks = Naval; all `requires=0`; all `unlocked(empty)=True`.

- [ ] **Step 2: Run the 46 EditMode tests** (42 existing + 4 new). Expected: all pass.

- [ ] **Step 3: Update CLAUDE.md** — under "Code conventions" / asset-driven definitions, add: "`BuildingDefinition` also carries `Category` (BuildCategory enum → build-menu tab) + `Requires` (prereq buildings; empty = always buildable). The left **BuildMenu** sidebar groups buildables by category with a scrollable grid + locked/greyed states via `BuildRequirements.IsUnlocked`; the bottom `ObjectInspector` only inspects + shows train/upgrade cards. Hover tooltips use the shared `UITooltip` (auto-sized)."

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(claude.md): build menu sidebar, BuildCategory/Requires, UITooltip"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** BuildCategory (T1), Category+Requires fields (T2), BuildRequirements+tests (T3), shared dynamic UITooltip (T4), ObjectInspector→UITooltip (T5), BuildMenu sidebar left-edge + tabs + scroll grid + locked states (T6), remove old menu (T7), scene wiring + categories (T8), verify + docs (T9). All spec sections mapped.
- **Migration:** `_farmerBuildables` (6 refs) → `BuildMenu._buildables` (T8), categories set (T8), Requires left empty (default).
- **Type consistency:** `BuildRequirements.IsUnlocked(def, Func<BuildingDefinition,bool>)` used identically in tests (T3), BuildMenu `OnCardClicked`/`RefreshAvailability` (T6). `ResourceUI.KindIcon` reused for `_resourceIcons` (T6/T8). `BuildingPlacer.AllOccupied`/`TryGetBuildingOwner`/`TryGetBuildingOrigin`/`Select` all exist.
- **Tooltip:** `UITooltip.Show(string,Font)` / `Hide()` used by both CardHover variants (T4/T5/T6).
- **Known Unity-MCP caveat:** new serialized fields may need explicit set if Unity zeroes them; T8 sets all BuildMenu fields + verifies after reopen. Building-asset `Category` set in T8 step 1.

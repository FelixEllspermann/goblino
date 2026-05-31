// BuildMenu.cs — left-edge action sidebar. Two modes:
//   • Build   (a Farmer is selected): category tabs + scrollable grid of buildable buildings → ghost placement.
//   • Actions (one of the player's own buildings is selected): that building's train cards (Keep→Farmer,
//             Barracks→Club/Archer, Docks→Boat) and/or upgrade cards (Workshop/Mill), with a production
//             progress line + queue count. Clicking trains/queues a unit or researches an upgrade.
// Driven by GoblinSelectionController.OnSelectionChanged (farmer) and ObjectInspector.OnBuildingInspected /
// OnInspectionCleared (building). The bottom ObjectInspector is info-only now (stats, no action cards).
// Built entirely at runtime (no prefab). Uses the shared UITooltip.
using System;
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
        [Tooltip("All buildings a Farmer can construct.")]
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

        private enum Mode { None, Build, Actions }
        private Mode _mode = Mode.None;

        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _tabColumn;
        private ScrollRect _scroll;
        private RectTransform _gridContent;
        private GridLayoutGroup _grid;
        private BuildCategory _activeCategory = BuildCategory.Economy;
        private bool _built;

        // Build-mode state.
        private bool _farmerSelected;
        // Actions-mode state (a selected own building).
        private bool _buildingActive;
        private Vector2Int _buildingOrigin;
        private BuildingDefinition _buildingDef;

        // Build-mode cards (buildings to place).
        private readonly List<(BuildingDefinition def, CanvasGroup costGroup, Button btn, Image bg)> _buildCards = new();
        // Actions-mode cards (train a unit / buy an upgrade). Availability via the predicate.
        private readonly List<ActionCard> _actionCards = new();
        private Text _progressLabel;
        private GameObject _progressRow;     // the whole progress widget (bar + label); hidden when idle
        private RectTransform _progressFill;  // scaled horizontally 0..1 by production progress

        private sealed class ActionCard
        {
            public GoblinUnitDefinition Unit;       // exactly one of Unit / Upgrade set
            public UpgradeDefinition Upgrade;
            public Button Btn;
            public Image Bg;
            public CanvasGroup CostGroup;
            public GameObject Root;                 // whole card (hidden when a single/maxed upgrade is done)
            public Text Label;                      // name label (shows "(Lv n/max)" for leveled upgrades)
            public int ShownLevel = -1;             // last level rendered, so cost/label rebuild only on change
        }

        private static readonly Color BgEnabled  = new(0.18f, 0.18f, 0.22f, 0.95f);
        private static readonly Color BgDisabled = new(0.10f, 0.10f, 0.12f, 0.7f);

        private void Start()
        {
            _canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (_selectionController != null)
                _selectionController.OnSelectionChanged += OnSelectionChanged;
            ObjectInspector.OnBuildingInspected += OnBuildingInspected;
            ObjectInspector.OnInspectionCleared += OnInspectionCleared;
            ResourceBank.OnChanged += OnResourceChanged;
            GoblinProduction.OnChanged += RefreshActions;
            BuildingConstruction.OnCompleted += OnConstructionCompleted;
            Resolve();
        }

        private void OnDestroy()
        {
            if (_selectionController != null)
                _selectionController.OnSelectionChanged -= OnSelectionChanged;
            ObjectInspector.OnBuildingInspected -= OnBuildingInspected;
            ObjectInspector.OnInspectionCleared -= OnInspectionCleared;
            ResourceBank.OnChanged -= OnResourceChanged;
            GoblinProduction.OnChanged -= RefreshActions;
            BuildingConstruction.OnCompleted -= OnConstructionCompleted;
        }

        private void OnResourceChanged(ResourceKind k, int v) { RefreshBuildAvailability(); RefreshActions(); }
        private void OnConstructionCompleted(Vector2Int o) { RefreshBuildAvailability(); RefreshActions(); }

        // ---------- Mode resolution ----------

        private void OnSelectionChanged()
        {
            _farmerSelected = false;
            if (_selectionController != null)
                foreach (var g in _selectionController.Selection)
                    if (g != null && g.Kind == "FarmerGoblin") { _farmerSelected = true; break; }
            // Selecting units supersedes a previously-inspected building.
            if (_farmerSelected) _buildingActive = false;
            Resolve();
        }

        private void OnBuildingInspected(Vector2Int origin, BuildingDefinition def, bool isLocal)
        {
            // Only OWN, FINISHED buildings with something to do (train or upgrade) open the actions panel.
            // A building still under construction is not usable yet — no train/upgrade cards.
            bool hasActions = isLocal && def != null && !BuildingConstruction.IsUnderConstruction(origin) &&
                ((def.TrainsUnits != null && def.TrainsUnits.Length > 0) ||
                 (def.ProvidesUpgrades != null && def.ProvidesUpgrades.Length > 0));
            if (!hasActions) { _buildingActive = false; Resolve(); return; }
            _buildingActive = true;
            _buildingOrigin = origin;
            _buildingDef = def;
            _farmerSelected = false;   // a building click clears the unit selection anyway
            Resolve();
        }

        private void OnInspectionCleared()
        {
            _buildingActive = false;
            Resolve();
        }

        // Pick the mode: a selected Farmer (build) wins; else a selected own building (actions); else hide.
        private void Resolve()
        {
            Mode want = _farmerSelected ? Mode.Build : _buildingActive ? Mode.Actions : Mode.None;
            _mode = want;
            if (want == Mode.None) { if (_root != null) _root.SetActive(false); return; }

            EnsureBuilt();
            if (want == Mode.Build) BuildBuildMode();
            else BuildActionsMode();
            _root.SetActive(true);
        }

        // ---------- UI scaffold (built once) ----------

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

            var scrollGo = new GameObject("Grid", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(_root.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = new Vector2(_tabWidth + 6f, 6f); srt.offsetMax = new Vector2(-6f, -6f);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false; _scroll.vertical = true; _scroll.scrollSensitivity = 24f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var vrt = (RectTransform)viewportGo.transform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            vrt.pivot = new Vector2(0f, 1f);
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            _gridContent = (RectTransform)contentGo.transform;
            _gridContent.anchorMin = new Vector2(0f, 1f); _gridContent.anchorMax = new Vector2(1f, 1f);
            _gridContent.pivot = new Vector2(0f, 1f);
            _gridContent.anchoredPosition = Vector2.zero;
            _grid = contentGo.GetComponent<GridLayoutGroup>();
            _grid.spacing = new Vector2(6f, 6f);
            _grid.padding = new RectOffset(6, 6, 6, 6);
            _grid.childAlignment = TextAnchor.UpperLeft;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.viewport = vrt;
            _scroll.content = _gridContent;
        }

        private void ClearGrid()
        {
            for (int i = _gridContent.childCount - 1; i >= 0; i--) Destroy(_gridContent.GetChild(i).gameObject);
            _buildCards.Clear();
            _actionCards.Clear();
            _progressLabel = null;
        }

        private void ClearTabs()
        {
            for (int i = _tabColumn.childCount - 1; i >= 0; i--) Destroy(_tabColumn.GetChild(i).gameObject);
        }

        // ---------- Build mode (farmer selected) ----------

        private void BuildBuildMode()
        {
            ClearTabs();
            ClearGrid();
            _tabColumn.gameObject.SetActive(true);

            if (BuildablesIn(_activeCategory).Count == 0)
                foreach (BuildCategory c in Enum.GetValues(typeof(BuildCategory)))
                    if (BuildablesIn(c).Count > 0) { _activeCategory = c; break; }

            foreach (BuildCategory cat in Enum.GetValues(typeof(BuildCategory)))
            {
                if (BuildablesIn(cat).Count == 0) continue;
                var captured = cat;
                var tab = MakeTab(cat.ToString(), () => { _activeCategory = captured; BuildBuildMode(); });
                tab.GetComponent<Image>().color = cat == _activeCategory ? BgEnabled : BgDisabled;
            }

            // Multi-column grid for buildings.
            _grid.cellSize = _cellSize;
            float avail = _panelWidth - _tabWidth - 12f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((avail - _grid.padding.left - _grid.padding.right + _grid.spacing.x)
                                                     / (_cellSize.x + _grid.spacing.x)));
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = cols;

            foreach (var def in BuildablesIn(_activeCategory))
                _buildCards.Add(MakeBuildCard(def));
            RefreshBuildAvailability();
        }

        private List<BuildingDefinition> BuildablesIn(BuildCategory cat)
        {
            var list = new List<BuildingDefinition>();
            if (_buildables == null) return list;
            foreach (var b in _buildables) if (b != null && b.Category == cat) list.Add(b);
            return list;
        }

        private (BuildingDefinition, CanvasGroup, Button, Image) MakeBuildCard(BuildingDefinition def)
        {
            var (card, bg, btn, costGroup, _) = MakeCardShell(def.DisplayName, BuildingTooltip(def), () => OnBuildCardClicked(def));
            AddCost(costGroup.transform, ResourceKind.Wood, def.WoodCost);
            AddCost(costGroup.transform, ResourceKind.Stone, def.StoneCost);
            return (def, costGroup, btn, bg);
        }

        private void OnBuildCardClicked(BuildingDefinition def)
        {
            if (_placer == null || def == null) return;
            ulong owner = WorldStartContext.LocalPlayer;
            if (!BuildRequirements.IsUnlocked(def, OwnsCompleted(owner))) return;
            if (ResourceBank.Wood < def.WoodCost) return;
            if (ResourceBank.Get(ResourceKind.Stone) < def.StoneCost) return;
            _placer.Select(def);
        }

        private void RefreshBuildAvailability()
        {
            if (_mode != Mode.Build) return;
            ulong owner = WorldStartContext.LocalPlayer;
            var owns = OwnsCompleted(owner);
            foreach (var (def, costGroup, btn, bg) in _buildCards)
            {
                bool enabled = BuildRequirements.IsUnlocked(def, owns)
                               && ResourceBank.Wood >= def.WoodCost
                               && ResourceBank.Get(ResourceKind.Stone) >= def.StoneCost;
                btn.interactable = enabled;
                bg.color = enabled ? BgEnabled : BgDisabled;
                if (costGroup != null) costGroup.alpha = enabled ? 1f : 0.5f;
            }
        }

        // ---------- Actions mode (own building selected) ----------

        private void BuildActionsMode()
        {
            ClearTabs();
            ClearGrid();
            // Left column shows the building name as a header instead of clickable tabs.
            _tabColumn.gameObject.SetActive(true);
            var header = MakeTab(_buildingDef != null ? _buildingDef.DisplayName : "Building", null);
            header.interactable = false;
            header.GetComponent<Image>().color = BgEnabled;

            // Single column of action cards spanning the grid width.
            float avail = _panelWidth - _tabWidth - 12f - _grid.padding.left - _grid.padding.right;
            _grid.cellSize = new Vector2(Mathf.Max(80f, avail), _cellSize.y);
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = 1;

            // Progress widget (first row): a dark track with a green fill bar + a label on top. Shown only
            // while the building is producing; the fill is scaled 0..1 each frame in UpdateProgressLabel().
            _progressRow = new GameObject("ProgressRow", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            _progressRow.transform.SetParent(_gridContent, false);
            _progressRow.GetComponent<LayoutElement>().preferredHeight = 24;
            _progressRow.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);   // track

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(_progressRow.transform, false);
            _progressFill = (RectTransform)fillGo.transform;
            _progressFill.anchorMin = new Vector2(0f, 0f); _progressFill.anchorMax = new Vector2(1f, 1f);
            _progressFill.offsetMin = Vector2.zero; _progressFill.offsetMax = Vector2.zero;
            _progressFill.pivot = new Vector2(0f, 0.5f);   // scale from the left edge
            fillGo.GetComponent<Image>().color = new Color(0.3f, 0.75f, 0.35f, 0.9f);
            fillGo.GetComponent<Image>().raycastTarget = false;

            var lblGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            lblGo.transform.SetParent(_progressRow.transform, false);
            var lrt = (RectTransform)lblGo.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(6f, 0f); lrt.offsetMax = new Vector2(-6f, 0f);
            _progressLabel = lblGo.GetComponent<Text>();
            _progressLabel.font = _font; _progressLabel.fontSize = 12;
            _progressLabel.color = Color.white;
            _progressLabel.alignment = TextAnchor.MiddleLeft;
            _progressLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _progressLabel.raycastTarget = false;

            if (_buildingDef.TrainsUnits != null)
                foreach (var u in _buildingDef.TrainsUnits)
                    if (u != null) _actionCards.Add(MakeUnitCard(u));
            if (_buildingDef.ProvidesUpgrades != null)
                foreach (var up in _buildingDef.ProvidesUpgrades)
                    if (up != null) _actionCards.Add(MakeUpgradeCard(up));

            RefreshActions();
        }

        private ActionCard MakeUnitCard(GoblinUnitDefinition u)
        {
            var (card, bg, btn, costGroup, label) = MakeCardShell(u.DisplayName, UnitTooltip(u), () => OnTrainClicked(u));
            AddCost(costGroup.transform, ResourceKind.Wood, u.WoodCost);
            AddCost(costGroup.transform, ResourceKind.Food, u.FoodCost);
            return new ActionCard { Unit = u, Btn = btn, Bg = bg, CostGroup = costGroup, Root = card, Label = label };
        }

        private ActionCard MakeUpgradeCard(UpgradeDefinition up)
        {
            var (card, bg, btn, costGroup, label) = MakeCardShell(up.DisplayName, UpgradeTooltip(up), () => OnUpgradeClicked(up));
            // Cost row is (re)built per current level in RefreshActions.
            return new ActionCard { Upgrade = up, Btn = btn, Bg = bg, CostGroup = costGroup, Root = card, Label = label };
        }

        private void OnTrainClicked(GoblinUnitDefinition unit)
        {
            if (!_buildingActive || unit == null) return;
            if (BuildingConstruction.IsUnderConstruction(_buildingOrigin)) return;   // not built yet
            if (!GoblinProduction.CanQueue(_buildingOrigin)) return;
            if (ResourceBank.Wood < unit.WoodCost) return;
            if (ResourceBank.Food < unit.FoodCost) return;
            if (!PopulationManager.CanAfford(unit.PopulationCost)) return;
            if (unit.WoodCost > 0) ResourceBank.AddWood(-unit.WoodCost);
            if (unit.FoodCost > 0) ResourceBank.AddFood(-unit.FoodCost);
            NetCommandIssuer.IssueTrainUnit(_buildingOrigin, unit, WorldStartContext.LocalPlayer);
            RefreshActions();
        }

        private void OnUpgradeClicked(UpgradeDefinition upgrade)
        {
            if (upgrade == null) return;
            if (BuildingConstruction.IsUnderConstruction(_buildingOrigin)) return;   // not built yet
            ulong owner = WorldStartContext.LocalPlayer;
            int next = PlayerUpgrades.Level(owner, upgrade.Kind) + 1;
            if (next > Mathf.Max(1, upgrade.MaxLevel)) return;                        // already maxed
            int wood = upgrade.WoodFor(next), iron = upgrade.IronFor(next), gold = upgrade.GoldFor(next), crystal = upgrade.CrystalFor(next);
            if (ResourceBank.Wood < wood || ResourceBank.Get(ResourceKind.Iron) < iron
                || ResourceBank.Get(ResourceKind.Gold) < gold || ResourceBank.Get(ResourceKind.Crystal) < crystal) return;
            if (wood > 0)    ResourceBank.AddWood(-wood);
            if (iron > 0)    ResourceBank.Add(ResourceKind.Iron, -iron);
            if (gold > 0)    ResourceBank.Add(ResourceKind.Gold, -gold);
            if (crystal > 0) ResourceBank.Add(ResourceKind.Crystal, -crystal);
            NetCommandIssuer.IssuePurchaseUpgrade(upgrade.Kind, owner);
            RefreshActions();
        }

        private void RefreshActions()
        {
            if (_mode != Mode.Actions || !_buildingActive) return;
            ulong owner = WorldStartContext.LocalPlayer;
            bool queueFull = !GoblinProduction.CanQueue(_buildingOrigin);
            foreach (var c in _actionCards)
            {
                bool enabled;
                if (c.Unit != null)
                {
                    enabled = !queueFull
                              && ResourceBank.Wood >= c.Unit.WoodCost
                              && ResourceBank.Food >= c.Unit.FoodCost
                              && PopulationManager.CanAfford(c.Unit.PopulationCost);
                }
                else
                {
                    int max = Mathf.Max(1, c.Upgrade.MaxLevel);
                    int lvl = PlayerUpgrades.Level(owner, c.Upgrade.Kind);
                    if (lvl >= max)   // fully researched → hide the card (don't show finished upgrades)
                    {
                        if (c.Root != null) c.Root.SetActive(false);
                        continue;
                    }
                    if (c.Root != null && !c.Root.activeSelf) c.Root.SetActive(true);
                    int next = lvl + 1;
                    int wood = c.Upgrade.WoodFor(next), iron = c.Upgrade.IronFor(next),
                        gold = c.Upgrade.GoldFor(next), crystal = c.Upgrade.CrystalFor(next);
                    // Rebuild the cost row + level label only when the level actually changed (avoids churn).
                    if (c.ShownLevel != lvl)
                    {
                        c.ShownLevel = lvl;
                        RebuildUpgradeCost(c.CostGroup, wood, iron, gold, crystal);
                        if (c.Label != null)
                            c.Label.text = max > 1 ? $"{c.Upgrade.DisplayName}  (Lv {next}/{max})" : c.Upgrade.DisplayName;
                    }
                    enabled = ResourceBank.Wood >= wood
                              && ResourceBank.Get(ResourceKind.Iron) >= iron
                              && ResourceBank.Get(ResourceKind.Gold) >= gold
                              && ResourceBank.Get(ResourceKind.Crystal) >= crystal;
                }
                c.Btn.interactable = enabled;
                c.Bg.color = enabled ? BgEnabled : BgDisabled;
                if (c.CostGroup != null) c.CostGroup.alpha = enabled ? 1f : 0.5f;
            }
            UpdateProgressLabel();
        }

        private void Update()
        {
            if (_mode == Mode.Actions && _buildingActive && _progressLabel != null) UpdateProgressLabel();
        }

        private void UpdateProgressLabel()
        {
            if (_progressRow == null) return;
            var slot = GoblinProduction.Get(_buildingOrigin);
            if (slot == null) { _progressRow.SetActive(false); return; }   // idle → hide the whole widget
            _progressRow.SetActive(true);
            // Scale the fill bar horizontally from the left (pivot.x = 0) to the production fraction.
            if (_progressFill != null)
                _progressFill.localScale = new Vector3(Mathf.Clamp01(slot.Progress), 1f, 1f);
            int waiting = GoblinProduction.QueuedBehind(_buildingOrigin);
            string pct = $"{Mathf.RoundToInt(slot.Progress * 100f)}%";
            _progressLabel.text = waiting > 0
                ? $"{slot.Def.DisplayName}  {pct}  (+{waiting})"
                : $"{slot.Def.DisplayName}  {pct}";
        }

        // ---------- Shared card/tab building ----------

        // A card shell: bg + button + name label + an (empty) cost row. Returns the cost row's transform
        // (via its CanvasGroup) so the caller appends cost icons.
        private (GameObject card, Image bg, Button btn, CanvasGroup costGroup, Text label) MakeCardShell(string title, string tooltip, UnityEngine.Events.UnityAction onClick)
        {
            var card = new GameObject($"Card_{title}", typeof(RectTransform), typeof(Image), typeof(Button));
            card.transform.SetParent(_gridContent, false);
            var bg = card.GetComponent<Image>();
            bg.sprite = _cardSprite; bg.type = Image.Type.Sliced; bg.color = BgEnabled;
            var btn = card.GetComponent<Button>();
            btn.onClick.AddListener(onClick);

            if (!string.IsNullOrEmpty(tooltip))
            {
                var hover = card.AddComponent<CardHover>();
                hover.Font = _font;
                hover.Tip = tooltip;
            }

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6); vlg.spacing = 2;
            vlg.childForceExpandHeight = false; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childControlWidth = true;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameGo.transform.SetParent(card.transform, false);
            var nameTxt = nameGo.GetComponent<Text>();
            nameTxt.text = title; nameTxt.font = _font; nameTxt.fontSize = 14;
            nameTxt.color = Color.white; nameTxt.alignment = TextAnchor.MiddleLeft;
            nameTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameTxt.raycastTarget = false;

            var costGo = new GameObject("Cost", typeof(RectTransform), typeof(CanvasGroup), typeof(HorizontalLayoutGroup));
            costGo.transform.SetParent(card.transform, false);
            var costGroup = costGo.GetComponent<CanvasGroup>();
            var chlg = costGo.GetComponent<HorizontalLayoutGroup>();
            chlg.spacing = 6; chlg.childForceExpandHeight = false; chlg.childForceExpandWidth = false;
            chlg.childControlHeight = true; chlg.childControlWidth = true;
            return (card, bg, btn, costGroup, nameTxt);
        }

        private Button MakeTab(string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Tab_{label}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_tabColumn, false);
            go.GetComponent<Image>().color = BgDisabled;
            go.GetComponent<LayoutElement>().preferredHeight = 40;
            var t = new GameObject("Label", typeof(RectTransform), typeof(Text));
            t.transform.SetParent(go.transform, false);
            var trt = (RectTransform)t.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(2f, 0f); trt.offsetMax = new Vector2(-2f, 0f);
            var txt = t.GetComponent<Text>();
            txt.text = label; txt.font = _font; txt.color = Color.white;
            txt.resizeTextForBestFit = true; txt.resizeTextMinSize = 8; txt.resizeTextMaxSize = 14;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.alignment = TextAnchor.MiddleCenter; txt.raycastTarget = false;
            var btn = go.GetComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        // Rebuild an upgrade card's cost row to reflect the current level's price.
        private void RebuildUpgradeCost(CanvasGroup costGroup, int wood, int iron, int gold, int crystal)
        {
            if (costGroup == null) return;
            var t = costGroup.transform;
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
            AddCost(t, ResourceKind.Wood, wood);
            AddCost(t, ResourceKind.Iron, iron);
            AddCost(t, ResourceKind.Gold, gold);
            AddCost(t, ResourceKind.Crystal, crystal);
        }

        private void AddCost(Transform parent, ResourceKind kind, int amount)
        {
            if (amount <= 0) return;
            var sprite = IconFor(kind);
            if (sprite != null)
            {
                var ig = new GameObject($"Icon_{kind}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                ig.transform.SetParent(parent, false);
                var im = ig.GetComponent<Image>(); im.sprite = sprite; im.preserveAspect = true; im.raycastTarget = false;
                var le = ig.GetComponent<LayoutElement>(); le.preferredWidth = 18; le.preferredHeight = 18;
            }
            var ng = new GameObject("Amount", typeof(RectTransform), typeof(Text));
            ng.transform.SetParent(parent, false);
            var t = ng.GetComponent<Text>();
            t.text = sprite != null ? amount.ToString() : $"{amount} {kind}";
            t.font = _font; t.fontSize = 13; t.color = new Color(0.92f, 0.88f, 0.7f);
            t.alignment = TextAnchor.MiddleLeft; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false;
        }

        private Sprite IconFor(ResourceKind kind)
        {
            foreach (var ki in _resourceIcons) if (ki != null && ki.Kind == kind) return ki.Icon;
            return null;
        }

        private Func<BuildingDefinition, bool> OwnsCompleted(ulong owner)
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

        // ---------- Tooltip text ----------

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

        private static string UnitTooltip(GoblinUnitDefinition u)
        {
            if (u == null) return "";
            string role = u.SpawnerKindName == "FarmerGoblin" ? "Worker: harvests resources and constructs buildings."
                        : u.WaterUnit ? "Transport boat: carries up to 6 units across water."
                        : u.ProjectileSprite != null ? "Ranged unit: fires arrows at enemies and buildings."
                        : u.AttackDamage > 0 ? "Melee fighter: attacks enemies and buildings up close."
                        : "Unit.";
            string stats = u.AttackDamage > 0
                ? $"\nHP {u.MaxHp} · DMG {u.AttackDamage} · RNG {u.AttackRange} · pop {u.PopulationCost}"
                : $"\nHP {u.MaxHp} · pop {u.PopulationCost}";
            return role + stats;
        }

        private static string UpgradeTooltip(UpgradeDefinition u)
        {
            if (u == null) return "";
            string effect = u.Kind switch
            {
                UpgradeKind.FarmerHarvestSpeed   => "Workers harvest faster.",
                UpgradeKind.ClubAttackDamage     => "Club Goblins deal more damage.",
                UpgradeKind.ClubMaxHp            => "Club Goblins have more HP.",
                UpgradeKind.MillBountifulHarvest => "Wheat fields you build yield +100% food (1000 instead of 500).",
                UpgradeKind.FarmerMoveSpeed      => "Workers move 25% faster.",
                UpgradeKind.FarmerCarryCapacity  => "Workers carry 50% more resources per trip.",
                UpgradeKind.BuildSpeed           => "Workers construct buildings 50% faster.",
                UpgradeKind.MeleeArmor           => "Melee units (Club, Spear) gain +5 armor.",
                UpgradeKind.RangedAttackRange    => "Ranged units (Archer) gain +1 attack range.",
                UpgradeKind.AllUnitsDamage       => "All combat units deal 25% more damage.",
                UpgradeKind.UnitTrainSpeed       => "Units train 25% faster.",
                UpgradeKind.SightRange           => "All your units and buildings see 25% farther.",
                UpgradeKind.WallHp               => "Your walls have +50% HP (existing + future).",
                UpgradeKind.TowerDamage          => "Your towers deal +50% arrow damage.",
                _                                => "Permanent upgrade.",
            };
            return effect + "\nOne-time research, applies to all your units.";
        }

        private sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Font Font;
            public string Tip;
            public void OnPointerEnter(PointerEventData e) => UITooltip.Show(Tip, Font);
            public void OnPointerExit(PointerEventData e)  => UITooltip.Hide();
        }
    }
}

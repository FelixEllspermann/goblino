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
        private GameObject _root;
        private RectTransform _tabColumn;
        private RectTransform _gridContent;
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

            if (BuildablesIn(_activeCategory).Count == 0)
                foreach (BuildCategory c in System.Enum.GetValues(typeof(BuildCategory)))
                    if (BuildablesIn(c).Count > 0) { _activeCategory = c; break; }

            foreach (BuildCategory cat in System.Enum.GetValues(typeof(BuildCategory)))
            {
                if (BuildablesIn(cat).Count == 0) continue;
                var captured = cat;
                var tab = MakeButton(_tabColumn, cat.ToString(),
                                     () => { _activeCategory = captured; BuildGrid(); BuildTabs(); });
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

            var hover = card.AddComponent<CardHover>();
            hover.Font = _font;
            hover.Tip = BuildingTooltip(def);

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6); vlg.spacing = 2;
            vlg.childForceExpandHeight = false; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childControlWidth = true;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameGo.transform.SetParent(card.transform, false);
            var nameTxt = nameGo.GetComponent<Text>();
            nameTxt.text = def.DisplayName; nameTxt.font = _font; nameTxt.fontSize = 14;
            nameTxt.color = Color.white; nameTxt.alignment = TextAnchor.MiddleLeft;
            nameTxt.horizontalOverflow = HorizontalWrapMode.Overflow;

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

        private sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Font Font;
            public string Tip;
            public void OnPointerEnter(PointerEventData e) => UITooltip.Show(Tip, Font);
            public void OnPointerExit(PointerEventData e)  => UITooltip.Hide();
        }
    }
}

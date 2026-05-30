// =============================================================================
// ObjectInspector.cs  —  RTSCL.World.Unity
//
// The bottom info/action panel. Three mutually exclusive display modes:
//   Building   — selected by left-clicking a placed building. Shows HP, trains-
//                unit cards (or upgrade cards) for local buildings.
//   Goblins    — driven by GoblinSelectionController.OnSelectionChanged. Shows
//                build-option cards for Farmer Goblins.
//   Decoration — selected by left-clicking a resource tile (tree, ore, wheat).
//                Shows remaining resource amount, live-updated each frame.
//
// Cards are rebuilt from scratch each time the display mode changes (ClearCards
// then BuildXCards). Affordability / pop-cap state is refreshed by Refresh(),
// which is triggered by ResourceBank.OnChanged, PopulationManager.OnChanged,
// and GoblinProduction.OnChanged so the UI stays reactive without polling.
//
// To add a new card type: add a BuildXCards method and a CardRefs entry with
// the relevant cost fields; extend Refresh() affordability logic.
// To add a new resource kind cost: add a field to CardRefs and extend IsValid
// and the affordability check in Refresh().
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    public sealed class ObjectInspector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera _camera;
        [SerializeField] private Tilemap _decorationMap;
        [SerializeField] private Tilemap _terrainMap;
        [SerializeField] private BuildingPlacer _placer;
        [SerializeField] private GoblinSelectionController _selectionController;

        [Header("Popup UI")]
        [SerializeField] private GameObject _popupRoot;
        [SerializeField] private Text _nameLabel;
        [SerializeField] private Text _descriptionLabel;
        [SerializeField] private RectTransform _unitCardsContainer;
        [SerializeField] private Image _progressFill;
        [SerializeField] private Text _progressLabel;
        [SerializeField] private GameObject _progressRow;

        [Header("Cards Style")]
        [SerializeField] private Sprite _cardSprite;
        [SerializeField] private Font _cardFont;
        [SerializeField] private Color _cardEnabledBg = new(0.18f, 0.18f, 0.22f, 0.95f);
        [SerializeField] private Color _cardDisabledBg = new(0.10f, 0.10f, 0.12f, 0.7f);
        [SerializeField] private Color _cardTextNormal = Color.white;
        [SerializeField] private Color _cardTextDisabled = new(0.55f, 0.55f, 0.55f, 1f);

        [Header("Cost Icons")]
        [Tooltip("Resource icons shown on cost rows (same sprites as the top resource bar).")]
        [SerializeField] private List<ResourceUI.KindIcon> _resourceIcons = new();

        private Sprite IconFor(ResourceKind kind)
        {
            foreach (var ki in _resourceIcons) if (ki != null && ki.Kind == kind) return ki.Icon;
            return null;
        }

        [Header("Rally")]
        [Tooltip("Flag icon shown at a building's rally point (GUI_33)")]
        [SerializeField] private Sprite _rallyIcon;

        // Which type of object is currently being inspected.
        private enum SelKind { None, Building, Decoration, Goblins, UnitInfo }
        private SelKind _selKind = SelKind.None;
        private Goblin _inspectedUnit;            // enemy/neutral unit shown in UnitInfo mode (read-only)
        // Cached state for the active display mode; updated each time Show*() is called.
        private Vector2Int _selOrigin;            // SW-corner of selected building (Building mode)
        private BuildingDefinition _selDef;       // definition of selected building
        private BuildingOwner _selBuildingOwner;  // component used to toggle the building selection ring
        private string _lastCarryLine = "";       // cached carry text to avoid redundant label updates
        private string _lastGoblinDescBase = "";  // base description without carry suffix (Goblins mode)
        private Vector3Int _selDecoCell;          // tile-space cell of selected decoration (Decoration mode)
        private bool _selDecoHarvestable;         // whether the selected tile yields a resource
        private string _selDecoBaseDesc = "";     // description without the live resource amount line
        private string _lastAmountLine = "";      // cached amount text to avoid redundant label updates

        /// <summary>Runtime card data: one entry per visible action card. Stores both the UI
        /// component refs (for tinting/enabling) and the cost values (for Refresh() affordability
        /// checks without re-querying the definition each frame).</summary>
        private struct CardRefs
        {
            public GameObject Root;
            public Button Button;
            public Image Bg;
            public Image Icon;
            public Text Name;
            public CanvasGroup CostGroup;   // the cost row (icons + numbers); dimmed when unaffordable
            // Exactly one of Unit / Building / Upgrade is non-null, identifying the card's action type.
            public GoblinUnitDefinition Unit;
            public BuildingDefinition Building;
            public int WoodCost;
            public int FoodCost;
            public int StoneCost;
            public int IronCost;
            public int GoldCost;
            public int CrystalCost;
            public UpgradeDefinition Upgrade;
            public UpgradeKind UpgradeKind;
        }
        private readonly List<CardRefs> _cards = new();

        // Extra line spacing applied to all info-panel / card text for a roomier, more readable layout.
        private const float LineSpacing = 1.45f;

        // Forwards pointer enter/exit on a card to the shared UITooltip. Added to each card GameObject.
        private sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Font Font;
            public string Tip;
            public void OnPointerEnter(PointerEventData e) => UITooltip.Show(Tip, Font);
            public void OnPointerExit(PointerEventData e)  => UITooltip.Hide();
        }

        private void Start()
        {
            if (_popupRoot != null) _popupRoot.SetActive(false);
            if (_progressRow != null) _progressRow.SetActive(false);
            if (_nameLabel != null) _nameLabel.lineSpacing = LineSpacing;
            if (_descriptionLabel != null) _descriptionLabel.lineSpacing = LineSpacing;
            if (_progressLabel != null) _progressLabel.lineSpacing = LineSpacing;
            // Subscribe to resource/population/production changes so cards refresh
            // affordability without polling. Lambda wraps Refresh to match the delegate signature.
            ResourceBank.OnChanged += (_, __) => Refresh();
            PopulationManager.OnChanged += Refresh;
            GoblinProduction.OnChanged += Refresh;
            if (_selectionController != null)
            {
                _selectionController.OnSelectionChanged += OnGoblinSelectionChanged;
                _selectionController.OnInspectUnit += ShowUnitInfo;
            }
        }

        private void OnDestroy()
        {
            // Always unsubscribe static events to prevent lingering delegates after scene reload.
            PopulationManager.OnChanged -= Refresh;
            GoblinProduction.OnChanged -= Refresh;
            if (_selectionController != null)
            {
                _selectionController.OnSelectionChanged -= OnGoblinSelectionChanged;
                _selectionController.OnInspectUnit -= ShowUnitInfo;
            }
        }

        private void Update()
        {
            if (_camera == null || Mouse.current == null) return;
            // Don't intercept clicks during placement mode
            if (_placer != null && _placer.Selected != null) return;

            // Live progress bar while a production runs for the currently-shown keep/barracks
            if (_selKind == SelKind.Building) UpdateProgressUI();
            if (_selKind == SelKind.Goblins) UpdateCarryUI();
            if (_selKind == SelKind.Decoration) UpdateResourceAmount();
            if (_selKind == SelKind.UnitInfo) UpdateInspectedUnit();

            // Right-click while a unit-training building is selected → set/move its rally point.
            if (Mouse.current.rightButton.wasPressedThisFrame
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                && BuildingTakesRally(out var bc))
            {
                Vector2 rmp = Mouse.current.position.ReadValue();
                Vector3 rworld = _camera.ScreenToWorldPoint(new Vector3(rmp.x, rmp.y, -_camera.transform.position.z));
                var rally = new Vector3(Mathf.FloorToInt(rworld.x) + 0.5f, Mathf.FloorToInt(rworld.y) + 0.5f, 0f);
                RallyPoints.Set(_selOrigin, rally);
                RallyVisual.Instance.Show(bc, rally, _rallyIcon);
                return;
            }

            if (!Mouse.current.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 mp = Mouse.current.position.ReadValue();
            Vector3 world = _camera.ScreenToWorldPoint(
                new Vector3(mp.x, mp.y, -_camera.transform.position.z));
            Vector3Int cell = (_terrainMap != null ? _terrainMap : _decorationMap).WorldToCell(world);
            Vector2Int cell2 = new(cell.x, cell.y);

            if (_placer != null && _placer.TryGetBuildingAt(cell2, out var building) && building != null)
            {
                _placer.TryGetBuildingOrigin(cell2, out _selOrigin);
                ShowBuilding(building, _selOrigin);
                return;
            }
            if (_decorationMap != null)
            {
                var deco = _decorationMap.GetTile(cell);
                if (deco != null)
                {
                    _selKind = SelKind.Decoration;
                    _selDef = null;
                    ShowResourceNode(cell, deco.name);
                    return;
                }
            }
            // Click on empty terrain — if no goblin selection, hide popup. (UnitInfo panels are opened by
            // GoblinSelectionController on mouse-release, a later frame than this press-driven check, so
            // this won't clobber them; clicking empty ground here correctly dismisses a shown panel.)
            if (_selectionController == null || _selectionController.Selection.Count == 0)
                Hide();
        }

        private void OnGoblinSelectionChanged()
        {
            if (_selectionController == null) return;
            var sel = _selectionController.Selection;
            if (sel.Count == 0)
            {
                if (_selKind == SelKind.Goblins) Hide();
                return;
            }
            ShowGoblinSelection(sel);
        }

        // ---------- Display modes ----------

        private void ShowGoblinSelection(IReadOnlyList<Goblin> sel)
        {
            _selKind = SelKind.Goblins;
            _selDef = null;
            RallyVisual.Instance.Hide();
            if (_selBuildingOwner != null) { _selBuildingOwner.SetSelected(false); _selBuildingOwner = null; }

            bool hasFarmer = false;
            string firstKind = null;
            int farmers = 0;
            foreach (var g in sel)
            {
                if (g == null) continue;
                firstKind ??= g.Kind;
                if (g.Kind == "FarmerGoblin") { hasFarmer = true; farmers++; }
            }

            string name = sel.Count == 1 ? PrettyKindName(firstKind) : $"{PrettyKindName(firstKind)} × {sel.Count}";
            // Single unit → full stat sheet (HP / damage / attack speed / range). Multi → brief summary.
            string desc = (sel.Count == 1 && sel[0] != null)
                ? UnitStatsDesc(sel[0])
                : (hasFarmer ? "Workers" : "Warriors");
            _lastGoblinDescBase = desc;
            _lastCarryLine = "";
            SetHeader(name, desc);

            // The build menu now lives in the BuildMenu sidebar; the inspector just clears its cards here.
            ClearCards();

            if (_progressRow != null) _progressRow.SetActive(false);
            _popupRoot?.SetActive(true);
            Refresh();
        }

        // Read-only info for an enemy / neutral unit clicked on the map (no action cards, no commands).
        private void ShowUnitInfo(Goblin g)
        {
            if (g == null) return;
            _selKind = SelKind.UnitInfo;
            _selDef = null;
            _inspectedUnit = g;
            RallyVisual.Instance.Hide();
            if (_selBuildingOwner != null) { _selBuildingOwner.SetSelected(false); _selBuildingOwner = null; }
            ClearCards();
            if (_progressRow != null) _progressRow.SetActive(false);
            SetHeader(UnitInfoTitle(g), UnitStatsDesc(g));
            _popupRoot?.SetActive(true);
        }

        // Live-refresh the inspected unit's HP; hide once it dies or is removed (e.g. boards a boat).
        private void UpdateInspectedUnit()
        {
            if (_inspectedUnit == null || _inspectedUnit.CurrentHp <= 0 || !_inspectedUnit.gameObject.activeInHierarchy)
            { Hide(); return; }
            if (_descriptionLabel != null) _descriptionLabel.text = UnitStatsDesc(_inspectedUnit);
        }

        private static string UnitInfoTitle(Goblin g)
        {
            string side = g.IsNeutral ? "Neutral" : "Enemy";
            return $"{side} {PrettyKindName(g.Kind)}";
        }

        // Shared stat sheet for a unit: type, HP, and (for combat units) damage / attack speed / range.
        private static string UnitStatsDesc(Goblin g)
        {
            string type = g.Kind == "FarmerGoblin" ? "Worker — chops trees, builds"
                        : (g.AttackDamage > 0 ? "Warrior" : "Unit");
            string s = $"{type}\nHP: {g.CurrentHp} / {g.MaxHp}";
            if (g.AttackDamage > 0)
            {
                float aps = g.AttackInterval > 0.001f ? 1f / g.AttackInterval : 0f;
                s += $"\nDamage: {g.AttackDamage}";
                s += $"\nAttack Speed: {aps:0.0}/s";
                s += $"\nRange: {g.AttackRange}";
            }
            return s;
        }

        private void ShowBuilding(BuildingDefinition def, Vector2Int origin)
        {
            _selKind = SelKind.Building;
            _selDef = def;

            // Turn off previous building's ring, then turn on this one's.
            if (_selBuildingOwner != null) _selBuildingOwner.SetSelected(false);
            _selBuildingOwner = FindBuildingOwnerAt(origin);
            if (_selBuildingOwner != null) _selBuildingOwner.SetSelected(true);

            // Determine if this is a local (or solo) building; only local buildings get action cards.
            bool isLocal = true;
            if (_placer != null && _placer.TryGetBuildingOwner(origin, out ulong owner))
                isLocal = (owner == WorldStartContext.LocalPlayer || owner == 0UL);

            string desc = DescribeBuilding(def);
            if (BuildingHP.TryGet(origin, out int cur, out int max))
                desc += $"\nHP: {cur} / {max}";
            SetHeader(def.DisplayName, desc);

            // Priority: unit training > upgrades > no cards. Enemy buildings show no cards.
            if (isLocal && def.TrainsUnits != null && def.TrainsUnits.Length > 0)
                BuildUnitCards(def.TrainsUnits);
            else if (isLocal && def.ProvidesUpgrades != null && def.ProvidesUpgrades.Length > 0)
                BuildUpgradeCards(def.ProvidesUpgrades);
            else
                ClearCards();

            _popupRoot?.SetActive(true);
            Refresh();
            ShowRallyVisual();
        }

        // True when the current selection is a local, unit-training building (eligible for a rally point).
        // Outputs the building's footprint center in world space (line start for the rally visual).
        private bool BuildingTakesRally(out Vector3 buildingCenter)
        {
            buildingCenter = default;
            if (_selKind != SelKind.Building || _selDef == null) return false;
            if (_selDef.TrainsUnits == null || _selDef.TrainsUnits.Length == 0) return false;
            if (_placer != null && _placer.TryGetBuildingOwner(_selOrigin, out ulong owner)
                && owner != WorldStartContext.LocalPlayer && owner != 0UL) return false;
            if (_terrainMap == null) return false;
            var w = _terrainMap.CellToWorld(new Vector3Int(_selOrigin.x, _selOrigin.y, 0));
            buildingCenter = w + new Vector3(_selDef.Footprint.x * 0.5f, _selDef.Footprint.y * 0.5f, 0f);
            return true;
        }

        // Show the rally flag + dashed line for the selected building (creating a default rally if none),
        // or hide the visual when the selection isn't a rally-eligible building.
        private void ShowRallyVisual()
        {
            if (!BuildingTakesRally(out var center)) { RallyVisual.Instance.Hide(); return; }
            if (!RallyPoints.TryGet(_selOrigin, out var rally))
            {
                rally = RallyPoints.Default(_selOrigin, _selDef.Footprint);
                RallyPoints.Set(_selOrigin, rally);
            }
            RallyVisual.Instance.Show(center, rally, _rallyIcon);
        }

        private void ShowSimple(string title, string description)
        {
            if (_selBuildingOwner != null) { _selBuildingOwner.SetSelected(false); _selBuildingOwner = null; }
            SetHeader(title, description);
            ClearCards();
            if (_progressRow != null) _progressRow.SetActive(false);
            _popupRoot?.SetActive(true);
        }

        // Locate the BuildingOwner MonoBehaviour that sits at the world position corresponding
        // to the given origin cell. FindObjectsByType is expensive; called only when the
        // selection changes (not every frame), so the cost is acceptable.
        private BuildingOwner FindBuildingOwnerAt(Vector2Int origin)
        {
            if (_terrainMap == null) return null;
            Vector3 originWorld = _terrainMap.CellToWorld(new Vector3Int(origin.x, origin.y, 0));
            var all = UnityEngine.Object.FindObjectsByType<BuildingOwner>(FindObjectsSortMode.None);
            const float eps = 0.01f;
            foreach (var bo in all)
            {
                if (bo == null) continue;
                Vector3 p = bo.transform.position;
                if (Mathf.Abs(p.x - originWorld.x) < eps && Mathf.Abs(p.y - originWorld.y) < eps)
                    return bo;
            }
            return null;
        }

        private void SetHeader(string title, string description)
        {
            if (_nameLabel != null)        _nameLabel.text = title;
            if (_descriptionLabel != null) _descriptionLabel.text = description;
        }

        private void Hide()
        {
            RallyVisual.Instance.Hide();
            if (_popupRoot != null) _popupRoot.SetActive(false);
            if (_selBuildingOwner != null) _selBuildingOwner.SetSelected(false);
            _selBuildingOwner = null;
            _selKind = SelKind.None;
            _selDef = null;
            _inspectedUnit = null;
            ClearCards();
        }

        // ---------- Cards (built on demand for the current display) ----------

        // Destroy all current card GameObjects and hide the container.
        // Called before rebuilding a new set (mode change) or on Hide().
        private void ClearCards()
        {
            if (_unitCardsContainer == null) return;
            for (int i = _unitCardsContainer.childCount - 1; i >= 0; i--)
                Destroy(_unitCardsContainer.GetChild(i).gameObject);
            _cards.Clear();
            _unitCardsContainer.gameObject.SetActive(false);
        }

        // Build one card per unit definition in the building's TrainsUnits list.
        // Each card stores WoodCost + FoodCost in CardRefs for Refresh() to check.
        private void BuildUnitCards(GoblinUnitDefinition[] units)
        {
            ClearCards();
            if (_unitCardsContainer == null) return;
            _unitCardsContainer.gameObject.SetActive(true);
            foreach (var u in units)
            {
                if (u == null) continue;
                var costs = new List<(ResourceKind, int)>();
                if (u.WoodCost > 0) costs.Add((ResourceKind.Wood, u.WoodCost));
                if (u.FoodCost > 0) costs.Add((ResourceKind.Food, u.FoodCost));
                var c = CreateCard(u.DisplayName, u.Icon, costs, () => OnUnitClicked(u), UnitTooltip(u));
                c.Unit = u;
                c.WoodCost = u.WoodCost;
                c.FoodCost = u.FoodCost;
                _cards.Add(c);
            }
        }

        // Build one card per upgrade in the building's ProvidesUpgrades list.
        // Already-purchased upgrades are greyed out and non-interactable (checked in Refresh).
        private void BuildUpgradeCards(UpgradeDefinition[] upgrades)
        {
            ClearCards();
            if (_unitCardsContainer == null) return;
            _unitCardsContainer.gameObject.SetActive(true);
            foreach (var u in upgrades)
            {
                if (u == null) continue;
                var costs = new List<(ResourceKind, int)>();
                if (u.IronCost > 0) costs.Add((ResourceKind.Iron, u.IronCost));
                if (u.GoldCost > 0) costs.Add((ResourceKind.Gold, u.GoldCost));
                if (u.CrystalCost > 0) costs.Add((ResourceKind.Crystal, u.CrystalCost));
                var c = CreateCard(u.DisplayName, u.Icon, costs, () => OnUpgradeClicked(u), UpgradeTooltip(u));
                c.Upgrade = u;
                c.UpgradeKind = u.Kind;
                c.IronCost = u.IronCost;
                c.GoldCost = u.GoldCost;
                c.CrystalCost = u.CrystalCost;
                _cards.Add(c);
            }
        }

        // Construct a single action card at runtime using Unity UI components.
        // Layout: HorizontalLayoutGroup (icon | VerticalLayoutGroup (name label / cost label)).
        // Returns the CardRefs struct so callers can store cost data alongside the UI refs.
        private CardRefs CreateCard(string title, Sprite icon, List<(ResourceKind kind, int amount)> costs, Action onClick, string tooltip = null)
        {
            var card = new GameObject($"Card_{title}");
            card.transform.SetParent(_unitCardsContainer, false);

            if (!string.IsNullOrEmpty(tooltip))
            {
                var hover = card.AddComponent<CardHover>();
                hover.Font = _cardFont;
                hover.Tip = tooltip;
            }

            var bg = card.AddComponent<Image>();
            bg.sprite = _cardSprite;
            bg.type = Image.Type.Sliced;
            bg.color = _cardEnabledBg;

            var btn = card.AddComponent<Button>();
            btn.targetGraphic = bg;

            var layout = card.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 12;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            var cardLE = card.AddComponent<LayoutElement>();
            cardLE.preferredHeight = 64;
            cardLE.flexibleWidth = 1;

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(card.transform, false);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            var iconLE = iconGo.AddComponent<LayoutElement>();
            iconLE.preferredWidth = 48;
            iconLE.preferredHeight = 48;
            iconLE.flexibleWidth = 0;

            var textGo = new GameObject("Texts");
            textGo.transform.SetParent(card.transform, false);
            var tlayout = textGo.AddComponent<VerticalLayoutGroup>();
            tlayout.spacing = 2;
            tlayout.childForceExpandHeight = false;
            tlayout.childForceExpandWidth = true;
            tlayout.childControlHeight = true;
            tlayout.childControlWidth = true;
            tlayout.childAlignment = TextAnchor.MiddleLeft;
            var textLE = textGo.AddComponent<LayoutElement>();
            textLE.flexibleWidth = 1;

            var nameGo = new GameObject("Name");
            nameGo.transform.SetParent(textGo.transform, false);
            var nameText = nameGo.AddComponent<Text>();
            nameText.text = title;
            nameText.font = _cardFont;
            nameText.fontSize = 16;
            nameText.color = _cardTextNormal;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameText.lineSpacing = LineSpacing;

            // Cost row: a horizontal strip of [resource-icon + number] pairs (no resource names → never
            // overflows). A CanvasGroup lets Refresh() dim the whole row when unaffordable. Skipped entirely
            // for category / Back cards (costs == null) which carry no price.
            CanvasGroup costGroup = null;
            if (costs != null)
            {
                var costGo = new GameObject("Cost", typeof(RectTransform));
                costGo.transform.SetParent(textGo.transform, false);
                costGroup = costGo.AddComponent<CanvasGroup>();
                var costRow = costGo.AddComponent<HorizontalLayoutGroup>();
                costRow.spacing = 8;
                costRow.childForceExpandHeight = false;
                costRow.childForceExpandWidth = false;
                costRow.childControlHeight = true;
                costRow.childControlWidth = true;
                costRow.childAlignment = TextAnchor.MiddleLeft;
                var costLE = costGo.AddComponent<LayoutElement>();
                costLE.preferredHeight = 22;

                if (costs.Count == 0)
                    AddCostText(costGo.transform, "Free");
                else
                foreach (var (kind, amount) in costs)
                {
                    var sprite = IconFor(kind);
                    if (sprite != null)
                    {
                        var ig = new GameObject($"Cost_{kind}", typeof(RectTransform));
                        ig.transform.SetParent(costGo.transform, false);
                        var im = ig.AddComponent<Image>();
                        im.sprite = sprite;
                        im.preserveAspect = true;
                        var ile = ig.AddComponent<LayoutElement>();
                        ile.preferredWidth = 20; ile.preferredHeight = 20; ile.flexibleWidth = 0;
                    }
                    // Number (falls back to "20 Wood"-style text when no icon is wired for this kind).
                    AddCostText(costGo.transform, sprite != null ? amount.ToString() : $"{amount} {kind}");
                }
            }

            btn.onClick.AddListener(() => onClick?.Invoke());

            return new CardRefs
            {
                Root = card,
                Button = btn,
                Bg = bg,
                Icon = iconImg,
                Name = nameText,
                CostGroup = costGroup,
            };
        }

        // One number/label chip inside the cost row.
        private void AddCostText(Transform parent, string text)
        {
            var go = new GameObject("Amount", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = _cardFont;
            t.fontSize = 14;
            t.color = new Color(0.92f, 0.88f, 0.7f);
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 20; le.flexibleWidth = 0;
        }

        // ---------- Refresh enabled/affordable state ----------

        // Re-evaluate enabled state for every visible card.
        // Upgrade cards also check PlayerUpgrades.IsPurchased to grey out bought upgrades.
        // A building that is busy producing blocks all unit cards (only one queue slot).
        private void Refresh()
        {
            if (_cards.Count == 0) return;

            // A unit can be trained as long as the building's production QUEUE isn't full (it no longer
            // has to be idle — extra orders queue up). Build/upgrade cards ignore this.
            bool queueFull = _selKind == SelKind.Building && !GoblinProduction.CanQueue(_selOrigin);
            foreach (var card in _cards)
            {
                bool affordable = card.Upgrade != null
                    ? ResourceBank.Get(ResourceKind.Iron) >= card.IronCost
                      && ResourceBank.Get(ResourceKind.Gold) >= card.GoldCost
                      && ResourceBank.Get(ResourceKind.Crystal) >= card.CrystalCost
                    : ResourceBank.Wood >= card.WoodCost
                      && ResourceBank.Food >= card.FoodCost
                      && ResourceBank.Get(ResourceKind.Stone) >= card.StoneCost;
                bool popOk = card.Unit == null || PopulationManager.CanAfford(card.Unit.PopulationCost);
                bool alreadyOwned = card.Upgrade != null
                    && PlayerUpgrades.IsPurchased(WorldStartContext.LocalPlayer, card.UpgradeKind);
                bool blockedByQueue = card.Unit != null && queueFull;   // only train cards care about the queue
                bool enabled = affordable && popOk && !blockedByQueue && !alreadyOwned;
                card.Button.interactable = enabled;
                if (card.Bg != null)   card.Bg.color = enabled ? _cardEnabledBg : _cardDisabledBg;
                if (card.Name != null) card.Name.color = enabled ? _cardTextNormal : _cardTextDisabled;
                if (card.CostGroup != null) card.CostGroup.alpha = enabled ? 1f : 0.5f;
                if (card.Icon != null) card.Icon.color = enabled ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.7f);
            }
            UpdateProgressUI();
        }

        // Update the production progress bar for the currently-selected building.
        // Bar is driven by scaling (pivot.x=0, scaleX = progress 0..1) rather than a
        // filled sprite, so any sprite (or null) works without a special import setting.
        private void UpdateProgressUI()
        {
            if (_progressRow == null) return;
            if (_selKind != SelKind.Building) { _progressRow.SetActive(false); return; }
            var slot = GoblinProduction.Get(_selOrigin);
            if (slot == null) { _progressRow.SetActive(false); return; }
            _progressRow.SetActive(true);
            // Drive the bar via horizontal scale (pivot.x = 0) so it works without a Filled sprite.
            if (_progressFill != null)
                _progressFill.rectTransform.localScale = new Vector3(Mathf.Clamp01(slot.Progress), 1f, 1f);
            if (_progressLabel != null)
            {
                int waiting = GoblinProduction.QueuedBehind(_selOrigin);
                _progressLabel.text = waiting > 0
                    ? $"Producing {slot.Def.DisplayName}…  (+{waiting} queued)"
                    : $"Producing {slot.Def.DisplayName}…";
            }
        }

        // Poll the selected farmer's carry slot each frame and append a carry line to the
        // description label when non-zero. Uses string equality to avoid redundant label writes.
        private void UpdateCarryUI()
        {
            if (_selectionController == null) return;
            var sel = _selectionController.Selection;
            if (sel.Count != 1 || sel[0] == null) return;   // only a single unit shows live stats
            var g = sel[0];
            string desc = UnitStatsDesc(g);
            if (g.Kind == "FarmerGoblin" && g.CarriedAmount > 0)
                desc += $"\nCarrying: {g.CarriedAmount} {g.CarriedKind.ToString().ToLowerInvariant()}";
            if (desc == _lastCarryLine) return;             // cache full text to skip redundant writes
            _lastCarryLine = desc;
            if (_descriptionLabel != null) _descriptionLabel.text = desc;
        }

        // Poll the resource node's remaining HP each frame and append an amount line.
        // Hides the popup if the tile has been removed since it was selected.
        private void UpdateResourceAmount()
        {
            if (!_selDecoHarvestable || _decorationMap == null) return;
            var tile = _decorationMap.GetTile(_selDecoCell);
            if (tile == null) { Hide(); return; } // node depleted/removed
            string newAmount = AmountLine(_selDecoCell, tile.name);
            if (newAmount == _lastAmountLine) return;
            _lastAmountLine = newAmount;
            if (_descriptionLabel != null)
                _descriptionLabel.text = _selDecoBaseDesc + "\n" + newAmount;
        }

        // ---------- Tooltip text builders (what each card does) ----------

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
                _                                => "Permanent upgrade.",
            };
            return effect + "\nOne-time research, applies to all your units.";
        }

        // ---------- Click handlers ----------

        // Train a unit: guard checks are duplicated here (button may be stale from a
        // brief window between Refresh() calls) before deducting resources and issuing
        // the net command. IssueTrainUnit handles actual spawning on all clients.
        private void OnUnitClicked(GoblinUnitDefinition unit)
        {
            if (_selKind != SelKind.Building || _selDef == null) return;
            if (!GoblinProduction.CanQueue(_selOrigin)) return;   // queue full
            if (ResourceBank.Wood < unit.WoodCost) return;
            if (ResourceBank.Food < unit.FoodCost) return;
            if (!PopulationManager.CanAfford(unit.PopulationCost)) return;
            if (unit.WoodCost > 0) ResourceBank.AddWood(-unit.WoodCost);
            if (unit.FoodCost > 0) ResourceBank.AddFood(-unit.FoodCost);
            ulong owner = WorldStartContext.LocalPlayer;
            NetCommandIssuer.IssueTrainUnit(_selOrigin, unit, owner);
            Refresh();
        }

        // Purchase an upgrade: deduct resources locally then issue the net command so all
        // clients call UpgradeEffects.ApplyExistingTo on their local goblins.
        private void OnUpgradeClicked(UpgradeDefinition upgrade)
        {
            if (upgrade == null) return;
            ulong owner = WorldStartContext.LocalPlayer;
            if (PlayerUpgrades.IsPurchased(owner, upgrade.Kind)) return;
            if (ResourceBank.Get(ResourceKind.Iron) < upgrade.IronCost) return;
            if (ResourceBank.Get(ResourceKind.Gold) < upgrade.GoldCost) return;
            if (ResourceBank.Get(ResourceKind.Crystal) < upgrade.CrystalCost) return;

            if (upgrade.IronCost > 0) ResourceBank.Add(ResourceKind.Iron, -upgrade.IronCost);
            if (upgrade.GoldCost > 0) ResourceBank.Add(ResourceKind.Gold, -upgrade.GoldCost);
            if (upgrade.CrystalCost > 0) ResourceBank.Add(ResourceKind.Crystal, -upgrade.CrystalCost);
            NetCommandIssuer.IssuePurchaseUpgrade(upgrade.Kind, owner);
            Refresh();
        }

        // ---------- Helpers ----------

        // Human-readable display name for goblin unit types. Extend when adding new kinds.
        private static string PrettyKindName(string kind) => kind switch
        {
            "FarmerGoblin" => "Farmer Goblin",
            "ClubGoblin"   => "Club Goblin",
            "ArcherGoblin" => "Archer Goblin",
            "SpearGoblin"  => "Spear Goblin",
            null           => "Goblin",
            _              => kind,
        };

        // Derive a brief material/size description from the asset name.
        // Convention: "Keep_0" → sheet="Keep"; "Barracks_3" → sheet="Barracks".
        private static string DescribeBuilding(BuildingDefinition def)
        {
            int us = def.name.IndexOf('_');
            string sheet = us > 0 ? def.name[..us] : def.name;
            return $"Wood / {sheet} • {def.Footprint.x}×{def.Footprint.y} cells";
        }

        // Enter Decoration mode: cache the tile info and show the initial popup.
        // After this, UpdateResourceAmount() keeps the remaining HP line current each frame.
        private void ShowResourceNode(Vector3Int cell, string tileName)
        {
            RallyVisual.Instance.Hide();
            _selDecoCell = cell;
            _selDecoHarvestable = Goblin.IsHarvestable(tileName);
            _selDecoBaseDesc = DecorationDesc(tileName);
            _lastAmountLine = "";
            string desc = _selDecoBaseDesc;
            if (_selDecoHarvestable) desc += "\n" + AmountLine(cell, tileName);
            ShowSimple(DecorationName(tileName), desc);
        }

        // Format the remaining resource HP as "Wood: 23 / 50". Uses TreeHP which tracks
        // per-tile hit-point state (shared across all clients on the owner's authority).
        private string AmountLine(Vector3Int cell, string tileName)
        {
            int max = Goblin.MaxHpForCell(cell, tileName);   // boosted wheat fields show 1000, not 500
            int cur = TreeHP.GetHP(cell, max);
            var kind = Goblin.KindOf(tileName);
            return $"{kind}: {cur} / {max}";
        }

        // Map tile-name prefix → human-readable display name for the popup title.
        private static string DecorationName(string tileName)
        {
            if (tileName.StartsWith("Trees_")) return "Oak Tree";
            if (tileName.StartsWith("PineTrees_") || tileName.StartsWith("WinterTrees_")) return "Pine Tree";
            if (tileName.StartsWith("CoconutTrees_")) return "Palm Tree";
            if (tileName.StartsWith("DeadTrees_") || tileName.StartsWith("WinterDeadTrees_")) return "Dead Tree";
            if (tileName.StartsWith("Wheatfield_")) return "Wheat Field";
            if (tileName.StartsWith("BerryBush")) return "Berry Bush";
            if (tileName.StartsWith("Rocks_")) return "Stone Deposit";
            if (tileName.StartsWith("GoldOre_")) return "Gold Deposit";
            if (tileName.StartsWith("IronOre_")) return "Iron Deposit";
            if (tileName.StartsWith("CrystalOre_")) return "Crystal Deposit";
            if (tileName.StartsWith("Cactus_")) return "Cactus";
            if (tileName.StartsWith("Tumbleweed_")) return "Tumbleweed";
            int us = tileName.IndexOf('_');
            return us > 0 ? tileName[..us] : tileName;
        }

        private static string DecorationDesc(string tileName)
        {
            if (Goblin.IsTreeTile(tileName)) return "Chop for Wood";
            if (tileName.StartsWith("Wheatfield_") || tileName.StartsWith("BerryBush")) return "Harvest for Food";
            if (tileName.StartsWith("Rocks_")) return "Mine for Stone";
            if (tileName.StartsWith("GoldOre_")) return "Mine for Gold";
            if (tileName.StartsWith("IronOre_")) return "Mine for Iron";
            if (tileName.StartsWith("CrystalOre_")) return "Mine for Crystal";
            return "Desert flora";
        }
    }
}

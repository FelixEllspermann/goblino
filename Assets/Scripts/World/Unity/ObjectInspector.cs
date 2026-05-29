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

        [Header("Worker Build Options")]
        [Tooltip("Buildings a Farmer Goblin can construct when selected")]
        [SerializeField] private List<BuildingDefinition> _farmerBuildables = new();

        private enum SelKind { None, Building, Decoration, Goblins }
        private SelKind _selKind = SelKind.None;
        private Vector2Int _selOrigin;
        private BuildingDefinition _selDef;
        private BuildingOwner _selBuildingOwner;
        private string _lastCarryLine = "";
        private string _lastGoblinDescBase = "";

        private struct CardRefs
        {
            public GameObject Root;
            public Button Button;
            public Image Bg;
            public Image Icon;
            public Text Name;
            public Text Cost;
            // Either a unit production action or a building placement action.
            public GoblinUnitDefinition Unit;
            public BuildingDefinition Building;
            public int WoodCost;
            public int FoodCost;
            public UpgradeDefinition Upgrade;
            public UpgradeKind UpgradeKind;
        }
        private readonly List<CardRefs> _cards = new();

        private void Start()
        {
            if (_popupRoot != null) _popupRoot.SetActive(false);
            if (_progressRow != null) _progressRow.SetActive(false);
            ResourceBank.OnWoodChanged += _ => Refresh();
            ResourceBank.OnFoodChanged += _ => Refresh();
            PopulationManager.OnChanged += Refresh;
            GoblinProduction.OnChanged += Refresh;
            if (_selectionController != null)
                _selectionController.OnSelectionChanged += OnGoblinSelectionChanged;
        }

        private void OnDestroy()
        {
            PopulationManager.OnChanged -= Refresh;
            GoblinProduction.OnChanged -= Refresh;
            if (_selectionController != null)
                _selectionController.OnSelectionChanged -= OnGoblinSelectionChanged;
        }

        private void Update()
        {
            if (_camera == null || Mouse.current == null) return;
            // Don't intercept clicks during placement mode
            if (_placer != null && _placer.Selected != null) return;

            // Live progress bar while a production runs for the currently-shown keep/barracks
            if (_selKind == SelKind.Building) UpdateProgressUI();
            if (_selKind == SelKind.Goblins) UpdateCarryUI();

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
                    ShowSimple(deco.name, DescribeDecoration(deco.name));
                    return;
                }
            }
            // Click on empty terrain — if no goblin selection, hide popup
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
            string desc = hasFarmer
                ? "Worker — chops trees, builds buildings"
                : "Warrior — melee unit";
            _lastGoblinDescBase = desc;
            _lastCarryLine = "";
            SetHeader(name, desc);

            if (hasFarmer && _farmerBuildables != null && _farmerBuildables.Count > 0)
                BuildBuildingCards(_farmerBuildables);
            else
                ClearCards();

            if (_progressRow != null) _progressRow.SetActive(false);
            _popupRoot?.SetActive(true);
            Refresh();
        }

        private void ShowBuilding(BuildingDefinition def, Vector2Int origin)
        {
            _selKind = SelKind.Building;
            _selDef = def;

            // Turn off previous building's ring, then turn on this one's.
            if (_selBuildingOwner != null) _selBuildingOwner.SetSelected(false);
            _selBuildingOwner = FindBuildingOwnerAt(origin);
            if (_selBuildingOwner != null) _selBuildingOwner.SetSelected(true);

            // Determine if this is a local (or solo) building.
            bool isLocal = true;
            if (_placer != null && _placer.TryGetBuildingOwner(origin, out ulong owner))
                isLocal = (owner == WorldStartContext.LocalPlayer || owner == 0UL);

            string desc = DescribeBuilding(def);
            if (BuildingHP.TryGet(origin, out int cur, out int max))
                desc += $"\nHP: {cur} / {max}";
            SetHeader(def.DisplayName, desc);

            // Only show production cards for local buildings.
            if (isLocal && def.TrainsUnits != null && def.TrainsUnits.Length > 0)
                BuildUnitCards(def.TrainsUnits);
            else if (isLocal && def.ProvidesUpgrades != null && def.ProvidesUpgrades.Length > 0)
                BuildUpgradeCards(def.ProvidesUpgrades);
            else
                ClearCards();

            _popupRoot?.SetActive(true);
            Refresh();
        }

        private void ShowSimple(string title, string description)
        {
            if (_selBuildingOwner != null) { _selBuildingOwner.SetSelected(false); _selBuildingOwner = null; }
            SetHeader(title, description);
            ClearCards();
            if (_progressRow != null) _progressRow.SetActive(false);
            _popupRoot?.SetActive(true);
        }

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
            if (_popupRoot != null) _popupRoot.SetActive(false);
            if (_selBuildingOwner != null) _selBuildingOwner.SetSelected(false);
            _selBuildingOwner = null;
            _selKind = SelKind.None;
            _selDef = null;
            ClearCards();
        }

        // ---------- Cards (built on demand for the current display) ----------

        private void ClearCards()
        {
            if (_unitCardsContainer == null) return;
            for (int i = _unitCardsContainer.childCount - 1; i >= 0; i--)
                Destroy(_unitCardsContainer.GetChild(i).gameObject);
            _cards.Clear();
            _unitCardsContainer.gameObject.SetActive(false);
        }

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

        private CardRefs CreateCard(string title, Sprite icon, string costText, Action onClick)
        {
            var card = new GameObject($"Card_{title}");
            card.transform.SetParent(_unitCardsContainer, false);

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
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var costGo = new GameObject("Cost");
            costGo.transform.SetParent(textGo.transform, false);
            var costLabel = costGo.AddComponent<Text>();
            costLabel.text = costText;
            costLabel.font = _cardFont;
            costLabel.fontSize = 12;
            costLabel.color = new Color(0.85f, 0.75f, 0.45f);
            costLabel.alignment = TextAnchor.MiddleLeft;
            costLabel.horizontalOverflow = HorizontalWrapMode.Overflow;

            btn.onClick.AddListener(() => onClick?.Invoke());

            return new CardRefs
            {
                Root = card,
                Button = btn,
                Bg = bg,
                Icon = iconImg,
                Name = nameText,
                Cost = costLabel,
            };
        }

        // ---------- Refresh enabled/affordable state ----------

        private void Refresh()
        {
            if (_cards.Count == 0) return;

            bool busy = _selKind == SelKind.Building && GoblinProduction.IsBusy(_selOrigin);
            foreach (var card in _cards)
            {
                bool affordable = ResourceBank.Wood >= card.WoodCost
                               && ResourceBank.Food >= card.FoodCost;
                bool popOk = card.Unit == null || PopulationManager.CanAfford(card.Unit.PopulationCost);
                bool alreadyOwned = card.Upgrade != null
                    && PlayerUpgrades.IsPurchased(WorldStartContext.LocalPlayer, card.UpgradeKind);
                bool enabled = affordable && popOk && !busy && !alreadyOwned;
                card.Button.interactable = enabled;
                if (card.Bg != null)   card.Bg.color = enabled ? _cardEnabledBg : _cardDisabledBg;
                if (card.Name != null) card.Name.color = enabled ? _cardTextNormal : _cardTextDisabled;
                if (card.Cost != null) card.Cost.color = enabled ? new Color(0.85f, 0.75f, 0.45f) : _cardTextDisabled;
                if (card.Icon != null) card.Icon.color = enabled ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.7f);
            }
            UpdateProgressUI();
        }

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
            if (_progressLabel != null) _progressLabel.text = $"Producing {slot.Def.DisplayName}…";
        }

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

        // ---------- Click handlers ----------

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

        private void OnBuildingClicked(BuildingDefinition def)
        {
            if (_placer == null || def == null) return;
            if (ResourceBank.Wood < def.WoodCost) return;
            _placer.Select(def);
        }

        private void OnUpgradeClicked(UpgradeDefinition upgrade)
        {
            if (upgrade == null) return;
            ulong owner = WorldStartContext.LocalPlayer;
            if (PlayerUpgrades.IsPurchased(owner, upgrade.Kind)) return;
            if (ResourceBank.Wood < upgrade.WoodCost) return;

            ResourceBank.AddWood(-upgrade.WoodCost);
            NetCommandIssuer.IssuePurchaseUpgrade(upgrade.Kind, owner);
            Refresh();
        }

        // ---------- Helpers ----------

        private static string PrettyKindName(string kind) => kind switch
        {
            "FarmerGoblin" => "Farmer Goblin",
            "ClubGoblin"   => "Club Goblin",
            "ArcherGoblin" => "Archer Goblin",
            "SpearGoblin"  => "Spear Goblin",
            null           => "Goblin",
            _              => kind,
        };

        private static string FormatUnitCost(int woodCost, int foodCost)
        {
            if (woodCost > 0 && foodCost > 0) return $"{woodCost} Wood, {foodCost} Food";
            if (woodCost > 0) return $"{woodCost} Wood";
            if (foodCost > 0) return $"{foodCost} Food";
            return "Free";
        }

        private static string DescribeBuilding(BuildingDefinition def)
        {
            int us = def.name.IndexOf('_');
            string sheet = us > 0 ? def.name[..us] : def.name;
            return $"Wood / {sheet} • {def.Footprint.x}×{def.Footprint.y} cells";
        }

        private static string DescribeDecoration(string tileName)
        {
            int us = tileName.IndexOf('_');
            string category = us > 0 ? tileName[..us] : tileName;
            return category switch
            {
                "Trees"           => "Nature • Deciduous tree (Forest/Grassland)",
                "PineTrees"       => "Nature • Pine tree (Snow)",
                "WinterTrees"     => "Nature • Snow-covered tree",
                "WinterDeadTrees" => "Nature • Bare winter tree",
                "DeadTrees"       => "Nature • Dead tree (DryGrass)",
                "CoconutTrees"    => "Nature • Coconut palm (Tropical)",
                "Cactus"          => "Nature • Cactus (Desert)",
                "Tumbleweed"      => "Nature • Tumbleweed (Desert/DryGrass)",
                "Wheatfield"      => "Nature • Wheat field",
                "Rocks"           => "Nature • Rock formation",
                _                 => $"Decoration • {category}",
            };
        }
    }
}

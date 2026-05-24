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

        [Header("Popup UI")]
        [SerializeField] private GameObject _popupRoot;
        [SerializeField] private Text _nameLabel;
        [SerializeField] private Text _descriptionLabel;
        [SerializeField] private RectTransform _unitCardsContainer;
        [SerializeField] private Image _progressFill;
        [SerializeField] private Text _progressLabel;
        [SerializeField] private GameObject _progressRow;

        [Header("Unit Cards Style")]
        [SerializeField] private Sprite _cardSprite;
        [SerializeField] private Font _cardFont;
        [SerializeField] private Color _cardEnabledBg = new(0.18f, 0.18f, 0.22f, 0.95f);
        [SerializeField] private Color _cardDisabledBg = new(0.10f, 0.10f, 0.12f, 0.7f);
        [SerializeField] private Color _cardTextNormal = Color.white;
        [SerializeField] private Color _cardTextDisabled = new(0.55f, 0.55f, 0.55f, 1f);

        [Header("Units")]
        [SerializeField] private List<GoblinUnitDefinition> _units = new();

        private enum SelKind { None, Building, Decoration }
        private SelKind _selKind = SelKind.None;
        private Vector2Int _selOrigin;
        private BuildingDefinition _selDef;

        private struct CardRefs
        {
            public GameObject Root;
            public Button Button;
            public Image Bg;
            public Image Icon;
            public Text Name;
            public Text Cost;
            public GoblinUnitDefinition Def;
        }
        private readonly List<CardRefs> _cards = new();

        private void Start()
        {
            if (_popupRoot != null) _popupRoot.SetActive(false);
            if (_progressRow != null) _progressRow.SetActive(false);
            BuildUnitCards();
            ResourceBank.OnWoodChanged += _ => Refresh();
            GoblinProduction.OnChanged += Refresh;
        }

        private void OnDestroy()
        {
            GoblinProduction.OnChanged -= Refresh;
        }

        private void Update()
        {
            if (_camera == null || Mouse.current == null) return;
            if (_placer != null && _placer.Selected != null) return;

            // Live-update the progress bar while a production is running for the
            // currently-displayed keep.
            if (_selKind == SelKind.Building && _selDef != null && _selDef.name.StartsWith("Keep"))
                UpdateProgressUI();

            if (!Mouse.current.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 mp = Mouse.current.position.ReadValue();
            Vector3 world = _camera.ScreenToWorldPoint(
                new Vector3(mp.x, mp.y, -_camera.transform.position.z));
            Vector3Int cell = (_terrainMap != null ? _terrainMap : _decorationMap).WorldToCell(world);
            Vector2Int cell2 = new(cell.x, cell.y);

            if (_placer != null && _placer.TryGetBuildingAt(cell2, out var building) && building != null)
            {
                _selKind = SelKind.Building;
                _selDef = building;
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
                    Show(deco.name, DescribeDecoration(deco.name), showCards: false);
                    return;
                }
            }

            _selKind = SelKind.None;
            Hide();
        }

        private void ShowBuilding(BuildingDefinition def, Vector2Int origin)
        {
            string desc = DescribeBuilding(def);
            if (BuildingHP.TryGet(origin, out int cur, out int max))
                desc += $"\nHP: {cur} / {max}";
            bool isKeep = def.name.StartsWith("Keep");
            Show(def.DisplayName, desc, showCards: isKeep);
            Refresh();
        }

        private void Show(string title, string description, bool showCards)
        {
            if (_popupRoot == null) return;
            if (_nameLabel != null)        _nameLabel.text = title;
            if (_descriptionLabel != null) _descriptionLabel.text = description;
            if (_unitCardsContainer != null)
                _unitCardsContainer.gameObject.SetActive(showCards);
            if (_progressRow != null)
                _progressRow.SetActive(showCards && _selKind == SelKind.Building
                                       && GoblinProduction.IsBusy(_selOrigin));
            _popupRoot.SetActive(true);
        }

        private void Hide()
        {
            if (_popupRoot != null) _popupRoot.SetActive(false);
            _selKind = SelKind.None;
            _selDef = null;
        }

        private void Refresh()
        {
            if (_selKind != SelKind.Building || _selDef == null) return;
            bool isKeep = _selDef.name.StartsWith("Keep");
            if (!isKeep) return;
            bool busy = GoblinProduction.IsBusy(_selOrigin);
            foreach (var card in _cards)
            {
                bool affordable = ResourceBank.Wood >= card.Def.WoodCost;
                bool enabled = affordable && !busy;
                card.Button.interactable = enabled;
                if (card.Bg != null)
                    card.Bg.color = enabled ? _cardEnabledBg : _cardDisabledBg;
                Color t = enabled ? _cardTextNormal : _cardTextDisabled;
                if (card.Name != null) card.Name.color = t;
                if (card.Cost != null) card.Cost.color = enabled ? new Color(0.85f, 0.75f, 0.45f) : _cardTextDisabled;
                if (card.Icon != null) card.Icon.color = enabled ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.7f);
            }
            UpdateProgressUI();
        }

        private void UpdateProgressUI()
        {
            if (_progressRow == null) return;
            var slot = GoblinProduction.Get(_selOrigin);
            if (slot == null)
            {
                _progressRow.SetActive(false);
                return;
            }
            _progressRow.SetActive(true);
            if (_progressFill != null) _progressFill.fillAmount = slot.Progress;
            if (_progressLabel != null) _progressLabel.text = $"Producing {slot.Def.DisplayName}…";
        }

        private void BuildUnitCards()
        {
            if (_unitCardsContainer == null) return;
            // Clear any pre-existing children
            for (int i = _unitCardsContainer.childCount - 1; i >= 0; i--)
                DestroyImmediate(_unitCardsContainer.GetChild(i).gameObject);
            _cards.Clear();

            foreach (var unit in _units)
            {
                if (unit == null) continue;
                _cards.Add(CreateCard(unit));
            }
        }

        private CardRefs CreateCard(GoblinUnitDefinition unit)
        {
            var card = new GameObject($"Card_{unit.name}");
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

            // Icon
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(card.transform, false);
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = unit.Icon;
            icon.preserveAspect = true;
            var iconLE = iconGo.AddComponent<LayoutElement>();
            iconLE.preferredWidth = 48;
            iconLE.preferredHeight = 48;
            iconLE.flexibleWidth = 0;

            // Text column (Name above Cost)
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
            nameText.text = unit.DisplayName;
            nameText.font = _cardFont;
            nameText.fontSize = 16;
            nameText.color = _cardTextNormal;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var costGo = new GameObject("Cost");
            costGo.transform.SetParent(textGo.transform, false);
            var costText = costGo.AddComponent<Text>();
            costText.text = $"{unit.WoodCost} Wood";
            costText.font = _cardFont;
            costText.fontSize = 12;
            costText.color = new Color(0.85f, 0.75f, 0.45f);
            costText.alignment = TextAnchor.MiddleLeft;
            costText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var captured = unit;
            btn.onClick.AddListener(() => OnUnitClicked(captured));

            return new CardRefs
            {
                Root = card,
                Button = btn,
                Bg = bg,
                Icon = icon,
                Name = nameText,
                Cost = costText,
                Def = unit,
            };
        }

        private void OnUnitClicked(GoblinUnitDefinition unit)
        {
            if (_selKind != SelKind.Building || _selDef == null) return;
            if (!_selDef.name.StartsWith("Keep")) return;
            if (GoblinProduction.IsBusy(_selOrigin)) return;
            if (ResourceBank.Wood < unit.WoodCost) return;

            ResourceBank.AddWood(-unit.WoodCost);
            GoblinProduction.TryStart(_selOrigin, unit);
            Refresh();
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

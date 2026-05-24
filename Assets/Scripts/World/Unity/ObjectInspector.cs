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
        [SerializeField] private GoblinSpawner _goblinSpawner;

        [Header("Popup UI")]
        [SerializeField] private GameObject _popupRoot;
        [SerializeField] private Text _nameLabel;
        [SerializeField] private Text _descriptionLabel;
        [SerializeField] private Button _actionButton;
        [SerializeField] private Text _actionButtonLabel;

        [Header("Spawn Cost")]
        [SerializeField] private int _goblinWoodCost = 20;

        // Currently-displayed selection (for the action button to know what to act on)
        private enum SelKind { None, Building, Decoration }
        private SelKind _selKind = SelKind.None;
        private Vector2Int _selOrigin;
        private BuildingDefinition _selDef;

        private void Start()
        {
            if (_popupRoot != null) _popupRoot.SetActive(false);
            if (_actionButton != null) _actionButton.onClick.AddListener(OnActionButton);
            ResourceBank.OnWoodChanged += _ => RefreshActionButton();
        }

        private void Update()
        {
            if (_camera == null || Mouse.current == null) return;

            // Don't inspect while the user is placing a building
            if (_placer != null && _placer.Selected != null) return;

            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            // Ignore clicks over UI (palette, popup itself, buttons)
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 mp = Mouse.current.position.ReadValue();
            Vector3 world = _camera.ScreenToWorldPoint(
                new Vector3(mp.x, mp.y, -_camera.transform.position.z));
            Vector3Int cell = (_terrainMap != null ? _terrainMap : _decorationMap).WorldToCell(world);
            Vector2Int cell2 = new(cell.x, cell.y);

            // Priority: building > decoration > nothing
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
                    Show(deco.name, DescribeDecoration(deco.name), showAction: false);
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
            Show(def.DisplayName, desc, showAction: isKeep);
            RefreshActionButton();
        }

        private void Show(string title, string description, bool showAction)
        {
            if (_popupRoot == null) return;
            if (_nameLabel != null)        _nameLabel.text = title;
            if (_descriptionLabel != null) _descriptionLabel.text = description;
            if (_actionButton != null)     _actionButton.gameObject.SetActive(showAction);
            _popupRoot.SetActive(true);
        }

        private void Hide()
        {
            if (_popupRoot != null) _popupRoot.SetActive(false);
            _selKind = SelKind.None;
            _selDef = null;
        }

        private void RefreshActionButton()
        {
            if (_actionButton == null || _selKind != SelKind.Building || _selDef == null) return;
            bool isKeep = _selDef.name.StartsWith("Keep");
            if (!isKeep) { _actionButton.gameObject.SetActive(false); return; }

            bool affordable = ResourceBank.Wood >= _goblinWoodCost;
            _actionButton.interactable = affordable;
            if (_actionButtonLabel != null)
                _actionButtonLabel.text = affordable
                    ? $"Spawn Goblin ({_goblinWoodCost} Wood)"
                    : $"Spawn Goblin ({_goblinWoodCost} Wood) — need {_goblinWoodCost - ResourceBank.Wood} more";
        }

        private void OnActionButton()
        {
            if (_selKind != SelKind.Building || _selDef == null || _goblinSpawner == null) return;
            if (!_selDef.name.StartsWith("Keep")) return;
            if (ResourceBank.Wood < _goblinWoodCost) return;

            // Spend wood
            ResourceBank.AddWood(-_goblinWoodCost);

            // Spawn one goblin near the keep's center
            Vector3 center = new(
                _selOrigin.x + _selDef.Footprint.x * 0.5f,
                _selOrigin.y + _selDef.Footprint.y * 0.5f, 0f);
            _goblinSpawner.SpawnGroupAt(center, 1);

            RefreshActionButton();
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

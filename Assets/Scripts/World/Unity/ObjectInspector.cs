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

        private void Start()
        {
            if (_popupRoot != null) _popupRoot.SetActive(false);
        }

        private void Update()
        {
            if (_camera == null || Mouse.current == null) return;

            // Don't inspect while the user is placing a building
            if (_placer != null && _placer.Selected != null) return;

            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            // Ignore clicks over UI (palette, popup itself)
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 mp = Mouse.current.position.ReadValue();
            Vector3 world = _camera.ScreenToWorldPoint(
                new Vector3(mp.x, mp.y, -_camera.transform.position.z));
            Vector3Int cell = (_terrainMap != null ? _terrainMap : _decorationMap)
                              .WorldToCell(world);
            Vector2Int cell2 = new(cell.x, cell.y);

            // Priority: building > decoration > nothing
            if (_placer != null && _placer.TryGetBuildingAt(cell2, out var building) && building != null)
            {
                Show(building.DisplayName, DescribeBuilding(building));
                return;
            }

            if (_decorationMap != null)
            {
                var deco = _decorationMap.GetTile(cell);
                if (deco != null)
                {
                    Show(deco.name, DescribeDecoration(deco.name));
                    return;
                }
            }

            // Empty cell → close popup
            Hide();
        }

        private void Show(string title, string description)
        {
            if (_popupRoot == null) return;
            if (_nameLabel != null) _nameLabel.text = title;
            if (_descriptionLabel != null) _descriptionLabel.text = description;
            _popupRoot.SetActive(true);
        }

        private void Hide()
        {
            if (_popupRoot != null) _popupRoot.SetActive(false);
        }

        private static string DescribeBuilding(BuildingDefinition def)
        {
            // BuildingDefinition.name follows the "<Sheet>_<index>" pattern, e.g. "Huts_2"
            int us = def.name.IndexOf('_');
            string sheet = us > 0 ? def.name[..us] : def.name;
            return $"Wood / {sheet} • {def.Footprint.x}×{def.Footprint.y} cells";
        }

        private static string DescribeDecoration(string tileName)
        {
            // tileName is like "Trees_2", "Cactus_5", "Rocks_10"
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

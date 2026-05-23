using System.Collections.Generic;
using RTSCL.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    public sealed class BuildingPlacer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Tilemap _terrainMap;
        [SerializeField] private Camera _camera;
        [SerializeField] private WorldGeneratorBootstrap _worldSource;

        [Header("Visuals")]
        [SerializeField] private Color _validTint   = new Color(0.5f, 1f, 0.5f, 0.6f);
        [SerializeField] private Color _invalidTint = new Color(1f, 0.4f, 0.4f, 0.6f);

        [Header("Building Parent")]
        [Tooltip("New buildings get parented under this transform")]
        [SerializeField] private Transform _buildingsRoot;

        private BuildingDefinition _selected;
        private GameObject _ghost;
        private SpriteRenderer _ghostRenderer;
        private readonly Dictionary<Vector2Int, BuildingDefinition> _cellOwners = new();

        public BuildingDefinition Selected => _selected;

        public bool TryGetBuildingAt(Vector2Int cell, out BuildingDefinition def) =>
            _cellOwners.TryGetValue(cell, out def);

        public void Select(BuildingDefinition def)
        {
            CancelGhost();
            _selected = def;
            if (def == null) return;
            _ghost = new GameObject($"Ghost_{def.name}");
            if (_buildingsRoot != null) _ghost.transform.SetParent(_buildingsRoot, false);
            _ghostRenderer = _ghost.AddComponent<SpriteRenderer>();
            _ghostRenderer.sprite = def.Sprite;
            _ghostRenderer.sortingOrder = 20;
        }

        public void Cancel() => CancelGhost();

        private void CancelGhost()
        {
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            _ghostRenderer = null;
            _selected = null;
        }

        private void Update()
        {
            if (_selected == null) return;
            if (_camera == null || _terrainMap == null) return;

            // Esc or right-click cancels
            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame))
            {
                Cancel();
                return;
            }
            if (Mouse.current == null) return;

            Vector2 mp = Mouse.current.position.ReadValue();
            Vector3 world = _camera.ScreenToWorldPoint(
                new Vector3(mp.x, mp.y, -_camera.transform.position.z));
            Vector3Int cell = _terrainMap.WorldToCell(world);

            Vector3 ghostPos = _terrainMap.CellToWorld(cell);  // bottom-left of cell
            _ghost.transform.position = ghostPos;
            bool valid = IsValid(new Vector2Int(cell.x, cell.y));
            _ghostRenderer.color = valid ? _validTint : _invalidTint;

            // Don't place if mouse is over UI
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (valid && Mouse.current.leftButton.wasPressedThisFrame)
                Place(new Vector2Int(cell.x, cell.y));
        }

        private bool IsValid(Vector2Int origin)
        {
            var world = _worldSource != null ? _worldSource.CurrentWorld : null;
            for (int dy = 0; dy < _selected.Footprint.y; dy++)
            for (int dx = 0; dx < _selected.Footprint.x; dx++)
            {
                int x = origin.x + dx;
                int y = origin.y + dy;

                // Terrain biome check
                var t = _terrainMap.GetTile(new Vector3Int(x, y, 0));
                if (t == null) return false;
                string n = t.name;
                if (n == "DeepWater" || n == "Shore" || n == "Cliff") return false;

                // Overlap with existing buildings
                if (_cellOwners.ContainsKey(new Vector2Int(x, y))) return false;

                // Overlap with resource clusters
                if (world != null && IsResourceCell(world, x, y)) return false;
            }
            return true;
        }

        private static bool IsResourceCell(WorldData world, int x, int y)
        {
            if (world.Resources == null) return false;
            foreach (var cluster in world.Resources)
                foreach (var c in cluster.Cells)
                    if (c.x == x && c.y == y) return true;
            return false;
        }

        private void Place(Vector2Int origin)
        {
            var go = new GameObject($"Building_{_selected.name}_{origin.x}_{origin.y}");
            if (_buildingsRoot != null) go.transform.SetParent(_buildingsRoot, false);
            go.transform.position = _terrainMap.CellToWorld(new Vector3Int(origin.x, origin.y, 0));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _selected.Sprite;
            sr.sortingOrder = 15;

            for (int dy = 0; dy < _selected.Footprint.y; dy++)
            for (int dx = 0; dx < _selected.Footprint.x; dx++)
                _cellOwners[new Vector2Int(origin.x + dx, origin.y + dy)] = _selected;
        }

        public void ClearAllPlaced()
        {
            _cellOwners.Clear();
            if (_buildingsRoot == null) return;
            for (int i = _buildingsRoot.childCount - 1; i >= 0; i--)
            {
                var c = _buildingsRoot.GetChild(i);
                if (c.name.StartsWith("Building_")) DestroyImmediate(c.gameObject);
            }
        }
    }
}

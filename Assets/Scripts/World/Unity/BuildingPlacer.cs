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
        [SerializeField] private GoblinSelectionController _selectionController;

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
        private readonly Dictionary<Vector2Int, Vector2Int> _cellToOrigin = new();
        private readonly Dictionary<Vector2Int, ulong> _cellToOwner = new();

        public BuildingDefinition Selected => _selected;

        private void OnEnable() => BuildingConstruction.OnCompleted += OnConstructionCompleted;
        private void OnDisable() => BuildingConstruction.OnCompleted -= OnConstructionCompleted;

        private void OnConstructionCompleted(Vector2Int origin)
        {
            if (_cellOwners.TryGetValue(origin, out var def) && def != null)
                PopulationManager.AddCap(def.PopulationProvided);
        }

        public bool TryGetBuildingAt(Vector2Int cell, out BuildingDefinition def) =>
            _cellOwners.TryGetValue(cell, out def);

        public bool TryGetBuildingOrigin(Vector2Int cell, out Vector2Int origin) =>
            _cellToOrigin.TryGetValue(cell, out origin);

        public bool TryGetBuildingOwner(Vector2Int cell, out ulong owner) =>
            _cellToOwner.TryGetValue(cell, out owner);

        /// <summary>Find the nearest building whose underlying BuildingDefinition asset is named `name`
        /// AND that is owned by `owner`. Returns false if none exists.</summary>
        public bool TryFindNearestBuildingByName(string name, ulong owner, Vector2Int from, out Vector2Int origin)
        {
            origin = default;
            int bestDistSq = int.MaxValue;
            bool found = false;
            foreach (var kvp in _cellOwners)
            {
                var def = kvp.Value;
                if (def == null || def.name != name) continue;
                if (!_cellToOwner.TryGetValue(kvp.Key, out ulong cellOwner) || cellOwner != owner) continue;
                if (!_cellToOrigin.TryGetValue(kvp.Key, out var thisOrigin)) continue;
                int dx = thisOrigin.x - from.x;
                int dy = thisOrigin.y - from.y;
                int d = dx * dx + dy * dy;
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    origin = thisOrigin;
                    found = true;
                }
            }
            return found;
        }

        public IEnumerable<KeyValuePair<Vector2Int, BuildingDefinition>> AllOccupied => _cellOwners;

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
            // Affordability (wood + stone)
            if (_selected.WoodCost > 0 && ResourceBank.Wood < _selected.WoodCost) return false;
            if (_selected.StoneCost > 0 && ResourceBank.Get(ResourceKind.Stone) < _selected.StoneCost) return false;

            var world = _worldSource != null ? _worldSource.CurrentWorld : null;
            for (int dy = 0; dy < _selected.Footprint.y; dy++)
            for (int dx = 0; dx < _selected.Footprint.x; dx++)
            {
                int x = origin.x + dx;
                int y = origin.y + dy;

                var t = _terrainMap.GetTile(new Vector3Int(x, y, 0));
                if (t == null) return false;
                string n = t.name;
                if (n == "DeepWater" || n == "Shore" || n == "Cliff") return false;

                if (_cellOwners.ContainsKey(new Vector2Int(x, y))) return false;

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
            if (_selected == null) return;
            ulong owner = WorldStartContext.LocalPlayer;
            NetCommandIssuer.IssuePlaceBuilding(_selected, origin, owner);

            // Auto-build: send the currently-selected worker goblins to construct it.
            if (_selectionController != null)
            {
                var workers = new System.Collections.Generic.List<Goblin>();
                foreach (var g in _selectionController.Selection)
                    if (g != null && g.Kind == "FarmerGoblin") workers.Add(g);
                if (workers.Count > 0) NetCommandIssuer.IssueBuildAssist(workers, origin);
            }

            Cancel();
        }

        /// <summary>Place a building. charge=true deducts WoodCost; requireConstruction=true makes it built-by-goblins.</summary>
        public void PlaceForce(BuildingDefinition def, Vector2Int origin,
                               bool charge = true, bool requireConstruction = true,
                               ulong owner = 0UL)
        {
            if (def == null || def.Sprite == null) return;

            if (charge)
            {
                if (ResourceBank.Wood < def.WoodCost) return;
                if (ResourceBank.Get(ResourceKind.Stone) < def.StoneCost) return;
                if (def.WoodCost > 0) ResourceBank.AddWood(-def.WoodCost);
                if (def.StoneCost > 0) ResourceBank.Add(ResourceKind.Stone, -def.StoneCost);
            }

            var go = new GameObject($"Building_{def.name}_{origin.x}_{origin.y}");
            if (_buildingsRoot != null) go.transform.SetParent(_buildingsRoot, false);
            go.transform.position = _terrainMap.CellToWorld(new Vector3Int(origin.x, origin.y, 0));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = def.Sprite;
            sr.sortingOrder = 15;

            var ownerComp = go.AddComponent<BuildingOwner>();
            ownerComp.Initialize(owner, def.Footprint);

            for (int dy = 0; dy < def.Footprint.y; dy++)
            for (int dx = 0; dx < def.Footprint.x; dx++)
            {
                var c = new Vector2Int(origin.x + dx, origin.y + dy);
                _cellOwners[c] = def;
                _cellToOrigin[c] = origin;
                _cellToOwner[c] = owner;
            }

            BuildingHP.Register(origin, BuildingHP.MaxHpFor(def));
            if (requireConstruction) BuildingConstruction.Register(origin, go);
            else PopulationManager.AddCap(def.PopulationProvided); // instant-place (e.g. starting keep)
        }

        public void ClearAllPlaced()
        {
            _cellOwners.Clear();
            _cellToOrigin.Clear();
            _cellToOwner.Clear();
            BuildingHP.Clear();
            BuildingConstruction.Clear();
            if (_buildingsRoot == null) return;
            for (int i = _buildingsRoot.childCount - 1; i >= 0; i--)
            {
                var c = _buildingsRoot.GetChild(i);
                if (c.name.StartsWith("Building_")) DestroyImmediate(c.gameObject);
            }
        }
    }
}

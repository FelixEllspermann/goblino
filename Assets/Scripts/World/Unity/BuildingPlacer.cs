// =============================================================================
// BuildingPlacer.cs  —  RTSCL.World.Unity
//
// Manages the "ghost placement" cursor (BuildingDefinition selected from the
// UI → semi-transparent preview that follows the mouse → left-click to place).
// Maintains three parallel dictionaries keyed by occupied cell:
//   _cellOwners    : cell → BuildingDefinition (what is here)
//   _cellToOrigin  : cell → SW-corner origin of the multi-cell footprint
//   _cellToOwner   : cell → Steam ID of the placing player
//
// PlaceForce is the canonical placement path (both the ghost-click path via
// Place() → NetCommandIssuer → NetCommandApplier → PlaceForce, and the
// starting-keep path in MainBaseSetup.SpawnTeamAt). PlaceForce handles sprite
// instantiation, dictionary registration, HP + construction registration.
//
// To add a new validity rule (e.g. max-one-per-player): extend IsValid.
// To add a cost type beyond wood/stone: extend IsValid + PlaceForce charge block.
// =============================================================================
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

        // Currently selected building type waiting to be placed; null = not in placement mode.
        private BuildingDefinition _selected;
        private GameObject _ghost;               // preview object that follows the mouse
        private SpriteRenderer _ghostRenderer;   // reference cached to tint the ghost each frame

        // Per-cell occupation registries. All three dictionaries are updated atomically in PlaceForce.
        // Keyed by every cell of the footprint (not just the origin) so occupancy checks are O(1).
        private readonly Dictionary<Vector2Int, BuildingDefinition> _cellOwners = new();
        private readonly Dictionary<Vector2Int, Vector2Int> _cellToOrigin = new();
        private readonly Dictionary<Vector2Int, ulong> _cellToOwner = new();
        private readonly Dictionary<Vector2Int, GameObject> _originToGo = new();   // origin → building GameObject

        /// <summary>The BuildingDefinition currently queued for placement, or null if not in placement mode.</summary>
        public BuildingDefinition Selected => _selected;

        // When construction completes, grant the population cap bonus from the building definition.
        private void OnEnable() => BuildingConstruction.OnCompleted += OnConstructionCompleted;
        private void OnDisable() => BuildingConstruction.OnCompleted -= OnConstructionCompleted;

        private void OnConstructionCompleted(Vector2Int origin)
        {
            // origin is the SW-corner cell; the _cellOwners entry is registered there.
            if (_cellOwners.TryGetValue(origin, out var def) && def != null)
            {
                ulong o = _cellToOwner.TryGetValue(origin, out var ow) ? ow : 0UL;
                if (WorldStartContext.IsSolo && o != 0UL) BotEconomy.AddCap(o, def.PopulationProvided);
                else PopulationManager.AddCap(def.PopulationProvided);
            }
        }

        /// <summary>Look up the BuildingDefinition occupying a cell. Returns false for empty cells.</summary>
        public bool TryGetBuildingAt(Vector2Int cell, out BuildingDefinition def) =>
            _cellOwners.TryGetValue(cell, out def);

        /// <summary>Map any occupied cell back to the SW-corner origin of its building footprint.</summary>
        public bool TryGetBuildingOrigin(Vector2Int cell, out Vector2Int origin) =>
            _cellToOrigin.TryGetValue(cell, out origin);

        /// <summary>Look up the rendered GameObject for a building by its origin (for hit effects).</summary>
        public bool TryGetBuildingGo(Vector2Int origin, out GameObject go) =>
            _originToGo.TryGetValue(origin, out go) && go != null;

        /// <summary>Look up the Steam ID of the player who placed the building occupying cell.</summary>
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

        /// <summary>Expose all occupied cells for iteration (e.g. NetworkCatalog building lookup).</summary>
        public IEnumerable<KeyValuePair<Vector2Int, BuildingDefinition>> AllOccupied => _cellOwners;

        /// <summary>Enter placement mode for the given building type. Spawns the ghost preview object.
        /// Called by ObjectInspector when the player clicks a building card.</summary>
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

        /// <summary>Exit placement mode without placing. Called on Esc/right-click or after a successful place.</summary>
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

        // Validity check run every frame during placement and again just before Place() commits.
        // Rules: (1) enough wood + stone; (2) every footprint cell has a terrain tile;
        // (3) no impassable terrain (water/shore/cliff); (4) cells not already occupied;
        // (5) cells not on a resource cluster from the WorldData generator.
        private bool IsValid(Vector2Int origin)
        {
            // Affordability (wood + stone)
            if (_selected.WoodCost > 0 && ResourceBank.Wood < _selected.WoodCost) return false;
            if (_selected.StoneCost > 0 && ResourceBank.Get(ResourceKind.Stone) < _selected.StoneCost) return false;

            // Docks: the land cell must be buildable AND have an adjacent water cell for the pier + boats.
            if (IsDock(_selected))
            {
                var t = _terrainMap.GetTile(new Vector3Int(origin.x, origin.y, 0));
                if (t == null) return false;
                string n = t.name;
                if (n == "DeepWater" || n == "Shore" || n == "Cliff") return false;        // dock body sits on land
                if (_cellOwners.ContainsKey(origin)) return false;
                var w = _worldSource != null ? _worldSource.CurrentWorld : null;
                if (w != null && IsResourceCell(w, origin.x, origin.y)) return false;
                return TryFindAdjacentWater(origin, out _);
            }

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

        // Check the WorldData resource cluster list to prevent building on pre-placed resource nodes.
        private static bool IsResourceCell(WorldData world, int x, int y)
        {
            if (world.Resources == null) return false;
            foreach (var cluster in world.Resources)
                foreach (var c in cluster.Cells)
                    if (c.x == x && c.y == y) return true;
            return false;
        }

        // A Dock is a coastal building: its body sits on land but it needs an adjacent water cell.
        private static bool IsDock(BuildingDefinition def) => def != null && def.name.StartsWith("Docks");

        // Nearest water cell (DeepWater/Shore) of a land cell, for the pier + boat spawn. Prefers an
        // ORTHOGONAL neighbour (boat sails straight out); falls back to a DIAGONAL one so a dock built at an
        // inner/concave coastline corner — where water only touches a corner — still gets a pier + boat
        // spawn cell. Without the diagonal fallback such docks drew no pier and couldn't spawn boats.
        private bool TryFindAdjacentWater(Vector2Int landCell, out Vector2Int waterCell)
        {
            Vector2Int[] ortho = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
            foreach (var d in ortho)
            {
                var c = new Vector3Int(landCell.x + d.x, landCell.y + d.y, 0);
                var t = _terrainMap.GetTile(c);
                if (t != null && (t.name == "DeepWater" || t.name == "Shore"))
                { waterCell = new Vector2Int(c.x, c.y); return true; }
            }
            Vector2Int[] diag = { new(1, 1), new(1, -1), new(-1, 1), new(-1, -1) };
            foreach (var d in diag)
            {
                var c = new Vector3Int(landCell.x + d.x, landCell.y + d.y, 0);
                var t = _terrainMap.GetTile(c);
                if (t != null && (t.name == "DeepWater" || t.name == "Shore"))
                { waterCell = new Vector2Int(c.x, c.y); return true; }
            }
            waterCell = default;
            return false;
        }

        // Commit: issue the net command (which routes through NetCommandApplier → PlaceForce
        // on every client), then auto-dispatch selected Farmer Goblins to build it.
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

        /// <summary>Canonical building-placement entry point. Called by NetCommandApplier.ApplyPlaceBuilding
        /// on every client, and by MainBaseSetup.SpawnTeamAt for starting keeps.
        /// <para>charge=false skips resource deduction (starting keeps, no-cost spawns).</para>
        /// <para>requireConstruction=false grants pop-cap immediately (keeps are pre-built).</para>
        /// </summary>
        public void PlaceForce(BuildingDefinition def, Vector2Int origin,
                               bool charge = true, bool requireConstruction = true,
                               ulong owner = 0UL)
        {
            if (def == null || def.Sprite == null) return;

            // Deduct resources before spawning so the ledger stays consistent even if
            // the building cannot be physically spawned (missing scene reference, etc.)
            if (charge)
            {
                if (ResourceBank.Wood < def.WoodCost) return;
                if (ResourceBank.Get(ResourceKind.Stone) < def.StoneCost) return;
                if (def.WoodCost > 0) ResourceBank.AddWood(-def.WoodCost);
                if (def.StoneCost > 0) ResourceBank.Add(ResourceKind.Stone, -def.StoneCost);
            }

            var go = new GameObject($"Building_{def.name}_{origin.x}_{origin.y}");
            if (_buildingsRoot != null) go.transform.SetParent(_buildingsRoot, false);
            // Tilemaps place origin at the tile's bottom-left corner; building sprite pivot should match.
            go.transform.position = _terrainMap.CellToWorld(new Vector3Int(origin.x, origin.y, 0));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = def.Sprite;
            sr.sortingOrder = 15;  // below goblins (25), above terrain tiles

            var ownerComp = go.AddComponent<BuildingOwner>();
            ownerComp.Initialize(owner, def.Footprint);

            // Register every footprint cell in all three dictionaries atomically.
            for (int dy = 0; dy < def.Footprint.y; dy++)
            for (int dx = 0; dx < def.Footprint.x; dx++)
            {
                var c = new Vector2Int(origin.x + dx, origin.y + dy);
                _cellOwners[c] = def;
                _cellToOrigin[c] = origin;
                _cellToOwner[c] = owner;
            }
            _originToGo[origin] = go;

            BuildingHP.Register(origin, BuildingHP.MaxHpFor(def));
            if (requireConstruction) BuildingConstruction.Register(origin, go);
            else if (WorldStartContext.IsSolo && owner != 0UL) BotEconomy.AddCap(owner, def.PopulationProvided); // bot instant-place
            else PopulationManager.AddCap(def.PopulationProvided); // instant-place (e.g. starting keep)

            // Give unit-training buildings a default rally point (below the footprint) if none set yet.
            if (def.TrainsUnits != null && def.TrainsUnits.Length > 0 && !RallyPoints.TryGet(origin, out _))
                RallyPoints.Set(origin, RallyPoints.Default(origin, def.Footprint));

            // Dock: record the adjacent water cell (boat spawn) and draw the pier sprite there.
            if (IsDock(def) && TryFindAdjacentWater(origin, out var waterCell))
            {
                DockRegistry.Set(origin, waterCell);
                if (def.WaterSprite != null)
                {
                    var pier = new GameObject($"DockPier_{origin.x}_{origin.y}");
                    pier.transform.SetParent(go.transform, false);   // child → destroyed with the dock
                    pier.transform.position = _terrainMap.CellToWorld(new Vector3Int(waterCell.x, waterCell.y, 0));
                    var psr = pier.AddComponent<SpriteRenderer>();
                    psr.sprite = def.WaterSprite;
                    psr.sortingOrder = 15;
                }
            }
        }

        /// <summary>The footprint of the building occupying a cell (via its definition). False if none.</summary>
        public bool TryGetFootprint(Vector2Int cell, out Vector2Int footprint)
        {
            if (_cellOwners.TryGetValue(cell, out var def) && def != null) { footprint = def.Footprint; return true; }
            footprint = Vector2Int.one; return false;
        }

        /// <summary>True if <paramref name="owner"/> still owns at least one building (alive faction).</summary>
        public bool HasAnyBuilding(ulong owner)
        {
            foreach (var o in _cellToOwner.Values) if (o == owner) return true;
            return false;
        }

        /// <summary>Destroy the building at <paramref name="origin"/>: refund pop cap (if it provided any
        /// and was completed), clear all registries for its footprint, drop construction, remove the
        /// GameObject, and play a burst. Safe to call with an unknown origin.</summary>
        public void RemoveBuilding(Vector2Int origin)
        {
            if (!_cellOwners.TryGetValue(origin, out var def) || def == null) return;
            ulong owner = _cellToOwner.TryGetValue(origin, out var ow) ? ow : 0UL;
            bool wasCompleted = !BuildingConstruction.IsUnderConstruction(origin);

            // Refund the population cap only for completed buildings that granted it.
            if (wasCompleted && def.PopulationProvided > 0)
            {
                if (WorldStartContext.IsSolo && owner != 0UL) BotEconomy.AddCap(owner, -def.PopulationProvided);
                else PopulationManager.AddCap(-def.PopulationProvided);
            }

            // Clear every footprint cell from the registries.
            for (int dy = 0; dy < def.Footprint.y; dy++)
            for (int dx = 0; dx < def.Footprint.x; dx++)
            {
                var c = new Vector2Int(origin.x + dx, origin.y + dy);
                _cellOwners.Remove(c);
                _cellToOrigin.Remove(c);
                _cellToOwner.Remove(c);
            }

            BuildingHP.Remove(origin);
            BuildingConstruction.Remove(origin);
            RallyPoints.Remove(origin);
            DockRegistry.Remove(origin);

            if (_originToGo.TryGetValue(origin, out var go) && go != null)
            {
                DeathBurst.SpawnCustom(go.transform.position + new Vector3(def.Footprint.x * 0.5f, def.Footprint.y * 0.5f, 0f),
                                       16, 0.18f, 1.5f, 3f, 0.6f, 0.7f, 0.6f, new Color(0.6f, 0.5f, 0.4f, 1f));
                Destroy(go);
            }
            _originToGo.Remove(origin);
        }

        /// <summary>Remove all placed buildings and reset associated static registries.
        /// Called by MainBaseSetup.OnNewWorld before generating a new world.</summary>
        public void ClearAllPlaced()
        {
            _cellOwners.Clear();
            _cellToOrigin.Clear();
            _cellToOwner.Clear();
            _originToGo.Clear();
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

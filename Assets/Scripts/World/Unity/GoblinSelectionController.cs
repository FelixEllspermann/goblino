// =============================================================================
// GoblinSelectionController.cs  —  RTSCL.World.Unity
//
// Handles all mouse-driven unit selection (click + drag-box) and right-click
// command dispatch for the local player's goblins. Only local-owner units can
// be selected; enemy units are click-targeted for attack but not selected.
//
// Right-click priority order: harvestable tile → building under construction
//   → enemy goblin → empty terrain (move formation).
// Shift held → appends to each unit's owner-local command queue instead of
// issuing immediately. Queue contents are invisible to remotes; each step is
// sent as a normal net command when ActivateNextQueued fires it.
//
// To add a new command type: add a TryGetXAt check in Update's right-click
// block, a CommandX method, and an EnqueueX method. Wire up the net command
// in NetCommandIssuer and add a case to Goblin.ActivateNextQueued.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    public sealed class GoblinSelectionController : MonoBehaviour
    {
        /// <summary>Currently selected local goblins. Read-only outside this class.</summary>
        public IReadOnlyList<Goblin> Selection => _selected;
        /// <summary>Fires whenever the selection list changes (units selected/deselected).</summary>
        public event Action OnSelectionChanged;
        /// <summary>Fires when an enemy/neutral unit is left-clicked (for read-only inspection — it is
        /// NOT added to the commandable selection, so the player can't control enemy units).</summary>
        public event Action<Goblin> OnInspectUnit;

        [SerializeField] private Camera _camera;
        [SerializeField] private BuildingPlacer _buildingPlacer;
        [SerializeField] private Tilemap _decorationMap;
        [SerializeField] private Tilemap _terrainMap;
        /// <summary>World-unit radius within which a single left-click picks the nearest goblin.</summary>
        [SerializeField] private float _clickPickRadius = 0.6f;       // world units
        /// <summary>Pixel distance the mouse must drag before switching from click-select to box-select.</summary>
        [SerializeField] private float _dragThresholdPx = 6f;
        [SerializeField] private float _formationSpacing = 1.0f;
        /// <summary>Unused field — harvest spread is handled entirely by HarvestReservations.</summary>
        [SerializeField] private int _harvestSpreadRadius = 6;

        private readonly List<Goblin> _selected = new();
        private Vector2 _dragStartScreen;
        private bool _mouseDown;
        private bool _isDragBox;

        // Gate: only the local player's goblins can be selected or commanded.
        private static bool IsLocalOwner(Goblin g) =>
            g != null && (g.Owner == WorldStartContext.LocalPlayer || g.Owner == 0UL);

        private void Update()
        {
            if (_camera == null || Mouse.current == null) return;

            // If user is mid-placement, leave clicks to BuildingPlacer
            if (_buildingPlacer != null && _buildingPlacer.Selected != null) return;

            // Left mouse: selection (down → maybe drag, up → resolve)
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (IsOverUI()) return;
                _dragStartScreen = Mouse.current.position.ReadValue();
                _mouseDown = true;
                _isDragBox = false;
            }
            else if (_mouseDown)
            {
                Vector2 cur = Mouse.current.position.ReadValue();
                if (!_isDragBox && (cur - _dragStartScreen).sqrMagnitude > _dragThresholdPx * _dragThresholdPx)
                    _isDragBox = true;

                if (Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    if (_isDragBox)
                        SelectInBox(_dragStartScreen, cur);
                    else
                        SelectAtPoint(cur);
                    _mouseDown = false;
                    _isDragBox = false;
                }
            }

            // Right mouse: command selected goblins. Shift held → append to queue; else immediate.
            if (Mouse.current.rightButton.wasPressedThisFrame
                && _selected.Count > 0 && !IsOverUI())
            {
                Vector2 mp = Mouse.current.position.ReadValue();
                Vector3 worldTarget = _camera.ScreenToWorldPoint(
                    new Vector3(mp.x, mp.y, -_camera.transform.position.z));

                bool shift = Keyboard.current != null
                    && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

                if (TryGetHarvestableAt(worldTarget, out var treeCell))
                {
                    Vector3 treeCenter = _decorationMap.CellToWorld(treeCell) + new Vector3(0.5f, 0.5f, 0f);
                    ClickFeedback.Spawn(treeCenter, new Color(0.4f, 1f, 0.4f, 0.85f));  // green = harvest
                    if (shift) EnqueueHarvest(treeCell); else { ClearQueues(); CommandHarvest(treeCell); }
                }
                else if (TryGetBuildingTarget(worldTarget, out var bOrigin, out bool bMine, out bool bUnderConstruction)
                         && ((bMine && bUnderConstruction) || !bMine))
                {
                    Vector3 c = new(bOrigin.x + 0.5f, bOrigin.y + 0.5f, 0f);
                    if (bMine)
                    {
                        ClickFeedback.Spawn(c, new Color(1f, 0.7f, 0.2f, 0.9f));  // orange = build-assist
                        if (shift) EnqueueBuildAssist(bOrigin); else { ClearQueues(); CommandBuildAssist(bOrigin); }
                    }
                    else
                    {
                        ClickFeedback.Spawn(c, new Color(1f, 0.3f, 0.3f, 0.9f)); // red = attack building
                        ClearQueues(); CommandAttackBuilding(bOrigin);
                    }
                }
                else if (TryGetFriendlyBoatAt(worldTarget, out var boat) && HasNonBoatSelected())
                {
                    ClickFeedback.Spawn(boat.transform.position, new Color(0.3f, 0.7f, 1f, 0.9f)); // cyan = board
                    ClearQueues();
                    foreach (var g in _selected) if (g != null && !g.IsBoat) g.SetBoardCommand(boat);
                }
                else if (TryGetGoblinAt(worldTarget, out var enemy))
                {
                    ClickFeedback.Spawn(enemy.transform.position, new Color(1f, 0.3f, 0.3f, 0.9f)); // red = attack
                    if (shift) EnqueueAttack(enemy); else { ClearQueues(); CommandAttack(enemy); }
                }
                else
                {
                    ClickFeedback.Spawn(worldTarget, new Color(1f, 1f, 1f, 0.85f));    // white = move
                    if (shift) EnqueueMove(worldTarget); else { ClearQueues(); CommandMoveOrUnload(worldTarget); }
                }
            }
        }

        // Resolve a building under the cursor: its origin, whether it's the local player's, and whether
        // it's still under construction. Drives right-click: own+construction → build-assist; enemy → attack.
        private bool TryGetBuildingTarget(Vector3 worldPos, out Vector2Int origin, out bool mine, out bool underConstruction)
        {
            origin = default; mine = false; underConstruction = false;
            if (_terrainMap == null || _buildingPlacer == null) return false;
            var cell = _terrainMap.WorldToCell(worldPos);
            var cell2 = new Vector2Int(cell.x, cell.y);
            if (!_buildingPlacer.TryGetBuildingAt(cell2, out var def) || def == null) return false;
            if (!_buildingPlacer.TryGetBuildingOrigin(cell2, out origin)) origin = cell2;
            ulong owner = _buildingPlacer.TryGetBuildingOwner(cell2, out var o) ? o : 0UL;
            mine = owner == WorldStartContext.LocalPlayer || owner == 0UL;
            underConstruction = BuildingConstruction.IsUnderConstruction(origin);
            return true;
        }

        // Order every combat-capable selected unit to attack the building at origin.
        private void CommandAttackBuilding(Vector2Int origin)
        {
            foreach (var g in _selected)
                if (g != null && g.AttackDamage > 0)
                    g.SetAttackBuildingCommand(origin);
        }

        // Returns true if the decoration tile at world falls under Goblin.IsHarvestable.
        private bool TryGetHarvestableAt(Vector3 world, out Vector3Int cell)
        {
            cell = default;
            if (_decorationMap == null) return false;
            cell = _decorationMap.WorldToCell(world);
            var t = _decorationMap.GetTile(cell);
            return t != null && Goblin.IsHarvestable(t.name);
        }

        // Send all selected worker goblins to the single clicked node.
        // HarvestReservations assigns each worker a unique adjacent standing cell,
        // so multiple farmers naturally fan out around the same resource node.
        private void CommandHarvest(Vector3Int clickedNode)
        {
            // Only worker units (Farmer Goblins) can harvest. All selected workers go to the
            // SAME clicked node; HarvestReservations fans them out onto distinct adjacent cells.
            var workers = new List<Goblin>();
            foreach (var g in _selected) if (IsWorker(g)) workers.Add(g);
            if (workers.Count == 0) return;
            NetCommandIssuer.IssueHarvest(workers, clickedNode);
        }

        /// <summary>Returns true if g is a harvesting-capable Farmer Goblin.</summary>
        private static bool IsWorker(Goblin g) =>
            g != null && g.Kind == "FarmerGoblin";

        // Gather up to maxCount harvestable tiles sorted by squared distance from origin.
        // Currently unused in the main command path (all farmers go to the clicked node);
        // kept for potential future "spread workers across nearby nodes" feature.
        private List<Vector3Int> FindNearbyHarvestables(Vector3Int origin, int maxCount, int radius)
        {
            var found = new List<(Vector3Int cell, int distSq)>();
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                var c = new Vector3Int(origin.x + dx, origin.y + dy, 0);
                var t = _decorationMap.GetTile(c);
                if (t == null) continue;
                if (!Goblin.IsHarvestable(t.name)) continue;
                found.Add((c, dx * dx + dy * dy));
            }
            found.Sort((a, b) => a.distSq.CompareTo(b.distSq));
            var result = new List<Vector3Int>(System.Math.Min(maxCount, found.Count));
            for (int i = 0; i < found.Count && i < maxCount; i++) result.Add(found[i].cell);
            return result;
        }

        // Pick the closest local goblin within _clickPickRadius. If none, clears selection.
        private void SelectAtPoint(Vector2 screenPos)
        {
            Vector3 world = _camera.ScreenToWorldPoint(
                new Vector3(screenPos.x, screenPos.y, -_camera.transform.position.z));
            Goblin best = null;
            float bestDist = _clickPickRadius;
            foreach (var g in Goblin.All)
            {
                if (g == null) continue;
                if (!IsLocalOwner(g) || g.IsNeutral) continue;
                // A direct hit on the sprite's visual bounds wins outright (handles big sprites like boats).
                if (PointInBounds(g.SelectionBounds, world)) { best = g; break; }
                float d = Vector2.Distance(g.transform.position, world);
                if (d < bestDist) { bestDist = d; best = g; }
            }
            if (best != null) { SetSelection(new List<Goblin> { best }); return; }

            // No friendly under the cursor → inspect an enemy/neutral unit (read-only; never commanded).
            Goblin other = null; float otherDist = _clickPickRadius;
            foreach (var g in Goblin.All)
            {
                if (g == null) continue;
                if (IsLocalOwner(g) && !g.IsNeutral) continue;   // our own units were handled above
                if (PointInBounds(g.SelectionBounds, world)) { other = g; break; }
                float d = Vector2.Distance(g.transform.position, world);
                if (d < otherDist) { otherDist = d; other = g; }
            }
            SetSelection(new List<Goblin>());                    // clear the commandable selection
            if (other != null) OnInspectUnit?.Invoke(other);
        }

        // Select all local goblins whose screen position falls within the drag rectangle.
        private void SelectInBox(Vector2 a, Vector2 b)
        {
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
            float minY = Mathf.Min(a.y, b.y), maxY = Mathf.Max(a.y, b.y);
            var hit = new List<Goblin>();
            foreach (var g in Goblin.All)
            {
                if (g == null) continue;
                if (!IsLocalOwner(g) || g.IsNeutral) continue;
                Vector3 sp = _camera.WorldToScreenPoint(g.transform.position);
                if (sp.x >= minX && sp.x <= maxX && sp.y >= minY && sp.y <= maxY)
                    hit.Add(g);
            }
            SetSelection(hit);
        }

        // Update selection rings and fire OnSelectionChanged so ObjectInspector and other
        // listeners can refresh their UI immediately.
        private void SetSelection(List<Goblin> newSel)
        {
            foreach (var g in _selected) if (g != null) g.SetSelected(false);
            _selected.Clear();
            foreach (var g in newSel)
            {
                _selected.Add(g);
                g.SetSelected(true);
            }
            OnSelectionChanged?.Invoke();
        }

        // Issue a move command; IssueMove computes the grid formation offsets internally.
        private void CommandFormation(Vector3 worldCenter)
        {
            NetCommandIssuer.IssueMove(_selected, worldCenter);
        }

        // Move command that also handles boats: a boat right-clicked onto land unloads there; onto
        // water it sails; land units move in formation as usual.
        private void CommandMoveOrUnload(Vector3 worldTarget)
        {
            bool targetIsLand = IsLandCell(worldTarget);
            var landUnits = new List<Goblin>();
            foreach (var g in _selected)
            {
                if (g == null) continue;
                if (g.IsBoat)
                {
                    if (targetIsLand) g.SetUnloadCommand(worldTarget);
                    else g.SetMoveCommand(worldTarget);
                }
                else landUnits.Add(g);
            }
            if (landUnits.Count > 0) NetCommandIssuer.IssueMove(landUnits, worldTarget);
        }

        private bool IsLandCell(Vector3 world)
        {
            if (_terrainMap == null) return true;
            var t = _terrainMap.GetTile(_terrainMap.WorldToCell(world));
            if (t == null) return false;
            return t.name != "DeepWater" && t.name != "Shore" && t.name != "Cliff";
        }

        private bool HasNonBoatSelected()
        {
            foreach (var g in _selected) if (g != null && !g.IsBoat) return true;
            return false;
        }

        // 2D point-in-AABB test (ignores z) against a sprite's world bounds.
        private static bool PointInBounds(Bounds b, Vector3 world)
            => world.x >= b.min.x && world.x <= b.max.x && world.y >= b.min.y && world.y <= b.max.y;

        // Nearest friendly boat under the cursor (for boarding), within the click pick radius.
        private bool TryGetFriendlyBoatAt(Vector3 worldPos, out Goblin boat)
        {
            boat = null;
            float bestSq = _clickPickRadius * _clickPickRadius;
            foreach (var g in Goblin.All)
            {
                if (g == null || !g.IsBoat || !IsLocalOwner(g)) continue;
                // Boats are large — a click anywhere on the hull (its sprite bounds) counts as a hit.
                if (PointInBounds(g.SelectionBounds, worldPos)) { boat = g; return true; }
                float d = (g.transform.position - worldPos).sqrMagnitude;
                if (d < bestSq) { bestSq = d; boat = g; }
            }
            return boat != null;
        }

        // Find the nearest goblin not in _selected within the click radius.
        // Excludes own selection so right-clicking on your own units doesn't friendly-fire.
        private bool TryGetGoblinAt(Vector3 worldPos, out Goblin target)
        {
            target = null;
            float bestDistSq = _clickPickRadius * _clickPickRadius;
            foreach (var g in Goblin.All)
            {
                if (g == null) continue;
                // Don't pick a currently-selected goblin (so right-click on your own selection
                // doesn't accidentally target one of your own as the victim).
                if (_selected.Contains(g)) continue;
                // No friendly fire: skip own (non-neutral, locally-owned) units — only enemies/monsters
                // are valid attack targets. A right-click on a friendly falls through to a move command.
                if (IsLocalOwner(g) && !g.IsNeutral) continue;
                float d = (g.transform.position - worldPos).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; target = g; }
            }
            return target != null;
        }

        // Only goblins with AttackDamage > 0 (i.e. combat units) receive the attack command.
        private void CommandAttack(Goblin target)
        {
            foreach (var g in _selected)
                if (g != null && g.AttackDamage > 0)
                    NetCommandIssuer.IssueAttack(g, target);
        }

        // Clear every selected unit's owner-local queue before issuing a fresh immediate command.
        private void ClearQueues()
        {
            foreach (var g in _selected) if (g != null) g.ClearQueue();
        }

        // Only Farmer Goblins send build-assist commands.
        private void CommandBuildAssist(Vector2Int buildOrigin)
        {
            var workers = new List<Goblin>();
            foreach (var g in _selected) if (IsWorker(g)) workers.Add(g);
            if (workers.Count > 0) NetCommandIssuer.IssueBuildAssist(workers, buildOrigin);
        }

        // EnqueueMove replicates the same square-formation offset that IssueMove would apply,
        // so each unit's queued move target lands at its correct formation position.
        private void EnqueueMove(Vector3 worldCenter)
        {
            // Same square-formation offset IssueMove computes, so queued moves keep formation.
            int n = _selected.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
            int rows = Mathf.CeilToInt((float)n / cols);
            const float spacing = 1.0f;
            for (int i = 0; i < n; i++)
            {
                var g = _selected[i];
                if (g == null) continue;
                int col = i % cols, row = i / cols;
                Vector3 offset = new(
                    (col - (cols - 1) * 0.5f) * spacing,
                    (row - (rows - 1) * 0.5f) * spacing, 0f);
                g.EnqueueCommand(new Goblin.GoblinCommand
                {
                    Type = Goblin.CommandType.Move,
                    Point = worldCenter + offset,
                });
            }
        }

        // Enqueue a harvest command for all selected worker goblins (non-workers are skipped).
        private void EnqueueHarvest(Vector3Int cell)
        {
            foreach (var g in _selected)
            {
                if (!IsWorker(g)) continue;
                g.EnqueueCommand(new Goblin.GoblinCommand { Type = Goblin.CommandType.Harvest, Cell = cell });
            }
        }

        // Enqueue a build-assist command for all selected worker goblins.
        private void EnqueueBuildAssist(Vector2Int origin)
        {
            foreach (var g in _selected)
            {
                if (!IsWorker(g)) continue;
                g.EnqueueCommand(new Goblin.GoblinCommand { Type = Goblin.CommandType.BuildAssist, Origin = origin });
            }
        }

        // Enqueue an attack command for all selected combat goblins (non-combatants skipped).
        private void EnqueueAttack(Goblin target)
        {
            foreach (var g in _selected)
            {
                if (g == null || g.AttackDamage <= 0) continue;
                g.EnqueueCommand(new Goblin.GoblinCommand { Type = Goblin.CommandType.Attack, Target = target });
            }
        }

        // Blocks selection/command processing when the cursor is over a UI element.
        private static bool IsOverUI()
        {
            return EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject();
        }

        // ---------- Marquee drag-box rendering (immediate-mode GUI) ----------

        // Semi-transparent 1×1 green texture stretched to fill the drag rectangle.
        private static Texture2D s_boxTex;
        private static Texture2D BoxTexture()
        {
            if (s_boxTex == null)
            {
                s_boxTex = new Texture2D(1, 1);
                s_boxTex.SetPixel(0, 0, new Color(0.4f, 1f, 0.4f, 0.25f));
                s_boxTex.Apply();
            }
            return s_boxTex;
        }

        private void OnGUI()
        {
            if (!_isDragBox) return;
            Vector2 cur = Mouse.current.position.ReadValue();
            // OnGUI uses top-left origin; InputSystem mouse uses bottom-left — flip Y.
            float aX = _dragStartScreen.x, bX = cur.x;
            float aY = Screen.height - _dragStartScreen.y, bY = Screen.height - cur.y;
            Rect r = Rect.MinMaxRect(Mathf.Min(aX, bX), Mathf.Min(aY, bY),
                                      Mathf.Max(aX, bX), Mathf.Max(aY, bY));
            GUI.DrawTexture(r, BoxTexture());
        }
    }
}

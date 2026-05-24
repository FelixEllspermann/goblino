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
        public IReadOnlyList<Goblin> Selection => _selected;
        public event Action OnSelectionChanged;

        [SerializeField] private Camera _camera;
        [SerializeField] private BuildingPlacer _buildingPlacer;
        [SerializeField] private Tilemap _decorationMap;
        [SerializeField] private Tilemap _terrainMap;
        [SerializeField] private float _clickPickRadius = 0.6f;       // world units
        [SerializeField] private float _dragThresholdPx = 6f;
        [SerializeField] private float _formationSpacing = 1.0f;
        [SerializeField] private int _harvestSpreadRadius = 6;

        private readonly List<Goblin> _selected = new();
        private Vector2 _dragStartScreen;
        private bool _mouseDown;
        private bool _isDragBox;

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

            // Right mouse: command selected goblins (harvest if tree, otherwise move)
            if (Mouse.current.rightButton.wasPressedThisFrame
                && _selected.Count > 0 && !IsOverUI())
            {
                Vector2 mp = Mouse.current.position.ReadValue();
                Vector3 worldTarget = _camera.ScreenToWorldPoint(
                    new Vector3(mp.x, mp.y, -_camera.transform.position.z));

                if (TryGetTreeAt(worldTarget, out var treeCell))
                {
                    Vector3 treeCenter = _decorationMap.CellToWorld(treeCell) + new Vector3(0.5f, 0.5f, 0f);
                    ClickFeedback.Spawn(treeCenter, new Color(0.4f, 1f, 0.4f, 0.85f));  // green = harvest
                    CommandHarvest(treeCell);
                }
                else if (TryGetConstructionAt(worldTarget, out var buildOrigin))
                {
                    Vector3 c = new(buildOrigin.x + 0.5f, buildOrigin.y + 0.5f, 0f);
                    ClickFeedback.Spawn(c, new Color(1f, 0.7f, 0.2f, 0.9f));  // orange = build
                    foreach (var g in _selected)
                        if (IsWorker(g)) g.SetBuildCommand(buildOrigin);
                }
                else if (TryGetGoblinAt(worldTarget, out var enemy))
                {
                    ClickFeedback.Spawn(enemy.transform.position, new Color(1f, 0.3f, 0.3f, 0.9f)); // red = attack
                    CommandAttack(enemy);
                }
                else
                {
                    ClickFeedback.Spawn(worldTarget, new Color(1f, 1f, 1f, 0.85f));    // white = move
                    CommandFormation(worldTarget);
                }
            }
        }

        private bool TryGetConstructionAt(Vector3 worldPos, out Vector2Int origin)
        {
            origin = default;
            if (_terrainMap == null || _buildingPlacer == null) return false;
            var cell = _terrainMap.WorldToCell(worldPos);
            var cell2 = new Vector2Int(cell.x, cell.y);
            if (!_buildingPlacer.TryGetBuildingOrigin(cell2, out origin)) return false;
            return BuildingConstruction.IsUnderConstruction(origin);
        }

        private bool TryGetTreeAt(Vector3 world, out Vector3Int cell)
        {
            cell = default;
            if (_decorationMap == null) return false;
            cell = _decorationMap.WorldToCell(world);
            var t = _decorationMap.GetTile(cell);
            return t != null && Goblin.IsTreeTile(t.name);
        }

        private void CommandHarvest(Vector3Int clickedTree)
        {
            // Only worker units (Farmer Goblins) can harvest
            var workers = new List<Goblin>();
            foreach (var g in _selected) if (IsWorker(g)) workers.Add(g);
            if (workers.Count == 0) return;

            // Find up to N nearest trees around the click (one per worker)
            var trees = FindNearbyTrees(clickedTree, workers.Count, _harvestSpreadRadius);
            if (trees.Count == 0) return;
            for (int i = 0; i < workers.Count; i++)
            {
                var assigned = trees[i % trees.Count];
                workers[i].SetHarvestCommand(assigned);
            }
        }

        private static bool IsWorker(Goblin g) =>
            g != null && g.Kind == "FarmerGoblin";

        private List<Vector3Int> FindNearbyTrees(Vector3Int origin, int maxCount, int radius)
        {
            var found = new List<(Vector3Int cell, int distSq)>();
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                var c = new Vector3Int(origin.x + dx, origin.y + dy, 0);
                var t = _decorationMap.GetTile(c);
                if (t == null) continue;
                if (!Goblin.IsTreeTile(t.name)) continue;
                found.Add((c, dx * dx + dy * dy));
            }
            found.Sort((a, b) => a.distSq.CompareTo(b.distSq));
            var result = new List<Vector3Int>(System.Math.Min(maxCount, found.Count));
            for (int i = 0; i < found.Count && i < maxCount; i++) result.Add(found[i].cell);
            return result;
        }

        private void SelectAtPoint(Vector2 screenPos)
        {
            Vector3 world = _camera.ScreenToWorldPoint(
                new Vector3(screenPos.x, screenPos.y, -_camera.transform.position.z));
            Goblin best = null;
            float bestDist = _clickPickRadius;
            foreach (var g in Goblin.All)
            {
                if (g == null) continue;
                float d = Vector2.Distance(g.transform.position, world);
                if (d < bestDist) { bestDist = d; best = g; }
            }
            SetSelection(best != null ? new List<Goblin> { best } : new List<Goblin>());
        }

        private void SelectInBox(Vector2 a, Vector2 b)
        {
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
            float minY = Mathf.Min(a.y, b.y), maxY = Mathf.Max(a.y, b.y);
            var hit = new List<Goblin>();
            foreach (var g in Goblin.All)
            {
                if (g == null) continue;
                Vector3 sp = _camera.WorldToScreenPoint(g.transform.position);
                if (sp.x >= minX && sp.x <= maxX && sp.y >= minY && sp.y <= maxY)
                    hit.Add(g);
            }
            SetSelection(hit);
        }

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

        private void CommandFormation(Vector3 worldCenter)
        {
            int n = _selected.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
            int rows = Mathf.CeilToInt((float)n / cols);
            for (int i = 0; i < n; i++)
            {
                int col = i % cols;
                int row = i / cols;
                Vector3 offset = new(
                    (col - (cols - 1) * 0.5f) * _formationSpacing,
                    (row - (rows - 1) * 0.5f) * _formationSpacing, 0);
                _selected[i].SetMoveCommand(worldCenter + offset);
            }
        }

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
                float d = (g.transform.position - worldPos).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; target = g; }
            }
            return target != null;
        }

        private void CommandAttack(Goblin target)
        {
            foreach (var g in _selected)
                if (g != null && g.AttackDamage > 0)
                    g.SetAttackCommand(target);
        }

        private static bool IsOverUI()
        {
            return EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject();
        }

        // Marquee box drawing
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
            // OnGUI uses top-left origin; InputSystem mouse uses bottom-left
            float aX = _dragStartScreen.x, bX = cur.x;
            float aY = Screen.height - _dragStartScreen.y, bY = Screen.height - cur.y;
            Rect r = Rect.MinMaxRect(Mathf.Min(aX, bX), Mathf.Min(aY, bY),
                                      Mathf.Max(aX, bX), Mathf.Max(aY, bY));
            GUI.DrawTexture(r, BoxTexture());
        }
    }
}

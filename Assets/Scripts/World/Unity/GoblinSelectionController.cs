using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RTSCL.World.Unity
{
    public sealed class GoblinSelectionController : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private BuildingPlacer _buildingPlacer;
        [SerializeField] private float _clickPickRadius = 0.6f;       // world units
        [SerializeField] private float _dragThresholdPx = 6f;
        [SerializeField] private float _formationSpacing = 1.0f;

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

            // Right mouse: command selected goblins to move
            if (Mouse.current.rightButton.wasPressedThisFrame
                && _selected.Count > 0 && !IsOverUI())
            {
                Vector2 mp = Mouse.current.position.ReadValue();
                Vector3 worldTarget = _camera.ScreenToWorldPoint(
                    new Vector3(mp.x, mp.y, -_camera.transform.position.z));
                CommandFormation(worldTarget);
            }
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
                _selected[i].SetCommand(worldCenter + offset);
            }
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

// RTSCamera2D.cs  (MonoBehaviour — RTSCL.World.Unity)
// Full RTS camera controller for a 2D orthographic camera. Supports:
//   - WASD + arrow keys keyboard pan
//   - Screen-edge pan (disable _enableEdgePan if it conflicts with UI)
//   - Middle-mouse drag pan
//   - Scroll-wheel zoom (clamped between _minOrthoSize and a map-size-derived max)
//   - Hard world-bounds clamp so the view never leaves the generated map
// Where to adjust:
//   - Pan speed: _panSpeed (Inspector). Speed scales with ortho size so it feels
//     consistent at different zoom levels.
//   - Edge pan sensitivity: _edgePanThreshold (pixels from screen edge).
//   - Zoom steps: _zoomStep. Min zoom: _minOrthoSize. Max: auto-computed from map
//     size multiplied by _maxOrthoExtra (1.2 = 20% extra beyond full-map view).
//   - Initial view: call FocusOn() after world generation; user interaction
//     re-enables clamping automatically.

using UnityEngine;
using UnityEngine.InputSystem;

namespace RTSCL.World.Unity
{
    /// <summary>RTS-style pan/zoom camera. Attach to the Camera GameObject.
    /// Relies on the New Input System (UnityEngine.InputSystem) — legacy Input is disabled.</summary>
    public sealed class RTSCamera2D : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Camera _camera;

        [Header("World Bounds (world units)")]
        [SerializeField] private float _mapWidth = 128f;
        [SerializeField] private float _mapHeight = 128f;

        [Header("Pan")]
        [SerializeField] private float _panSpeed = 30f;
        [SerializeField] private float _edgePanThreshold = 20f;
        [SerializeField] private bool _enableEdgePan = true;
        [SerializeField] private bool _enableWasd = true;
        [SerializeField] private bool _enableMiddleMouseDrag = true;

        [Header("Zoom")]
        [SerializeField] private float _minOrthoSize = 5f;
        [SerializeField] private float _maxOrthoExtra = 1.2f;  // multiplier on top of full-map ortho size
        [SerializeField] private float _zoomStep = 3f;

        private bool _dragging;
        private Vector2 _dragStartMouse;
        private Vector3 _dragStartCamera;
        // False until the user pans or zooms, so FocusOn() initial placement is
        // not overridden by ClampPosition() on the first frame.
        private bool _userInteracted;

        // Auto-assign _camera if component is on the same GameObject.
        private void Reset() => _camera = GetComponent<Camera>();

        /// <summary>Called by the world generator / bootstrap to update the clamp rectangle
        /// whenever a new map is loaded.</summary>
        public void SetWorldBounds(float width, float height)
        {
            _mapWidth = width;
            _mapHeight = height;
        }

        /// <summary>Programmatically center camera on a world point — bypasses edge-clamp until user pans/zooms.</summary>
        public void FocusOn(Vector3 worldPos, float orthoSize)
        {
            if (_camera == null) return;
            _camera.orthographic = true;
            _camera.orthographicSize = orthoSize;
            _camera.transform.position = new Vector3(worldPos.x, worldPos.y, -10f);
            _userInteracted = false;
        }

        /// <summary>Center the camera on a world XY (keeps z + ortho size) and clamp to map bounds.
        /// Used by the minimap for click-to-navigate.</summary>
        public void JumpTo(Vector2 worldXY)
        {
            if (_camera == null) return;
            _camera.transform.position = new Vector3(worldXY.x, worldXY.y, -10f);
            _userInteracted = true;
            ClampPosition();
        }

        private void LateUpdate()
        {
            if (_camera == null || !_camera.orthographic) return;
            // Suppress keyboard pan when a UI input field has focus.
            bool typing = GUIUtility.keyboardControl != 0;
            HandleZoom();
            HandlePan(typing);
            // Always clamp ortho size (handles window resize), but only clamp
            // position once the user has interacted — keeps initial focus exact
            // even when spawn is near a map edge.
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize, _minOrthoSize, MaxOrtho());
            if (_userInteracted) ClampPosition();
        }

        /// <summary>Handles scroll-wheel zoom by stepping orthographicSize up or down.</summary>
        private void HandleZoom()
        {
            if (Mouse.current == null) return;
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;
            _camera.orthographicSize -= Mathf.Sign(scroll) * _zoomStep;
            _userInteracted = true;
        }

        /// <summary>Returns the maximum allowed ortho size derived from map dimensions and
        /// the _maxOrthoExtra multiplier. Keeps the camera from zooming out past the map.</summary>
        private float MaxOrtho()
        {
            float aspect = Mathf.Max(_camera.aspect, 0.01f);
            return Mathf.Max(_mapWidth / aspect, _mapHeight) * 0.5f * _maxOrthoExtra;
        }

        /// <summary>Handles all pan input: middle-mouse drag, WASD/arrow keys, and screen-edge pan.
        /// Middle-mouse drag takes priority and returns early if active.</summary>
        private void HandlePan(bool typing)
        {
            if (_enableMiddleMouseDrag && !typing && Mouse.current != null)
            {
                var middle = Mouse.current.middleButton;
                if (middle.wasPressedThisFrame)
                {
                    _dragging = true;
                    _dragStartMouse = Mouse.current.position.ReadValue();
                    _dragStartCamera = _camera.transform.position;
                    _userInteracted = true;
                }
                else if (!middle.isPressed)
                {
                    _dragging = false;
                }
                if (_dragging)
                {
                    Vector2 cur = Mouse.current.position.ReadValue();
                    Vector2 d = cur - _dragStartMouse;
                    // Convert pixel delta to world units: orthoSize*2 spans the full screen height.
                    float worldPerPx = _camera.orthographicSize * 2f / Mathf.Max(Screen.height, 1);
                    _camera.transform.position = _dragStartCamera
                                                 + new Vector3(-d.x, -d.y, 0) * worldPerPx;
                    return;
                }
            }

            Vector2 delta = Vector2.zero;

            if (_enableWasd && !typing && Keyboard.current != null)
            {
                var kb = Keyboard.current;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  delta.x -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) delta.x += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  delta.y -= 1;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    delta.y += 1;
            }

            if (_enableEdgePan && Mouse.current != null)
            {
                Vector2 mp = Mouse.current.position.ReadValue();
                // Only trigger edge-pan when the cursor is actually inside the window.
                if (mp.x >= 0 && mp.x <= Screen.width && mp.y >= 0 && mp.y <= Screen.height)
                {
                    if (mp.x < _edgePanThreshold)                  delta.x -= 1;
                    if (mp.x > Screen.width - _edgePanThreshold)   delta.x += 1;
                    if (mp.y < _edgePanThreshold)                  delta.y -= 1;
                    if (mp.y > Screen.height - _edgePanThreshold)  delta.y += 1;
                }
            }

            if (delta.sqrMagnitude > 0.01f)
            {
                delta = delta.normalized;
                // Scale pan speed by current zoom level so it feels consistent.
                float speed = _panSpeed * (_camera.orthographicSize / 20f);
                _camera.transform.position += new Vector3(delta.x, delta.y, 0)
                                              * speed * Time.unscaledDeltaTime;
                _userInteracted = true;
            }
        }

        /// <summary>Clamps the camera position so the viewport edges never leave the map rectangle.
        /// If the map is smaller than the viewport, centres the camera instead of clamping.</summary>
        private void ClampPosition()
        {
            float halfH = _camera.orthographicSize;
            float halfW = halfH * Mathf.Max(_camera.aspect, 0.01f);

            Vector3 p = _camera.transform.position;
            p.x = (_mapWidth > halfW * 2)
                ? Mathf.Clamp(p.x, halfW, _mapWidth - halfW)
                : _mapWidth * 0.5f;
            p.y = (_mapHeight > halfH * 2)
                ? Mathf.Clamp(p.y, halfH, _mapHeight - halfH)
                : _mapHeight * 0.5f;
            p.z = -10f;
            _camera.transform.position = p;
        }
    }
}

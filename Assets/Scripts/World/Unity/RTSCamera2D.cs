using UnityEngine;
using UnityEngine.InputSystem;

namespace RTSCL.World.Unity
{
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
        [SerializeField] private float _maxOrthoExtra = 1.2f;
        [SerializeField] private float _zoomStep = 3f;

        private bool _dragging;
        private Vector2 _dragStartMouse;
        private Vector3 _dragStartCamera;

        private void Reset() => _camera = GetComponent<Camera>();

        public void SetWorldBounds(float width, float height)
        {
            _mapWidth = width;
            _mapHeight = height;
        }

        private void LateUpdate()
        {
            if (_camera == null || !_camera.orthographic) return;
            bool typing = GUIUtility.keyboardControl != 0;
            HandleZoom();
            HandlePan(typing);
            ClampCamera();
        }

        private void HandleZoom()
        {
            if (Mouse.current == null) return;
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;
            _camera.orthographicSize -= Mathf.Sign(scroll) * _zoomStep;
        }

        private float MaxOrtho()
        {
            float aspect = Mathf.Max(_camera.aspect, 0.01f);
            return Mathf.Max(_mapWidth / aspect, _mapHeight) * 0.5f * _maxOrthoExtra;
        }

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
                }
                else if (!middle.isPressed)
                {
                    _dragging = false;
                }
                if (_dragging)
                {
                    Vector2 cur = Mouse.current.position.ReadValue();
                    Vector2 d = cur - _dragStartMouse;
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
                float speed = _panSpeed * (_camera.orthographicSize / 20f);
                _camera.transform.position += new Vector3(delta.x, delta.y, 0)
                                              * speed * Time.unscaledDeltaTime;
            }
        }

        private void ClampCamera()
        {
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize, _minOrthoSize, MaxOrtho());
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

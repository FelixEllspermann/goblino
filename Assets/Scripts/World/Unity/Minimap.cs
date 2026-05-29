using System.Collections.Generic;
using RTSCL.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    /// <summary>Corner minimap: static biome terrain texture, per-frame fog overlay,
    /// pooled unit/building dots (enemy dots gated by FoW), camera viewport frame,
    /// and click/drag-to-navigate.</summary>
    public sealed class Minimap : MonoBehaviour, IPointerClickHandler, IDragHandler
    {
        [Header("Sources")]
        [SerializeField] private WorldGeneratorBootstrap _worldSource;
        [SerializeField] private FogOfWar _fogOfWar;
        [SerializeField] private BuildingPlacer _buildingPlacer;
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private RTSCamera2D _cameraRig;

        [Header("UI")]
        [SerializeField] private RawImage _terrainImage;
        [SerializeField] private RawImage _fogImage;
        [SerializeField] private RectTransform _minimapRect;
        [SerializeField] private RectTransform _dotsRoot;
        [SerializeField] private RectTransform _viewportFrame;

        [Header("Dot Style")]
        [SerializeField] private float _unitDotSize = 3f;
        [SerializeField] private float _buildingDotSize = 5f;

        private static readonly Color32[] _biomeColors =
        {
            new Color32(30, 55, 120, 255),   // DeepWater
            new Color32(90, 130, 180, 255),  // Shore
            new Color32(110, 110, 115, 255), // Cliff
            new Color32(235, 240, 245, 255), // Snow
            new Color32(210, 195, 120, 255), // Desert
            new Color32(90, 200, 190, 255),  // TropicalCoast
            new Color32(40, 95, 50, 255),    // Forest
            new Color32(95, 165, 75, 255),   // Grassland
            new Color32(140, 150, 80, 255),  // DryGrass
        };

        private WorldData _knownWorld;
        private int _w, _h;
        private Texture2D _terrainTex;
        private Texture2D _fogTex;
        private Color32[] _fogPixels;
        private FogOfWar.Visibility[,] _prevFog;

        private readonly List<Image> _dotPool = new();
        private int _dotCursor;
        private Sprite _dotSprite;
        private readonly HashSet<Vector2Int> _drawnOrigins = new();

        private void Update()
        {
            var world = _worldSource != null ? _worldSource.CurrentWorld : null;
            if (world != null && world != _knownWorld)
            {
                _knownWorld = world;
                _w = world.Width;
                _h = world.Height;
                BakeTerrain(world);
                InitFog();
            }
            if (_knownWorld == null) return;

            UpdateFog();
            UpdateDots();
            UpdateViewport();
        }

        // ---------- Terrain ----------

        private void BakeTerrain(WorldData world)
        {
            _terrainTex = new Texture2D(_w, _h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[_w * _h];
            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                int b = (int)world.BiomeAt(x, y);
                px[y * _w + x] = (b >= 0 && b < _biomeColors.Length) ? _biomeColors[b] : new Color32(0, 0, 0, 255);
            }
            _terrainTex.SetPixels32(px);
            _terrainTex.Apply();
            if (_terrainImage != null) _terrainImage.texture = _terrainTex;
        }

        // ---------- Fog ----------

        private void InitFog()
        {
            _fogTex = new Texture2D(_w, _h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            _fogPixels = new Color32[_w * _h];
            for (int i = 0; i < _fogPixels.Length; i++) _fogPixels[i] = new Color32(0, 0, 0, 255);
            _fogTex.SetPixels32(_fogPixels);
            _fogTex.Apply();
            if (_fogImage != null) _fogImage.texture = _fogTex;

            _prevFog = new FogOfWar.Visibility[_w, _h];
            for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
                _prevFog[x, y] = (FogOfWar.Visibility)255; // sentinel forces first paint
        }

        private void UpdateFog()
        {
            if (_fogTex == null || _fogOfWar == null) return;
            bool dirty = false;
            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                var v = _fogOfWar.GetVisibility(x, y);
                if (v == _prevFog[x, y]) continue;
                _prevFog[x, y] = v;
                _fogPixels[y * _w + x] = v switch
                {
                    FogOfWar.Visibility.Hidden   => new Color32(0, 0, 0, 255),
                    FogOfWar.Visibility.Explored => new Color32(0, 0, 0, 140),
                    _                            => new Color32(0, 0, 0, 0),
                };
                dirty = true;
            }
            if (dirty) { _fogTex.SetPixels32(_fogPixels); _fogTex.Apply(); }
        }

        // ---------- Dots ----------

        private void UpdateDots()
        {
            _dotCursor = 0;
            _drawnOrigins.Clear();
            ulong local = WorldStartContext.LocalPlayer;

            foreach (var g in Goblin.All)
            {
                if (g == null) continue;
                bool isLocal = g.Owner == local || g.Owner == 0UL;
                if (!isLocal && VisAt(CellOf(g.transform.position)) != FogOfWar.Visibility.Visible)
                    continue;
                Color col = isLocal ? Color.white : WorldStartContext.GetPlayerColor(g.Owner);
                PlaceDot(g.transform.position.x, g.transform.position.y, _unitDotSize, col);
            }

            if (_buildingPlacer != null)
            {
                foreach (var kv in _buildingPlacer.AllOccupied)
                {
                    var cell = kv.Key;
                    if (!_buildingPlacer.TryGetBuildingOrigin(cell, out var origin)) origin = cell;
                    if (!_drawnOrigins.Add(origin)) continue;
                    ulong owner = _buildingPlacer.TryGetBuildingOwner(cell, out var o) ? o : 0UL;
                    bool isLocal = owner == local || owner == 0UL;
                    if (!isLocal && VisAt(origin) != FogOfWar.Visibility.Visible)
                        continue;
                    Color col = isLocal ? Color.white : WorldStartContext.GetPlayerColor(owner);
                    PlaceDot(origin.x + 0.5f, origin.y + 0.5f, _buildingDotSize, col);
                }
            }

            for (int i = _dotCursor; i < _dotPool.Count; i++)
                if (_dotPool[i].gameObject.activeSelf) _dotPool[i].gameObject.SetActive(false);
        }

        private FogOfWar.Visibility VisAt(Vector2Int c) =>
            _fogOfWar != null ? _fogOfWar.GetVisibility(c.x, c.y) : FogOfWar.Visibility.Visible;

        private static Vector2Int CellOf(Vector3 world) =>
            new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

        private void PlaceDot(float wx, float wy, float size, Color col)
        {
            Image dot = _dotCursor < _dotPool.Count ? _dotPool[_dotCursor] : CreateDot();
            _dotCursor++;
            if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
            dot.color = col;
            var rt = (RectTransform)dot.transform;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = WorldToMinimap(wx, wy);
        }

        private Image CreateDot()
        {
            var go = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_dotsRoot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.sprite = DotSprite();
            img.raycastTarget = false;
            _dotPool.Add(img);
            return img;
        }

        private Sprite DotSprite()
        {
            if (_dotSprite != null) return _dotSprite;
            var tex = Texture2D.whiteTexture;
            _dotSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), tex.width);
            return _dotSprite;
        }

        // ---------- Viewport frame ----------

        private void UpdateViewport()
        {
            if (_viewportFrame == null || _mainCamera == null) return;
            float halfH = _mainCamera.orthographicSize;
            float halfW = halfH * Mathf.Max(_mainCamera.aspect, 0.01f);
            Vector3 c = _mainCamera.transform.position;
            Vector2 minCorner = WorldToMinimap(c.x - halfW, c.y - halfH);
            Vector2 maxCorner = WorldToMinimap(c.x + halfW, c.y + halfH);
            _viewportFrame.anchoredPosition = minCorner;
            _viewportFrame.sizeDelta = maxCorner - minCorner;
        }

        // ---------- Coordinate mapping ----------

        private Vector2 WorldToMinimap(float wx, float wy)
        {
            if (_minimapRect == null || _w == 0 || _h == 0) return Vector2.zero;
            var r = _minimapRect.rect;
            return new Vector2(wx / _w * r.width, wy / _h * r.height);
        }

        private Vector2 MinimapToWorld(float lx, float ly)
        {
            var r = _minimapRect.rect;
            return new Vector2(lx / r.width * _w, ly / r.height * _h);
        }

        // ---------- Click / drag navigation ----------

        public void OnPointerClick(PointerEventData e) => NavigateTo(e);
        public void OnDrag(PointerEventData e) => NavigateTo(e);

        private void NavigateTo(PointerEventData e)
        {
            if (_minimapRect == null || _cameraRig == null || _knownWorld == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _minimapRect, e.position, e.pressEventCamera, out var local))
                return;
            var rect = _minimapRect.rect;
            float lx = local.x - rect.xMin;
            float ly = local.y - rect.yMin;
            _cameraRig.JumpTo(MinimapToWorld(lx, ly));
        }
    }
}

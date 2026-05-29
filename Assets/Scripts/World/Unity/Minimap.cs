// Minimap.cs  (MonoBehaviour — RTSCL.World.Unity)
// Corner minimap panel. Composed of three stacked RawImages (terrain / fog / dots)
// plus a viewport rectangle frame, all driven in Update().
//
// Layers (bottom → top):
//   1. _terrainImage  — baked once per world from _biomeColors (Texture2D, FilterMode.Point)
//   2. _fogImage      — updated per-frame only for changed cells (dirty-flag approach)
//   3. _dotsRoot      — pooled UI Image dots for units and buildings
//   4. _viewportFrame — RectTransform resized each frame to match the main camera frustum
//
// Where to adjust:
//   - Biome colours: _biomeColors array (order must match Biome enum integer values).
//   - Dot sizes: _unitDotSize / _buildingDotSize (Inspector).
//   - Fog opacity: FogOfWar.Visibility switch in UpdateFog() — Hidden=255α, Explored=140α, Visible=0α.
//   - Click-to-navigate: delegates to RTSCamera2D.JumpTo().

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

        // Indexed by (int)Biome — must stay in sync with the Biome enum order.
        // Adjust colours here to tweak how each biome appears on the minimap.
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
            // Re-bake terrain and fog whenever a new world is generated.
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

        /// <summary>Bakes a static pixel-per-cell texture from biome data. Called once per world.
        /// FilterMode.Point keeps pixels sharp at minimap scale.</summary>
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

        /// <summary>Allocates the fog texture and _prevFog sentinel array.
        /// Sentinel value 255 guarantees every cell is repainted on the first UpdateFog call.</summary>
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

        /// <summary>Uploads only changed fog cells each frame to avoid a full texture upload.
        /// Alpha values: Hidden=255 (opaque black), Explored=140 (semi), Visible=0 (clear).</summary>
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
            // Only upload to GPU when something changed.
            if (dirty) { _fogTex.SetPixels32(_fogPixels); _fogTex.Apply(); }
        }

        // ---------- Dots ----------

        /// <summary>Reuses a pooled Image dot list (grow-only, disabled when not needed).
        /// Enemy units and buildings are hidden when their cell is not Visible in FoW.
        /// _drawnOrigins deduplicates multi-cell buildings to a single dot.</summary>
        private void UpdateDots()
        {
            _dotCursor = 0;
            _drawnOrigins.Clear();
            ulong local = WorldStartContext.LocalPlayer;

            foreach (var g in Goblin.All)
            {
                if (g == null) continue;
                // Neutral monsters are never "local" — they only show when currently in vision.
                bool isLocal = !g.IsNeutral && (g.Owner == local || g.Owner == 0UL);
                // Hide enemy units AND neutral monsters when their cell isn't currently visible.
                if (!isLocal && VisAt(CellOf(g.transform.position)) != FogOfWar.Visibility.Visible)
                    continue;
                Color col = isLocal ? Color.white
                    : g.IsNeutral ? new Color(0.9f, 0.4f, 0.2f)   // neutral monster dot
                    : WorldStartContext.GetPlayerColor(g.Owner);
                PlaceDot(g.transform.position.x, g.transform.position.y, _unitDotSize, col);
            }

            if (_buildingPlacer != null)
            {
                foreach (var kv in _buildingPlacer.AllOccupied)
                {
                    var cell = kv.Key;
                    // Resolve any interior cell to the building's origin so multi-cell
                    // footprints only draw a single dot.
                    if (!_buildingPlacer.TryGetBuildingOrigin(cell, out var origin)) origin = cell;
                    if (!_drawnOrigins.Add(origin)) continue;
                    ulong owner = _buildingPlacer.TryGetBuildingOwner(cell, out var o) ? o : 0UL;
                    bool isLocal = owner == local || owner == 0UL;
                    if (!isLocal && VisAt(origin) != FogOfWar.Visibility.Visible)
                        continue;
                    Color col = isLocal ? Color.white : WorldStartContext.GetPlayerColor(owner);
                    // +0.5 centres the dot on the cell.
                    PlaceDot(origin.x + 0.5f, origin.y + 0.5f, _buildingDotSize, col);
                }
            }

            // Hide excess pool entries that were active last frame.
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

        /// <summary>Allocates a new pooled dot Image with bottom-left anchoring so
        /// anchoredPosition directly maps to minimap-local pixel coordinates.</summary>
        private Image CreateDot()
        {
            var go = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_dotsRoot, false);
            var rt = (RectTransform)go.transform;
            // Anchor at bottom-left corner so anchoredPosition == minimap pixel offset.
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.sprite = DotSprite();
            img.raycastTarget = false;
            _dotPool.Add(img);
            return img;
        }

        /// <summary>Lazily creates a 1×1 white square sprite shared by all dots.
        /// In this project builtin UI sprites return null, so we bake our own.</summary>
        private Sprite DotSprite()
        {
            if (_dotSprite != null) return _dotSprite;
            var tex = Texture2D.whiteTexture;
            _dotSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), tex.width);
            return _dotSprite;
        }

        // ---------- Viewport frame ----------

        /// <summary>Repositions and resizes _viewportFrame to match the main camera frustum
        /// in minimap-local coordinates (updated every frame).</summary>
        private void UpdateViewport()
        {
            if (_viewportFrame == null || _mainCamera == null) return;
            float halfH = _mainCamera.orthographicSize;
            float halfW = halfH * Mathf.Max(_mainCamera.aspect, 0.01f);
            Vector3 c = _mainCamera.transform.position;
            // Map the camera's world-space corners to minimap UI coordinates.
            Vector2 minCorner = WorldToMinimap(c.x - halfW, c.y - halfH);
            Vector2 maxCorner = WorldToMinimap(c.x + halfW, c.y + halfH);
            _viewportFrame.anchoredPosition = minCorner;
            _viewportFrame.sizeDelta = maxCorner - minCorner;
        }

        // ---------- Coordinate mapping ----------

        /// <summary>Converts a world-space position to a minimap RectTransform local offset
        /// (bottom-left origin, matching the dot anchor convention).</summary>
        private Vector2 WorldToMinimap(float wx, float wy)
        {
            if (_minimapRect == null || _w == 0 || _h == 0) return Vector2.zero;
            var r = _minimapRect.rect;
            return new Vector2(wx / _w * r.width, wy / _h * r.height);
        }

        /// <summary>Inverse of WorldToMinimap — converts a minimap-local pixel offset
        /// back to world XY for click-to-navigate.</summary>
        private Vector2 MinimapToWorld(float lx, float ly)
        {
            var r = _minimapRect.rect;
            return new Vector2(lx / r.width * _w, ly / r.height * _h);
        }

        // ---------- Click / drag navigation ----------

        // Both click and drag call the same NavigateTo so the user can hold and scrub.
        public void OnPointerClick(PointerEventData e) => NavigateTo(e);
        public void OnDrag(PointerEventData e) => NavigateTo(e);

        /// <summary>Converts the pointer screen position to a minimap-local coordinate,
        /// then translates to world XY and tells the camera rig to jump there.</summary>
        private void NavigateTo(PointerEventData e)
        {
            if (_minimapRect == null || _cameraRig == null || _knownWorld == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _minimapRect, e.position, e.pressEventCamera, out var local))
                return;
            var rect = _minimapRect.rect;
            // rect.xMin/yMin converts from RectTransform centre-origin to bottom-left origin.
            float lx = local.x - rect.xMin;
            float ly = local.y - rect.yMin;
            _cameraRig.JumpTo(MinimapToWorld(lx, ly));
        }
    }
}

// FogOfWar.cs  (MonoBehaviour — RTSCL.World.Unity)
// Drives a dedicated "fog" Tilemap layer that overlays the terrain.
// Mechanism: one black tile per cell, per-cell color overridden each frame via
// Tilemap.SetColor (requires TileFlags.None on every cell — set in ResetForWorld).
//
// Visibility states:
//   Hidden   — never seen, fully opaque black (_hiddenTint   alpha 1.0)
//   Explored — seen at some point, semi-transparent (_exploredTint alpha 0.55)
//   Visible  — currently in LOS of a friendly unit/building (transparent, _visibleTint alpha 0.0)
//
// Each frame: all Visible cells are demoted to Explored, then re-marked from
// current unit/building positions. Only changed cells call SetColor.
//
// Where to adjust:
//   - Vision radii: _goblinRadius / _keepRadius / _buildingRadius (Inspector).
//   - Fog colours: _hiddenTint / _exploredTint / _visibleTint (Inspector).
//   - Keep detection: checked by def.name.StartsWith("Keep") — rename if the asset changes.
//   - Sorting: fog Tilemap's Order in Layer must sit above terrain but below units.

using UnityEngine;
using UnityEngine.Tilemaps;
using RTSCL.World;

namespace RTSCL.World.Unity
{
    /// <summary>Per-cell visibility fog driven by a Tilemap colour overlay.
    /// Exposes GetVisibility() for the Minimap and other consumers.</summary>
    public sealed class FogOfWar : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Tilemap _fogMap;
        [SerializeField] private WorldGeneratorBootstrap _worldSource;
        [SerializeField] private BuildingPlacer _buildingPlacer;

        [Header("Vision Radii (cells)")]
        [SerializeField] private int _goblinRadius   = 5;
        [SerializeField] private int _keepRadius     = 10;
        [SerializeField] private int _buildingRadius = 4;

        [Header("Tint")]
        [SerializeField] private Color _hiddenTint   = new(0f, 0f, 0f, 1.00f);
        [SerializeField] private Color _exploredTint = new(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color _visibleTint  = new(0f, 0f, 0f, 0.00f);

        /// <summary>Three-state visibility per map cell. Byte-sized for compact 2D array storage.</summary>
        public enum Visibility : byte { Hidden = 0, Explored = 1, Visible = 2 }
        private Visibility[,] _state;
        // _prevState mirrors last-painted values; cells that haven't changed skip SetColor.
        private Visibility[,] _prevState;
        private TileBase _blackTile;
        private WorldData _knownWorld;
        private int _width, _height;

        private void Update()
        {
            var world = _worldSource != null ? _worldSource.CurrentWorld : null;
            // Reinitialise whenever a fresh world is generated.
            if (world != null && world != _knownWorld)
            {
                _knownWorld = world;
                ResetForWorld(world);
            }
            if (_state == null || _fogMap == null) return;

            ComputeVisibility();
            ApplyChangedColors();
        }

        /// <summary>Fills the fog tilemap with black tiles and clears all visibility state
        /// for the new world dimensions. TileFlags.None is required before SetColor works.</summary>
        private void ResetForWorld(WorldData world)
        {
            _width = world.Width;
            _height = world.Height;
            _state = new Visibility[_width, _height];
            _prevState = new Visibility[_width, _height];
            for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
            {
                _state[x, y] = Visibility.Hidden;
                _prevState[x, y] = (Visibility)255;  // sentinel → forces first paint
            }

            EnsureBlackTile();
            _fogMap.ClearAllTiles();

            var positions = new Vector3Int[_width * _height];
            var tiles = new TileBase[_width * _height];
            int i = 0;
            for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                positions[i] = new Vector3Int(x, y, 0);
                tiles[i] = _blackTile;
                i++;
            }
            _fogMap.SetTiles(positions, tiles);

            // Allow per-cell color overrides — Unity requires TileFlags.None for SetColor to work.
            for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
                _fogMap.SetTileFlags(new Vector3Int(x, y, 0), TileFlags.None);
        }

        /// <summary>Recomputes the Visible set each frame.
        /// Strategy: demote all Visible→Explored, then re-mark circles around every
        /// friendly unit and building. Hidden cells can only advance to Explored once seen.</summary>
        private void ComputeVisibility()
        {
            // Demote Visible → Explored before re-marking
            for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
                if (_state[x, y] == Visibility.Visible) _state[x, y] = Visibility.Explored;

            // Goblins — all units in Goblin.All are assumed friendly (local ownership).
            foreach (var g in Goblin.All)
            {
                if (g == null) continue;
                var c = _fogMap.WorldToCell(g.transform.position);
                MarkCircle(c.x, c.y, _goblinRadius);
            }

            // Buildings
            if (_buildingPlacer != null)
            {
                foreach (var kv in _buildingPlacer.AllOccupied)
                {
                    var def = kv.Value;
                    // Keep buildings have a larger radius; all others use _buildingRadius.
                    int r = (def != null && def.name.StartsWith("Keep")) ? _keepRadius : _buildingRadius;
                    MarkCircle(kv.Key.x, kv.Key.y, r);
                }
            }
        }

        /// <summary>Marks all cells within <paramref name="radius"/> of (cx, cy) as Visible.
        /// Uses squared-distance check to avoid sqrt; clamps to map bounds for safety.</summary>
        private void MarkCircle(int cx, int cy, int radius)
        {
            int r2 = radius * radius;
            int xMin = Mathf.Max(0, cx - radius);
            int xMax = Mathf.Min(_width - 1, cx + radius);
            int yMin = Mathf.Max(0, cy - radius);
            int yMax = Mathf.Min(_height - 1, cy + radius);
            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= r2)
                    _state[x, y] = Visibility.Visible;
            }
        }

        /// <summary>Calls Tilemap.SetColor only for cells whose visibility changed since last frame,
        /// avoiding a full tilemap repaint each update.</summary>
        private void ApplyChangedColors()
        {
            for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                if (_state[x, y] == _prevState[x, y]) continue;
                _prevState[x, y] = _state[x, y];
                Color c = _state[x, y] switch
                {
                    Visibility.Hidden   => _hiddenTint,
                    Visibility.Explored => _exploredTint,
                    Visibility.Visible  => _visibleTint,
                    _                   => _hiddenTint,
                };
                _fogMap.SetColor(new Vector3Int(x, y, 0), c);
            }
        }

        /// <summary>Per-cell visibility for external consumers (e.g. the minimap).
        /// Returns Hidden for out-of-range cells or before the world is initialized.</summary>
        public Visibility GetVisibility(int x, int y)
        {
            if (_state == null || x < 0 || y < 0 || x >= _width || y >= _height)
                return Visibility.Hidden;
            return _state[x, y];
        }

        /// <summary>Lazily creates a 16×16 black tile at runtime (no asset dependency).
        /// Shared across all fog cells to keep the tile atlas minimal.</summary>
        private void EnsureBlackTile()
        {
            if (_blackTile != null) return;
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[16 * 16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 255);
            tex.SetPixels32(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            _blackTile = tile;
        }
    }
}

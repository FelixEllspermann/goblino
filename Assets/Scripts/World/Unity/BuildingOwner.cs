// BuildingOwner.cs — MonoBehaviour attached to every placed building by BuildingPlacer.PlaceForce.
// Responsibilities:
//   - Stores the SteamID (ulong) of the player who owns this building.
//   - Applies faction tint: local player = white, enemy players = their faction color from WorldStartContext.
//   - Manages the selection ring child object (shown/hidden by BuildingPaletteUI or ObjectInspector).
//
// To adjust faction colors: edit PlayerRegistry / WorldStartContext.GetPlayerColor (set by GameStartLoader).
// To adjust ring size/opacity: see BuildRing() and the sortingOrder constant (currently 14, below buildings at 15).
// To adjust tint alpha: modify the 0.35f constant passed to WithAlpha in SetOwner.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-building ownership + visuals (sprite tint + selection ring).
    /// Attached by BuildingPlacer.PlaceForce on each placed building.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingOwner : MonoBehaviour
    {
        /// <summary>SteamID of the owning player. 0 = unowned / solo.</summary>
        public ulong Owner { get; private set; }

        private SpriteRenderer _renderer;
        private GameObject _ring;
        private SpriteRenderer _ringRenderer;

        // Shared 1×1 white texture used as the selection ring sprite (runtime-generated once).
        private static Sprite s_sprite;

        /// <summary>Current tint applied to this building's SpriteRenderer.
        /// White for local/unowned; faction color for enemy buildings.</summary>
        public Color CurrentTint { get; private set; } = Color.white;

        /// <summary>Called once by BuildingPlacer.PlaceForce immediately after instantiation.
        /// Grabs SpriteRenderer, creates the selection ring child, then delegates to SetOwner.</summary>
        /// <param name="owner">SteamID of the placing player; 0 in solo.</param>
        /// <param name="footprint">Building size in cells, used to center and scale the ring.</param>
        public void Initialize(ulong owner, Vector2Int footprint)
        {
            _renderer = GetComponent<SpriteRenderer>();
            BuildRing(footprint);
            SetOwner(owner);
            SetSelected(false);
        }

        /// <summary>Updates ownership and re-applies the faction tint.
        /// Local player (or unowned in solo) → white. Any other SteamID → faction color via WorldStartContext.</summary>
        /// <param name="ownerSteamId">SteamID as ulong. 0UL = treat as local/unowned.</param>
        public void SetOwner(ulong ownerSteamId)
        {
            Owner = ownerSteamId;
            // Owner == 0 covers solo play where SteamID is not set.
            bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
            CurrentTint = isLocal ? Color.white : WorldStartContext.GetPlayerColor(Owner);
            if (_renderer != null) _renderer.color = CurrentTint;
            // Ring tint matches faction color but is semi-transparent.
            if (_ringRenderer != null) _ringRenderer.color = WithAlpha(CurrentTint, 0.35f);
        }

        /// <summary>Shows or hides the selection ring child GameObject.
        /// Called by BuildingPaletteUI / ObjectInspector when the player selects or deselects this building.</summary>
        public void SetSelected(bool selected)
        {
            if (_ring != null) _ring.SetActive(selected);
        }

        /// <summary>Creates the selection ring as a child GameObject with a runtime-generated 1×1 white sprite,
        /// scaled to cover the building footprint with a small padding border.</summary>
        private void BuildRing(Vector2Int footprint)
        {
            if (s_sprite == null) s_sprite = BuildSquareSprite();
            _ring = new GameObject("SelectionRing");
            _ring.transform.SetParent(transform, false);
            // Center the ring over the footprint (footprint cells start at local origin).
            _ring.transform.localPosition = new Vector3(footprint.x * 0.5f, footprint.y * 0.5f, 0f);
            _ringRenderer = _ring.AddComponent<SpriteRenderer>();
            _ringRenderer.sprite = s_sprite;
            _ringRenderer.sortingOrder = 14;  // below buildings (15)
            _ringRenderer.color = new Color(1, 1, 1, 0.35f);
            // +0.4 adds a small border around the building footprint.
            _ring.transform.localScale = new Vector3(footprint.x + 0.4f, footprint.y + 0.4f, 1f);
        }

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

        /// <summary>Generates a 1×1 white RGBA32 texture and wraps it in a Sprite.
        /// Created once and cached in s_sprite for all BuildingOwner instances.</summary>
        private static Sprite BuildSquareSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}

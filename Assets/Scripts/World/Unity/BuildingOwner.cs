using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-building ownership + visuals (sprite tint + selection ring).
    /// Attached by BuildingPlacer.PlaceForce on each placed building.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingOwner : MonoBehaviour
    {
        public ulong Owner { get; private set; }

        private SpriteRenderer _renderer;
        private GameObject _ring;
        private SpriteRenderer _ringRenderer;
        private static Sprite s_sprite;

        public Color CurrentTint { get; private set; } = Color.white;

        public void Initialize(ulong owner, Vector2Int footprint)
        {
            _renderer = GetComponent<SpriteRenderer>();
            BuildRing(footprint);
            SetOwner(owner);
            SetSelected(false);
        }

        public void SetOwner(ulong ownerSteamId)
        {
            Owner = ownerSteamId;
            bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
            CurrentTint = isLocal ? Color.white : WorldStartContext.GetPlayerColor(Owner);
            if (_renderer != null) _renderer.color = CurrentTint;
            if (_ringRenderer != null) _ringRenderer.color = WithAlpha(CurrentTint, 0.35f);
        }

        public void SetSelected(bool selected)
        {
            if (_ring != null) _ring.SetActive(selected);
        }

        private void BuildRing(Vector2Int footprint)
        {
            if (s_sprite == null) s_sprite = BuildSquareSprite();
            _ring = new GameObject("SelectionRing");
            _ring.transform.SetParent(transform, false);
            _ring.transform.localPosition = new Vector3(footprint.x * 0.5f, footprint.y * 0.5f, 0f);
            _ringRenderer = _ring.AddComponent<SpriteRenderer>();
            _ringRenderer.sprite = s_sprite;
            _ringRenderer.sortingOrder = 14;  // below buildings (15)
            _ringRenderer.color = new Color(1, 1, 1, 0.35f);
            _ring.transform.localScale = new Vector3(footprint.x + 0.4f, footprint.y + 0.4f, 1f);
        }

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

        private static Sprite BuildSquareSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}

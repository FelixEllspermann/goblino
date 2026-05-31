// BuildingHealthBar.cs (MonoBehaviour — RTSCL.World.Unity)
// Floating HP bar attached as a child of a placed building's GameObject. Mirrors GoblinHealthBar
// (world-space SpriteRenderers, not UGUI) but reads its HP from the static BuildingHP store keyed by
// the building's origin cell. Hidden while at full HP; appears once the building takes damage.
//
// Width scales with the footprint so big buildings get a proportionally wider bar. Positioned above
// the footprint's top-center. Self-destructs when its building is removed from BuildingHP.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Floating HP bar above a building, fed by BuildingHP. Hidden at full HP.</summary>
    public sealed class BuildingHealthBar : MonoBehaviour
    {
        private const float BarHeight = 0.14f;   // world units tall
        private const float BorderPad = 0.05f;   // extra size on each side for the border rect
        private const float YOffsetAboveTop = 0.35f;

        private Vector2Int _origin;
        private float _barWidth;
        private SpriteRenderer _border, _bg, _fill;
        private Transform _fillT;
        private static Sprite s_sprite;

        /// <summary>Create a HealthBar child of a building and return it. <paramref name="footprint"/>
        /// sizes + positions the bar; <paramref name="owner"/> tints the border by faction.</summary>
        public static BuildingHealthBar AttachTo(GameObject buildingGo, Vector2Int origin, Vector2Int footprint, ulong owner)
        {
            var go = new GameObject("HealthBar");
            go.transform.SetParent(buildingGo.transform, false);
            // Position above the actual rendered sprite top (works for tall towers + any pivot); fall back
            // to the footprint top if there's no sprite yet.
            var sr = buildingGo.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                // sprite.bounds = the asset's pivot-relative local bounds in world units (correct even
                // before the renderer has drawn a frame). Top-center of the sprite, above its peak.
                var lb = sr.sprite.bounds;
                go.transform.localPosition = new Vector3(lb.center.x, lb.max.y + YOffsetAboveTop, 0f);
            }
            else
            {
                go.transform.localPosition = new Vector3(footprint.x * 0.5f, footprint.y + YOffsetAboveTop, 0f);
            }
            var hb = go.AddComponent<BuildingHealthBar>();
            hb._origin = origin;
            hb._barWidth = Mathf.Max(0.75f, footprint.x * 0.9f);
            hb.Build(owner);
            return hb;
        }

        private void Build(ulong owner)
        {
            if (s_sprite == null) s_sprite = BuildSquareSprite();

            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(transform, false);
            _border = borderGo.AddComponent<SpriteRenderer>();
            _border.sprite = s_sprite;
            _border.sortingOrder = 30;   // above buildings (15) and unit bars (27-29)
            borderGo.transform.localScale = new Vector3(_barWidth + BorderPad, BarHeight + BorderPad, 1f);
            bool isLocal = owner == WorldStartContext.LocalPlayer || owner == 0UL;
            _border.color = isLocal ? Color.white : WorldStartContext.GetPlayerColor(owner);

            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(transform, false);
            _bg = bgGo.AddComponent<SpriteRenderer>();
            _bg.sprite = s_sprite;
            _bg.color = new Color(0.12f, 0.05f, 0.05f, 0.9f);
            _bg.sortingOrder = 31;
            bgGo.transform.localScale = new Vector3(_barWidth, BarHeight, 1f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            _fill = fillGo.AddComponent<SpriteRenderer>();
            _fill.sprite = s_sprite;
            _fill.sortingOrder = 32;
            _fillT = fillGo.transform;
            _fillT.localScale = new Vector3(_barWidth, BarHeight, 1f);
            _fillT.localPosition = new Vector3(-_barWidth * 0.5f, 0f, 0f);
        }

        private void LateUpdate()
        {
            // Building gone from the HP store → it was destroyed; remove our bar too.
            if (!BuildingHP.TryGet(_origin, out int cur, out int max) || max <= 0) { Destroy(gameObject); return; }
            float frac = Mathf.Clamp01((float)cur / max);
            bool show = frac < 1f;     // only visible once damaged
            _border.enabled = show; _bg.enabled = show; _fill.enabled = show;
            if (!show) return;

            _fillT.localScale = new Vector3(_barWidth * frac, BarHeight, 1f);
            _fillT.localPosition = new Vector3(-_barWidth * 0.5f + (_barWidth * frac) * 0.5f, 0f, 0f);
            _fill.color = frac > 0.5f
                ? Color.Lerp(new Color(0.9f, 0.85f, 0.2f), new Color(0.3f, 0.85f, 0.3f), (frac - 0.5f) * 2f)
                : Color.Lerp(new Color(0.85f, 0.2f, 0.2f), new Color(0.9f, 0.85f, 0.2f), frac * 2f);
        }

        private static Sprite BuildSquareSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}

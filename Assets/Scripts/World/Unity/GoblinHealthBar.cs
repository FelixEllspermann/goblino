using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Floating HP bar above a Goblin. Hidden when HP is full and not selected.
    /// Adds a thin faction-colored border behind the bar.</summary>
    public sealed class GoblinHealthBar : MonoBehaviour
    {
        private const float BarWidth = 0.75f;
        private const float BarHeight = 0.10f;
        private const float BorderPad = 0.04f;
        private const float YOffset = 0.85f;

        private Goblin _goblin;
        private SpriteRenderer _border;
        private SpriteRenderer _bg;
        private SpriteRenderer _fill;
        private Transform _fillT;
        private static Sprite s_sprite;

        public static GoblinHealthBar AttachTo(Goblin owner)
        {
            var go = new GameObject("HealthBar");
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = new Vector3(0f, YOffset, 0f);
            var hb = go.AddComponent<GoblinHealthBar>();
            hb._goblin = owner;
            return hb;
        }

        private void Awake()
        {
            if (s_sprite == null) s_sprite = BuildSquareSprite();

            // Owner-colored border (behind bg)
            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(transform, false);
            _border = borderGo.AddComponent<SpriteRenderer>();
            _border.sprite = s_sprite;
            _border.sortingOrder = 27;
            borderGo.transform.localScale = new Vector3(BarWidth + BorderPad, BarHeight + BorderPad, 1f);

            // Dark background
            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(transform, false);
            _bg = bgGo.AddComponent<SpriteRenderer>();
            _bg.sprite = s_sprite;
            _bg.color = new Color(0.12f, 0.05f, 0.05f, 0.9f);
            _bg.sortingOrder = 28;
            bgGo.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);

            // HP fill
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            _fill = fillGo.AddComponent<SpriteRenderer>();
            _fill.sprite = s_sprite;
            _fill.sortingOrder = 29;
            _fillT = fillGo.transform;
            _fillT.localScale = new Vector3(BarWidth, BarHeight, 1f);
            _fillT.localPosition = new Vector3(-BarWidth * 0.5f, 0f, 0f);
        }

        private void Start()
        {
            // Owner color cached at start — owner doesn't change mid-game.
            if (_goblin != null && _border != null)
            {
                bool isLocal = _goblin.Owner == WorldStartContext.LocalPlayer || _goblin.Owner == 0UL;
                Color faction = isLocal ? Color.white : WorldStartContext.GetPlayerColor(_goblin.Owner);
                _border.color = faction;
            }
        }

        private void LateUpdate()
        {
            if (_goblin == null) { Destroy(gameObject); return; }
            float frac = _goblin.MaxHp > 0 ? (float)_goblin.CurrentHp / _goblin.MaxHp : 0f;
            bool show = frac < 1f || _goblin.IsSelected;
            _border.enabled = show;
            _bg.enabled = show;
            _fill.enabled = show;
            if (!show) return;

            _fillT.localScale = new Vector3(BarWidth * frac, BarHeight, 1f);
            _fillT.localPosition = new Vector3(-BarWidth * 0.5f + (BarWidth * frac) * 0.5f, 0f, 0f);

            Color c = frac > 0.5f
                ? Color.Lerp(new Color(0.9f, 0.85f, 0.2f), new Color(0.3f, 0.85f, 0.3f), (frac - 0.5f) * 2f)
                : Color.Lerp(new Color(0.85f, 0.2f, 0.2f), new Color(0.9f, 0.85f, 0.2f), frac * 2f);
            _fill.color = c;
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

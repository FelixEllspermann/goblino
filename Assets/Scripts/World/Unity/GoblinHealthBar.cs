using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Floating HP bar above a Goblin. Hidden when HP is full and not selected.</summary>
    public sealed class GoblinHealthBar : MonoBehaviour
    {
        private const float BarWidth = 0.75f;
        private const float BarHeight = 0.10f;
        private const float YOffset = 0.85f;

        private Goblin _goblin;
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

            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(transform, false);
            _bg = bgGo.AddComponent<SpriteRenderer>();
            _bg.sprite = s_sprite;
            _bg.color = new Color(0.12f, 0.05f, 0.05f, 0.9f);
            _bg.sortingOrder = 28;
            bgGo.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            _fill = fillGo.AddComponent<SpriteRenderer>();
            _fill.sprite = s_sprite;
            _fill.sortingOrder = 29;
            _fillT = fillGo.transform;
            _fillT.localScale = new Vector3(BarWidth, BarHeight, 1f);
            _fillT.localPosition = new Vector3(-BarWidth * 0.5f, 0f, 0f);
        }

        private void LateUpdate()
        {
            if (_goblin == null) { Destroy(gameObject); return; }
            float frac = _goblin.MaxHp > 0 ? (float)_goblin.CurrentHp / _goblin.MaxHp : 0f;
            bool show = frac < 1f || _goblin.IsSelected;
            _bg.enabled = show;
            _fill.enabled = show;
            if (!show) return;

            // Scale fill width and shift so it stays left-anchored
            _fillT.localScale = new Vector3(BarWidth * frac, BarHeight, 1f);
            _fillT.localPosition = new Vector3(-BarWidth * 0.5f + (BarWidth * frac) * 0.5f, 0f, 0f);

            // Green at full, yellow at half, red at low
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

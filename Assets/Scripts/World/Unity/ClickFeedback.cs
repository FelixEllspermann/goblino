using UnityEngine;

namespace RTSCL.World.Unity
{
    public sealed class ClickFeedback : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Color _startColor;
        private float _t;
        private const float Duration = 0.40f;

        public static void Spawn(Vector3 worldPos, Color color)
        {
            var go = new GameObject("ClickFx");
            go.transform.position = worldPos;
            var fx = go.AddComponent<ClickFeedback>();
            fx._renderer = go.AddComponent<SpriteRenderer>();
            fx._renderer.sprite = RingSprite();
            fx._renderer.sortingOrder = 50;
            fx._startColor = color;
            fx._renderer.color = color;
            go.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float n = _t / Duration;
            if (n >= 1f) { Destroy(gameObject); return; }

            // Expand from 0.5 to 2.5 over duration
            float s = Mathf.Lerp(0.5f, 2.5f, n);
            transform.localScale = new Vector3(s, s, 1f);

            // Fade alpha
            var c = _startColor;
            c.a = Mathf.Lerp(_startColor.a, 0f, n);
            _renderer.color = c;
        }

        private static Sprite s_ring;
        private static Sprite RingSprite()
        {
            if (s_ring != null) return s_ring;
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            Vector2 center = new(size / 2f - 0.5f, size / 2f - 0.5f);
            float outerR = size / 2f - 0.5f;
            float innerR = outerR - 2.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                bool ring = d < outerR && d >= innerR;
                pixels[y * size + x] = ring ? new Color32(255, 255, 255, 255)
                                            : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            s_ring = Sprite.Create(tex, new Rect(0, 0, size, size),
                                   new Vector2(0.5f, 0.5f), size);
            return s_ring;
        }
    }
}

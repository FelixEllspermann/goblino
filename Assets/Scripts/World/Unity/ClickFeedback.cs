// ClickFeedback.cs  (MonoBehaviour — RTSCL.World.Unity)
// Spawns a short-lived expanding ring at the clicked world position to confirm orders.
// Self-destructs after Duration seconds. Sorting order 60 places it above fog (≈45).
//
// Where to adjust:
//   - Ring lifetime: Duration constant.
//   - Ring expansion: Lerp(0.5, 2.5, n) in Update.
//   - Sorting layer / order: _renderer.sortingOrder in Spawn().
//   - Ring thickness: innerR = outerR - 2.5f in RingSprite().
//   - Color is passed by the caller (typically white for move, yellow for harvest, etc.).

using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Short-lived expanding ring VFX placed at a clicked world point.
    /// Uses a procedurally-baked ring texture shared across all instances (static s_ring).</summary>
    public sealed class ClickFeedback : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Color _startColor;
        private float _t;
        private const float Duration = 0.40f;

        /// <summary>Creates and launches a ClickFeedback ring at <paramref name="worldPos"/>
        /// with the given tint color. Caller does not need to hold a reference.</summary>
        public static void Spawn(Vector3 worldPos, Color color)
        {
            var go = new GameObject("ClickFx");
            go.transform.position = worldPos;
            var fx = go.AddComponent<ClickFeedback>();
            fx._renderer = go.AddComponent<SpriteRenderer>();
            fx._renderer.sprite = RingSprite();
            fx._renderer.sortingOrder = 60;  // above fog (45)
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

            // Fade alpha to zero as the ring expands
            var c = _startColor;
            c.a = Mathf.Lerp(_startColor.a, 0f, n);
            _renderer.color = c;
        }

        // Static so the 32×32 ring texture is baked only once and reused.
        private static Sprite s_ring;

        /// <summary>Lazily bakes a 32×32 RGBA ring sprite with pixel-exact distance check.
        /// FilterMode.Bilinear gives smooth edges when the ring is scaled up.</summary>
        private static Sprite RingSprite()
        {
            if (s_ring != null) return s_ring;
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            Vector2 center = new(size / 2f - 0.5f, size / 2f - 0.5f);
            float outerR = size / 2f - 0.5f;
            float innerR = outerR - 2.5f;  // ring thickness = 2.5 pixels
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
            // Pivot at (0.5, 0.5) so the ring centres on the world position.
            s_ring = Sprite.Create(tex, new Rect(0, 0, size, size),
                                   new Vector2(0.5f, 0.5f), size);
            return s_ring;
        }
    }
}

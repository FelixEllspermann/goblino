// =============================================================================
// UnitAlert.cs  —  RTSCL.World.Unity
//
// A transient red "!" that wobbles (shakes left/right) above a unit to signal
// "I can't do that" — e.g. a farmer told to harvest a node with no free spot
// and no alternative within range, or a unit given an unreachable move target.
//
// Attaches as a CHILD of the unit so it follows along, auto-destroys after a
// short lifetime, and refreshes (resets its timer) instead of stacking if the
// unit already has one. To trigger it from anywhere: UnitAlert.Show(goblin).
// To tune look/feel: Lifetime, WobbleSpeedDeg, WobbleAmplitude, HeightOffset.
// =============================================================================
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Self-animating red "!" indicator parented to a unit. Created via the static
    /// <see cref="Show"/> factory; ticks its own wobble + fade in Update and destroys itself.</summary>
    public sealed class UnitAlert : MonoBehaviour
    {
        private const float Lifetime = 1.2f;          // total seconds before self-destruct
        private const float WobbleSpeedDeg = 720f;    // phase speed of the left/right shake (deg/sec)
        private const float WobbleAmplitude = 20f;    // max tilt in degrees each way
        private const float FadeOutDuration = 0.3f;   // last N seconds fade alpha to 0
        private const float HeightOffset = 0.7f;      // world units above the unit's origin

        private float _t;
        private SpriteRenderer _renderer;

        /// <summary>Show (or refresh) a red "!" above the given goblin. Idempotent per unit:
        /// if one is already active it just resets its timer rather than spawning a second.</summary>
        public static void Show(Goblin g)
        {
            if (g == null) return;

            var existing = g.GetComponentInChildren<UnitAlert>();
            if (existing != null) { existing._t = 0f; return; }

            var go = new GameObject("UnitAlert");
            go.transform.SetParent(g.transform, false);
            go.transform.localPosition = new Vector3(0f, HeightOffset, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AlertSprite();
            sr.sortingOrder = 50;   // above health bars (25) and buildings (15)
            go.AddComponent<UnitAlert>()._renderer = sr;
        }

        private void Awake()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            _t += Time.deltaTime;

            // Wobble: rotate Z back and forth like a "no" head-shake.
            float wob = Mathf.Sin(_t * WobbleSpeedDeg * Mathf.Deg2Rad) * WobbleAmplitude;
            transform.localRotation = Quaternion.Euler(0f, 0f, wob);

            // Fade out over the final FadeOutDuration seconds.
            if (_renderer != null)
            {
                float a = _t > Lifetime - FadeOutDuration
                    ? Mathf.Clamp01((Lifetime - _t) / FadeOutDuration)
                    : 1f;
                var c = _renderer.color; c.a = a; _renderer.color = c;
            }

            if (_t >= Lifetime) Destroy(gameObject);
        }

        // Procedurally drawn red "!" (16x16). Builtin UI sprites return null in this project,
        // so — like the other VFX — we bake a tiny texture once and reuse it.
        private static Sprite s_sprite;
        private static Sprite AlertSprite()
        {
            if (s_sprite != null) return s_sprite;
            const int W = 16, H = 16;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var clear = new Color32(0, 0, 0, 0);
            var red = new Color32(230, 40, 40, 255);
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            // Stem of the "!" (rows 5..14, columns 6..9)
            for (int y = 5; y <= 14; y++) for (int x = 6; x <= 9; x++) px[y * W + x] = red;
            // Dot of the "!" (rows 1..3)
            for (int y = 1; y <= 3; y++) for (int x = 6; x <= 9; x++) px[y * W + x] = red;
            tex.SetPixels32(px);
            tex.Apply();
            s_sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 16);
            return s_sprite;
        }
    }
}

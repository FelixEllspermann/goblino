// DeathBurst.cs  (MonoBehaviour — RTSCL.World.Unity)
// Spawns red square particles that explode radially, arc upward (UpwardBias), fall under simulated
// gravity, and fade out before self-destructing. Used by Goblin death (full burst) and by HitFeedback
// (small spritz) via SpawnCustom.
//
// Where to adjust:
//   - Default death burst: the constants below (used by Spawn).
//   - Custom bursts (e.g. hit spritz): pass params to SpawnCustom.
//   - Sorting order: sr.sortingOrder (50 = above most world objects, below click feedback).

using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Short-lived red particle burst at a world position. Self-destructs.</summary>
    public sealed class DeathBurst : MonoBehaviour
    {
        private const int   ParticleCount = 12;
        private const float BurstLifetime = 0.5f;
        private const float ParticleFadeOver = 0.4f;
        private const float SpeedMin = 1.5f;
        private const float SpeedMax = 2.5f;
        private const float UpwardBias = 0.6f;
        private const float Gravity = -4f;       // world-units/s² downward pull

        // Minimal struct — avoids GC per particle; array is pre-allocated in Build.
        private struct Particle
        {
            public Transform T;
            public SpriteRenderer SR;
            public Vector3 Velocity;
        }

        private Particle[] _particles;
        private float _age;
        private float _lifetime = BurstLifetime;
        private float _fadeOver = ParticleFadeOver;
        // Shared 1×1 white square sprite — baked once, reused by all instances.
        private static Sprite s_sprite;

        /// <summary>Default red death burst at <paramref name="worldPos"/>.</summary>
        public static DeathBurst Spawn(Vector3 worldPos) =>
            SpawnCustom(worldPos, ParticleCount, 0.15f, SpeedMin, SpeedMax, UpwardBias,
                        BurstLifetime, ParticleFadeOver, new Color(0.95f, 0.15f, 0.15f, 1f));

        /// <summary>Configurable burst (used for the small on-hit spritz).</summary>
        public static DeathBurst SpawnCustom(Vector3 worldPos, int count, float scale,
                                             float speedMin, float speedMax, float upBias,
                                             float lifetime, float fadeOver, Color color)
        {
            var go = new GameObject("Burst");
            go.transform.position = worldPos;
            var b = go.AddComponent<DeathBurst>();
            b.Build(count, scale, speedMin, speedMax, upBias, lifetime, fadeOver, color);
            return b;
        }

        private void Build(int count, float scale, float speedMin, float speedMax, float upBias,
                           float lifetime, float fadeOver, Color color)
        {
            if (s_sprite == null) s_sprite = BuildSquareSprite();
            _lifetime = Mathf.Max(0.05f, lifetime);
            _fadeOver = Mathf.Max(0.05f, fadeOver);
            count = Mathf.Max(1, count);
            _particles = new Particle[count];
            for (int i = 0; i < count; i++)
            {
                var p = new GameObject($"P{i}");
                p.transform.SetParent(transform, false);
                var sr = p.AddComponent<SpriteRenderer>();
                sr.sprite = s_sprite;
                sr.color = color;
                sr.sortingOrder = 50;
                p.transform.localScale = Vector3.one * scale;
                float angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                float speed = Random.Range(speedMin, speedMax);
                Vector3 v = new(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed + upBias, 0f);
                _particles[i] = new Particle { T = p.transform, SR = sr, Velocity = v };
            }
        }

        private void Update()
        {
            if (_particles == null) return;
            _age += Time.deltaTime;
            if (_age >= _lifetime) { Destroy(gameObject); return; }
            float alpha = Mathf.Clamp01(1f - _age / _fadeOver);
            for (int i = 0; i < _particles.Length; i++)
            {
                var p = _particles[i];
                p.Velocity.y += Gravity * Time.deltaTime;
                p.T.position += p.Velocity * Time.deltaTime;
                var c = p.SR.color; c.a = alpha; p.SR.color = c;
                _particles[i] = p;
            }
        }

        /// <summary>Bakes a 1×1 white pixel sprite shared by all particles. Tinted via SpriteRenderer.color.</summary>
        private static Sprite BuildSquareSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}

// DeathBurst.cs  (MonoBehaviour — RTSCL.World.Unity)
// Spawns 12 red square particles that explode radially, arc upward (UpwardBias),
// fall under simulated gravity, and fade out before self-destructing.
// Used by Goblin.Die() to mark unit death visually.
//
// Where to adjust:
//   - Particle count: ParticleCount constant.
//   - Burst duration / fade: BurstLifetime / ParticleFadeOver.
//   - Speed range: SpeedMin / SpeedMax.
//   - Gravity strength: Gravity (negative = downward).
//   - Particle colour: sr.color in Awake() (currently dark red 0.95, 0.15, 0.15).
//   - Sorting order: sr.sortingOrder (50 = above most world objects, below click feedback).

using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Short-lived red particle burst at the given world position.</summary>
    public sealed class DeathBurst : MonoBehaviour
    {
        private const int   ParticleCount = 12;
        private const float BurstLifetime = 0.5f;
        private const float ParticleFadeOver = 0.4f;  // fade starts immediately; fully gone at 0.4s
        private const float SpeedMin = 1.5f;
        private const float SpeedMax = 2.5f;
        private const float UpwardBias = 0.6f;  // added to y velocity so burst arcs upward
        private const float Gravity = -4f;       // world-units/s² downward pull

        // Minimal struct — avoids GC per particle; array is pre-allocated in Awake.
        private struct Particle
        {
            public Transform T;
            public SpriteRenderer SR;
            public Vector3 Velocity;
        }

        private Particle[] _particles;
        private float _age;
        // Shared 1×1 white square sprite — baked once, reused by all DeathBurst instances.
        private static Sprite s_sprite;

        /// <summary>Instantiates a DeathBurst at <paramref name="worldPos"/> and returns it.
        /// Caller can ignore the return value; the component self-destructs.</summary>
        public static DeathBurst Spawn(Vector3 worldPos)
        {
            var go = new GameObject("DeathBurst");
            go.transform.position = worldPos;
            return go.AddComponent<DeathBurst>();
        }

        /// <summary>Creates child particle GameObjects and initialises radial velocities
        /// with a random jitter and upward bias.</summary>
        private void Awake()
        {
            if (s_sprite == null) s_sprite = BuildSquareSprite();
            _particles = new Particle[ParticleCount];
            for (int i = 0; i < ParticleCount; i++)
            {
                var p = new GameObject($"P{i}");
                p.transform.SetParent(transform, false);
                var sr = p.AddComponent<SpriteRenderer>();
                sr.sprite = s_sprite;
                sr.color = new Color(0.95f, 0.15f, 0.15f, 1f);
                sr.sortingOrder = 50;
                p.transform.localScale = Vector3.one * 0.15f;
                // Distribute evenly around the circle with a small random jitter per particle.
                float angle = (i / (float)ParticleCount) * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                float speed = Random.Range(SpeedMin, SpeedMax);
                Vector3 v = new(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed + UpwardBias, 0f);
                _particles[i] = new Particle { T = p.transform, SR = sr, Velocity = v };
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= BurstLifetime) { Destroy(gameObject); return; }
            // Alpha fades linearly over ParticleFadeOver seconds (clamped so it doesn't go negative).
            float alpha = Mathf.Clamp01(1f - _age / ParticleFadeOver);
            for (int i = 0; i < _particles.Length; i++)
            {
                var p = _particles[i];
                // Apply simulated gravity each frame.
                p.Velocity.y += Gravity * Time.deltaTime;
                p.T.position += p.Velocity * Time.deltaTime;
                var c = p.SR.color; c.a = alpha; p.SR.color = c;
                // Write back — Particle is a value type.
                _particles[i] = p;
            }
        }

        /// <summary>Bakes a 1×1 white pixel sprite shared by all particles.
        /// Tinted to red at spawn time via SpriteRenderer.color.</summary>
        private static Sprite BuildSquareSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}

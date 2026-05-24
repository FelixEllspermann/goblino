using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Short-lived red particle burst at the given world position.</summary>
    public sealed class DeathBurst : MonoBehaviour
    {
        private const int   ParticleCount = 12;
        private const float BurstLifetime = 0.5f;
        private const float ParticleFadeOver = 0.4f;
        private const float SpeedMin = 1.5f;
        private const float SpeedMax = 2.5f;
        private const float UpwardBias = 0.6f;
        private const float Gravity = -4f;

        private struct Particle
        {
            public Transform T;
            public SpriteRenderer SR;
            public Vector3 Velocity;
        }

        private Particle[] _particles;
        private float _age;
        private static Sprite s_sprite;

        public static DeathBurst Spawn(Vector3 worldPos)
        {
            var go = new GameObject("DeathBurst");
            go.transform.position = worldPos;
            return go.AddComponent<DeathBurst>();
        }

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
            float alpha = Mathf.Clamp01(1f - _age / ParticleFadeOver);
            for (int i = 0; i < _particles.Length; i++)
            {
                var p = _particles[i];
                p.Velocity.y += Gravity * Time.deltaTime;
                p.T.position += p.Velocity * Time.deltaTime;
                var c = p.SR.color; c.a = alpha; p.SR.color = c;
                _particles[i] = p;
            }
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

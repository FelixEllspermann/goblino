// HitFeedback.cs — On-hit reaction for any unit: a brief white flash, a quick rotational wobble
// (flinch), and a small red particle spritz. Attached to every Goblin in Init and triggered from
// Goblin.TakeDamage (which runs on all clients, so the feedback is universal + cosmetic).
//
// Flash restores the renderer's faction/neutral tint afterward. Wobble uses rotation (movement only
// writes position, so they don't fight) and restores to identity. Particles reuse DeathBurst.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>White flash + wobble + red spritz when the unit is hit.</summary>
    public sealed class HitFeedback : MonoBehaviour
    {
        private const float FlashDur = 0.12f;
        private const float WobbleDur = 0.18f;
        private const float WobbleDeg = 12f;
        private const float WobbleFreq = 60f;

        private SpriteRenderer _sr;
        private Color _baseColor;
        private float _flashT = -1f;
        private float _wobbleT = -1f;

        private void Awake() => _sr = GetComponent<SpriteRenderer>();

        /// <summary>Trigger the hit reaction. Safe to call repeatedly (re-arms the flash/wobble).</summary>
        public void Play()
        {
            if (_sr != null) { _baseColor = _sr.color; _flashT = 0f; }
            _wobbleT = 0f;
            DeathBurst.SpawnCustom(transform.position, 6, 0.09f, 1.0f, 2.0f, 0.3f, 0.30f, 0.26f,
                                   new Color(0.95f, 0.2f, 0.2f, 1f));
        }

        private void Update()
        {
            if (_flashT >= 0f && _sr != null)
            {
                _flashT += Time.deltaTime;
                float k = _flashT / FlashDur;
                if (k >= 1f) { _sr.color = _baseColor; _flashT = -1f; }
                else _sr.color = Color.Lerp(Color.white, _baseColor, k);
            }

            if (_wobbleT >= 0f)
            {
                _wobbleT += Time.deltaTime;
                if (_wobbleT >= WobbleDur) { transform.rotation = Quaternion.identity; _wobbleT = -1f; }
                else
                {
                    float decay = 1f - _wobbleT / WobbleDur;
                    float ang = Mathf.Sin(_wobbleT * WobbleFreq) * WobbleDeg * decay;
                    transform.rotation = Quaternion.Euler(0f, 0f, ang);
                }
            }
        }
    }
}

// =============================================================================
// Arrow.cs  —  RTSCL.World.Unity
//
// Cosmetic-but-owner-authoritative projectile fired by ranged goblins (Archer).
// Spawned on every client when an attack swing fires (the Attacking state runs
// on all clients). It homes toward the target's current position, rotating to
// face its travel direction. On impact:
//   - if DealsDamage (owner client only) and the target is still alive, it calls
//     NetCommandIssuer.IssueDamage → applies locally + broadcasts EvDamage, the
//     same path melee uses. Remotes spawn the arrow with DealsDamage=false (pure
//     visual) and receive the HP change via EvDamage. No new wire message.
// To tune: Speed, ArrivalEpsilon, sorting order.
// =============================================================================
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Self-propelled homing arrow. Created via the static <see cref="Spawn"/> factory;
    /// ticks its own travel + impact in Update and destroys itself.</summary>
    public sealed class Arrow : MonoBehaviour
    {
        private const float Speed = 10f;            // world units / second
        private const float ArrivalEpsilon = 0.15f; // distance to target at which it "hits"
        private const float MaxLifetime = 2f;       // safety self-destruct if target vanishes

        private Goblin _target;
        private Vector3 _fallbackPos;   // last-known target pos, used if target dies mid-flight
        private int _damage;
        private Goblin _attacker;
        private bool _dealsDamage;
        private float _age;

        /// <summary>Spawn an arrow travelling from <paramref name="from"/> toward <paramref name="target"/>.
        /// <paramref name="dealsDamage"/> must be true ONLY on the attacker's owner client.</summary>
        public static void Spawn(Vector3 from, Goblin target, int damage, Goblin attacker, bool dealsDamage, Sprite sprite)
        {
            if (target == null || sprite == null) return;
            var go = new GameObject("Arrow");
            go.transform.position = from;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 30; // above goblins (25)
            var a = go.AddComponent<Arrow>();
            a._target = target;
            a._fallbackPos = target.transform.position;
            a._damage = damage;
            a._attacker = attacker;
            a._dealsDamage = dealsDamage;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= MaxLifetime) { Destroy(gameObject); return; }

            // Aim point: live target position while alive, else last-known fallback.
            Vector3 aim = (_target != null && _target.CurrentHp > 0) ? _target.transform.position : _fallbackPos;
            Vector3 delta = aim - transform.position;
            float dist = delta.magnitude;

            if (dist <= ArrivalEpsilon)
            {
                // Impact. Owner client applies + broadcasts damage; remotes are visual-only.
                if (_dealsDamage && _target != null && _target.CurrentHp > 0)
                    NetCommandIssuer.IssueDamage(_target, _damage, _attacker);
                Destroy(gameObject);
                return;
            }

            Vector3 dir = delta / dist;
            transform.position += dir * (Speed * Time.deltaTime);
            // Rotate sprite to point along travel direction (ArrowLong_0 points +X).
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, ang);
        }
    }
}

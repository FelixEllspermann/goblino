// MonsterAI.cs — Drives a neutral monster Goblin: gentle wander around Home, aggro the nearest
// player goblin within AggroRadius, chase, and disengage+return when dragged past LeashRadius.
// Decision logic runs ONLY on the authoritative client (the monster's owner = host in MP, local in
// solo) and issues normal net commands so remotes mirror. Attached at spawn by MonsterSpawner.
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Wander/aggro/leash brain for a neutral monster. Requires a Goblin on the same object.</summary>
    public sealed class MonsterAI : MonoBehaviour
    {
        public Vector3 Home;

        private const float WanderRadius = 4f;
        private const float AggroRadius = 6f;
        private const float LeashRadius = 12f;
        private const float WanderMinDelay = 2.5f;
        private const float WanderMaxDelay = 5.5f;
        private const float ReturnArrive = 1.5f;

        private Goblin _self;
        private Goblin _target;
        private bool _returning;
        private float _nextWander;
        private float _scanTimer;
        private readonly List<Goblin> _solo = new(1);

        private void Awake() => _self = GetComponent<Goblin>();

        private void Update()
        {
            if (_self == null || _self.CurrentHp <= 0) return;
            if (!_self.IsOwnedLocally) return;   // only the authoritative client decides; others mirror

            float homeDist = Vector2.Distance(transform.position, Home);

            // Returning home after a leash break — ignore aggro until back near home.
            if (_returning)
            {
                if (homeDist <= ReturnArrive) _returning = false;
                return;
            }

            // Dragged past leash → give up and walk home.
            if (_target != null && homeDist > LeashRadius)
            {
                _target = null;
                _returning = true;
                IssueMove(Home);
                return;
            }

            // Drop dead/destroyed target.
            if (_target != null && (!_target || _target.CurrentHp <= 0)) _target = null;

            // Acquire a target periodically when we have none.
            _scanTimer -= Time.deltaTime;
            if (_target == null && _scanTimer <= 0f)
            {
                _scanTimer = 0.4f;
                _target = FindNearestPlayerGoblin();
                if (_target != null) NetCommandIssuer.IssueAttack(_self, _target);
            }

            // Idle wandering near home when not engaged.
            if (_target == null && _self.IsIdle && Time.time >= _nextWander)
            {
                _nextWander = Time.time + Random.Range(WanderMinDelay, WanderMaxDelay);
                Vector2 off = Random.insideUnitCircle * WanderRadius;
                IssueMove(new Vector3(Home.x + off.x, Home.y + off.y, 0f));
            }
        }

        private void IssueMove(Vector3 dest)
        {
            _solo.Clear();
            _solo.Add(_self);
            NetCommandIssuer.IssueMove(_solo, dest);
        }

        private Goblin FindNearestPlayerGoblin()
        {
            Goblin best = null;
            float bestSq = AggroRadius * AggroRadius;
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.CurrentHp <= 0) continue;
                float d = (g.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = g; }
            }
            return best;
        }
    }
}

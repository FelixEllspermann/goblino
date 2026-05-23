using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Random = UnityEngine.Random;

namespace RTSCL.World.Unity
{
    public sealed class Goblin : MonoBehaviour
    {
        public static readonly List<Goblin> All = new();

        public string Kind { get; private set; } = "Goblin";

        private Sprite[] _frames;
        private Tilemap _terrainMap;
        private SpriteRenderer _renderer;

        private Vector3 _target;
        private float _moveSpeed = 1.2f;       // world units per second
        private float _frameTimer;
        private int _frameIndex;
        private float _retargetCooldown;

        private const float FrameDuration = 0.18f;
        private const float TargetReachedEpsilon = 0.05f;
        private const float WanderRadius = 4f;
        private const float FlockRadius = 6f;
        private const float FlockBias = 0.35f;  // 0 = pure random, 1 = always toward flock center

        public void Init(string kind, Sprite[] walkFrames, Tilemap terrainMap)
        {
            Kind = kind;
            _frames = walkFrames;
            _terrainMap = terrainMap;
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sprite = walkFrames[0];
            _target = transform.position;
            PickNewTarget();
        }

        private void OnEnable()  => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            if (_frames == null || _frames.Length == 0) return;

            // Frame cycle
            _frameTimer += Time.deltaTime;
            if (_frameTimer >= FrameDuration)
            {
                _frameTimer = 0f;
                _frameIndex = (_frameIndex + 1) % _frames.Length;
                _renderer.sprite = _frames[_frameIndex];
            }

            // Movement
            var delta = _target - transform.position;
            if (delta.sqrMagnitude < TargetReachedEpsilon * TargetReachedEpsilon)
            {
                _retargetCooldown -= Time.deltaTime;
                if (_retargetCooldown <= 0f) PickNewTarget();
                return;
            }
            var dir = delta.normalized;
            transform.position += dir * _moveSpeed * Time.deltaTime;

            if (Mathf.Abs(dir.x) > 0.1f)
                _renderer.flipX = dir.x < 0f;
        }

        private void PickNewTarget()
        {
            // Random target + flock-center bias
            Vector3 flockCenter = transform.position;
            int neighbors = 0;
            Vector3 sum = Vector3.zero;
            foreach (var g in All)
            {
                if (g == this || g == null) continue;
                var d = g.transform.position - transform.position;
                if (d.sqrMagnitude > FlockRadius * FlockRadius) continue;
                sum += g.transform.position;
                neighbors++;
            }
            if (neighbors > 0) flockCenter = sum / neighbors;

            for (int tries = 0; tries < 8; tries++)
            {
                Vector3 randomOffset = new(
                    Random.Range(-WanderRadius, WanderRadius),
                    Random.Range(-WanderRadius, WanderRadius), 0);
                Vector3 flockOffset = (flockCenter - transform.position) * FlockBias;
                Vector3 candidate = transform.position + randomOffset + flockOffset;
                if (IsPassable(candidate)) { _target = candidate; _retargetCooldown = Random.Range(0.5f, 1.5f); return; }
            }
            // Fallback: stay put a moment, try again later
            _target = transform.position;
            _retargetCooldown = 0.5f;
        }

        private bool IsPassable(Vector3 worldPos)
        {
            if (_terrainMap == null) return true;
            var cell = _terrainMap.WorldToCell(worldPos);
            var tile = _terrainMap.GetTile(cell);
            if (tile == null) return false;
            var n = tile.name;
            return n != "DeepWater" && n != "Cliff";
        }
    }
}

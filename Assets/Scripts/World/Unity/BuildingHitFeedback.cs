// BuildingHitFeedback.cs — On-hit reaction for buildings: a brief white flash on the building sprite
// plus a small red particle spritz, mirroring HitFeedback for units. Triggered from Goblin's
// AttackingBuilding swings (melee immediately, ranged on arrow impact) so every client shows it.
//
// The static Play(origin) looks up the building GameObject via NetCommandApplier.Placer, attaches the
// flash component on demand, and spawns the burst at the building's visual center.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>White flash + red spritz when a building is hit. Attached on demand to the building GO.</summary>
    public sealed class BuildingHitFeedback : MonoBehaviour
    {
        private const float FlashDur = 0.12f;

        private SpriteRenderer _sr;
        private Color _baseColor;
        private float _t = -1f;

        private void Awake() => _sr = GetComponent<SpriteRenderer>();

        /// <summary>Play the hit reaction on the building at <paramref name="origin"/> (all clients).</summary>
        public static void Play(Vector2Int origin)
        {
            var placer = NetCommandApplier.Placer;
            if (placer == null || !placer.TryGetBuildingGo(origin, out var go) || go == null) return;
            var sr = go.GetComponent<SpriteRenderer>();
            Vector3 center = sr != null ? sr.bounds.center : go.transform.position;

            var fb = go.GetComponent<BuildingHitFeedback>();
            if (fb == null) fb = go.AddComponent<BuildingHitFeedback>();
            fb.Trigger();

            DeathBurst.SpawnCustom(center, 7, 0.10f, 1.2f, 2.4f, 0.35f, 0.34f, 0.30f,
                                   new Color(0.95f, 0.2f, 0.2f, 1f));
        }

        // Arm the flash. Capture the base color only when not already flashing (avoids saving a white frame).
        private void Trigger()
        {
            if (_sr != null && _t < 0f) _baseColor = _sr.color;
            _t = 0f;
        }

        private void Update()
        {
            if (_t < 0f || _sr == null) return;
            _t += Time.deltaTime;
            float k = _t / FlashDur;
            if (k >= 1f) { _sr.color = _baseColor; _t = -1f; }
            else _sr.color = Color.Lerp(Color.white, _baseColor, k);
        }
    }
}

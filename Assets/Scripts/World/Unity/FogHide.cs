// FogHide.cs — Hides a unit's visuals when its cell is not currently in the player's vision.
// Added to neutral monsters by MonsterSpawner so they stay concealed under the fog of war and only
// appear when a friendly unit/building has line of sight. Runs on every client (purely cosmetic).
//
// Toggles the root SpriteRenderer and the "HealthBar" child object (deactivating the HP bar object
// also stops its LateUpdate from re-showing itself). Selection ring is left alone (monsters are never
// selected). Fails open (visible) when there is no FogOfWar in the scene.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Shows/hides a unit based on FogOfWar visibility of its current cell.</summary>
    public sealed class FogHide : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private Transform _healthBar;

        private void Start()
        {
            _sr = GetComponent<SpriteRenderer>();
            _healthBar = transform.Find("HealthBar");
        }

        private void LateUpdate()
        {
            var fow = FogOfWar.Instance;
            bool vis = fow == null || fow.IsVisible(transform.position);
            if (_sr != null && _sr.enabled != vis) _sr.enabled = vis;
            if (_healthBar != null && _healthBar.gameObject.activeSelf != vis)
                _healthBar.gameObject.SetActive(vis);
        }
    }
}

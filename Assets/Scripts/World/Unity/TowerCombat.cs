// TowerCombat.cs (MonoBehaviour — RTSCL.World.Unity)
// Attached by BuildingPlacer to defensive buildings (towers) whose BuildingDefinition has combat stats
// (AttackDamage > 0, AttackRange > 0, ProjectileSprite). Once the tower is BUILT it auto-fires homing
// arrows at the nearest hostile unit in range on a fixed interval. Owner-authoritative: only the tower
// owner's client picks targets + applies/broadcasts damage (via Arrow → NetCommandIssuer.IssueTowerDamage);
// remotes see the target's hit reaction through the synced EvDamage. Hostile = neutral monsters OR units
// of a different owner.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Auto-firing arrow defense for a placed tower building.</summary>
    public sealed class TowerCombat : MonoBehaviour
    {
        private Vector2Int _origin;
        private ulong _owner;
        private Vector3 _muzzle;     // footprint-center world position arrows fly from
        private int _damage;
        private int _range;
        private float _interval;
        private Sprite _projectile;
        private float _timer;

        /// <summary>Wire up a tower's combat from its definition. Call right after placement.</summary>
        public static void AttachTo(GameObject buildingGo, Vector2Int origin, Vector2Int footprint, ulong owner, BuildingDefinition def)
        {
            var t = buildingGo.AddComponent<TowerCombat>();
            t._origin = origin;
            t._owner = owner;
            t._damage = def.AttackDamage;
            t._range = def.AttackRange;
            t._interval = Mathf.Max(0.2f, def.AttackInterval);
            t._projectile = def.ProjectileSprite;
            // Fire from the middle of the rendered sprite (looks right for tall towers); fall back to the
            // footprint center if there's no sprite.
            var sr = buildingGo.GetComponent<SpriteRenderer>();
            t._muzzle = (sr != null && sr.sprite != null)
                ? buildingGo.transform.position + (Vector3)sr.sprite.bounds.center   // mid-sprite (pivot-relative)
                : buildingGo.transform.position + new Vector3(footprint.x * 0.5f, footprint.y * 0.5f, 0f);
        }

        // Only the owner's client authorities firing (in solo every unit is owned locally).
        private bool OwnedLocally => WorldStartContext.IsSolo || _owner == WorldStartContext.LocalPlayer || _owner == 0UL;

        private void Update()
        {
            if (!OwnedLocally) return;
            if (BuildingConstruction.IsUnderConstruction(_origin)) return;   // not built yet → no shooting
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            var target = FindTarget();
            if (target == null) return;
            _timer = _interval;
            // Owner's Tower-Damage upgrade scales the shot (read live → applies to existing towers too).
            int dmg = Mathf.Max(1, Mathf.RoundToInt(_damage * TowerPower.Get(_owner)));
            Arrow.SpawnFromTower(_muzzle, target, dmg, _owner, dealsDamage: true, _projectile);
        }

        // Nearest living hostile unit within range AND in vision: a neutral monster, or any unit of a
        // different owner. A target hidden in fog of war is NOT shot (no sniping through the fog).
        private Goblin FindTarget()
        {
            float r2 = (float)_range * _range;
            // Only the local player's towers respect fog (the player's vision). Bot towers have no fog map.
            bool gateOnFog = (_owner == WorldStartContext.LocalPlayer || _owner == 0UL) && FogOfWar.Instance != null;
            Goblin best = null;
            float bestSq = float.MaxValue;
            foreach (var g in Goblin.All)
            {
                if (g == null || g.CurrentHp <= 0 || !g.gameObject.activeInHierarchy) continue;
                if (!(g.IsNeutral || g.Owner != _owner)) continue;   // skip own units
                float d = (g.transform.position - _muzzle).sqrMagnitude;
                if (d > r2 || d >= bestSq) continue;
                if (gateOnFog && !FogOfWar.Instance.IsVisible(g.transform.position)) continue;   // in fog → don't shoot
                bestSq = d; best = g;
            }
            return best;
        }
    }
}

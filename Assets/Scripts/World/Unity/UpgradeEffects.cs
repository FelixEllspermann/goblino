using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Applies upgrade effects to existing or newly-spawned units.
    /// Constants live here so Issuer and Applier share identical logic.</summary>
    public static class UpgradeEffects
    {
        public const float FarmerHarvestSpeedMul = 1.25f;
        public const float ClubAttackDamageMul = 1.25f;
        public const float ClubMaxHpMul = 1.5f;

        /// <summary>Apply a single upgrade to all currently-living units owned by `owner`.
        /// Called once at purchase time.</summary>
        public static void ApplyToOwnedUnits(ulong owner, UpgradeKind kind)
        {
            foreach (var g in Goblin.All)
            {
                if (g == null || g.Owner != owner) continue;
                ApplyOne(g, kind);
            }
        }

        /// <summary>Apply ALL previously-purchased upgrades for this goblin's owner to it.
        /// Called by GoblinSpawner after a fresh spawn so back-applied upgrades stick.</summary>
        public static void ApplyExistingTo(Goblin g)
        {
            if (g == null) return;
            foreach (var kind in PlayerUpgrades.AllFor(g.Owner))
                ApplyOne(g, kind);
        }

        private static void ApplyOne(Goblin g, UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.FarmerHarvestSpeed:
                    if (g.Kind == "FarmerGoblin")
                        g.SetHarvestSpeedMul(FarmerHarvestSpeedMul);
                    break;
                case UpgradeKind.ClubAttackDamage:
                    if (g.Kind == "ClubGoblin")
                        g.SetAttackDamage(Mathf.RoundToInt(g.AttackDamage * ClubAttackDamageMul));
                    break;
                case UpgradeKind.ClubMaxHp:
                    if (g.Kind == "ClubGoblin")
                    {
                        float ratio = g.MaxHp > 0 ? (float)g.CurrentHp / g.MaxHp : 1f;
                        int newMax = Mathf.RoundToInt(g.MaxHp * ClubMaxHpMul);
                        g.SetMaxHp(newMax, Mathf.RoundToInt(newMax * ratio));
                    }
                    break;
            }
        }
    }
}

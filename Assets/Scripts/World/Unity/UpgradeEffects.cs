// UpgradeEffects.cs — pure logic for applying upgrade stat mutations to Goblin instances.
// Multiplier constants are defined here so NetCommandIssuer (preview cost text) and
// NetCommandApplier (actual application) use the exact same values — no drift possible.
// Two entry points: ApplyToOwnedUnits() at purchase time; ApplyExistingTo() for freshly-spawned
// units so they inherit upgrades already purchased by their owner before they existed.
// To add a new upgrade: add an UpgradeKind value, add a constant here, add a case in ApplyOne(),
// and wire up the purchase UI / network command.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>
    /// Applies upgrade effects to existing or newly-spawned Goblin instances.
    /// Multiplier constants live here so Issuer and Applier share identical logic.
    /// </summary>
    public static class UpgradeEffects
    {
        // Multipliers applied when the corresponding upgrade is purchased.
        // Adjust these constants to re-balance upgrade strength.
        public const float FarmerHarvestSpeedMul = 1.25f;  // +25 % harvest rate
        public const float ClubAttackDamageMul   = 1.25f;  // +25 % attack damage
        public const float ClubMaxHpMul          = 1.5f;   // +50 % max HP

        /// <summary>
        /// Apply a single upgrade to all currently-living units owned by <paramref name="owner"/>.
        /// Call immediately after MarkPurchased so existing units benefit right away.
        /// </summary>
        public static void ApplyToOwnedUnits(ulong owner, UpgradeKind kind)
        {
            foreach (var g in Goblin.All)
            {
                if (g == null || g.Owner != owner) continue;
                ApplyOne(g, kind);
            }
        }

        /// <summary>
        /// Apply ALL upgrades already purchased for this goblin's owner to the given unit.
        /// Called by GoblinSpawner immediately after a fresh unit is spawned so
        /// back-purchased upgrades (bought before this unit existed) are retroactively applied.
        /// </summary>
        public static void ApplyExistingTo(Goblin g)
        {
            if (g == null) return;
            foreach (var kind in PlayerUpgrades.AllFor(g.Owner))
                ApplyOne(g, kind);
        }

        /// <summary>Apply a single upgrade effect to one goblin. Guards on g.Kind so Farmer upgrades don't affect Clubs and vice versa.</summary>
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
                        // Preserve proportional current HP so the goblin isn't suddenly "over-healed".
                        float ratio = g.MaxHp > 0 ? (float)g.CurrentHp / g.MaxHp : 1f;
                        int newMax = Mathf.RoundToInt(g.MaxHp * ClubMaxHpMul);
                        g.SetMaxHp(newMax, Mathf.RoundToInt(newMax * ratio));
                    }
                    break;
            }
        }
    }
}

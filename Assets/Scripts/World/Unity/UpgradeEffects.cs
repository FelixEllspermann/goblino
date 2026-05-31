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
        public const float FarmerMoveSpeedMul    = 1.25f;  // +25 % move speed (farmers)
        public const float FarmerCarryMul        = 1.5f;   // +50 % carry capacity (farmers)
        public const float BuildSpeedMul         = 1.5f;   // +50 % construction speed (farmers)
        public const float MeleeArmorBonus       = 5f;     // +5 flat armor points (melee units)
        public const int   RangedRangeBonus      = 1;      // +1 attack range (ranged units)
        public const float AllDamageMul          = 1.25f;  // +25 % attack damage (all combat units)
        public const float UnitTrainSpeedMul     = 1.25f;  // +25 % training speed (per-owner)

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

        /// <summary>Single entry point called once when <paramref name="owner"/> purchases <paramref name="kind"/>
        /// (player via NetCommandApplier, bot via BotController). Applies owner-level effects (training speed)
        /// AND the per-unit effects to all currently-living units. Newly-spawned units pick up per-unit
        /// effects later via ApplyExistingTo.</summary>
        public static void OnPurchased(ulong owner, UpgradeKind kind)
        {
            if (kind == UpgradeKind.UnitTrainSpeed) TrainingSpeed.Multiply(owner, UnitTrainSpeedMul);
            ApplyToOwnedUnits(owner, kind);
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

                // --- Mill economy upgrades (farmers only) ---
                case UpgradeKind.FarmerMoveSpeed:
                    if (g.Kind == "FarmerGoblin") g.MultiplyMoveSpeed(FarmerMoveSpeedMul);
                    break;
                case UpgradeKind.FarmerCarryCapacity:
                    if (g.Kind == "FarmerGoblin") g.MultiplyCarryCapacity(FarmerCarryMul);
                    break;
                case UpgradeKind.BuildSpeed:
                    if (g.Kind == "FarmerGoblin") g.MultiplyBuildSpeed(BuildSpeedMul);
                    break;

                // --- Workshop military upgrades (by combat role, not kind, so future units inherit) ---
                case UpgradeKind.MeleeArmor:
                    if (!g.IsRanged && !g.IsBoat && g.AttackDamage > 0) g.AddArmor(MeleeArmorBonus);
                    break;
                case UpgradeKind.RangedAttackRange:
                    if (g.IsRanged) g.AddAttackRange(RangedRangeBonus);
                    break;
                case UpgradeKind.AllUnitsDamage:
                    if (g.AttackDamage > 0) g.SetAttackDamage(Mathf.RoundToInt(g.AttackDamage * AllDamageMul));
                    break;

                // UnitTrainSpeed is owner-level (handled in OnPurchased), not a per-unit stat.
            }
        }
    }
}

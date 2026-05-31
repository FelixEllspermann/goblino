// CombatBonus.cs — pure combat-damage rule (no Unity deps), so it is unit-testable in RTSCL.World.Tests.
// Encodes the Speargoblin's "bonus vs monsters + enemy melee goblins" rule decided in
// .council/requirements.md. Everything else (Club/Archer) passes a multiplier of 1 and is unaffected.
namespace RTSCL.World
{
    /// <summary>Damage modifiers that depend on the target's type. Kept Unity-free + deterministic so the
    /// rule can be tested in isolation; Goblin.cs feeds it live target flags.</summary>
    public static class CombatBonus
    {
        /// <summary>True if <paramref name="bonusMult"/> applies to a target with the given traits.
        /// The bonus hits (a) neutral monsters and (b) enemy MELEE combatants (not ranged, not water
        /// transports, and actually able to fight). Ranged units (archers), boats, and non-combatants
        /// (farmers, damage 0) are excluded — that is what keeps the Speargoblin weak vs archers.</summary>
        public static bool Qualifies(bool targetIsMonster, bool targetIsRanged, bool targetIsWater, int targetAttackDamage)
        {
            if (targetIsMonster) return true;
            return !targetIsRanged && !targetIsWater && targetAttackDamage > 0;
        }

        /// <summary>Effective damage after the per-target bonus. With <paramref name="bonusMult"/> &lt;= 1
        /// (every non-Speargoblin unit) this returns <paramref name="baseDamage"/> unchanged. When the
        /// multiplier is &gt; 1 and the target qualifies, damage is scaled and rounded to the nearest int.</summary>
        public static int Effective(int baseDamage, float bonusMult,
                                    bool targetIsMonster, bool targetIsRanged, bool targetIsWater, int targetAttackDamage)
        {
            if (bonusMult <= 1f) return baseDamage;
            if (!Qualifies(targetIsMonster, targetIsRanged, targetIsWater, targetAttackDamage)) return baseDamage;
            return (int)System.Math.Round(baseDamage * (double)bonusMult, System.MidpointRounding.AwayFromZero);
        }
    }
}

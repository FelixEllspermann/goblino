// ArmorMath.cs — pure damage-reduction rule (no Unity deps), unit-testable in RTSCL.World.Tests.
// Armor reduces incoming damage by a diminishing-returns fraction armor/(armor+K): each point is worth
// progressively less, the fraction asymptotes toward but never reaches 1, and a hard cap + a min-1-damage
// floor guarantee a hit always lands for at least 1 (and reduction never reaches 100%). Decided in
// .council/requirements.md: Club armor 4 → 10%, Speargoblin armor 15 → ~29%, Archer 0 → none.
// Apply FLOORS the reduced damage (rounds in the defender's favour) so small armor reliably chips.
namespace RTSCL.World
{
    /// <summary>Defender-side armor damage reduction. Deterministic + Unity-free so it can be tested in
    /// isolation; Goblin.TakeDamage feeds it the unit's armor.</summary>
    public static class ArmorMath
    {
        /// <summary>Diminishing-returns constant. With K = 36: armor 4 → 0.10, armor 15 → ~0.294.</summary>
        public const float DiminishingK = 36f;
        /// <summary>Hard ceiling on the reduction fraction so damage can never be fully negated (the
        /// formula already asymptotes below 1; this is an explicit guarantee that it never reaches 100%).</summary>
        public const float MaxReduction = 0.8f;

        /// <summary>Fraction of damage removed by <paramref name="armor"/> points (0..MaxReduction).</summary>
        public static float Reduction(float armor)
        {
            if (armor <= 0f) return 0f;
            float r = armor / (armor + DiminishingK);
            return r > MaxReduction ? MaxReduction : r;
        }

        /// <summary>Damage actually taken after armor. Returns <paramref name="damage"/> unchanged when
        /// armor is 0; otherwise scales by (1 - reduction) and FLOORS the result (rounds in the defender's
        /// favour) so even a little armor reliably shaves at least the nominal fraction off small integer
        /// hits — then clamps to a minimum of 1 so every hit still chips HP. Non-positive incoming damage
        /// is passed through untouched (never turned into 1).</summary>
        public static int Apply(int damage, float armor)
        {
            if (damage <= 0 || armor <= 0f) return damage;
            int reduced = (int)System.Math.Floor(damage * (1f - Reduction(armor)));
            return reduced < 1 ? 1 : reduced;
        }
    }
}

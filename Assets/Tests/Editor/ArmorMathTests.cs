using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    // Verifies the armor damage-reduction rule (diminishing returns, capped, floored, min-1).
    // Reference: .council/requirements.md. K=36 → Club armor 4 = 10%, Speargoblin armor 15 ≈ 29%,
    // Archer 0 = none. Apply FLOORS the reduced damage (rounds in the defender's favour).
    public class ArmorMathTests
    {
        [Test]
        public void NoArmor_ReductionZero_DamageUnchanged()
        {
            Assert.AreEqual(0f, ArmorMath.Reduction(0f), 1e-5f);
            Assert.AreEqual(10, ArmorMath.Apply(10, 0f));
        }

        [Test]
        public void ClubArmor_ReducesTenPercent()
        {
            Assert.AreEqual(0.10f, ArmorMath.Reduction(4f), 1e-4f);   // 4/(4+36) = 0.10
        }

        [Test]
        public void SpearArmor_ReducesAboutTwentyNinePercent()
        {
            Assert.AreEqual(0.2941f, ArmorMath.Reduction(15f), 1e-3f); // 15/(15+36) = 0.2941
        }

        [Test]
        public void Apply_Floors_InDefendersFavour()
        {
            // Club 10%: floor(14*0.9)=floor(12.6)=12; floor(5*0.9)=floor(4.5)=4.
            Assert.AreEqual(12, ArmorMath.Apply(14, 4f));
            Assert.AreEqual(4, ArmorMath.Apply(5, 4f));
        }

        [Test]
        public void Apply_ClubArmor_ReliablyChipsSmallHits()
        {
            // The whole point of flooring: a 5- or 4-damage hit is actually reduced (was a no-op under rounding).
            Assert.AreEqual(4, ArmorMath.Apply(5, 4f));   // floor(4.5)
            Assert.AreEqual(3, ArmorMath.Apply(4, 4f));   // floor(3.6)
        }

        [Test]
        public void Apply_SpearArmor_ClearlyMoreThanClub()
        {
            // Spear armor 15 vs Club armor 4 must produce a visibly bigger reduction at common damage values.
            Assert.AreEqual(3, ArmorMath.Apply(5, 15f));  // floor(5*0.7059)=floor(3.53)
            Assert.AreEqual(2, ArmorMath.Apply(4, 15f));  // floor(4*0.7059)=floor(2.82)
            Assert.AreEqual(5, ArmorMath.Apply(8, 15f));  // floor(8*0.7059)=floor(5.65)
        }

        [Test]
        public void Reduction_NeverReachesOne_EvenAtHugeArmor()
        {
            Assert.Less(ArmorMath.Reduction(100000f), 1f);
            Assert.AreEqual(ArmorMath.MaxReduction, ArmorMath.Reduction(100000f), 1e-5f); // clamped to the cap
        }

        [Test]
        public void Apply_HighArmor_FloorsAtOne_NeverZero()
        {
            // Even at the 80% cap, a 1-damage hit must still chip at least 1 (floor(0.2) → clamped to 1).
            Assert.AreEqual(1, ArmorMath.Apply(1, 100000f));
        }

        [Test]
        public void Apply_NonPositiveDamage_PassesThrough()
        {
            Assert.AreEqual(0, ArmorMath.Apply(0, 15f));
        }

        [Test]
        public void Reduction_Capped_AtEightyPercent()
        {
            // Armor 200 → 200/236 = 0.847 raw → clamped to 0.80.
            Assert.AreEqual(0.80f, ArmorMath.Reduction(200f), 1e-5f);
        }
    }
}

using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    // Verifies the Speargoblin damage rule (bonus vs monsters + enemy melee, nothing else).
    // Reference: .council/requirements.md. Base spear damage 8, multiplier 1.75 → 14 vs qualifying targets.
    public class CombatBonusTests
    {
        const int Base = 8;
        const float SpearMult = 1.75f;

        [Test]
        public void NoBonus_MultiplierOne_ReturnsBaseRegardlessOfTarget()
        {
            // A normal unit (Club/Archer) has multiplier 1 → never modified, even vs a monster.
            Assert.AreEqual(5, CombatBonus.Effective(5, 1f, targetIsMonster: true, targetIsRanged: false, targetIsWater: false, targetAttackDamage: 5));
        }

        [Test]
        public void Spear_VsMonster_AppliesBonus()
        {
            Assert.AreEqual(14, CombatBonus.Effective(Base, SpearMult, targetIsMonster: true, targetIsRanged: false, targetIsWater: false, targetAttackDamage: 0));
        }

        [Test]
        public void Spear_VsEnemyMelee_AppliesBonus()
        {
            // Club: melee combatant, not ranged, not water, deals damage → qualifies.
            Assert.AreEqual(14, CombatBonus.Effective(Base, SpearMult, targetIsMonster: false, targetIsRanged: false, targetIsWater: false, targetAttackDamage: 5));
        }

        [Test]
        public void Spear_VsArcher_NoBonus()
        {
            // Ranged target → excluded (this is what keeps the spear weak vs archers).
            Assert.AreEqual(Base, CombatBonus.Effective(Base, SpearMult, targetIsMonster: false, targetIsRanged: true, targetIsWater: false, targetAttackDamage: 4));
        }

        [Test]
        public void Spear_VsFarmer_NoBonus()
        {
            // Non-combatant (0 damage) → excluded.
            Assert.AreEqual(Base, CombatBonus.Effective(Base, SpearMult, targetIsMonster: false, targetIsRanged: false, targetIsWater: false, targetAttackDamage: 0));
        }

        [Test]
        public void Spear_VsBoat_NoBonus()
        {
            // Water transport → excluded (bonus vs boats is out of scope).
            Assert.AreEqual(Base, CombatBonus.Effective(Base, SpearMult, targetIsMonster: false, targetIsRanged: false, targetIsWater: true, targetAttackDamage: 0));
        }

        [Test]
        public void Spear_VsMonster_EvenIfRangedOrWater_StillQualifies()
        {
            // Monsters always qualify regardless of their weapon/movement flags.
            Assert.IsTrue(CombatBonus.Qualifies(targetIsMonster: true, targetIsRanged: true, targetIsWater: true, targetAttackDamage: 0));
        }

        [Test]
        public void Effective_RoundsToNearest()
        {
            // 5 * 1.75 = 8.75 → 9 (round half away from zero).
            Assert.AreEqual(9, CombatBonus.Effective(5, 1.75f, targetIsMonster: true, targetIsRanged: false, targetIsWater: false, targetAttackDamage: 0));
        }
    }
}

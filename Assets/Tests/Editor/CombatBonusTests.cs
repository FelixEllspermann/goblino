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

        // --- Armor-piercing (Archer bonus vs armored targets) ---

        [Test]
        public void ArmorPierce_VsArmoredTarget_AppliesBonus()
        {
            // Archer base 4 × 1.5 vs an armored target (Club/Spear) = 6 (before the target's own armor).
            Assert.AreEqual(6, CombatBonus.WithArmorPierce(4, 1.5f, targetHasArmor: true));
        }

        [Test]
        public void ArmorPierce_VsUnarmoredTarget_NoBonus()
        {
            // No armor → archer hits for the flat amount.
            Assert.AreEqual(4, CombatBonus.WithArmorPierce(4, 1.5f, targetHasArmor: false));
        }

        [Test]
        public void ArmorPierce_MultiplierOne_NoBonus()
        {
            // A non-piercing unit (multiplier 1) is unaffected even vs armored targets.
            Assert.AreEqual(4, CombatBonus.WithArmorPierce(4, 1f, targetHasArmor: true));
        }

        [Test]
        public void ArmorPierce_RoundsHalfAwayFromZero()
        {
            // 5 * 1.5 = 7.5 → 8.
            Assert.AreEqual(8, CombatBonus.WithArmorPierce(5, 1.5f, targetHasArmor: true));
        }

        // --- Siege (Minotaur bonus vs buildings, extra vs defenses) ---

        [Test]
        public void VsBuilding_NormalBuilding_UsesBuildingBonus()
        {
            // 20 * 2.5 = 50 vs a normal building.
            Assert.AreEqual(50, CombatBonus.EffectiveVsBuilding(20, 2.5f, 4f, targetIsDefensive: false));
        }

        [Test]
        public void VsBuilding_DefensiveBuilding_UsesLargerDefenseBonus()
        {
            // Walls/towers take the bigger multiplier: 20 * 4 = 80.
            Assert.AreEqual(80, CombatBonus.EffectiveVsBuilding(20, 2.5f, 4f, targetIsDefensive: true));
        }

        [Test]
        public void VsBuilding_NoBonus_ReturnsBase()
        {
            Assert.AreEqual(20, CombatBonus.EffectiveVsBuilding(20, 1f, 1f, targetIsDefensive: true));
        }

        [Test]
        public void VsBuilding_DefensiveNeverLessThanNormal()
        {
            // Even if the defense multiplier is left at 1, a defensive target uses the (larger) building bonus.
            Assert.AreEqual(40, CombatBonus.EffectiveVsBuilding(20, 2f, 1f, targetIsDefensive: true));
        }
    }
}

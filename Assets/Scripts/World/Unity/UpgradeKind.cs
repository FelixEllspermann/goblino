// UpgradeKind.cs — enum of all purchasable one-time upgrades.
// byte-backed so it fits in a single wire byte for CmdPurchaseUpgrade.
// Values are fixed; never reorder or the network protocol breaks mid-session.
// To add an upgrade: append a new value, add effects in UpgradeEffects, and add UI + network wiring.
namespace RTSCL.World.Unity
{
    /// <summary>Identifies a purchasable one-time upgrade. Byte-valued for compact network serialisation.</summary>
    public enum UpgradeKind : byte
    {
        FarmerHarvestSpeed = 0, // Farmer goblins harvest 25 % faster
        ClubAttackDamage   = 1, // Club goblins deal 25 % more damage per hit
        ClubMaxHp          = 2, // Club goblins gain 50 % more max HP
        MillBountifulHarvest = 3, // Wheat fields YOU build yield +100 % food (1000 instead of 500)
        // --- Mill (economy) ---
        FarmerMoveSpeed     = 4, // Farmer goblins move 25 % faster
        FarmerCarryCapacity = 5, // Farmer goblins carry 50 % more per trip
        BuildSpeed          = 6, // Farmer goblins construct buildings 50 % faster
        // --- Workshop (military) ---
        MeleeArmor          = 7, // Melee units (Club, Spear) gain +5 armor points
        RangedAttackRange   = 8, // Ranged units (Archer) gain +1 attack range
        AllUnitsDamage      = 9, // All combat units deal 25 % more damage
        UnitTrainSpeed      = 10, // Units train 25 % faster (per-owner production speed)
    }
}

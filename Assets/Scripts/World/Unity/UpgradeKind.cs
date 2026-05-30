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
    }
}

// UpgradeDefinition.cs — ScriptableObject asset that defines a single research upgrade.
// Upgrades are referenced by BuildingDefinition.ProvidesUpgrades (e.g. Workshop) and
// purchased via NetCommandIssuer.IssuePurchaseUpgrade → PlayerUpgrades.MarkPurchased.
//
// To add a new upgrade:
//   1. Create asset via RTSCL/Upgrade Definition.
//   2. Add a matching UpgradeKind enum value.
//   3. Implement the effect in UpgradeEffects.ApplyToOwnedUnits.
//   4. Reference the asset from the relevant BuildingDefinition.ProvidesUpgrades list.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Asset-driven definition for a one-time research upgrade purchasable at a building.
    /// Values live in the .asset file; edit them in the Inspector.
    /// The <see cref="Kind"/> enum value is sent over the wire as a byte (cast to byte in NetWireFormat).</summary>
    [CreateAssetMenu(menuName = "RTSCL/Upgrade Definition", fileName = "Upgrade")]
    public sealed class UpgradeDefinition : ScriptableObject
    {
        /// <summary>Human-readable name shown in upgrade card UI.</summary>
        public string DisplayName;

        /// <summary>Icon displayed in the upgrade card.</summary>
        public Sprite Icon;

        // --- Purchase costs (deducted from ResourceBank on purchase) ---
        public int WoodCost = 0;
        public int IronCost = 0;
        public int GoldCost = 0;
        public int CrystalCost = 0;

        /// <summary>Enum tag identifying which effect to apply in UpgradeEffects.
        /// Cast to byte on the wire; keep values &lt; 256.</summary>
        public UpgradeKind Kind;
    }
}

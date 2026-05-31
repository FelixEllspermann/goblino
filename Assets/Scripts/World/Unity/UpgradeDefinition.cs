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

        /// <summary>How many times this upgrade can be purchased (1 = single one-time upgrade; &gt;1 = leveled,
        /// e.g. archer range L1→L2→L3). The cost of each level scales: level N costs the base cost × N.</summary>
        [Tooltip("Number of purchasable levels (1 = one-time). Cost of level N = base cost × N.")]
        public int MaxLevel = 1;

        // Cost to buy a given level (1-based). Level 1 = base cost, level 2 = 2×, level 3 = 3× …
        public int WoodFor(int level)    => WoodCost    * Mathf.Max(1, level);
        public int IronFor(int level)    => IronCost    * Mathf.Max(1, level);
        public int GoldFor(int level)    => GoldCost    * Mathf.Max(1, level);
        public int CrystalFor(int level) => CrystalCost * Mathf.Max(1, level);
    }
}

// BuildingDefinition.cs — ScriptableObject asset that defines a single building type.
// Each building is an asset under Assets/Data/Buildings/ (or wherever the project stores them).
// They are referenced by BuildingCatalog, BuildingPlacer, and NetworkCatalog (which maps them
// to a byte index for wire messages).
//
// To add a new building:
//   1. Create asset via RTSCL/Building Definition in the Project menu.
//   2. Fill in Footprint, costs, TrainsUnits, ProvidesUpgrades.
//   3. Add the asset to BuildingCatalog.asset — order matters (NetworkCatalog uses list index as wire byte).
//   4. Assign a prefab/sprite; ensure WoodCost matches design intent.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Asset-driven definition for a placeable building.
    /// Values live in the .asset file; edit them in the Inspector, not in code.
    /// NetworkCatalog maps BuildingDefinition ↔ byte index for the wire protocol.</summary>
    [CreateAssetMenu(menuName = "RTSCL/Building Definition", fileName = "Building")]
    public sealed class BuildingDefinition : ScriptableObject
    {
        /// <summary>Human-readable name shown in UI cards and inspector.</summary>
        public string DisplayName;

        /// <summary>Full building sprite rendered by the placed SpriteRenderer.</summary>
        public Sprite Sprite;                          // full building sprite (one PNG)

        /// <summary>Docks only: the pier sprite (Docks_1) drawn on the adjacent water cell.</summary>
        public Sprite WaterSprite;

        /// <summary>Size of the building in world cells (x = width, y = height).</summary>
        public Vector2Int Footprint = Vector2Int.one;  // in cells

        // --- Construction costs (deducted from ResourceBank at placement time) ---
        public int WoodCost = 50;
        public int StoneCost = 0;

        /// <summary>Population cap added globally when construction of this building completes.
        /// Hut = 5. Adjust in the asset; PopulationManager reads this after BuildingConstruction fires OnCompleted.</summary>
        [Tooltip("Population cap added when this building's construction completes")]
        public int PopulationProvided = 0;

        /// <summary>Unit definitions this building can train (e.g. Keep→Farmer, Barracks→Club).
        /// Shown as cards in BuildingPaletteUI. Empty = no production.</summary>
        [Tooltip("Units that this building can train (Keep→Farmer, Barracks→Club, etc.)")]
        public GoblinUnitDefinition[] TrainsUnits;

        /// <summary>Upgrade definitions purchasable from this building (e.g. Workshop upgrades).
        /// Empty = no upgrades available.</summary>
        [Tooltip("Upgrades that this building can purchase (Workshop)")]
        public UpgradeDefinition[] ProvidesUpgrades;

        /// <summary>Build-menu category this building appears under (groups the sidebar tabs).</summary>
        [Tooltip("Which build-menu category/tab this building appears under.")]
        public BuildCategory Category = BuildCategory.Economy;

        /// <summary>Buildings that must be owned + finished before this one can be built.
        /// Empty = always buildable. Drives the build menu's locked/greyed state (tech-tree foundation).</summary>
        [Tooltip("Prerequisite buildings (must be owned + finished). Empty = always buildable.")]
        public BuildingDefinition[] Requires;
    }
}

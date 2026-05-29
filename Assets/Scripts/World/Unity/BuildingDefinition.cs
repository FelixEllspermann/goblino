using UnityEngine;

namespace RTSCL.World.Unity
{
    [CreateAssetMenu(menuName = "RTSCL/Building Definition", fileName = "Building")]
    public sealed class BuildingDefinition : ScriptableObject
    {
        public string DisplayName;
        public Sprite Sprite;                          // full building sprite (one PNG)
        public Vector2Int Footprint = Vector2Int.one;  // in cells
        public int WoodCost = 50;
        public int StoneCost = 0;
        [Tooltip("Population cap added when this building's construction completes")]
        public int PopulationProvided = 0;
        [Tooltip("Units that this building can train (Keep→Farmer, Barracks→Club, etc.)")]
        public GoblinUnitDefinition[] TrainsUnits;
        [Tooltip("Upgrades that this building can purchase (Workshop)")]
        public UpgradeDefinition[] ProvidesUpgrades;
    }
}

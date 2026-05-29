// BuildingCatalog.cs — ScriptableObject asset that lists all reachable BuildingDefinitions.
// There is one shared catalog asset (Assets/Data/BuildingCatalog.asset) referenced by:
//   - BuildingPaletteUI (shows cards for placeable buildings)
//   - NetworkCatalog.PopulateFromCatalog() (called once by MainBaseSetup.OnNewWorld)
//
// IMPORTANT: The list ORDER determines the byte index used in wire messages (CmdPlaceBuilding).
// Never reorder entries without a matching protocol version bump that all clients agree on.
// To add a new building: append it to the end of the list in the Inspector.
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Catalog ScriptableObject: the authoritative ordered list of all BuildingDefinitions.
    /// List index == wire byte index used in CmdPlaceBuilding. Populate via the Inspector asset.</summary>
    [CreateAssetMenu(menuName = "RTSCL/Building Catalog", fileName = "BuildingCatalog")]
    public sealed class BuildingCatalog : ScriptableObject
    {
        /// <summary>Ordered list of building definitions. Index 0 → wire byte 0, index 1 → wire byte 1, etc.
        /// All clients must reference the same asset with identical ordering.</summary>
        public List<BuildingDefinition> Buildings = new();
    }
}

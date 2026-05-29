// NetworkCatalog.cs — Runtime mapping between ScriptableObject references and stable byte indices
// used in the wire protocol. Lives in RTSCL.World.Unity (no Steamworks dependency).
//
// WHY: Wire messages cannot carry full asset references — they use 1-byte indices instead.
// All clients agree on the same indices because they all load the same BuildingCatalog asset
// in the same order. The catalog is populated once per session by MainBaseSetup.OnNewWorld.
//
// Building index source: BuildingCatalog.Buildings list order.
// Unit index source: derived from BuildingDefinition.TrainsUnits arrays, visited in catalog order.
//   A unit appearing in multiple buildings is registered once (first occurrence wins).
//
// Byte indices are limited to 0–255 (byte). If catalog exceeds 256 items, excess are silently dropped.
// Max wire payload for CmdPlaceBuilding / CmdTrainUnit is 18–20 bytes regardless of catalog size.
//
// To add a building: append to BuildingCatalog.Buildings in the Inspector (never reorder existing entries).
// To add a unit: add to a BuildingDefinition.TrainsUnits array; index is assigned on next PopulateFromCatalog call.
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Static index ↔ definition lookups. Populated once per world by
    /// <see cref="MainBaseSetup"/>. All clients see the same indices because
    /// they all run against the same shipped assets in the same BuildingCatalog order.</summary>
    public static class NetworkCatalog
    {
        // Parallel lists: index == wire byte value.
        private static readonly List<BuildingDefinition> _buildings = new();
        private static readonly List<GoblinUnitDefinition> _units = new();

        // Reverse maps for IssueX → byte lookup.
        private static readonly Dictionary<BuildingDefinition, byte> _buildingToIdx = new();
        private static readonly Dictionary<GoblinUnitDefinition, byte> _unitToIdx = new();

        /// <summary>Clears and repopulates the catalog from a BuildingCatalog asset.
        /// Called once by MainBaseSetup.OnNewWorld at scene start.
        /// Building index = position in catalog.Buildings list (skipping nulls).
        /// Unit index = first-occurrence order across all BuildingDefinition.TrainsUnits arrays.</summary>
        public static void PopulateFromCatalog(BuildingCatalog catalog)
        {
            _buildings.Clear();
            _buildingToIdx.Clear();
            _units.Clear();
            _unitToIdx.Clear();

            if (catalog == null) return;

            for (int i = 0; i < catalog.Buildings.Count && i < 256; i++)
            {
                var b = catalog.Buildings[i];
                if (b == null) continue;
                byte idx = (byte)_buildings.Count;
                _buildings.Add(b);
                _buildingToIdx[b] = idx;

                // Register units from this building's TrainsUnits in encounter order.
                // A unit that appears in multiple buildings keeps its first-encountered index.
                if (b.TrainsUnits == null) continue;
                foreach (var u in b.TrainsUnits)
                {
                    if (u == null) continue;
                    if (_unitToIdx.ContainsKey(u)) continue; // already registered
                    if (_units.Count >= 256) continue;        // byte index overflow guard
                    byte uidx = (byte)_units.Count;
                    _units.Add(u);
                    _unitToIdx[u] = uidx;
                }
            }
        }

        /// <summary>Returns the wire byte index for a BuildingDefinition, or false if not registered.</summary>
        public static bool TryGetBuildingIndex(BuildingDefinition def, out byte idx) =>
            _buildingToIdx.TryGetValue(def, out idx);

        /// <summary>Returns the BuildingDefinition for a wire byte index, or null if out of range.</summary>
        public static BuildingDefinition GetBuilding(byte idx) =>
            idx < _buildings.Count ? _buildings[idx] : null;

        /// <summary>Returns the wire byte index for a GoblinUnitDefinition, or false if not registered.</summary>
        public static bool TryGetUnitIndex(GoblinUnitDefinition def, out byte idx) =>
            _unitToIdx.TryGetValue(def, out idx);

        /// <summary>Returns the GoblinUnitDefinition for a wire byte index, or null if out of range.</summary>
        public static GoblinUnitDefinition GetUnit(byte idx) =>
            idx < _units.Count ? _units[idx] : null;
    }
}

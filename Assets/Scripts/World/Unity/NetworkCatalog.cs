using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Static index ↔ definition lookups. Populated once per world by
    /// <see cref="MainBaseSetup"/>. All clients see the same indices because
    /// they all run against the same shipped assets.</summary>
    public static class NetworkCatalog
    {
        private static readonly List<BuildingDefinition> _buildings = new();
        private static readonly List<GoblinUnitDefinition> _units = new();
        private static readonly Dictionary<BuildingDefinition, byte> _buildingToIdx = new();
        private static readonly Dictionary<GoblinUnitDefinition, byte> _unitToIdx = new();

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

                if (b.TrainsUnits == null) continue;
                foreach (var u in b.TrainsUnits)
                {
                    if (u == null) continue;
                    if (_unitToIdx.ContainsKey(u)) continue;
                    if (_units.Count >= 256) continue;
                    byte uidx = (byte)_units.Count;
                    _units.Add(u);
                    _unitToIdx[u] = uidx;
                }
            }
        }

        public static bool TryGetBuildingIndex(BuildingDefinition def, out byte idx) =>
            _buildingToIdx.TryGetValue(def, out idx);

        public static BuildingDefinition GetBuilding(byte idx) =>
            idx < _buildings.Count ? _buildings[idx] : null;

        public static bool TryGetUnitIndex(GoblinUnitDefinition def, out byte idx) =>
            _unitToIdx.TryGetValue(def, out idx);

        public static GoblinUnitDefinition GetUnit(byte idx) =>
            idx < _units.Count ? _units[idx] : null;
    }
}

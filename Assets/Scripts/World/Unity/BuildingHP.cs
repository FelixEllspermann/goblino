// BuildingHP.cs — static store of (max, current) HP for every placed building.
// Keyed by the building's origin cell (bottom-left of its footprint, same key used by
// BuildingPlacer._cellOwners and BuildingConstruction). All three stores share this key.
// Reset on new world via MainBaseSetup.OnNewWorld → Clear().
// To add HP for a new building type: extend MaxHpFor() with a new name-prefix branch.
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    public static class BuildingHP
    {
        // Keyed by the building's origin cell (bottom-left of footprint).
        // Value is a value-tuple so Damage can update current without boxing.
        private static readonly Dictionary<Vector2Int, (int max, int current)> _hp = new();

        /// <summary>
        /// Returns the design-time max HP for a building based on its asset name prefix.
        /// To tune durability: adjust the constants in each branch, or add new prefixes.
        /// Falls back to 150 for unrecognised types (Huts, Markets, etc.).
        /// </summary>
        public static int MaxHpFor(BuildingDefinition def)
        {
            if (def == null) return 100;
            if (def.name.StartsWith("Keep")) return 500;
            if (def.name.StartsWith("HordeHall")) return 400;
            if (def.name.StartsWith("Wall")) return 300;   // tanky but cheap; meant to be spammed
            if (def.name.StartsWith("Tower")) return 250;
            if (def.name.StartsWith("Barracks")) return 350;
            return 150; // default for Hut, Market, and other structures
        }

        /// <summary>
        /// Register a newly-placed building at full HP.
        /// Called by BuildingPlacer after a building is placed (construction start).
        /// </summary>
        public static void Register(Vector2Int originCell, int max)
        {
            _hp[originCell] = (max, max);
        }

        /// <summary>
        /// Retrieve current and max HP for the building at <paramref name="originCell"/>.
        /// Returns false (and zeros) when no building is registered there.
        /// </summary>
        public static bool TryGet(Vector2Int originCell, out int current, out int max)
        {
            if (_hp.TryGetValue(originCell, out var v)) { current = v.current; max = v.max; return true; }
            current = 0; max = 0; return false;
        }

        /// <summary>Apply damage to the building at <paramref name="originCell"/>; clamps to zero. No death side-effect here — caller is responsible for destroy logic.</summary>
        public static void Damage(Vector2Int originCell, int amount)
        {
            if (!_hp.TryGetValue(originCell, out var v)) return;
            int cur = Mathf.Max(0, v.current - amount);
            _hp[originCell] = (v.max, cur);
        }

        /// <summary>Scale a building's max HP (and current proportionally) by <paramref name="factor"/>.
        /// Used by the Wall-HP upgrade. No-op for unknown origins.</summary>
        public static void ScaleMax(Vector2Int originCell, float factor)
        {
            if (factor <= 0f || !_hp.TryGetValue(originCell, out var v)) return;
            int newMax = Mathf.Max(1, Mathf.RoundToInt(v.max * factor));
            int newCur = Mathf.Clamp(Mathf.RoundToInt(v.current * factor), 1, newMax);
            _hp[originCell] = (newMax, newCur);
        }

        /// <summary>Unregister a destroyed building. Call before or alongside GameObject.Destroy.</summary>
        public static void Remove(Vector2Int originCell) => _hp.Remove(originCell);

        /// <summary>Wipe all building HP entries. Called when a new world is generated.</summary>
        public static void Clear() => _hp.Clear();
    }
}

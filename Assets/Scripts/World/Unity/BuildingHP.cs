using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    public static class BuildingHP
    {
        // Keyed by the building's origin cell (bottom-left of footprint)
        private static readonly Dictionary<Vector2Int, (int max, int current)> _hp = new();

        public static int MaxHpFor(BuildingDefinition def)
        {
            if (def == null) return 100;
            if (def.name.StartsWith("Keep")) return 500;
            if (def.name.StartsWith("Tower")) return 250;
            if (def.name.StartsWith("Barracks")) return 350;
            return 150;
        }

        public static void Register(Vector2Int originCell, int max)
        {
            _hp[originCell] = (max, max);
        }

        public static bool TryGet(Vector2Int originCell, out int current, out int max)
        {
            if (_hp.TryGetValue(originCell, out var v)) { current = v.current; max = v.max; return true; }
            current = 0; max = 0; return false;
        }

        public static void Damage(Vector2Int originCell, int amount)
        {
            if (!_hp.TryGetValue(originCell, out var v)) return;
            int cur = Mathf.Max(0, v.current - amount);
            _hp[originCell] = (v.max, cur);
        }

        public static void Remove(Vector2Int originCell) => _hp.Remove(originCell);

        public static void Clear() => _hp.Clear();
    }
}

// WallStrength.cs — per-owner wall HP multiplier (Workshop "Wall HP" upgrade).
// Applied to a wall's max HP at placement (BuildingPlacer) and retroactively to existing walls when
// the upgrade is purchased (UpgradeEffects). Default 1. Reset on new world via MainBaseSetup.OnNewWorld.
using System.Collections.Generic;

namespace RTSCL.World.Unity
{
    /// <summary>Per-owner wall max-HP multiplier. 1 = base.</summary>
    public static class WallStrength
    {
        private static readonly Dictionary<ulong, float> _mul = new();
        public static float Get(ulong owner) => _mul.TryGetValue(owner, out var m) ? m : 1f;
        public static void Multiply(ulong owner, float mul) => _mul[owner] = System.Math.Max(0.01f, Get(owner) * mul);
        public static void Clear() => _mul.Clear();
    }
}

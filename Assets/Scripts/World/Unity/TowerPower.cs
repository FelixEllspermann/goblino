// TowerPower.cs — per-owner tower damage multiplier (Workshop "Tower Damage" upgrade).
// TowerCombat reads this at fire time, so the bonus applies to existing AND future towers with no
// retroactive plumbing. Default 1 (base damage). Reset on new world via MainBaseSetup.OnNewWorld.
using System.Collections.Generic;

namespace RTSCL.World.Unity
{
    /// <summary>Per-owner tower arrow-damage multiplier. 1 = base.</summary>
    public static class TowerPower
    {
        private static readonly Dictionary<ulong, float> _mul = new();
        public static float Get(ulong owner) => _mul.TryGetValue(owner, out var m) ? m : 1f;
        public static void Multiply(ulong owner, float mul) => _mul[owner] = System.Math.Max(0.01f, Get(owner) * mul);
        public static void Clear() => _mul.Clear();
    }
}

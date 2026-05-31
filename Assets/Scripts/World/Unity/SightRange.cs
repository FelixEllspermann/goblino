// SightRange.cs — per-owner vision-radius multiplier (Keep "Watchtowers" upgrade).
// Like TrainingSpeed, this is an owner-level multiplier (not a per-unit stat):
//   - FogOfWar scales the local player's goblin/building/keep reveal radii by it;
//   - BotController scales its own scouting/vision radii by it.
// Default 1 (base sight). Reset on new world via MainBaseSetup.OnNewWorld → Clear().
using System.Collections.Generic;

namespace RTSCL.World.Unity
{
    /// <summary>Per-owner vision-range multiplier. 1 = base; >1 = sees farther.</summary>
    public static class SightRange
    {
        private static readonly Dictionary<ulong, float> _mul = new();

        /// <summary>Vision multiplier for <paramref name="owner"/> (1 if no upgrade purchased).</summary>
        public static float Get(ulong owner) => _mul.TryGetValue(owner, out var m) ? m : 1f;

        /// <summary>Compound a multiplier onto the owner's sight range.</summary>
        public static void Multiply(ulong owner, float mul) => _mul[owner] = System.Math.Max(0.01f, Get(owner) * mul);

        /// <summary>Scale a base radius by the owner's sight multiplier (rounded, min 1).</summary>
        public static int Scale(int baseRadius, ulong owner) =>
            System.Math.Max(1, (int)System.Math.Round(baseRadius * (double)Get(owner)));

        /// <summary>Wipe all sight-range state. Called when a new world is generated.</summary>
        public static void Clear() => _mul.Clear();
    }
}

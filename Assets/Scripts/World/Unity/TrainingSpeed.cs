// TrainingSpeed.cs — per-owner unit-training speed multiplier (Workshop "Drill Sergeant" upgrade).
// Unlike the per-unit stat upgrades (UpgradeEffects), training speed is an owner-level production rate:
//   - the player's queued production (GoblinProduction.Tick) advances elapsed time by this factor;
//   - the bot's train timers (BotController) are divided by it.
// Default 1 (no speedup). Reset on new world via MainBaseSetup.OnNewWorld → Clear().
using System.Collections.Generic;

namespace RTSCL.World.Unity
{
    /// <summary>Per-owner training-speed multiplier. 1 = base; >1 = faster unit production.</summary>
    public static class TrainingSpeed
    {
        private static readonly Dictionary<ulong, float> _mul = new();

        /// <summary>Training-speed factor for <paramref name="owner"/> (1 if no upgrade purchased).</summary>
        public static float Get(ulong owner) => _mul.TryGetValue(owner, out var m) ? m : 1f;

        /// <summary>Compound a multiplier onto the owner's training speed (clamped to a sane minimum).</summary>
        public static void Multiply(ulong owner, float mul) => _mul[owner] = System.Math.Max(0.01f, Get(owner) * mul);

        /// <summary>Wipe all training-speed state. Called when a new world is generated.</summary>
        public static void Clear() => _mul.Clear();
    }
}

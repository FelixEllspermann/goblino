// PlayerUpgrades.cs — records the LEVEL each player (owner SteamID) has reached for each upgrade.
// Most upgrades are single-level (level 0 = not bought, 1 = bought); some (e.g. archer range) have
// multiple levels. Applying effects is handled by UpgradeEffects. Synced across clients via
// CmdPurchaseUpgrade (each purchase = one LevelUp on every client). Reset on new world via OnNewWorld.
using System.Collections.Generic;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks per-owner upgrade LEVELS. Synced across clients via CmdPurchaseUpgrade.</summary>
    public static class PlayerUpgrades
    {
        private static readonly Dictionary<(ulong owner, UpgradeKind kind), int> _levels = new();

        /// <summary>Current level of <paramref name="kind"/> for <paramref name="owner"/> (0 = not bought).</summary>
        public static int Level(ulong owner, UpgradeKind kind) =>
            _levels.TryGetValue((owner, kind), out var l) ? l : 0;

        /// <summary>True if at least level 1 has been bought.</summary>
        public static bool IsPurchased(ulong owner, UpgradeKind kind) => Level(owner, kind) > 0;

        /// <summary>Raise the level by one and return the new level. Caller applies effects afterwards.</summary>
        public static int LevelUp(ulong owner, UpgradeKind kind)
        {
            int lvl = Level(owner, kind) + 1;
            _levels[(owner, kind)] = lvl;
            return lvl;
        }

        /// <summary>Back-compat alias: records one more level.</summary>
        public static void MarkPurchased(ulong owner, UpgradeKind kind) => LevelUp(owner, kind);

        /// <summary>Wipe all upgrade records. Called when a new world is generated.</summary>
        public static void Reset() => _levels.Clear();

        /// <summary>Every (kind, level) this owner holds. Used by UpgradeEffects.ApplyExistingTo to
        /// re-apply each upgrade once per level to a freshly-spawned unit.</summary>
        public static IEnumerable<(UpgradeKind kind, int level)> AllForWithLevel(ulong owner)
        {
            foreach (var kv in _levels)
                if (kv.Key.owner == owner && kv.Value > 0) yield return (kv.Key.kind, kv.Value);
        }
    }
}

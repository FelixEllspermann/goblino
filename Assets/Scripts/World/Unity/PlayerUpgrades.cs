// PlayerUpgrades.cs — records which one-time upgrades each player (owner SteamID) has purchased.
// Upgrades are global and permanent for the match; applying effects to units is handled by UpgradeEffects.
// Key: (owner ulong, UpgradeKind) tuple — HashSet for O(1) membership check.
// In multiplayer, NetCommandApplier calls MarkPurchased on every client after CmdPurchaseUpgrade.
// Reset on new world via MainBaseSetup.OnNewWorld → Reset().
using System.Collections.Generic;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks one-time-purchase upgrades per owner. Synced across clients via CmdPurchaseUpgrade.</summary>
    public static class PlayerUpgrades
    {
        // Composite key (owner, kind) so different players' upgrades don't collide.
        private static readonly HashSet<(ulong owner, UpgradeKind kind)> _purchased = new();

        /// <summary>Returns true if <paramref name="owner"/> has already purchased <paramref name="kind"/>.</summary>
        public static bool IsPurchased(ulong owner, UpgradeKind kind) =>
            _purchased.Contains((owner, kind));

        /// <summary>
        /// Record that <paramref name="owner"/> purchased <paramref name="kind"/>.
        /// Does NOT apply effects to existing units — call UpgradeEffects.ApplyToOwnedUnits() after.
        /// </summary>
        public static void MarkPurchased(ulong owner, UpgradeKind kind) =>
            _purchased.Add((owner, kind));

        /// <summary>Wipe all purchase records. Called when a new world is generated.</summary>
        public static void Reset() => _purchased.Clear();

        /// <summary>Enumerates every UpgradeKind purchased by <paramref name="owner"/>. Used by UpgradeEffects.ApplyExistingTo() when a fresh unit spawns.</summary>
        public static IEnumerable<UpgradeKind> AllFor(ulong owner)
        {
            foreach (var (o, k) in _purchased)
                if (o == owner) yield return k;
        }
    }
}

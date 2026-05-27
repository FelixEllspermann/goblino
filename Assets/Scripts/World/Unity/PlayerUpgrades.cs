using System.Collections.Generic;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks one-time-purchase upgrades per owner. Synced via CmdPurchaseUpgrade.</summary>
    public static class PlayerUpgrades
    {
        private static readonly HashSet<(ulong owner, UpgradeKind kind)> _purchased = new();

        public static bool IsPurchased(ulong owner, UpgradeKind kind) =>
            _purchased.Contains((owner, kind));

        public static void MarkPurchased(ulong owner, UpgradeKind kind) =>
            _purchased.Add((owner, kind));

        public static void Reset() => _purchased.Clear();

        public static IEnumerable<UpgradeKind> AllFor(ulong owner)
        {
            foreach (var (o, k) in _purchased)
                if (o == owner) yield return k;
        }
    }
}

// UpgradeCatalog.cs — maps UpgradeKind → UpgradeDefinition so code that only has the kind (network
// applier, bot, BuildMenu) can look up MaxLevel + per-level costs. Populated from every building's
// ProvidesUpgrades by MainBaseSetup.OnNewWorld. Reset there too.
using System.Collections.Generic;

namespace RTSCL.World.Unity
{
    /// <summary>Kind → UpgradeDefinition lookup (MaxLevel, costs). Populated at world setup.</summary>
    public static class UpgradeCatalog
    {
        private static readonly Dictionary<UpgradeKind, UpgradeDefinition> _byKind = new();

        public static void Clear() => _byKind.Clear();

        /// <summary>Register every upgrade referenced by the catalog's buildings.</summary>
        public static void PopulateFromCatalog(BuildingCatalog catalog)
        {
            _byKind.Clear();
            if (catalog == null) return;
            foreach (var b in catalog.Buildings)
            {
                if (b == null || b.ProvidesUpgrades == null) continue;
                foreach (var u in b.ProvidesUpgrades)
                    if (u != null) _byKind[u.Kind] = u;
            }
        }

        public static UpgradeDefinition Get(UpgradeKind kind) => _byKind.TryGetValue(kind, out var u) ? u : null;

        /// <summary>Max purchasable level for a kind (1 if unknown).</summary>
        public static int MaxLevel(UpgradeKind kind) => _byKind.TryGetValue(kind, out var u) ? System.Math.Max(1, u.MaxLevel) : 1;
    }
}

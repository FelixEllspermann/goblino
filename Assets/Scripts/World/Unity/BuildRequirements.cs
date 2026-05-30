// BuildRequirements.cs — tech-tree gate for the build menu. A building is unlocked when all of its
// BuildingDefinition.Requires entries are owned + finished. The ownership check is injected so this stays
// pure-logic + unit-testable; BuildMenu supplies the real BuildingPlacer/BuildingConstruction lookup.
using System;

namespace RTSCL.World.Unity
{
    /// <summary>Decides whether a building is unlocked given which prerequisite buildings are owned+built.</summary>
    public static class BuildRequirements
    {
        /// <summary>True if <paramref name="def"/> has no prerequisites, or every prerequisite passes
        /// <paramref name="ownsCompleted"/> (owned by the player AND finished constructing).</summary>
        public static bool IsUnlocked(BuildingDefinition def, Func<BuildingDefinition, bool> ownsCompleted)
        {
            if (def == null) return false;
            if (def.Requires == null || def.Requires.Length == 0) return true;
            foreach (var req in def.Requires)
            {
                if (req == null) continue;                // null entry = ignore
                if (ownsCompleted == null || !ownsCompleted(req)) return false;
            }
            return true;
        }
    }
}

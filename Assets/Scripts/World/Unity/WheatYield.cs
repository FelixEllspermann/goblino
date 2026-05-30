// WheatYield.cs — per-cell food yield for harvestable wheat-field decorations.
// A wheat field normally yields 500 food (its MaxHp). When the field's BUILDER has researched the Mill's
// "Bountiful Harvest" upgrade, the resulting field is marked here as a boosted cell and yields 1000 instead.
// Because the bonus is baked into the cell at construction time (based on who built it), it is naturally
// per-player: a player/bot without the upgrade still gets normal 500-food fields.
// Reset on new world via MainBaseSetup.OnNewWorld → Clear().
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks which wheat-field cells were built with the Bountiful Harvest bonus (×2 food).</summary>
    public static class WheatYield
    {
        /// <summary>Base wheat-field food yield (matches Goblin.MaxHpFor for Wheatfield tiles).</summary>
        public const int BaseYield = 500;
        /// <summary>Yield multiplier granted by the Mill's Bountiful Harvest upgrade.</summary>
        public const float BonusMul = 2f;

        private static readonly HashSet<Vector3Int> _boosted = new();

        /// <summary>Mark a wheat-field cell as boosted (built by an owner who has the upgrade).</summary>
        public static void MarkBoosted(Vector3Int cell) => _boosted.Add(cell);

        /// <summary>True if this cell yields the boosted amount.</summary>
        public static bool IsBoosted(Vector3Int cell) => _boosted.Contains(cell);

        /// <summary>Max food for a wheat-field cell: boosted cells return BaseYield×BonusMul, else fallback.</summary>
        public static int MaxFor(Vector3Int cell, int fallback) =>
            _boosted.Contains(cell) ? Mathf.RoundToInt(BaseYield * BonusMul) : fallback;

        /// <summary>Wipe all boosted-cell records. Called when a new world is generated.</summary>
        public static void Clear() => _boosted.Clear();
    }
}

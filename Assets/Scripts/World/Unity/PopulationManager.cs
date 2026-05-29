// PopulationManager.cs — single source of truth for used/cap population.
// Cap = BaseCap (20) + sum of PopulationProvided by all completed buildings (e.g. +5 per Hut).
// Used = sum of PopulationCost of living units + units currently in production (reserved at train-start).
// OnChanged fires on every mutation so ResourceUI / HUD can update without polling.
// Reset on new world via MainBaseSetup.OnNewWorld → Reset().
// To change starting cap: adjust BaseCap. Per-building bonus comes from BuildingDefinition.PopulationProvided.
using System;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks population cap and currently-used population. Cap = BaseCap + sum of completed buildings' PopulationProvided.</summary>
    public static class PopulationManager
    {
        /// <summary>Starting population cap before any buildings are completed. Adjust here to tune early-game pressure.</summary>
        public const int BaseCap = 20;

        private static int _bonusCap;   // extra cap granted by completed buildings (e.g. +5 per Hut)
        private static int _used;       // sum of living unit costs + in-production reserved costs

        /// <summary>Total population cap: BaseCap plus any building bonuses.</summary>
        public static int Cap => BaseCap + _bonusCap;
        /// <summary>Population currently occupied by living and in-production units.</summary>
        public static int Used => _used;
        /// <summary>Remaining population slots available for new units.</summary>
        public static int Free => Cap - _used;

        /// <summary>Fires whenever Used or Cap changes. Subscribe in ResourceUI to refresh the pop counter.</summary>
        public static event Action OnChanged;

        /// <summary>Returns true if adding <paramref name="populationCost"/> would not exceed Cap.</summary>
        public static bool CanAfford(int populationCost) => _used + populationCost <= Cap;

        /// <summary>Reserve population when a unit is spawned or training begins.</summary>
        public static void AddUsed(int amount)
        {
            if (amount == 0) return;
            _used += amount;
            OnChanged?.Invoke();
        }

        /// <summary>Release population when a unit dies or production is cancelled.</summary>
        public static void RemoveUsed(int amount)
        {
            if (amount == 0) return;
            _used = Math.Max(0, _used - amount);
            OnChanged?.Invoke();
        }

        /// <summary>Increase the cap, typically called by BuildingConstruction.OnCompleted when a Hut finishes.</summary>
        public static void AddCap(int amount)
        {
            if (amount == 0) return;
            _bonusCap += amount;
            OnChanged?.Invoke();
        }

        /// <summary>Wipe both used and bonus-cap counters. Called when a new world is generated.</summary>
        public static void Reset()
        {
            _bonusCap = 0;
            _used = 0;
            OnChanged?.Invoke();
        }
    }
}

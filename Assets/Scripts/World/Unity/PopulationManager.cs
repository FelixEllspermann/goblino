using System;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks population cap and currently-used population. Cap = base + sum of completed buildings' PopulationProvided.</summary>
    public static class PopulationManager
    {
        public const int BaseCap = 20;

        private static int _bonusCap;   // from completed buildings
        private static int _used;       // sum of living + producing unit costs

        public static int Cap => BaseCap + _bonusCap;
        public static int Used => _used;
        public static int Free => Cap - _used;

        public static event Action OnChanged;

        public static bool CanAfford(int populationCost) => _used + populationCost <= Cap;

        public static void AddUsed(int amount)
        {
            if (amount == 0) return;
            _used += amount;
            OnChanged?.Invoke();
        }

        public static void RemoveUsed(int amount)
        {
            if (amount == 0) return;
            _used = Math.Max(0, _used - amount);
            OnChanged?.Invoke();
        }

        public static void AddCap(int amount)
        {
            if (amount == 0) return;
            _bonusCap += amount;
            OnChanged?.Invoke();
        }

        public static void Reset()
        {
            _bonusCap = 0;
            _used = 0;
            OnChanged?.Invoke();
        }
    }
}

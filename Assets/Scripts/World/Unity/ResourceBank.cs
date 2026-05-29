// ResourceBank.cs — global resource store, array-backed by ResourceKind index (0..5).
// All mutations go through Add(kind, amount); negative amounts deduct resources.
// OnChanged fires after every mutation so ResourceUI can update without polling.
// Reset fires OnChanged for every kind so UI resets cleanly on new-world / scene load.
// To add a new resource type: add a value to ResourceKind (keep count ≤ 6 or widen the array).
using System;

namespace RTSCL.World.Unity
{
    public static class ResourceBank
    {
        // Indexed by (int)ResourceKind — array length must match the number of ResourceKind values.
        private static readonly int[] _amounts = new int[6]; // Wood=0, Food=1, Stone=2, Gold=3, Iron=4, Crystal=5

        /// <summary>Fires (kind, newValue) on any change. Subscribe in ResourceUI to refresh counters.</summary>
        public static event Action<ResourceKind, int> OnChanged;

        /// <summary>Returns the current stored amount for the given resource kind.</summary>
        public static int Get(ResourceKind k) => _amounts[(int)k];

        /// <summary>
        /// Add (positive) or deduct (negative) resources of the given kind.
        /// Fires OnChanged after the mutation. No floor clamp — callers must check affordability first.
        /// </summary>
        public static void Add(ResourceKind k, int amount)
        {
            _amounts[(int)k] += amount;
            int v = _amounts[(int)k];
            OnChanged?.Invoke(k, v);
        }

        /// <summary>Zero all resource amounts and broadcast OnChanged for each kind. Called when a new world is generated.</summary>
        public static void Reset()
        {
            for (int i = 0; i < _amounts.Length; i++) _amounts[i] = 0;
            // Fire separately so subscribers see final state after all zeros are set.
            for (int i = 0; i < _amounts.Length; i++) OnChanged?.Invoke((ResourceKind)i, 0);
        }

        // Convenience accessors for the two most-used resources — avoids casting at call sites.
        public static int Wood => Get(ResourceKind.Wood);
        public static int Food => Get(ResourceKind.Food);
        public static void AddWood(int amount) => Add(ResourceKind.Wood, amount);
        public static void AddFood(int amount) => Add(ResourceKind.Food, amount);
    }
}

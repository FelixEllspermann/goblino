using System;

namespace RTSCL.World.Unity
{
    public static class ResourceBank
    {
        private static readonly int[] _amounts = new int[6]; // ResourceKind count

        /// <summary>Fires (kind, newValue) on any change.</summary>
        public static event Action<ResourceKind, int> OnChanged;

        // Legacy single-resource events — kept during migration, removed in Task 4.
        public static event Action<int> OnWoodChanged;
        public static event Action<int> OnFoodChanged;

        public static int Get(ResourceKind k) => _amounts[(int)k];

        public static void Add(ResourceKind k, int amount)
        {
            _amounts[(int)k] += amount;
            int v = _amounts[(int)k];
            OnChanged?.Invoke(k, v);
            if (k == ResourceKind.Wood) OnWoodChanged?.Invoke(v);
            else if (k == ResourceKind.Food) OnFoodChanged?.Invoke(v);
        }

        public static void Reset()
        {
            for (int i = 0; i < _amounts.Length; i++) _amounts[i] = 0;
            for (int i = 0; i < _amounts.Length; i++) OnChanged?.Invoke((ResourceKind)i, 0);
            OnWoodChanged?.Invoke(0);
            OnFoodChanged?.Invoke(0);
        }

        // Convenience for existing call sites.
        public static int Wood => Get(ResourceKind.Wood);
        public static int Food => Get(ResourceKind.Food);
        public static void AddWood(int amount) => Add(ResourceKind.Wood, amount);
        public static void AddFood(int amount) => Add(ResourceKind.Food, amount);
    }
}

using System;

namespace RTSCL.World.Unity
{
    public static class ResourceBank
    {
        public static int Wood { get; private set; }
        public static int Food { get; private set; }

        public static event Action<int> OnWoodChanged;
        public static event Action<int> OnFoodChanged;

        public static void AddWood(int amount)
        {
            Wood += amount;
            OnWoodChanged?.Invoke(Wood);
        }

        public static void AddFood(int amount)
        {
            Food += amount;
            OnFoodChanged?.Invoke(Food);
        }

        public static void Reset()
        {
            Wood = 0;
            Food = 0;
            OnWoodChanged?.Invoke(Wood);
            OnFoodChanged?.Invoke(Food);
        }
    }
}

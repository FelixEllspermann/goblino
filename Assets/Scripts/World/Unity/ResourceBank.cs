using System;

namespace RTSCL.World.Unity
{
    public static class ResourceBank
    {
        public static int Wood { get; private set; }

        public static event Action<int> OnWoodChanged;

        public static void AddWood(int amount)
        {
            Wood += amount;
            OnWoodChanged?.Invoke(Wood);
        }

        public static void Reset()
        {
            Wood = 0;
            OnWoodChanged?.Invoke(Wood);
        }
    }
}

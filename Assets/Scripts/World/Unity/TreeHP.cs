using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    public static class TreeHP
    {
        public const int MaxHP = 50;
        private static readonly Dictionary<Vector3Int, int> _hp = new();

        public static int GetHP(Vector3Int cell, int maxHp = MaxHP) =>
            _hp.TryGetValue(cell, out var hp) ? hp : maxHp;

        /// <summary>Returns remaining HP after the hit (0 = dead). `maxHp` defines lazy-init for unseen cells.</summary>
        public static int Hit(Vector3Int cell, int amount = 1, int maxHp = MaxHP)
        {
            int next = Mathf.Max(0, GetHP(cell, maxHp) - amount);
            if (next > 0) _hp[cell] = next;
            else _hp.Remove(cell);
            return next;
        }

        public static void Clear() => _hp.Clear();
    }
}

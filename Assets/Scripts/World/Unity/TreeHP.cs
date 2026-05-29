// TreeHP.cs — static store of remaining HP for every harvestable tilemap cell
// (trees, rocks, ore deposits, wheat fields). Keyed by Vector3Int tilemap cell.
// Lazy-initialised: an unseen cell is assumed to have full HP (MaxHP or caller-supplied maxHp).
// Reset on new world via MainBaseSetup.OnNewWorld → Clear().
// To change default resource-node durability: adjust MaxHP or pass a custom maxHp to Hit().
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    public static class TreeHP
    {
        /// <summary>Default maximum HP for a harvestable node. Adjust here to tune global resource durability.</summary>
        public const int MaxHP = 50;

        // Cells are only stored when their HP differs from maxHp (i.e. after first hit).
        // A missing key means the cell is at full HP.
        private static readonly Dictionary<Vector3Int, int> _hp = new();

        /// <summary>
        /// Returns the current HP of the cell.
        /// Cells not yet hit are treated as having <paramref name="maxHp"/> HP (lazy initialisation).
        /// </summary>
        public static int GetHP(Vector3Int cell, int maxHp = MaxHP) =>
            _hp.TryGetValue(cell, out var hp) ? hp : maxHp;

        /// <summary>Returns remaining HP after the hit (0 = dead). `maxHp` defines lazy-init for unseen cells.</summary>
        /// <param name="cell">Tilemap cell coordinates of the resource node.</param>
        /// <param name="amount">Damage dealt by one harvest strike.</param>
        /// <param name="maxHp">Full HP for this node type; only needed if it differs from MaxHP.</param>
        /// <returns>Remaining HP; 0 means the node is exhausted and should be removed from the tilemap.</returns>
        public static int Hit(Vector3Int cell, int amount = 1, int maxHp = MaxHP)
        {
            int next = Mathf.Max(0, GetHP(cell, maxHp) - amount);
            // Remove the key entirely when dead so dead cells don't bloat the dictionary.
            if (next > 0) _hp[cell] = next;
            else _hp.Remove(cell);
            return next;
        }

        /// <summary>Wipe all stored HP entries. Called when a new world is generated.</summary>
        public static void Clear() => _hp.Clear();
    }
}

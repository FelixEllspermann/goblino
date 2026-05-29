// BotEconomy.cs — Per-bot resource + population pools (keyed by owner id). Bots use this INSTEAD of
// the player's ResourceBank/PopulationManager, so a bot can never build or train without the real
// resources (no cheating). Solo-only. Seed() each bot at spawn; Reset() on a new world.
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Resource + population bookkeeping for AI bots. Not networked (solo-only).</summary>
    public static class BotEconomy
    {
        private const int BasePopCap = 20;
        private static readonly Dictionary<ulong, int[]> _res = new();   // index = (int)ResourceKind
        private static readonly Dictionary<ulong, int> _popUsed = new();
        private static readonly Dictionary<ulong, int> _popCap = new();

        /// <summary>Initialise a bot's economy with starting wood/food and the base population cap.</summary>
        public static void Seed(ulong owner, int wood, int food)
        {
            var r = new int[6];
            r[(int)ResourceKind.Wood] = wood;
            r[(int)ResourceKind.Food] = food;
            _res[owner] = r;
            _popUsed[owner] = 0;
            _popCap[owner] = BasePopCap;
        }

        /// <summary>All bot owner ids that currently have an economy (for the BotController to iterate).</summary>
        public static IEnumerable<ulong> Owners => _res.Keys;

        public static bool Has(ulong owner) => _res.ContainsKey(owner);
        public static int Get(ulong owner, ResourceKind k) => _res.TryGetValue(owner, out var r) ? r[(int)k] : 0;

        public static void Add(ulong owner, ResourceKind k, int amt)
        {
            if (!_res.TryGetValue(owner, out var r)) return;
            r[(int)k] = Mathf.Max(0, r[(int)k] + amt);
        }

        public static int PopUsed(ulong owner) => _popUsed.TryGetValue(owner, out var v) ? v : 0;
        public static int PopCap(ulong owner)  => _popCap.TryGetValue(owner, out var v) ? v : 0;
        public static void AddUsed(ulong owner, int n) { if (_popUsed.ContainsKey(owner)) _popUsed[owner] += n; }
        public static void RemoveUsed(ulong owner, int n) { if (_popUsed.ContainsKey(owner)) _popUsed[owner] = Mathf.Max(0, _popUsed[owner] - n); }
        public static void AddCap(ulong owner, int n) { if (_popCap.ContainsKey(owner)) _popCap[owner] += n; }
        public static bool CanAffordPop(ulong owner, int cost) => PopUsed(owner) + cost <= PopCap(owner);

        public static void Reset() { _res.Clear(); _popUsed.Clear(); _popCap.Clear(); }
    }
}

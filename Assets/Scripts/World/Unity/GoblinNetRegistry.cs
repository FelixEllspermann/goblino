using System.Collections.Generic;
using RTSCL.World;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks live goblins by NetId and hands out owner-scoped LocalIndex values.</summary>
    public static class GoblinNetRegistry
    {
        private static readonly Dictionary<GoblinNetId, Goblin> _byId = new();
        private static readonly Dictionary<ulong, ushort> _nextIndex = new();

        /// <summary>Reserve the next available LocalIndex for the given owner.</summary>
        public static ushort NextLocalIndex(ulong owner)
        {
            _nextIndex.TryGetValue(owner, out ushort idx);
            _nextIndex[owner] = (ushort)(idx + 1);
            return idx;
        }

        public static void Register(GoblinNetId id, Goblin g)
        {
            _byId[id] = g;
        }

        public static void Unregister(GoblinNetId id)
        {
            _byId.Remove(id);
        }

        public static bool TryGet(GoblinNetId id, out Goblin g) =>
            _byId.TryGetValue(id, out g);

        public static IEnumerable<Goblin> All => _byId.Values;

        public static void Reset()
        {
            _byId.Clear();
            _nextIndex.Clear();
        }
    }
}

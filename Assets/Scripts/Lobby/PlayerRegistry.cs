using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace RTSCL.Lobby
{
    /// <summary>Maps Steam IDs to per-game slot indices and faction colors.
    /// Populated by GameStartLoader from NetworkSession.PlayerSlots, or by
    /// MainBaseSetup in solo mode.</summary>
    public static class PlayerRegistry
    {
        // P0 Blue, P1 Red, P2 Yellow, P3 Green
        private static readonly Color[] _colors =
        {
            new(0.25f, 0.45f, 0.85f, 1f),
            new(0.85f, 0.30f, 0.30f, 1f),
            new(0.95f, 0.85f, 0.25f, 1f),
            new(0.30f, 0.75f, 0.35f, 1f),
        };

        private static readonly Dictionary<CSteamID, int> _slotByPlayer = new();

        public static void Reset() => _slotByPlayer.Clear();
        public static void Register(CSteamID id, int slotIndex) => _slotByPlayer[id] = slotIndex;
        public static int GetSlot(CSteamID id) => _slotByPlayer.TryGetValue(id, out var s) ? s : -1;
        public static Color GetColor(int slot) => slot >= 0 && slot < _colors.Length ? _colors[slot] : Color.gray;
        public static Color GetColorForPlayer(CSteamID id) => GetColor(GetSlot(id));
        public static IEnumerable<KeyValuePair<CSteamID, int>> All => _slotByPlayer;
    }
}

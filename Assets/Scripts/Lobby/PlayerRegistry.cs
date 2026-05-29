// PlayerRegistry.cs
// Role: Authoritative mapping from Steam ID → slot index → faction color.
//       Populated by GameStartLoader.LoadGameScene from NetworkSession.PlayerSlots just
//       before SampleScene loads; also read by GameStartLoader to set
//       WorldStartContext.GetPlayerColor so the world layer can tint units without
//       depending on Steamworks types.
//
// Slot convention (fixed at game-start by LobbyPanel.OnStart):
//   Slot 0 = Blue  (host)
//   Slot 1 = Red
//   Slot 2 = Yellow
//   Slot 3 = Green
//
// In solo mode this registry may be empty or pre-populated by MainBaseSetup;
// GetColorForPlayer returns Color.gray for any unregistered ID.
//
// Where to adjust:
//   • Faction colors: edit the _colors array (indices = slot numbers).
//   • More than 4 players: extend _colors and increase CreateLobby max-members in LobbyManager.
//   • Persistent cross-session data (e.g. ELO): add a separate static Dictionary here and
//     populate it after the session resolves.

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
        // P0 Blue, P1 Red, P2 Yellow, P3 Green — index = SpawnIndex / slot number.
        private static readonly Color[] _colors =
        {
            new(0.25f, 0.45f, 0.85f, 1f),
            new(0.85f, 0.30f, 0.30f, 1f),
            new(0.95f, 0.85f, 0.25f, 1f),
            new(0.30f, 0.75f, 0.35f, 1f),
        };

        private static readonly Dictionary<CSteamID, int> _slotByPlayer = new();

        /// <summary>Clears all registrations. Call before populating for a new game session.</summary>
        public static void Reset() => _slotByPlayer.Clear();

        /// <summary>Registers a player's slot index. Overwrites any previous registration for the same ID.</summary>
        public static void Register(CSteamID id, int slotIndex) => _slotByPlayer[id] = slotIndex;

        /// <summary>Returns the slot index for the given player, or -1 if unregistered.</summary>
        public static int GetSlot(CSteamID id) => _slotByPlayer.TryGetValue(id, out var s) ? s : -1;

        /// <summary>Returns the faction color for a given slot index. Returns Color.gray for out-of-range slots.</summary>
        public static Color GetColor(int slot) => slot >= 0 && slot < _colors.Length ? _colors[slot] : Color.gray;

        /// <summary>Convenience: looks up slot by Steam ID, then returns the faction color.</summary>
        public static Color GetColorForPlayer(CSteamID id) => GetColor(GetSlot(id));

        /// <summary>Iterates all registered (SteamID → slotIndex) pairs. Used by WorldStartContext to tint units.</summary>
        public static IEnumerable<KeyValuePair<CSteamID, int>> All => _slotByPlayer;
    }
}

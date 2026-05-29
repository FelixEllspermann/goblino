// LobbyInfo.cs
// Role: Plain data snapshot of one Steam lobby entry as seen from the lobby-list query.
//       Populated by LobbyManager.OnSteamLobbyMatchList from SteamMatchmaking metadata,
//       then handed to MultiplayerPanel to build the browser UI rows.
//
// Where to adjust:
//   • Extra lobby metadata (map name, game mode …): add fields here and
//     set/read them via SteamMatchmaking.SetLobbyData / GetLobbyData in LobbyManager.

using Steamworks;

namespace RTSCL.Lobby
{
    /// <summary>Snapshot of a single lobby for the browser UI.
    /// Immutable after construction — created fresh on each lobby-list refresh.</summary>
    public sealed class LobbyInfo
    {
        /// <summary>The Steam lobby ID. Passed to LobbyManager.JoinLobby when the player clicks Join.</summary>
        public CSteamID Id;
        /// <summary>Persona name of the lobby owner, read from the "host_name" lobby metadata key.</summary>
        public string HostName;
        /// <summary>Current occupancy reported by the matchmaking service at list-fetch time.</summary>
        public int CurrentMembers;
        /// <summary>Maximum capacity — fixed at 4 when the lobby is created (see LobbyManager.CreateLobby).</summary>
        public int MaxMembers;
    }
}

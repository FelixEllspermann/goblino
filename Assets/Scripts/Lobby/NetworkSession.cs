// NetworkSession.cs
// Role: Static cross-scene state bag for the current networked game session.
//       Holds data that must survive the scene load from MainMenu → SampleScene and that
//       both the Assembly-CSharp (lobby/network) and RTSCL.World.Unity layers need.
//
// Population flow:
//   1. NetworkManager.HandleLobbyEntered → sets LocalPlayer, HostPlayer, IsHost.
//   2. LobbyPanel.OnStart (host) → writes GameSeed + PlayerSlots → calls
//        NetworkManager.SendToAll(PackGameStart) + RaiseGameStartReceived.
//   3. NetworkManager.RouteMessage (clients) → TryUnpackGameStart → writes GameSeed +
//        PlayerSlots → calls RaiseGameStartReceived.
//   4. GameStartLoader.LoadGameScene → reads all fields, pushes into WorldStartContext /
//        WorldGeneratorBootstrap, wires NetCommandBridge.OutgoingSender, loads SampleScene.
//
// Reset flow:
//   NetworkManager.HandleLobbyLeft → Reset() clears all fields.
//
// Where to adjust:
//   • Additional session-wide flags (e.g. game mode, map size): add a static property here
//     and populate it in steps 2–3 above after extending PackGameStart / TryUnpackGameStart
//     in NetMessages.cs.

using System;
using System.Collections.Generic;
using Steamworks;

namespace RTSCL.Lobby
{
    /// <summary>Cross-scene state for the current networked session. Cleared on lobby leave.</summary>
    public static class NetworkSession
    {
        /// <summary>True when the local player is the lobby owner (authoritative host).</summary>
        public static bool IsHost { get; internal set; }

        /// <summary>Steam ID of the local player. Set by NetworkManager when the lobby is entered.</summary>
        public static CSteamID LocalPlayer { get; internal set; }

        /// <summary>Steam ID of the lobby owner. Used to open P2P connections toward the host.</summary>
        public static CSteamID HostPlayer { get; internal set; }

        /// <summary>World generation seed broadcast by the host. All clients use this to
        /// deterministically reproduce the same map.</summary>
        public static int GameSeed { get; internal set; }

        /// <summary>Ordered list of per-player spawn assignments. Index 0 is always the host.
        /// Translated into WorldStartContext.PendingSlots by GameStartLoader.</summary>
        public static List<PlayerSlot> PlayerSlots { get; internal set; }

        /// <summary>Fired when the GameStart payload has been unpacked and session state is ready.
        /// GameStartLoader subscribes to trigger the scene load.</summary>
        public static event Action OnGameStartReceived;

        /// <summary>Resets all session state. Called by NetworkManager when leaving a lobby.</summary>
        public static void Reset()
        {
            IsHost = false;
            LocalPlayer = CSteamID.Nil;
            HostPlayer = CSteamID.Nil;
            GameSeed = 0;
            PlayerSlots = null;
        }

        /// <summary>Fires OnGameStartReceived. Called by LobbyPanel (host path) and
        /// NetworkManager.RouteMessage (client path) after session state is populated.</summary>
        internal static void RaiseGameStartReceived() => OnGameStartReceived?.Invoke();
    }
}

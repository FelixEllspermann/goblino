using System;
using Steamworks;

namespace RTSCL.Lobby
{
    /// <summary>Cross-scene state for the current networked session. Cleared on lobby leave.</summary>
    public static class NetworkSession
    {
        public static bool IsHost { get; internal set; }
        public static CSteamID LocalPlayer { get; internal set; }
        public static CSteamID HostPlayer { get; internal set; }
        public static int GameSeed { get; internal set; }

        public static event Action OnGameStartReceived;

        public static void Reset()
        {
            IsHost = false;
            LocalPlayer = CSteamID.Nil;
            HostPlayer = CSteamID.Nil;
            GameSeed = 0;
        }

        internal static void RaiseGameStartReceived() => OnGameStartReceived?.Invoke();
    }
}

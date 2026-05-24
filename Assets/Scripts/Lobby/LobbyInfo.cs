using Steamworks;

namespace RTSCL.Lobby
{
    /// <summary>Snapshot of a single lobby for the browser UI.</summary>
    public sealed class LobbyInfo
    {
        public CSteamID Id;
        public string HostName;
        public int CurrentMembers;
        public int MaxMembers;
    }
}

// LobbyManager.cs
// Role: Singleton wrapping Steam matchmaking (SteamMatchmaking API).
//       Manages public-lobby create / browse / join / leave and membership change detection.
//       All public API is static; consumers subscribe to the static events rather than
//       holding a reference to the instance.
//
// Lifecycle:
//   [BeforeSceneLoad] Bootstrap() → Awake() checks command-line "+connect_lobby" arg.
//   OnEnable() registers all Callback<T> / CallResult<T> wrappers — these are garbage-
//   collected handles, so they must be stored as fields for the lifetime of the object.
//   SteamManager.Update pumps SteamAPI.RunCallbacks() so callbacks fire on the main thread.
//
// Event flow (create path):
//   CreateLobby() → SteamMatchmaking.CreateLobby (async)
//     → OnSteamLobbyCreated: sets metadata + fires RaiseLobbyCreated
//     → Steam auto-fires LobbyEnter_t for the creator → OnSteamLobbyEntered
//
// Event flow (join path):
//   JoinLobby(id) → SteamMatchmaking.JoinLobby (async)
//     → OnSteamLobbyEntered: sets _currentLobby + fires RaiseLobbyEntered
//   NetworkManager listens to OnLobbyEntered to open/connect P2P sockets.
//   MenuRoot listens to navigate to LobbyPanel.
//
// Where to adjust:
//   • Max players: change the 4 in CreateLobby → SteamMatchmaking.CreateLobby(_, 4).
//   • Extra lobby filters (map, mode, etc.): add
//       SteamMatchmaking.AddRequestLobbyListStringFilter(...) calls in RequestLobbyList.
//   • Game version: bump GameVersion const — lobbies on different versions won't appear
//       in each other's lists because RequestLobbyList filters by this value.
//   • Adding new events: declare a static Action, add an internal Raise* helper, fire it
//       from the appropriate callback handler below.

using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace RTSCL.Lobby
{
    /// <summary>Singleton wrapping Steam matchmaking. Auto-bootstraps before scene load
    /// and survives scene loads via DontDestroyOnLoad. All public API is static; UI
    /// subscribes to events.</summary>
    [DisallowMultipleComponent]
    public sealed class LobbyManager : MonoBehaviour
    {
        // Lobby metadata keys written by the host and read by the browser.
        public const string LobbyDataGameVersion = "game_version";
        public const string LobbyDataHostName = "host_name";
        // Increment when lobby wire-format changes to prevent cross-version joins.
        public const string GameVersion = "0.1";

        // ── Static events ─────────────────────────────────────────────────────────────
        // All static so subscribers don't need the MonoBehaviour instance.
        // OnLobbyCreated  fires when the host's CreateLobby call succeeds.
        // OnLobbyEntered  fires for both host (after create) and joining client.
        // OnLobbyListReceived  fires with the refreshed list for the browser UI.
        // OnLobbyMembersChanged  fires when any player joins or leaves (but not on host-left).
        // OnLobbyLeft  fires when we voluntarily leave or the host disconnects.
        // OnError  fires with a human-readable string for any matchmaking failure.
        public static event Action<CSteamID> OnLobbyCreated;
        public static event Action<CSteamID> OnLobbyEntered;
        public static event Action<List<LobbyInfo>> OnLobbyListReceived;
        public static event Action OnLobbyMembersChanged;
        public static event Action OnLobbyLeft;
        public static event Action<string> OnError;

        /// <summary>The lobby we are currently in, or CSteamID.Nil if not in one.</summary>
        public static CSteamID CurrentLobby => _instance != null ? _instance._currentLobby : CSteamID.Nil;
        public static bool InLobby => CurrentLobby != CSteamID.Nil;

        private static LobbyManager _instance;
        private CSteamID _currentLobby = CSteamID.Nil;

        // Callback/CallResult wrappers must be stored in fields — Steamworks.NET keeps only
        // a weak reference internally; GC would collect them if they were local variables.
        private Callback<LobbyCreated_t> _cbLobbyCreated;
        private Callback<LobbyEnter_t> _cbLobbyEntered;
        private Callback<LobbyChatUpdate_t> _cbLobbyChat;
        private Callback<GameLobbyJoinRequested_t> _cbJoinRequested;
        // CallResult is used instead of Callback for RequestLobbyList because the result
        // is tied to a specific API call handle rather than being a global broadcast.
        private CallResult<LobbyMatchList_t> _crLobbyList;

        /// <summary>Creates the singleton GameObject before any scene is loaded.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject(nameof(LobbyManager));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LobbyManager>();
        }

        private void Awake()
        {
            // Steam launches the app with "+connect_lobby <id>" when joining from the friends list while the app isn't running.
            // We can't call JoinLobby here because SteamManager may not be initialized yet;
            // store the id and let Update() retry until SteamManager.Initialized is true.
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "+connect_lobby") continue;
                if (!ulong.TryParse(args[i + 1], out ulong id)) continue;
                // Defer the JoinLobby until Steam is initialized.
                _pendingJoinId = new CSteamID(id);
                return;
            }
        }

        // Non-nil when a "+connect_lobby" command-line arg was found but Steam
        // wasn't ready yet. Consumed in Update() once SteamManager.Initialized is true.
        private CSteamID _pendingJoinId = CSteamID.Nil;

        private void Update()
        {
            if (_pendingJoinId == CSteamID.Nil) return;
            if (!SteamManager.Initialized) return;
            var id = _pendingJoinId;
            _pendingJoinId = CSteamID.Nil; // clear before call to avoid re-entry
            JoinLobby(id);
        }

        private void OnEnable()
        {
            // Register all Steam callbacks. Each Callback<T>.Create allocates a managed
            // wrapper that the Steamworks pump will call. Stored in fields to prevent GC.
            _cbLobbyCreated = Callback<LobbyCreated_t>.Create(OnSteamLobbyCreated);
            _cbLobbyEntered = Callback<LobbyEnter_t>.Create(OnSteamLobbyEntered);
            _crLobbyList = CallResult<LobbyMatchList_t>.Create(OnSteamLobbyMatchList);
            _cbLobbyChat = Callback<LobbyChatUpdate_t>.Create(OnSteamLobbyChat);
            _cbJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnSteamGameLobbyJoinRequested);
        }

        // ── Public API — all methods no-op if Steam isn't initialized. ────────────────

        /// <summary>Creates a new public lobby with up to 4 slots.
        /// Result arrives via OnLobbyCreated / OnError.</summary>
        public static void CreateLobby()
        {
            if (_instance == null) return;
            if (!SteamManager.Initialized) { RaiseError("Steam not running"); return; }
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 4);
        }

        /// <summary>Queries Steam for public lobbies matching the current GameVersion.
        /// Result arrives via OnLobbyListReceived.</summary>
        public static void RequestLobbyList()
        {
            if (_instance == null) return;
            if (!SteamManager.Initialized) { RaiseError("Steam not running"); return; }
            // Filter so clients on different game versions never see each other's lobbies.
            SteamMatchmaking.AddRequestLobbyListStringFilter(
                LobbyDataGameVersion, GameVersion, ELobbyComparison.k_ELobbyComparisonEqual);
            var call = SteamMatchmaking.RequestLobbyList();
            // Bind the SteamAPICall_t handle to our CallResult so only this specific
            // response triggers OnSteamLobbyMatchList (not a stale or unrelated result).
            _instance._crLobbyList.Set(call);
        }

        /// <summary>Attempts to join an existing lobby by ID.
        /// Result arrives via OnLobbyEntered / OnError.</summary>
        public static void JoinLobby(CSteamID id)
        {
            if (_instance == null) return;
            if (!SteamManager.Initialized) { RaiseError("Steam not running"); return; }
            if (id == CSteamID.Nil) { RaiseError("Invalid lobby id"); return; }
            SteamMatchmaking.JoinLobby(id);
            // LobbyEnter_t fires asynchronously
        }

        /// <summary>Leaves the current lobby and fires OnLobbyLeft.
        /// No-op if not in a lobby.</summary>
        public static void LeaveLobby()
        {
            if (_instance == null) return;
            if (_instance._currentLobby == CSteamID.Nil) return;
            SteamMatchmaking.LeaveLobby(_instance._currentLobby);
            _instance._currentLobby = CSteamID.Nil;
            RaiseLobbyLeft();
        }

        /// <summary>Returns (SteamID, personaName, isHost) tuples for every current lobby member.
        /// Returns empty list if not in a lobby. Caller can sort/display as needed.</summary>
        public static List<(CSteamID id, string name, bool isHost)> GetCurrentMembers()
        {
            var list = new List<(CSteamID, string, bool)>();
            if (_instance == null || _instance._currentLobby == CSteamID.Nil) return list;
            var lobby = _instance._currentLobby;
            var owner = SteamMatchmaking.GetLobbyOwner(lobby);
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++)
            {
                var id = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
                list.Add((id, SteamFriends.GetFriendPersonaName(id), id == owner));
            }
            return list;
        }

        // ── Steam callback handlers ───────────────────────────────────────────────────

        private void OnSteamLobbyCreated(LobbyCreated_t e)
        {
            if (e.m_eResult != EResult.k_EResultOK)
            {
                RaiseError($"Create lobby failed: {e.m_eResult}");
                return;
            }
            var id = new CSteamID(e.m_ulSteamIDLobby);
            // Write metadata so other clients can read host name and version in the browser.
            SteamMatchmaking.SetLobbyData(id, LobbyDataGameVersion, GameVersion);
            SteamMatchmaking.SetLobbyData(id, LobbyDataHostName, SteamFriends.GetPersonaName());
            RaiseLobbyCreated(id);
            // LobbyEnter_t fires next for the creator — OnSteamLobbyEntered handles navigation.
        }

        private void OnSteamLobbyMatchList(LobbyMatchList_t e, bool ioFailure)
        {
            var list = new List<LobbyInfo>();
            if (ioFailure) { RaiseError("Lobby list IO failure"); RaiseLobbyListReceived(list); return; }
            int n = (int)e.m_nLobbiesMatching;
            for (int i = 0; i < n; i++)
            {
                var id = SteamMatchmaking.GetLobbyByIndex(i);
                var host = SteamMatchmaking.GetLobbyData(id, LobbyDataHostName);
                list.Add(new LobbyInfo
                {
                    Id = id,
                    HostName = string.IsNullOrEmpty(host) ? "(unknown)" : host,
                    CurrentMembers = SteamMatchmaking.GetNumLobbyMembers(id),
                    MaxMembers = SteamMatchmaking.GetLobbyMemberLimit(id),
                });
            }
            RaiseLobbyListReceived(list);
        }

        private void OnSteamLobbyEntered(LobbyEnter_t e)
        {
            var resp = (EChatRoomEnterResponse)e.m_EChatRoomEnterResponse;
            var enteredId = new CSteamID(e.m_ulSteamIDLobby);
            if (resp != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                RaiseError($"Enter lobby failed: {resp}");
                // Only clear if the failed lobby is our current one. A stale callback
                // for a different lobby must not corrupt state.
                if (enteredId == _currentLobby) _currentLobby = CSteamID.Nil;
                return;
            }
            _currentLobby = enteredId;
            RaiseLobbyEntered(_currentLobby);
        }

        private void OnSteamGameLobbyJoinRequested(GameLobbyJoinRequested_t e)
        {
            // Triggered when user clicks "Join Game" in Steam Friends overlay while app is running.
            JoinLobby(e.m_steamIDLobby);
        }

        private void OnSteamLobbyChat(LobbyChatUpdate_t e)
        {
            // Filter to our current lobby — Steam fires this globally for all lobbies.
            if ((CSteamID)e.m_ulSteamIDLobby != _currentLobby) return;
            var change = (EChatMemberStateChange)e.m_rgfChatMemberStateChange;
            var changedUser = new CSteamID(e.m_ulSteamIDUserChanged);

            // Detect host departure (left / disconnected / kicked / banned).
            // If the lobby owner leaves, Steam may transfer ownership — but we treat it
            // as a session end for simplicity; clients are booted back to main menu.
            bool ownerLeft = false;
            if ((change & (EChatMemberStateChange.k_EChatMemberStateChangeLeft
                         | EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
                         | EChatMemberStateChange.k_EChatMemberStateChangeKicked
                         | EChatMemberStateChange.k_EChatMemberStateChangeBanned)) != 0)
            {
                var owner = SteamMatchmaking.GetLobbyOwner(_currentLobby);
                if (changedUser == owner || owner == CSteamID.Nil) ownerLeft = true;
            }

            if (ownerLeft)
            {
                RaiseError("Host left the lobby");
                LeaveLobby();
                return;
            }
            RaiseLobbyMembersChanged();
        }

        // ── Internal event raisers ────────────────────────────────────────────────────
        // `internal` so tests and other RTSCL.Lobby types can fire them without reflection.
        internal static void RaiseLobbyCreated(CSteamID id)  => OnLobbyCreated?.Invoke(id);
        internal static void RaiseLobbyEntered(CSteamID id)  => OnLobbyEntered?.Invoke(id);
        internal static void RaiseLobbyListReceived(List<LobbyInfo> l) => OnLobbyListReceived?.Invoke(l);
        internal static void RaiseLobbyMembersChanged() => OnLobbyMembersChanged?.Invoke();
        internal static void RaiseLobbyLeft() => OnLobbyLeft?.Invoke();
        internal static void RaiseError(string msg) => OnError?.Invoke(msg);
    }
}

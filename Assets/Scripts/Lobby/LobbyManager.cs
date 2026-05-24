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
        public const string LobbyDataGameVersion = "game_version";
        public const string LobbyDataHostName = "host_name";
        public const string GameVersion = "0.1";

        // Events (static — call site doesn't need to know about the instance)
        public static event Action<CSteamID> OnLobbyCreated;
        public static event Action<CSteamID> OnLobbyEntered;
        public static event Action<List<LobbyInfo>> OnLobbyListReceived;
        public static event Action OnLobbyMembersChanged;
        public static event Action OnLobbyLeft;
        public static event Action<string> OnError;

        public static CSteamID CurrentLobby => _instance != null ? _instance._currentLobby : CSteamID.Nil;
        public static bool InLobby => CurrentLobby != CSteamID.Nil;

        private static LobbyManager _instance;
        private CSteamID _currentLobby = CSteamID.Nil;

        private Callback<LobbyCreated_t> _cbLobbyCreated;
        private Callback<LobbyEnter_t> _cbLobbyEntered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject(nameof(LobbyManager));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LobbyManager>();
        }

        private void OnEnable()
        {
            _cbLobbyCreated = Callback<LobbyCreated_t>.Create(OnSteamLobbyCreated);
            _cbLobbyEntered = Callback<LobbyEnter_t>.Create(OnSteamLobbyEntered);
        }

        // Public API — all methods no-op if Steam isn't initialized.
        public static void CreateLobby()
        {
            if (_instance == null) return;
            if (!SteamManager.Initialized) { RaiseError("Steam not running"); return; }
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 4);
        }
        public static void RequestLobbyList(){ /* Task 3 */ }
        public static void JoinLobby(CSteamID id) { /* Task 4 */ }
        public static void LeaveLobby()      { /* Task 5 */ }

        private void OnSteamLobbyCreated(LobbyCreated_t e)
        {
            if (e.m_eResult != EResult.k_EResultOK)
            {
                RaiseError($"Create lobby failed: {e.m_eResult}");
                return;
            }
            var id = new CSteamID(e.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(id, LobbyDataGameVersion, GameVersion);
            SteamMatchmaking.SetLobbyData(id, LobbyDataHostName, SteamFriends.GetPersonaName());
            RaiseLobbyCreated(id);
            // LobbyEnter_t fires next for the creator
        }

        private void OnSteamLobbyEntered(LobbyEnter_t e)
        {
            var resp = (EChatRoomEnterResponse)e.m_EChatRoomEnterResponse;
            if (resp != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                RaiseError($"Enter lobby failed: {resp}");
                _currentLobby = CSteamID.Nil;
                return;
            }
            _currentLobby = new CSteamID(e.m_ulSteamIDLobby);
            RaiseLobbyEntered(_currentLobby);
        }

        // Internal event raisers (used by Steam callback handlers in later tasks)
        internal static void RaiseLobbyCreated(CSteamID id)  => OnLobbyCreated?.Invoke(id);
        internal static void RaiseLobbyEntered(CSteamID id)  => OnLobbyEntered?.Invoke(id);
        internal static void RaiseLobbyListReceived(List<LobbyInfo> l) => OnLobbyListReceived?.Invoke(l);
        internal static void RaiseLobbyMembersChanged() => OnLobbyMembersChanged?.Invoke();
        internal static void RaiseLobbyLeft() => OnLobbyLeft?.Invoke();
        internal static void RaiseError(string msg) => OnError?.Invoke(msg);
    }
}

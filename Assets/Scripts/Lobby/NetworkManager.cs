using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace RTSCL.Lobby
{
    /// <summary>Singleton wrapping Steam Networking Sockets P2P transport.
    /// Connection lifecycle is driven by LobbyManager events.</summary>
    [DisallowMultipleComponent]
    public sealed class NetworkManager : MonoBehaviour
    {
        public static event Action<CSteamID> OnConnected;
        public static event Action<CSteamID> OnDisconnected;
        public static event Action<string> OnError;

        public static int ConnectedCount =>
            _instance != null ? _instance._connections.Count : 0;

        private static NetworkManager _instance;
        private readonly Dictionary<HSteamNetConnection, CSteamID> _connections = new();
        private Callback<SteamNetConnectionStatusChangedCallback_t> _cbStatus;
        private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
        private HSteamNetPollGroup _pollGroup = HSteamNetPollGroup.Invalid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject(nameof(NetworkManager));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<NetworkManager>();
        }

        private void OnEnable()
        {
            LobbyManager.OnLobbyEntered += HandleLobbyEntered;
            LobbyManager.OnLobbyLeft    += HandleLobbyLeft;
            _cbStatus = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnSteamStatusChanged);
            if (SteamManager.Initialized) SteamNetworkingUtils.InitRelayNetworkAccess();
        }

        private void OnDisable()
        {
            LobbyManager.OnLobbyEntered -= HandleLobbyEntered;
            LobbyManager.OnLobbyLeft    -= HandleLobbyLeft;
        }

        private void HandleLobbyEntered(CSteamID lobby)
        {
            if (!SteamManager.Initialized) { OnError?.Invoke("Steam not running"); return; }
            var owner = SteamMatchmaking.GetLobbyOwner(lobby);
            NetworkSession.LocalPlayer = SteamUser.GetSteamID();
            NetworkSession.HostPlayer  = owner;
            NetworkSession.IsHost      = owner == NetworkSession.LocalPlayer;
            if (NetworkSession.IsHost) StartListening();
            // ConnectToHost added in Task 5.
        }

        private void HandleLobbyLeft()
        {
            Disconnect();
            NetworkSession.Reset();
        }

        private void OnSteamStatusChanged(SteamNetConnectionStatusChangedCallback_t e)
        {
            var conn = e.m_hConn;
            var remote = e.m_info.m_identityRemote.GetSteamID();
            var state = e.m_info.m_eState;
            Debug.Log($"[Net] Status change: conn={conn.m_HSteamNetConnection} state={state} remote={remote}");

            if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting
             && _listenSocket != HSteamListenSocket.Invalid)
            {
                var accept = SteamNetworkingSockets.AcceptConnection(conn);
                if (accept != EResult.k_EResultOK)
                {
                    Debug.LogWarning($"[Net] AcceptConnection failed: {accept}");
                    SteamNetworkingSockets.CloseConnection(conn, 0, "accept-failed", false);
                    return;
                }
                SteamNetworkingSockets.SetConnectionPollGroup(conn, _pollGroup);
            }

            if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                if (_listenSocket != HSteamListenSocket.Invalid) // host-side: a guest just connected
                {
                    _connections[conn] = remote;
                    OnConnected?.Invoke(remote);
                }
            }

            if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
             || state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                if (_connections.Remove(conn))
                    OnDisconnected?.Invoke(remote);
                SteamNetworkingSockets.CloseConnection(conn, 0, string.Empty, false);
            }
        }

        // Public API — actual implementations come in later tasks.
        public static void SendToAll(byte[] payload) { /* Task 6 */ }

        // Internal lifecycle methods invoked by HandleLobbyEntered/Left in later tasks
        private void StartListening()
        {
            if (_listenSocket != HSteamListenSocket.Invalid) return; // idempotent
            var opts = Array.Empty<SteamNetworkingConfigValue_t>();
            _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, opts.Length, opts);
            _pollGroup = SteamNetworkingSockets.CreatePollGroup();
            Debug.Log($"[Net] Listen socket created: {_listenSocket.m_HSteamListenSocket}");
        }

        private void ConnectToHost(CSteamID host) { /* Task 5 */ }

        private void Disconnect()
        {
            foreach (var conn in _connections.Keys)
                SteamNetworkingSockets.CloseConnection(conn, 0, "disconnect", false);
            _connections.Clear();
            if (_listenSocket != HSteamListenSocket.Invalid)
            {
                SteamNetworkingSockets.CloseListenSocket(_listenSocket);
                _listenSocket = HSteamListenSocket.Invalid;
            }
            if (_pollGroup != HSteamNetPollGroup.Invalid)
            {
                SteamNetworkingSockets.DestroyPollGroup(_pollGroup);
                _pollGroup = HSteamNetPollGroup.Invalid;
            }
        }
    }
}

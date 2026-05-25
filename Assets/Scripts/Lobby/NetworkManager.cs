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
        private HSteamNetConnection _serverConnection = HSteamNetConnection.Invalid;

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

        private void OnDestroy()
        {
            Disconnect();
        }

        private void HandleLobbyEntered(CSteamID lobby)
        {
            if (!SteamManager.Initialized) { OnError?.Invoke("Steam not running"); return; }
            var owner = SteamMatchmaking.GetLobbyOwner(lobby);
            NetworkSession.LocalPlayer = SteamUser.GetSteamID();
            NetworkSession.HostPlayer  = owner;
            NetworkSession.IsHost      = owner == NetworkSession.LocalPlayer;
            if (NetworkSession.IsHost) StartListening();
            else                       ConnectToHost(owner);
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
                if (_listenSocket != HSteamListenSocket.Invalid)
                {
                    _connections[conn] = remote;
                    OnConnected?.Invoke(remote);
                }
                else if (conn == _serverConnection)
                {
                    OnConnected?.Invoke(remote);
                }
            }

            if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
             || state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                if (_connections.Remove(conn))
                    OnDisconnected?.Invoke(remote);
                if (conn == _serverConnection)
                {
                    _serverConnection = HSteamNetConnection.Invalid;
                    OnDisconnected?.Invoke(remote);
                    // Client lost the host → bail out of the lobby
                    LobbyManager.LeaveLobby();
                }
                SteamNetworkingSockets.CloseConnection(conn, 0, string.Empty, false);
            }
        }

        // Public API
        public static void SendToAll(byte[] payload)
        {
            if (_instance == null || payload == null || payload.Length == 0) return;
            _instance.SendToAllImpl(payload);
        }

        private void SendToAllImpl(byte[] payload)
        {
            int flags = Constants.k_nSteamNetworkingSend_Reliable;
            // Steamworks.NET's SendMessageToConnection takes IntPtr — pin the byte[] once,
            // reuse for every send in this batch, then free.
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                payload, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject();
                if (NetworkSession.IsHost)
                {
                    foreach (var conn in _connections.Keys)
                    {
                        var res = SteamNetworkingSockets.SendMessageToConnection(
                            conn, ptr, (uint)payload.Length, flags, out _);
                        if (res != EResult.k_EResultOK)
                            Debug.LogWarning($"[Net] SendToConnection failed: {res}");
                    }
                }
                else if (_serverConnection != HSteamNetConnection.Invalid)
                {
                    var res = SteamNetworkingSockets.SendMessageToConnection(
                        _serverConnection, ptr, (uint)payload.Length, flags, out _);
                    if (res != EResult.k_EResultOK)
                        Debug.LogWarning($"[Net] SendToServer failed: {res}");
                }
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Host-only: broadcast payload to all connected clients EXCEPT the given one.
        /// Used to echo a command back out after the host receives it from a client.</summary>
        public static void SendToOthers(byte[] payload, HSteamNetConnection except)
        {
            if (_instance == null || payload == null || payload.Length == 0) return;
            _instance.SendToOthersImpl(payload, except);
        }

        private void SendToOthersImpl(byte[] payload, HSteamNetConnection except)
        {
            if (!NetworkSession.IsHost) return;
            int flags = Constants.k_nSteamNetworkingSend_Reliable;
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                payload, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject();
                foreach (var conn in _connections.Keys)
                {
                    if (conn.Equals(except)) continue;
                    var res = SteamNetworkingSockets.SendMessageToConnection(
                        conn, ptr, (uint)payload.Length, flags, out _);
                    if (res != EResult.k_EResultOK)
                        Debug.LogWarning($"[Net] SendToOthers failed: {res}");
                }
            }
            finally { handle.Free(); }
        }

        private void Update()
        {
            if (!SteamManager.Initialized) return;
            var msgs = new System.IntPtr[64];

            if (NetworkSession.IsHost && _pollGroup != HSteamNetPollGroup.Invalid)
            {
                int n = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(_pollGroup, msgs, msgs.Length);
                for (int i = 0; i < n; i++) DispatchAndRelease(msgs[i]);
            }
            else if (!NetworkSession.IsHost && _serverConnection != HSteamNetConnection.Invalid)
            {
                int n = SteamNetworkingSockets.ReceiveMessagesOnConnection(_serverConnection, msgs, msgs.Length);
                for (int i = 0; i < n; i++) DispatchAndRelease(msgs[i]);
            }
        }

        private void DispatchAndRelease(System.IntPtr ptr)
        {
            var msg = System.Runtime.InteropServices.Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptr);
            var data = new byte[msg.m_cbSize];
            System.Runtime.InteropServices.Marshal.Copy(msg.m_pData, data, 0, msg.m_cbSize);
            var sender = msg.m_identityPeer.GetSteamID();
            var senderConn = msg.m_conn;
            RouteMessage(sender, data, senderConn);
            SteamNetworkingMessage_t.Release(ptr);
        }

        private void RouteMessage(CSteamID sender, byte[] payload, HSteamNetConnection senderConn)
        {
            if (payload == null || payload.Length == 0) return;
            switch ((NetMessageType)payload[0])
            {
                case NetMessageType.GameStart:
                    if (NetworkSession.GameSeed != 0)
                    {
                        Debug.LogWarning("[Net] Duplicate GameStart ignored");
                        return;
                    }
                    if (NetMessages.TryUnpackGameStart(payload, out int seed, out var slots))
                    {
                        NetworkSession.GameSeed = seed;
                        NetworkSession.PlayerSlots = slots;
                        NetworkSession.RaiseGameStartReceived();
                    }
                    break;
                case NetMessageType.CmdMove:
                case NetMessageType.CmdHarvest:
                case NetMessageType.CmdBuildAssist:
                case NetMessageType.CmdPlaceBuilding:
                case NetMessageType.CmdTrainUnit:
                case NetMessageType.CmdAttack:
                case NetMessageType.EvDamage:
                    // Apply on this client.
                    RTSCL.World.Unity.NetCommandApplier.Apply(payload, sender.m_SteamID);
                    // Host: echo to all OTHER connected clients (so the rest of the lobby sees it).
                    if (NetworkSession.IsHost)
                        SendToOthers(payload, senderConn);
                    break;
                default:
                    Debug.LogWarning($"[Net] Unknown message type: {payload[0]}");
                    break;
            }
        }

        // Internal lifecycle methods invoked by HandleLobbyEntered/Left in later tasks
        private void StartListening()
        {
            if (_listenSocket != HSteamListenSocket.Invalid) return; // idempotent
            var opts = Array.Empty<SteamNetworkingConfigValue_t>();
            _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, opts.Length, opts);
            _pollGroup = SteamNetworkingSockets.CreatePollGroup();
            Debug.Log($"[Net] Listen socket created: {_listenSocket.m_HSteamListenSocket}");
        }

        private void ConnectToHost(CSteamID host)
        {
            if (_serverConnection != HSteamNetConnection.Invalid) return; // idempotent
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(host);
            var opts = Array.Empty<SteamNetworkingConfigValue_t>();
            _serverConnection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, opts.Length, opts);
            Debug.Log($"[Net] Connecting to host {host} conn={_serverConnection.m_HSteamNetConnection}");
        }

        private void Disconnect()
        {
            // If Steamworks already shut down (e.g. on editor Stop / app quit), skip the
            // native cleanup — those handles are gone with Steamworks itself.
            if (!SteamManager.Initialized)
            {
                _serverConnection = HSteamNetConnection.Invalid;
                _connections.Clear();
                _listenSocket = HSteamListenSocket.Invalid;
                _pollGroup = HSteamNetPollGroup.Invalid;
                return;
            }

            if (_serverConnection != HSteamNetConnection.Invalid)
            {
                SteamNetworkingSockets.CloseConnection(_serverConnection, 0, "disconnect", false);
                _serverConnection = HSteamNetConnection.Invalid;
            }
            foreach (var conn in new System.Collections.Generic.List<HSteamNetConnection>(_connections.Keys))
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

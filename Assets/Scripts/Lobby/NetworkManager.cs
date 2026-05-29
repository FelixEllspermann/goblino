// NetworkManager.cs
// Role: Singleton owning the Steam Networking Sockets P2P transport layer.
//       Manages connection lifecycle (listen socket + poll group for host, single
//       server connection for clients) and dispatches inbound messages.
//
// Topology:
//   Host:   CreateListenSocketP2P + CreatePollGroup. Accepts all inbound connections.
//           Polls ReceiveMessagesOnPollGroup each Update.
//   Client: ConnectP2P to lobby owner. Polls ReceiveMessagesOnConnection each Update.
//   Relay:  SteamNetworkingUtils.InitRelayNetworkAccess enables NAT punch + SDR relay
//           so hosts behind strict NATs are reachable.
//
// Host-echo dispatch (RouteMessage):
//   Issuer applies command locally and sends to host (or host sends directly to all).
//   Host receives → Apply locally → SendToOthers (all except the sender's connection).
//   This means every non-issuing client applies the command once; issuer already applied it.
//
// GCHandle pinning for SendMessageToConnection:
//   SteamNetworkingSockets.SendMessageToConnection takes an IntPtr to the payload buffer.
//   We pin the managed byte[] with GCHandle.Alloc(Pinned) once per batch (per call to
//   SendToAllImpl / SendToOthersImpl), then free in `finally` regardless of errors.
//   Pinning is safe to reuse across multiple Send calls within the same synchronous batch
//   because all sends complete before the `finally` block runs.
//
// Where to adjust:
//   • Unreliable channel: change k_nSteamNetworkingSend_Reliable to _Unreliable for
//     high-frequency positional updates (adds own sequence/drop logic).
//   • Message batch size: the IntPtr[64] receive buffer in Update processes up to 64
//     messages per frame. Increase if high message rates cause lag.
//   • Adding a new message type: add a case to RouteMessage's switch; pack/unpack
//     lives in NetWireFormat.cs (world side) or NetMessages.cs (lobby side).
//   • Disconnect policy on host loss: currently calls LobbyManager.LeaveLobby() which
//     navigates back to the main menu. Swap in a reconnect flow here if needed.

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
        /// <summary>Fires when a P2P connection to a remote peer is fully established.</summary>
        public static event Action<CSteamID> OnConnected;
        /// <summary>Fires when a P2P connection to a remote peer is lost or closed.</summary>
        public static event Action<CSteamID> OnDisconnected;
        public static event Action<string> OnError;

        /// <summary>Number of open P2P connections. On the host this equals lobby members minus 1;
        /// on clients this is 0 or 1. Used by LobbyPanel to gate the Start Game button.</summary>
        public static int ConnectedCount =>
            _instance != null ? _instance._connections.Count : 0;

        private static NetworkManager _instance;

        // Host side: maps each client's connection handle → their Steam ID.
        private readonly Dictionary<HSteamNetConnection, CSteamID> _connections = new();

        // Stored as a field to prevent GC from collecting the delegate while Steam holds a ptr.
        private Callback<SteamNetConnectionStatusChangedCallback_t> _cbStatus;

        // Host-only: the P2P listen socket that accepts inbound client connections.
        private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;

        // Host-only: poll group that aggregates inbound messages from all client connections
        // into a single ReceiveMessagesOnPollGroup call, avoiding per-connection polling.
        private HSteamNetPollGroup _pollGroup = HSteamNetPollGroup.Invalid;

        // Client-only: the single connection to the host.
        private HSteamNetConnection _serverConnection = HSteamNetConnection.Invalid;

        /// <summary>Creates the singleton GameObject before any scene is loaded.</summary>
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
            // Register the connection-state change callback. This fires for both host
            // (incoming connections) and client (outgoing connection state).
            _cbStatus = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnSteamStatusChanged);
            // InitRelayNetworkAccess pre-fetches relay info so the first connection
            // attempt doesn't incur an extra round-trip delay.
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

        /// <summary>Called when we enter a lobby. Sets up NetworkSession identity and
        /// either starts listening (host) or connects to the host (client).</summary>
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

        /// <summary>Steam fires this callback for every connection state transition on this process.
        /// Handles: accept inbound (host), track Connected, and clean up on close/error.</summary>
        private void OnSteamStatusChanged(SteamNetConnectionStatusChangedCallback_t e)
        {
            var conn = e.m_hConn;
            var remote = e.m_info.m_identityRemote.GetSteamID();
            var state = e.m_info.m_eState;
            Debug.Log($"[Net] Status change: conn={conn.m_HSteamNetConnection} state={state} remote={remote}");

            // Host path: a new client is knocking — accept and add to the poll group.
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
                // Adding the connection to the poll group lets ReceiveMessagesOnPollGroup
                // aggregate messages from all clients into one call.
                SteamNetworkingSockets.SetConnectionPollGroup(conn, _pollGroup);
            }

            if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                if (_listenSocket != HSteamListenSocket.Invalid)
                {
                    // Host side: a new client is fully connected — record it.
                    _connections[conn] = remote;
                    OnConnected?.Invoke(remote);
                }
                else if (conn == _serverConnection)
                {
                    // Client side: our outbound connection to the host is ready.
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

        // ── Send API ──────────────────────────────────────────────────────────────────

        /// <summary>Sends payload to all connected peers reliably.
        /// Host: broadcasts to all clients. Client: sends to the host (which echoes to others).</summary>
        public static void SendToAll(byte[] payload)
        {
            if (_instance == null || payload == null || payload.Length == 0) return;
            _instance.SendToAllImpl(payload);
        }

        private void SendToAllImpl(byte[] payload)
        {
            int flags = Constants.k_nSteamNetworkingSend_Reliable;
            // SendMessageToConnection takes IntPtr — pin the byte[] once, reuse for every
            // send in this batch, free unconditionally in finally to prevent memory leaks.
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                payload, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject();
                if (NetworkSession.IsHost)
                {
                    // Host sends directly to every client connection.
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
                    // Client sends only to the host; host echoes to others via RouteMessage.
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
            // Same GCHandle pinning pattern as SendToAllImpl — one pin, N sends, one free.
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                payload, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject();
                foreach (var conn in _connections.Keys)
                {
                    if (conn.Equals(except)) continue; // skip original sender
                    var res = SteamNetworkingSockets.SendMessageToConnection(
                        conn, ptr, (uint)payload.Length, flags, out _);
                    if (res != EResult.k_EResultOK)
                        Debug.LogWarning($"[Net] SendToOthers failed: {res}");
                }
            }
            finally { handle.Free(); }
        }

        // ── Receive loop ──────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!SteamManager.Initialized) return;
            var msgs = new System.IntPtr[64]; // up to 64 messages drained per frame

            // Host polls the poll group (aggregates all client connections into one call).
            // Client polls its single server connection.
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

        /// <summary>Copies message data out of the unmanaged SteamNetworkingMessage_t buffer,
        /// dispatches it, then releases the native buffer back to Steam.</summary>
        private void DispatchAndRelease(System.IntPtr ptr)
        {
            var msg = System.Runtime.InteropServices.Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptr);
            var data = new byte[msg.m_cbSize];
            System.Runtime.InteropServices.Marshal.Copy(msg.m_pData, data, 0, msg.m_cbSize);
            var sender = msg.m_identityPeer.GetSteamID();
            var senderConn = msg.m_conn;
            RouteMessage(sender, data, senderConn);
            // Must release after we've finished reading msg.m_pData to avoid a use-after-free.
            SteamNetworkingMessage_t.Release(ptr);
        }

        /// <summary>Dispatches an inbound message by type byte.
        /// GameStart is handled in-layer. All command/event types are forwarded to
        /// NetCommandApplier and then echoed to other clients by the host.</summary>
        private void RouteMessage(CSteamID sender, byte[] payload, HSteamNetConnection senderConn)
        {
            if (payload == null || payload.Length == 0) return;
            switch ((NetMessageType)payload[0])
            {
                case NetMessageType.GameStart:
                    // Clients receive this from the host. The host never receives it
                    // (it sends, then calls RaiseGameStartReceived locally via LobbyPanel).
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
                case NetMessageType.CmdPurchaseUpgrade:
                    // Apply on this client (local-immediate + echo pattern).
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

        // ── P2P socket lifecycle ──────────────────────────────────────────────────────

        /// <summary>Host-only: opens a P2P listen socket and creates a poll group.
        /// Idempotent — safe to call multiple times.</summary>
        private void StartListening()
        {
            if (_listenSocket != HSteamListenSocket.Invalid) return; // idempotent
            var opts = Array.Empty<SteamNetworkingConfigValue_t>();
            // Virtual port 0 — we only have one logical game session per process.
            _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, opts.Length, opts);
            _pollGroup = SteamNetworkingSockets.CreatePollGroup();
            Debug.Log($"[Net] Listen socket created: {_listenSocket.m_HSteamListenSocket}");
        }

        /// <summary>Client-only: initiates a P2P connection to the lobby owner.
        /// Idempotent — safe to call multiple times.</summary>
        private void ConnectToHost(CSteamID host)
        {
            if (_serverConnection != HSteamNetConnection.Invalid) return; // idempotent
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(host);
            var opts = Array.Empty<SteamNetworkingConfigValue_t>();
            _serverConnection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, opts.Length, opts);
            Debug.Log($"[Net] Connecting to host {host} conn={_serverConnection.m_HSteamNetConnection}");
        }

        /// <summary>Closes all connections and destroys the listen socket / poll group.
        /// Called on lobby leave and OnDestroy. Skips native cleanup if Steamworks is
        /// already shut down to avoid invalid-handle crashes.</summary>
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
            // Snapshot the keys to avoid modifying the dictionary while iterating.
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

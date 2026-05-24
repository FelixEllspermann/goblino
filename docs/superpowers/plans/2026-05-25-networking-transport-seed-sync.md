# Networking Transport + World Seed Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Steam P2P transport (SteamNetworkingSockets) bound to lobby lifecycle and use it to sync the world seed at game start. After this plan, host clicks "Start Game" and all members load SampleScene with identical maps.

**Architecture:** `NetworkManager` singleton wraps SteamNetworkingSockets (modern API). Connections come up when `LobbyManager.OnLobbyEntered` fires (host listens, clients connect to lobby owner). Static `NetworkSession` holds session state across scene loads. Binary protocol (1-byte type + payload). `GameStart` message carries the seed. `GameStartLoader` listens for the event and triggers `SceneManager.LoadScene("SampleScene")` on both host and clients.

**Tech Stack:** Unity 6 / Steamworks.NET 2025.163.0 / Steam Networking Sockets / C# / Unity MCP for editor automation.

**Spec:** `docs/superpowers/specs/2026-05-25-networking-transport-seed-sync-design.md`
**Style reference:** `Assets/Scripts/SteamManager.cs` and `Assets/Scripts/Lobby/LobbyManager.cs` (singleton pattern to mirror).

---

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Scripts/Lobby/NetworkSession.cs` | Static session state (IsHost, GameSeed, host/local IDs, OnGameStartReceived event) |
| `Assets/Scripts/Lobby/NetMessages.cs` | Static encode/decode helpers + NetMessageType enum |
| `Assets/Scripts/Lobby/NetworkManager.cs` | Singleton wrapping SteamNetworkingSockets (lifecycle + send/receive) |
| `Assets/Scripts/Lobby/GameStartLoader.cs` | Singleton MonoBehaviour that loads SampleScene when OnGameStartReceived fires |
| `Assets/Scripts/Lobby/LobbyPanel.cs` | **modify** — Start button host-gated + connection-count gate + click handler |
| `Assets/Scripts/World/Unity/WorldGeneratorBootstrap.cs` | **modify** — `Start()` uses NetworkSession.GameSeed if non-zero |

All scripts live in `Assembly-CSharp` (default, no asmdef). The Lobby folder already contains LobbyInfo / LobbyManager / panels from the previous sub-project — these new files sit alongside.

---

## Verification Convention

Each task ends with:
1. **Compile check** via `mcp__unity-mcp__Unity_RunCommand` running `AssetDatabase.Refresh()`, then `mcp__unity-mcp__Unity_ReadConsole` Types=["Error"]. Expected: 0 errors.
2. **Behavior verification** where feasible. Steam-bound flows (host/client handshake) require two Steam clients — fallback is compile-clean + structural correctness via reading code/scene.
3. **Single commit** per task.

MCP `RunCommand` wraps your code in `namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`. The class must be `internal class CommandScript : IRunCommand`. If you reference `Image`, alias it: `using UImage = UnityEngine.UI.Image;`.

The MCP sandbox blocks `System.Reflection` and types from Steamworks.NET in lambda generics — work around by writing pure logging code or by calling project static APIs that return primitives. Multi-client Steam testing is deferred to manual play-mode by the user.

---

## Task 1: NetworkSession + NetMessages

**Files:**
- Create: `Assets/Scripts/Lobby/NetworkSession.cs`
- Create: `Assets/Scripts/Lobby/NetworkSession.cs.meta`
- Create: `Assets/Scripts/Lobby/NetMessages.cs`
- Create: `Assets/Scripts/Lobby/NetMessages.cs.meta`

- [ ] **Step 1: Create NetworkSession.cs**

```csharp
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
```

- [ ] **Step 2: Create NetworkSession.cs.meta**

```yaml
fileFormatVersion: 2
guid: a7e92103c5d18b94f9a0e6543fbcd6182
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Create NetMessages.cs**

```csharp
using System.IO;

namespace RTSCL.Lobby
{
    public enum NetMessageType : byte
    {
        GameStart = 1,
    }

    /// <summary>Binary pack/unpack helpers for the wire protocol. Frame = [byte type | payload].</summary>
    public static class NetMessages
    {
        public static byte[] PackGameStart(int seed)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)NetMessageType.GameStart);
            w.Write(seed);
            return ms.ToArray();
        }

        public static bool TryUnpackGameStart(byte[] payload, out int seed)
        {
            seed = 0;
            if (payload == null || payload.Length < 5) return false;
            if (payload[0] != (byte)NetMessageType.GameStart) return false;
            using var ms = new MemoryStream(payload, 1, payload.Length - 1);
            using var r = new BinaryReader(ms);
            seed = r.ReadInt32();
            return true;
        }
    }
}
```

- [ ] **Step 4: Create NetMessages.cs.meta**

```yaml
fileFormatVersion: 2
guid: b8f03214d6e29ca50ab1f7654fcde7293
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 5: Compile check via MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `mcp__unity-mcp__Unity_ReadConsole` Types=["Error"]. Expected: clean.

- [ ] **Step 6: Round-trip verify NetMessages via MCP**

```csharp
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var bytes = NetMessages.PackGameStart(12345);
        result.Log("Packed length: {0}, type byte: {1}", bytes.Length, bytes[0]);
        bool ok = NetMessages.TryUnpackGameStart(bytes, out int seed);
        result.Log("Unpacked: ok={0} seed={1}", ok, seed);
        // Negative path
        bool fail1 = NetMessages.TryUnpackGameStart(null, out _);
        bool fail2 = NetMessages.TryUnpackGameStart(new byte[]{ 0, 0 }, out _);
        bool fail3 = NetMessages.TryUnpackGameStart(new byte[]{ 99, 0, 0, 0, 0 }, out _);
        result.Log("Failure cases all false: {0}", !fail1 && !fail2 && !fail3);
    }
}
```

Expected:
- `Packed length: 5, type byte: 1`
- `Unpacked: ok=True seed=12345`
- `Failure cases all false: True`

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Lobby/NetworkSession.cs Assets/Scripts/Lobby/NetworkSession.cs.meta Assets/Scripts/Lobby/NetMessages.cs Assets/Scripts/Lobby/NetMessages.cs.meta
git commit -m "feat(net): NetworkSession state + NetMessages binary protocol (GameStart)"
```

---

## Task 2: NetworkManager skeleton + lobby event hookups

**Files:**
- Create: `Assets/Scripts/Lobby/NetworkManager.cs`
- Create: `Assets/Scripts/Lobby/NetworkManager.cs.meta`

- [ ] **Step 1: Create NetworkManager.cs (skeleton only — no SNS calls yet)**

```csharp
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
            // Listen/Connect added in later tasks.
        }

        private void HandleLobbyLeft()
        {
            Disconnect();
            NetworkSession.Reset();
        }

        // Public API — actual implementations come in later tasks.
        public static void SendToAll(byte[] payload) { /* Task 6 */ }

        // Internal lifecycle methods invoked by HandleLobbyEntered/Left in later tasks
        private void StartListening()             { /* Task 4 */ }
        private void ConnectToHost(CSteamID host) { /* Task 5 */ }
        private void Disconnect()                 { _connections.Clear(); /* Task 3+ extend */ }
    }
}
```

- [ ] **Step 2: Create NetworkManager.cs.meta**

```yaml
fileFormatVersion: 2
guid: c9013425e7f3adb610c2087650de8304
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Compile check via MCP**

Same as Task 1 Step 5. Expected: 0 errors.

- [ ] **Step 4: Verify auto-bootstrap in play mode**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        var nm = Object.FindFirstObjectByType<NetworkManager>();
        result.Log("NetworkManager present: {0}", nm != null);
        result.Log("ConnectedCount: {0}", NetworkManager.ConnectedCount);
        result.Log("Session IsHost={0} Seed={1}", NetworkSession.IsHost, NetworkSession.GameSeed);
    }
}
```

Run twice if first call entered play mode. Expected:
- `NetworkManager present: True`
- `ConnectedCount: 0`
- `Session IsHost=False Seed=0`

Exit play.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Lobby/NetworkManager.cs Assets/Scripts/Lobby/NetworkManager.cs.meta
git commit -m "feat(net): NetworkManager singleton skeleton + lobby event hookups"
```

---

## Task 3: SNS init + Status callback registration

**Files:**
- Modify: `Assets/Scripts/Lobby/NetworkManager.cs`

- [ ] **Step 1: Add SNS callback field + InitRelayNetworkAccess**

Add field near `_connections`:

```csharp
private Callback<SteamNetConnectionStatusChangedCallback_t> _cbStatus;
```

Extend `OnEnable` (keep existing lobby subscriptions):

```csharp
private void OnEnable()
{
    LobbyManager.OnLobbyEntered += HandleLobbyEntered;
    LobbyManager.OnLobbyLeft    += HandleLobbyLeft;
    _cbStatus = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnSteamStatusChanged);
    if (SteamManager.Initialized) SteamNetworkingUtils.InitRelayNetworkAccess();
}
```

- [ ] **Step 2: Add the Status handler (logs only — accept/connect logic in Tasks 4/5)**

```csharp
private void OnSteamStatusChanged(SteamNetConnectionStatusChangedCallback_t e)
{
    var conn = e.m_hConn;
    var remote = e.m_info.m_identityRemote.GetSteamID();
    var state = e.m_info.m_eState;
    Debug.Log($"[Net] Status change: conn={conn.m_HSteamNetConnection} state={state} remote={remote}");

    if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
     || state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
    {
        if (_connections.Remove(conn))
            OnDisconnected?.Invoke(remote);
        SteamNetworkingSockets.CloseConnection(conn, 0, string.Empty, false);
    }
}
```

- [ ] **Step 3: Compile check via MCP**

Expected: clean.

- [ ] **Step 4: Play-mode smoke test — confirm callback registers without error**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        result.Log("Steam initialized: {0}", SteamManager.Initialized);
        result.Log("NetworkManager.ConnectedCount: {0}", NetworkManager.ConnectedCount);
    }
}
```

Then read console for errors. Expected: no errors (the `[Net] Status change` log only fires when there's actual connection activity, which hasn't started yet).

Exit play.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Lobby/NetworkManager.cs
git commit -m "feat(net): InitRelayNetworkAccess + SNS Status callback (logging only)"
```

---

## Task 4: Host StartListening + accept inbound connections

**Files:**
- Modify: `Assets/Scripts/Lobby/NetworkManager.cs`

- [ ] **Step 1: Add listen-socket + poll-group fields**

```csharp
private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
private HSteamNetPollGroup _pollGroup = HSteamNetPollGroup.Invalid;
```

- [ ] **Step 2: Replace `StartListening()` stub with real implementation**

```csharp
private void StartListening()
{
    if (_listenSocket != HSteamListenSocket.Invalid) return; // idempotent
    var opts = Array.Empty<SteamNetworkingConfigValue_t>();
    _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, opts.Length, opts);
    _pollGroup = SteamNetworkingSockets.CreatePollGroup();
    Debug.Log($"[Net] Listen socket created: {_listenSocket.m_HSteamListenSocket}");
}
```

- [ ] **Step 3: Wire it into HandleLobbyEntered**

Replace the `// Listen/Connect added in later tasks.` comment in `HandleLobbyEntered` with:

```csharp
if (NetworkSession.IsHost) StartListening();
// ConnectToHost added in Task 5.
```

- [ ] **Step 4: Extend Status handler to AcceptConnection on Connecting state**

Add this block at the top of `OnSteamStatusChanged` (before the disconnect block):

```csharp
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
```

- [ ] **Step 5: Extend Disconnect to clean up listen socket + poll group**

Replace the `Disconnect()` body:

```csharp
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
```

- [ ] **Step 6: Compile check via MCP**

Expected: clean.

- [ ] **Step 7: Smoke test — host-alone create lobby**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        LobbyManager.CreateLobby();
        result.Log("Created lobby. Wait 3s then run second cmd to check state.");
    }
}
```

Wait 3s, then:

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        result.Log("InLobby={0} IsHost={1} ConnectedCount={2}",
            LobbyManager.InLobby, NetworkSession.IsHost, NetworkManager.ConnectedCount);
    }
}
```

Expected (if Steam connected): `InLobby=True IsHost=True ConnectedCount=0`. Console may show `[Net] Listen socket created: <some number>`. No errors.

Exit play.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Lobby/NetworkManager.cs
git commit -m "feat(net): host listen socket + poll group + accept inbound connections"
```

---

## Task 5: Client ConnectToHost

**Files:**
- Modify: `Assets/Scripts/Lobby/NetworkManager.cs`

- [ ] **Step 1: Add server-connection field**

```csharp
private HSteamNetConnection _serverConnection = HSteamNetConnection.Invalid;
```

- [ ] **Step 2: Replace `ConnectToHost(CSteamID)` stub with real implementation**

```csharp
private void ConnectToHost(CSteamID host)
{
    if (_serverConnection != HSteamNetConnection.Invalid) return; // idempotent
    var identity = new SteamNetworkingIdentity();
    identity.SetSteamID(host);
    var opts = Array.Empty<SteamNetworkingConfigValue_t>();
    _serverConnection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, opts.Length, opts);
    Debug.Log($"[Net] Connecting to host {host} conn={_serverConnection.m_HSteamNetConnection}");
}
```

- [ ] **Step 3: Wire it into HandleLobbyEntered (replace the `// ConnectToHost added in Task 5.` comment)**

The full `HandleLobbyEntered` becomes:

```csharp
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
```

- [ ] **Step 4: Extend Status handler — client-side Connected/Disconnected**

In `OnSteamStatusChanged`, extend the existing `Connected` block to also handle the client case:

```csharp
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
```

In the disconnect block, also clear `_serverConnection`:

```csharp
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
```

- [ ] **Step 5: Extend Disconnect to close server connection**

Add this at the top of `Disconnect()` (before the host-side cleanup):

```csharp
if (_serverConnection != HSteamNetConnection.Invalid)
{
    SteamNetworkingSockets.CloseConnection(_serverConnection, 0, "disconnect", false);
    _serverConnection = HSteamNetConnection.Invalid;
}
```

- [ ] **Step 6: Compile check via MCP**

Expected: clean.

- [ ] **Step 7: Single-client smoke — verify client-path code at least doesn't crash when host=self**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        LobbyManager.CreateLobby();
        result.Log("Created (own lobby). Wait 3s then check.");
    }
}
```

Wait 3s, then check console + state. Expected: `IsHost=True ConnectedCount=0` and no errors. Multi-client handshake testing is deferred to manual play with 2 Steam clients.

Exit play.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Lobby/NetworkManager.cs
git commit -m "feat(net): client ConnectToHost + connection state machine on both sides"
```

---

## Task 6: Send + receive + message dispatch (GameStart wired)

**Files:**
- Modify: `Assets/Scripts/Lobby/NetworkManager.cs`

- [ ] **Step 1: Implement SendToAll (host or client paths)**

Replace the `SendToAll` stub with:

```csharp
public static void SendToAll(byte[] payload)
{
    if (_instance == null || payload == null || payload.Length == 0) return;
    _instance.SendToAllImpl(payload);
}

private void SendToAllImpl(byte[] payload)
{
    int flags = Constants.k_nSteamNetworkingSend_Reliable;
    if (NetworkSession.IsHost)
    {
        foreach (var conn in _connections.Keys)
        {
            var res = SteamNetworkingSockets.SendMessageToConnection(
                conn, payload, (uint)payload.Length, flags, out _);
            if (res != EResult.k_EResultOK)
                Debug.LogWarning($"[Net] SendToConnection failed: {res}");
        }
    }
    else if (_serverConnection != HSteamNetConnection.Invalid)
    {
        var res = SteamNetworkingSockets.SendMessageToConnection(
            _serverConnection, payload, (uint)payload.Length, flags, out _);
        if (res != EResult.k_EResultOK)
            Debug.LogWarning($"[Net] SendToServer failed: {res}");
    }
}
```

- [ ] **Step 2: Add Update loop to poll incoming messages**

```csharp
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
    RouteMessage(sender, data);
    SteamNetworkingMessage_t.Release(ptr);
}

private void RouteMessage(CSteamID sender, byte[] payload)
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
            if (NetMessages.TryUnpackGameStart(payload, out int seed))
            {
                NetworkSession.GameSeed = seed;
                NetworkSession.RaiseGameStartReceived();
            }
            break;
        default:
            Debug.LogWarning($"[Net] Unknown message type: {payload[0]}");
            break;
    }
}
```

- [ ] **Step 3: Compile check via MCP**

Expected: clean.

- [ ] **Step 4: Verify send-to-self routing (host alone, send to empty connection list)**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        // Without a lobby, NetworkManager isn't in host mode — SendToAll just no-ops.
        // With a host-alone lobby, SendToAll iterates 0 connections (no-op too).
        // This task's behavior is verified end-to-end in Task 10's smoke test.
        result.Log("ConnectedCount={0} IsHost={1}", NetworkManager.ConnectedCount, NetworkSession.IsHost);
        NetworkManager.SendToAll(NetMessages.PackGameStart(42));
        result.Log("SendToAll(GameStart(42)) called. Check console for warnings/errors.");
    }
}
```

Read console after. Expected: no warnings, no errors. (SendToAll iterates 0 connections; harmless.)

Exit play.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Lobby/NetworkManager.cs
git commit -m "feat(net): SendToAll + per-frame message polling + GameStart dispatch"
```

---

## Task 7: GameStartLoader (auto-spawned scene loader)

**Files:**
- Create: `Assets/Scripts/Lobby/GameStartLoader.cs`
- Create: `Assets/Scripts/Lobby/GameStartLoader.cs.meta`

- [ ] **Step 1: Create GameStartLoader.cs**

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RTSCL.Lobby
{
    /// <summary>Listens for NetworkSession.OnGameStartReceived and loads the SampleScene.
    /// Single source of truth for the host + client scene load.</summary>
    [DisallowMultipleComponent]
    public sealed class GameStartLoader : MonoBehaviour
    {
        private static GameStartLoader _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject(nameof(GameStartLoader));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameStartLoader>();
        }

        private void OnEnable()  => NetworkSession.OnGameStartReceived += LoadGameScene;
        private void OnDisable() => NetworkSession.OnGameStartReceived -= LoadGameScene;

        private void LoadGameScene() => SceneManager.LoadScene("SampleScene");
    }
}
```

- [ ] **Step 2: Create GameStartLoader.cs.meta**

```yaml
fileFormatVersion: 2
guid: d0124536f8043bc721d319876fef9415
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Compile check via MCP**

Expected: clean.

- [ ] **Step 4: Verify loader fires a scene-load when the event is raised**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        var before = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        result.Log("Before: scene={0}", before);
        NetworkSession.GameSeed = 7777;
        NetworkSession.RaiseGameStartReceived();
        result.Log("Event raised. Re-run cmd in ~1s to see new scene.");
    }
}
```

Wait 1s, then:

```csharp
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        result.Log("After: scene={0}", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        result.Log("Seed={0}", RTSCL.Lobby.NetworkSession.GameSeed);
    }
}
```

Expected: `After: scene=SampleScene` (or whatever the game scene is). `Seed=7777`.

Exit play.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Lobby/GameStartLoader.cs Assets/Scripts/Lobby/GameStartLoader.cs.meta
git commit -m "feat(net): GameStartLoader auto-spawned scene loader"
```

---

## Task 8: LobbyPanel Start button — host-gated, connection-count aware

**Files:**
- Modify: `Assets/Scripts/Lobby/LobbyPanel.cs`

- [ ] **Step 1: Add new event subscriptions + Start handler**

Replace `LobbyPanel.cs` body with:

```csharp
using UnityEngine;
using UnityEngine.UI;
using Steamworks;

namespace RTSCL.Lobby
{
    public sealed class LobbyPanel : MonoBehaviour
    {
        [SerializeField] private MenuRoot _root;
        [SerializeField] private Text _headerLabel;
        [SerializeField] private RectTransform _memberListContent;
        [SerializeField] private Button _leaveButton;
        [SerializeField] private Button _startButton;
        [SerializeField] private Font _rowFont;
        [SerializeField] private Sprite _rowSprite;

        private void OnEnable()
        {
            if (_leaveButton != null) _leaveButton.onClick.AddListener(OnLeave);
            if (_startButton != null) _startButton.onClick.AddListener(OnStart);
            LobbyManager.OnLobbyMembersChanged += Refresh;
            NetworkManager.OnConnected         += OnConnectionsChanged;
            NetworkManager.OnDisconnected      += OnConnectionsChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (_leaveButton != null) _leaveButton.onClick.RemoveListener(OnLeave);
            if (_startButton != null) _startButton.onClick.RemoveListener(OnStart);
            LobbyManager.OnLobbyMembersChanged -= Refresh;
            NetworkManager.OnConnected         -= OnConnectionsChanged;
            NetworkManager.OnDisconnected      -= OnConnectionsChanged;
        }

        private void OnLeave() => LobbyManager.LeaveLobby();

        private void OnStart()
        {
            if (!NetworkSession.IsHost) return;
            var seed = Random.Range(1, int.MaxValue);
            NetworkSession.GameSeed = seed;
            NetworkManager.SendToAll(NetMessages.PackGameStart(seed));
            SteamMatchmaking.SetLobbyJoinable(LobbyManager.CurrentLobby, false);
            NetworkSession.RaiseGameStartReceived();
        }

        private void OnConnectionsChanged(CSteamID _) => RefreshStartButton();

        private void Refresh()
        {
            if (!LobbyManager.InLobby) return;

            string host = SteamMatchmaking.GetLobbyData(LobbyManager.CurrentLobby, LobbyManager.LobbyDataHostName);
            if (string.IsNullOrEmpty(host)) host = "Unknown";
            if (_headerLabel != null) _headerLabel.text = $"{host}'s Lobby";

            if (_memberListContent != null)
            {
                for (int i = _memberListContent.childCount - 1; i >= 0; i--)
                    Destroy(_memberListContent.GetChild(i).gameObject);
                foreach (var (id, name, isHost) in LobbyManager.GetCurrentMembers())
                    BuildRow(name, isHost);
            }

            RefreshStartButton();
        }

        private void RefreshStartButton()
        {
            if (_startButton == null) return;
            bool isHost = NetworkSession.IsHost;
            int memberCount = LobbyManager.InLobby
                ? SteamMatchmaking.GetNumLobbyMembers(LobbyManager.CurrentLobby)
                : 0;
            int connected = NetworkManager.ConnectedCount;
            bool ready = isHost && connected >= memberCount - 1;
            _startButton.interactable = ready;
            var label = _startButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                if (!isHost)
                    label.text = "Waiting for host…";
                else if (ready)
                    label.text = "Start Game";
                else
                    label.text = $"Waiting for {memberCount - 1 - connected} player(s)…";
            }
        }

        private void BuildRow(string name, bool isHost)
        {
            var row = new GameObject($"Member_{name}", typeof(RectTransform));
            row.transform.SetParent(_memberListContent, false);
            var bg = row.AddComponent<Image>();
            bg.sprite = _rowSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.18f, 0.18f, 0.22f, 0.95f);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 40;
            le.flexibleWidth = 1;

            var lbl = new GameObject("Label", typeof(RectTransform));
            lbl.transform.SetParent(row.transform, false);
            var lrt = lbl.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(12, 0); lrt.offsetMax = new Vector2(-12, 0);
            var txt = lbl.AddComponent<Text>();
            txt.text = isHost ? $"{name}  (host)" : name;
            txt.font = _rowFont; txt.fontSize = 16; txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }
}
```

(Only changes from the previous LobbyPanel: `OnEnable`/`OnDisable` subscribe to `NetworkManager.OnConnected/Disconnected`, the start button is no longer forced `interactable = false`, new `OnStart` handler, new `RefreshStartButton` method, `Refresh` calls `RefreshStartButton`.)

- [ ] **Step 2: Compile check via MCP**

Expected: clean.

- [ ] **Step 3: Smoke — create a lobby alone and verify button state**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        LobbyManager.CreateLobby();
        result.Log("Lobby creation requested. Wait 3s, then check state.");
    }
}
```

Wait 3s, then:

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var lp = Object.FindFirstObjectByType<LobbyPanel>(FindObjectsInactive.Include);
        result.Log("InLobby={0} IsHost={1} ConnectedCount={2}",
            LobbyManager.InLobby, NetworkSession.IsHost, NetworkManager.ConnectedCount);
        // Need to look at Start button. Find by traversal:
        if (lp != null)
        {
            var btnField = lp.GetType().GetField("_startButton",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            // Reflection may be blocked by MCP sandbox — fall back to compile-clean evidence.
            result.Log("LobbyPanel found: {0}. Manual verify Start button is enabled if isHost+0 connections needed.", lp != null);
        }
    }
}
```

Manually verify in editor: after creating own lobby, "Start Game" button shows "Start Game" (enabled) because IsHost=True + memberCount-1=0 == ConnectedCount=0.

Exit play.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Lobby/LobbyPanel.cs
git commit -m "feat(menu): LobbyPanel Start button host-gated with connection-count check"
```

---

## Task 9: WorldGeneratorBootstrap reads NetworkSession.GameSeed

**Files:**
- Modify: `Assets/Scripts/World/Unity/WorldGeneratorBootstrap.cs`

- [ ] **Step 1: Update Start() to branch on NetworkSession.GameSeed**

Find the `Start()` method (likely a one-liner `private void Start() => Regenerate();`). Replace with:

```csharp
private void Start()
{
    if (RTSCL.Lobby.NetworkSession.GameSeed != 0)
        RegenerateWithSeed(RTSCL.Lobby.NetworkSession.GameSeed);
    else
        Regenerate();
}
```

(Uses fully-qualified namespace to avoid adding a using statement at the top of the file.)

- [ ] **Step 2: Compile check via MCP**

Expected: clean.

- [ ] **Step 3: Verify Solo path still works (random seed)**

```csharp
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        // Ensure NetworkSession is clean (no MP seed)
        NetworkSession.Reset();
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        result.Log("Active scene: {0}, NetworkSession.GameSeed={1}",
            SceneManager.GetActiveScene().name, NetworkSession.GameSeed);
    }
}
```

Then click "Play Solo" in the running MainMenu (manual): SampleScene should load and generate a new random map (different each restart). No errors in console.

Exit play.

- [ ] **Step 4: Verify MP path uses the set seed**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        // Simulate: set seed + raise event (GameStartLoader will load SampleScene)
        NetworkSession.GameSeed = 12345;
        NetworkSession.RaiseGameStartReceived();
        result.Log("Set seed=12345 + raised event. Re-run cmd in 1s.");
    }
}
```

Wait 1s, then:

```csharp
using UnityEngine;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        result.Log("Scene={0} Seed={1}",
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            NetworkSession.GameSeed);
        // Check the WorldGenerator if reachable
        var bootstrap = Object.FindFirstObjectByType<RTSCL.World.Unity.WorldGeneratorBootstrap>();
        result.Log("Bootstrap present: {0}", bootstrap != null);
        if (bootstrap != null && bootstrap.CurrentWorld != null)
            result.Log("World Seed: {0}", bootstrap.CurrentWorld.Seed);
    }
}
```

Expected: `Scene=SampleScene`, `World Seed: 12345`.

Exit play.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/WorldGeneratorBootstrap.cs
git commit -m "feat(world): WorldGenerator uses NetworkSession.GameSeed in MP, random in Solo"
```

---

## Task 10: End-to-end host-alone smoke test + commit message

**Files:**
- None (verification + closing commit)

- [ ] **Step 1: Run the full MP boot path with single client**

This is a behavioral test that exercises every component end-to-end on one machine.

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        result.Log("Scene: {0}", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        result.Log("Expected: MainMenu. NetworkManager auto-bootstrapped.");
    }
}
```

Then manually in the editor:
1. Click "Multiplayer" → switches to MultiplayerPanel.
2. Click "Create Lobby" → after Steam round-trip, switches to LobbyPanel.
3. Confirm Start Game button is enabled (you're host, 0 other connections needed).
4. Click "Start Game" → SampleScene loads, world generates with a random seed (visible as a map).
5. Check Console: should see `[Net] Listen socket created`, no errors.

If Steam is unreachable in your test environment, this manual test is deferred to the user.

- [ ] **Step 2: Final compile + console check via MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `mcp__unity-mcp__Unity_ReadConsole` Types=["Error"]. Expected: 0 errors.

- [ ] **Step 3: No commit needed — Task 10 is purely verification**

If steps 1-2 pass, the feature is complete. If they reveal a bug, fix in a follow-up commit.

---

## Self-Review Checklist

- [x] **Spec coverage:**
  - NetworkManager singleton + lifecycle → Tasks 2, 3, 4, 5
  - Connection state machine → Tasks 4, 5
  - Binary message protocol → Task 1
  - GameStart message → Tasks 1, 6
  - NetworkSession state → Task 1
  - LobbyManager integration → Task 2 (hookup), Task 5 (host-left → LeaveLobby)
  - LobbyPanel Start button → Task 8
  - WorldGeneratorBootstrap seed integration → Task 9
  - GameStartLoader → Task 7
  - Lobby unjoinable on start → Task 8 (OnStart)
- [x] **No placeholders** — every code block is complete
- [x] **Type consistency** — `NetworkSession`, `NetMessages`, `NetMessageType.GameStart`, `NetworkManager.SendToAll`, `NetworkManager.ConnectedCount`, `NetworkManager.OnConnected/OnDisconnected`, `NetworkSession.OnGameStartReceived`, `NetworkSession.RaiseGameStartReceived` used uniformly across tasks
- [x] **Each task is one commit** (Task 10 is verification, no commit)
- [x] **MCP-based verification per task** — multi-client Steam testing flagged as manual

## Open verification gaps (acknowledged)

- Two-Steam-client handshake (host + remote guest connecting, receiving GameStart, both loading SampleScene with identical seed) requires two physical Steam logins on different machines. Per-task structural correctness + host-alone smoke (Task 10) is the bar; cross-client validation deferred to manual.
- `InitRelayNetworkAccess` failure mode (no relay available) cannot be reproduced without disabling Steam relay — falls back to slower connection establishment, no functional regression.
- Mid-game host-disconnect path (client returns to MainPanel) verified by code review of Task 5 Step 4; live test requires two clients.

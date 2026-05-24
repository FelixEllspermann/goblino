# Networking Transport + World Seed Sync

Date: 2026-05-25
Status: Approved for implementation

## Purpose

Build the Steam P2P networking foundation and use it to synchronise the world seed at game start. After this sub-project, the Lobby's "Start Game" button is functional: host clicks Start, all members load `SampleScene` with the same generated map. Player ownership, command sync, and combat-over-network remain in later sub-projects, but the message-passing infrastructure is in place for them.

## Scope

In scope:
- `NetworkManager` singleton wrapping Steam Networking Sockets (SNS — modern API)
- Listen socket (host) + outgoing connection (client) lifecycle bound to lobby join/leave
- Reliable typed message protocol (1-byte type + binary payload)
- `NetworkSession` static state (IsHost, LocalPlayer, HostPlayer, GameSeed)
- `GameStart` message carrying the world seed
- LobbyPanel "Start Game" button activated for host once all members are connected
- WorldGeneratorBootstrap uses `NetworkSession.GameSeed` for MP, random seed for Solo
- Lobby auto-unjoinable after game start

Out of scope (later sub-projects):
- Game-state replication / command sync (#5)
- Combat over the network (#6)
- Reconnect after dropped connection
- Lobby chat over the P2P channel
- Voice chat
- End-of-game / "play again" flow

## Architecture

### Persistent managers

| Manager | Source | Responsibility |
|---|---|---|
| `SteamManager` | existing | Bootstrap + shutdown Steam API |
| `LobbyManager` | existing | Steam matchmaking, lobby membership |
| `NetworkManager` | **new** | Steam Networking Sockets transport, send/receive |
| `NetworkSession` | **new (static)** | Session state (IsHost, GameSeed, host/local IDs) |

All managers auto-spawn via `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`, hold `DontDestroyOnLoad`, and survive scene transitions. `NetworkSession` is plain-static, holds no MonoBehaviour, gets reset by NetworkManager when lobby is left.

### Connection lifecycle

- `LobbyManager.OnLobbyEntered` → `NetworkManager.HandleLobbyEntered`:
  - Read lobby owner. Set `NetworkSession.LocalPlayer = SteamUser.GetSteamID()`, `NetworkSession.HostPlayer = owner`, `NetworkSession.IsHost = (owner == LocalPlayer)`.
  - If host → `StartListening()` (CreateListenSocketP2P + CreatePollGroup).
  - Else → `ConnectToHost(owner)` (ConnectP2P with `SteamNetworkingIdentity`).
- `LobbyManager.OnLobbyLeft` → `NetworkManager.HandleLobbyLeft`:
  - `Disconnect()` (close socket / connection, destroy poll group, clear connection map).
  - `NetworkSession.Reset()`.

### Status callback

`SteamNetConnectionStatusChangedCallback_t` drives the connection state machine:
- `Connecting` (host receiving an inbound connect): `AcceptConnection` + `SetConnectionPollGroup`.
- `Connected`: add to `_connections` map (host) or store `_serverConnection` (client) + emit `OnConnected(remoteId)`.
- `ProblemDetectedLocally` / `ClosedByPeer`: `CloseConnection` + remove from map + emit `OnDisconnected(remoteId)`. If client lost connection to host → trigger `LobbyManager.LeaveLobby` so the UI returns to the main menu.

### Update loop

Each frame, `NetworkManager.Update`:
- Host: `ReceiveMessagesOnPollGroup(_pollGroup, msgs, 64)` → dispatch.
- Client: `ReceiveMessagesOnConnection(_serverConnection, msgs, 64)` → dispatch.
- Per message: copy native bytes to a managed `byte[]`, call `Marshal.FreeHGlobal(msg)`, then `RouteMessage(senderId, payload)`.

Steam callbacks are already pumped by `SteamManager.Update` (it calls `SteamAPI.RunCallbacks()`), so NetworkManager only needs to poll its own message queues.

## Message Protocol

Messages are framed as `[ byte type | payload ... ]`.

```csharp
public enum NetMessageType : byte
{
    GameStart = 1,   // payload: int seed (4 bytes)
    // GameOver = 2, Command = 3, ... in later sub-projects
}
```

Encoding/decoding helpers in `NetMessages`:

```csharp
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
```

All messages in this sub-project use `k_nSteamNetworkingSend_Reliable` — GameStart must arrive exactly once.

`NetworkManager.RouteMessage(senderId, payload)` switches on `payload[0]`:
- `GameStart` → `NetMessages.TryUnpackGameStart(payload, out seed)` → `NetworkSession.GameSeed = seed` → emit `NetworkSession.OnGameStartReceived`.

## NetworkSession

```csharp
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
```

## NetworkManager — public surface

```csharp
public sealed class NetworkManager : MonoBehaviour
{
    public static event Action<CSteamID> OnConnected;
    public static event Action<CSteamID> OnDisconnected;
    public static event Action<string> OnError;
    public static int ConnectedCount { get; }       // host: number of accepted connections; client: 0 or 1

    public static void SendToAll(byte[] payload);   // reliable
    // Internal lifecycle methods (StartListening, ConnectToHost, Disconnect)
    // are invoked from LobbyManager event handlers and are not part of the public API.
}
```

## Integration with existing systems

### LobbyPanel

The "Start Game" button changes from "always disabled" to host-only-when-everyone-connected:

```csharp
private void RefreshStartButton()
{
    if (_startButton == null) return;
    bool isHost = NetworkSession.IsHost;
    int memberCount = SteamMatchmaking.GetNumLobbyMembers(LobbyManager.CurrentLobby);
    int connectedCount = NetworkManager.ConnectedCount;
    // Host counts itself + N accepted connections; we need N == memberCount - 1.
    bool ready = isHost && connectedCount >= memberCount - 1;
    _startButton.interactable = ready;

    var label = _startButton.GetComponentInChildren<Text>();
    if (label != null)
        label.text = !isHost ? "Waiting for host…"
                   : ready   ? "Start Game"
                             : $"Waiting for {memberCount - 1 - connectedCount} player(s)…";
}
```

Subscribe to `LobbyManager.OnLobbyMembersChanged` and `NetworkManager.OnConnected`/`OnDisconnected` to refresh.

### Start handler

```csharp
private void OnStart()
{
    if (!NetworkSession.IsHost) return;
    var seed = UnityEngine.Random.Range(1, int.MaxValue);
    NetworkSession.GameSeed = seed;
    NetworkManager.SendToAll(NetMessages.PackGameStart(seed));
    SteamMatchmaking.SetLobbyJoinable(LobbyManager.CurrentLobby, false);
    // Trigger the same scene-load path as clients use:
    NetworkSession.RaiseGameStartReceived();
}
```

### Scene loader

A small `GameStartLoader` MonoBehaviour (also `DontDestroyOnLoad`, auto-spawned) subscribes once to `NetworkSession.OnGameStartReceived` and calls `SceneManager.LoadScene("SampleScene")`. Used by both host and client to keep a single load path.

### WorldGeneratorBootstrap

```csharp
private void Start()
{
    if (NetworkSession.GameSeed != 0)
        RegenerateWithSeed(NetworkSession.GameSeed);  // MP path — deterministic
    else
        Regenerate();                                 // Solo path — existing random-seed logic
}
```

Solo behaviour unchanged (`_seed = -1` default → `Regenerate()` picks `Random.Range(1, int.MaxValue)`).

## Edge Cases

| Scenario | Handling |
|---|---|
| Steam not initialised when entering lobby | `NetworkManager.HandleLobbyEntered` early-returns + raises `OnError("Steam not running")`. Shouldn't happen in practice (lobby join already requires Steam). |
| `InitRelayNetworkAccess()` fails | Logged as warning. Connections still work, just slower handshake (~10s). |
| Client cannot connect to host (firewall, NAT) | Status callback → `OnDisconnected` → `LobbyManager.LeaveLobby` → back to MainPanel with error toast. |
| Host disconnects mid-game | Client connection fires `ClosedByPeer` → same path as above. Lobby-side `LobbyChatUpdate_t` host-left detection also fires; both paths converge on `LeaveLobby` (idempotent). |
| Client disconnects mid-game | Host removes from `_connections` map. Game continues. End-of-game handling is later-sub-project work. |
| Host clicks Start before all clients connect | Start button is disabled until `ConnectedCount == memberCount - 1`. UI shows "Waiting for X player(s)…". |
| Mid-game join | After Start, host calls `SteamMatchmaking.SetLobbyJoinable(false)` → no new members. |
| Duplicate GameStart on client | Defensive guard: if `NetworkSession.GameSeed != 0`, ignore the message. |
| App quit while in network session | `NetworkManager.OnDestroy` closes listen socket + all connections + destroys poll group. `SteamAPI.Shutdown` (owned by SteamManager) handles the rest. |

## Out of Scope

- Game-state replication (#5)
- Reconnect on disconnect
- Lobby chat
- Voice chat
- Encryption configuration (SNS handles this transparently via Steam cert)
- End-of-game / play-again flow
- Player color / team selection (still #4)

## Testing

Single-client smoke (always feasible):
1. Solo: Play Solo → SampleScene loads with random seed (unchanged).
2. Host alone: Multiplayer → Create Lobby → LobbyPanel shows Start Game enabled (no other members needed if `memberCount - 1 == 0 == ConnectedCount`). Click Start → SampleScene loads, host gets random seed via `Random.Range`.

Multi-client (requires two Steam clients):
3. Both: see each other in LobbyPanel member list.
4. Both: P2P connection established within seconds (Status callbacks fire `Connected`).
5. Host: Start Game button becomes enabled when guest's connection state turns Connected.
6. Click Start: both load SampleScene; both world generators run with same seed → identical map (verifiable by spawn positions, terrain, resources).
7. Guest leaves mid-game: host sees disconnect, game continues.
8. Host disconnects mid-game: guest returns to MainPanel with error.

Automated tests: not feasible for Steam-bound networking. Pure helpers (`NetMessages.PackGameStart` / `TryUnpackGameStart` round-trip) are unit-testable and should have at least one test in `Assets/Tests/Editor/` against the existing `RTSCL.World.Tests` asmdef pattern (new asmdef OK if the existing one can't see new assembly).

## Open Questions / Future Work

- Should `ConnectedCount` count include the host itself? Currently no — host counts accepted connections. May want to change for clarity in #4 when player slots become a thing.
- World seed range is `[1, int.MaxValue)` — does world generator handle the upper range cleanly? `_seed < 0` is the existing "random" sentinel; positive values are direct passthrough so this is fine.
- Lobby data field `game_started=1` could be set in parallel for late-joiners who somehow slip in (race between SetLobbyJoinable and Steam server). Not needed for #3.
- Should a re-Start be possible (e.g., guest joined a fresh lobby after first game)? Currently no — once `NetworkSession.GameSeed != 0` is set, defensive guard ignores further GameStarts. Would need to reset on returning to MainPanel.

# Main Menu + Steam Lobby Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace direct-to-SampleScene boot with a MainMenu scene that offers Play Solo, Multiplayer (Steam public-lobby create/browse/join), and Quit. Game-state networking is out of scope — the lobby's Start button is disabled here and wired in the next sub-project.

**Architecture:** `LobbyManager` MonoBehaviour (singleton, `DontDestroyOnLoad`, auto-bootstrap via `RuntimeInitializeOnLoadMethod`) wraps Steamworks.NET matchmaking and exposes static events. The new `MainMenu.unity` scene contains a Canvas with three sibling Panels (Main / Multiplayer / Lobby), each driven by its own MonoBehaviour. A small `MenuRoot` switches between panels in response to button clicks and LobbyManager events.

**Tech Stack:** Unity 6 / URP 2D / C# / Steamworks.NET 2025.163.0 / Unity MCP for editor automation.

**Spec:** `docs/superpowers/specs/2026-05-24-main-menu-steam-lobby-design.md`
**Style reference:** `Assets/Scripts/SteamManager.cs` (existing singleton pattern to mirror)

---

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Scripts/Lobby/LobbyInfo.cs` | Data class for lobby browser rows |
| `Assets/Scripts/Lobby/LobbyManager.cs` | Singleton wrapping Steam matchmaking, static events, command-line parsing |
| `Assets/Scripts/Lobby/MenuRoot.cs` | Owns the three panels, handles panel switching + LobbyManager event routing |
| `Assets/Scripts/Lobby/MainPanel.cs` | Play Solo / Multiplayer / Quit buttons |
| `Assets/Scripts/Lobby/MultiplayerPanel.cs` | Create / Browse / Refresh / Back + dynamic lobby list rows |
| `Assets/Scripts/Lobby/LobbyPanel.cs` | Header + member list + Leave / Start (disabled) |
| `Assets/Scenes/MainMenu.unity` | New scene with Canvas + 3 panels |
| `ProjectSettings/EditorBuildSettings.asset` | Build index 0 = MainMenu, index 1 = SampleScene |

All scripts live in `Assembly-CSharp` (default — same as existing `SteamManager.cs`). No new asmdef needed.

---

## Verification Convention

Each task ends with:
1. **Compile check** via Unity MCP: `AssetDatabase.Refresh()` + `Unity_ReadConsole` Types=["Error"]. Expected: clean.
2. **Behavior verification** where feasible (Steam-bound flows require Steam running; UI flows require manual interaction or scripted MCP play-mode tests).
3. **Single commit** per task with a focused message.

The Unity MCP `RunCommand` wraps your code in `namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`. The class must be named `CommandScript` and marked `internal`. If you reference any type named `Image`, alias it: `using UImage = UnityEngine.UI.Image;`.

---

## Task 1: LobbyInfo + LobbyManager skeleton

**Files:**
- Create: `Assets/Scripts/Lobby/LobbyInfo.cs`
- Create: `Assets/Scripts/Lobby/LobbyInfo.cs.meta`
- Create: `Assets/Scripts/Lobby/LobbyManager.cs`
- Create: `Assets/Scripts/Lobby/LobbyManager.cs.meta`

- [ ] **Step 1: Create LobbyInfo.cs**

```csharp
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
```

- [ ] **Step 2: Create LobbyInfo.cs.meta**

```yaml
fileFormatVersion: 2
guid: d1a6708292ce8a73f86ef432fab9405a
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

- [ ] **Step 3: Create LobbyManager.cs (skeleton — no Steam calls yet)**

```csharp
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject(nameof(LobbyManager));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LobbyManager>();
        }

        // Public API — all methods no-op if Steam isn't initialized.
        public static void CreateLobby()     { /* Task 2 */ }
        public static void RequestLobbyList(){ /* Task 3 */ }
        public static void JoinLobby(CSteamID id) { /* Task 4 */ }
        public static void LeaveLobby()      { /* Task 5 */ }

        // Internal event raisers (used by Steam callback handlers in later tasks)
        internal static void RaiseLobbyCreated(CSteamID id)  => OnLobbyCreated?.Invoke(id);
        internal static void RaiseLobbyEntered(CSteamID id)  => OnLobbyEntered?.Invoke(id);
        internal static void RaiseLobbyListReceived(List<LobbyInfo> l) => OnLobbyListReceived?.Invoke(l);
        internal static void RaiseLobbyMembersChanged() => OnLobbyMembersChanged?.Invoke();
        internal static void RaiseLobbyLeft() => OnLobbyLeft?.Invoke();
        internal static void RaiseError(string msg) => OnError?.Invoke(msg);
    }
}
```

- [ ] **Step 4: Create LobbyManager.cs.meta**

```yaml
fileFormatVersion: 2
guid: e2b78193a3df9b84097fa543faca5061
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
    public void Execute(ExecutionResult result)
    {
        AssetDatabase.Refresh();
        result.Log("Refreshed");
    }
}
```

Then `mcp__unity-mcp__Unity_ReadConsole` Types=["Error"]. Expected: clean.

- [ ] **Step 6: Verify LobbyManager auto-bootstraps in play mode**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        var lm = Object.FindFirstObjectByType<LobbyManager>();
        result.Log("LobbyManager present: {0}, InLobby: {1}", lm != null, LobbyManager.InLobby);
    }
}
```

Expected (second run): `LobbyManager present: True, InLobby: False`. Exit play after.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Lobby/
git commit -m "feat(lobby): LobbyInfo + LobbyManager singleton skeleton"
```

---

## Task 2: Create Lobby

**Files:**
- Modify: `Assets/Scripts/Lobby/LobbyManager.cs`

- [ ] **Step 1: Add callback fields and OnEnable registration**

In LobbyManager.cs, add private fields near the existing ones:

```csharp
private Callback<LobbyCreated_t> _cbLobbyCreated;
private Callback<LobbyEnter_t> _cbLobbyEntered;
```

Add OnEnable method:

```csharp
private void OnEnable()
{
    _cbLobbyCreated = Callback<LobbyCreated_t>.Create(OnSteamLobbyCreated);
    _cbLobbyEntered = Callback<LobbyEnter_t>.Create(OnSteamLobbyEntered);
}
```

- [ ] **Step 2: Implement CreateLobby + the two callbacks**

Replace the `CreateLobby()` stub with:

```csharp
public static void CreateLobby()
{
    if (_instance == null) return;
    if (!SteamManager.Initialized) { RaiseError("Steam not running"); return; }
    SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 4);
}
```

Add the two private callback handlers in the same class:

```csharp
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
```

- [ ] **Step 3: Compile check via MCP**

Same as Task 1 Step 5. Expected: clean.

- [ ] **Step 4: Verify create-lobby fires events in play mode**

```csharp
using UnityEngine;
using UnityEditor;
using Steamworks;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        bool createdFired = false, enteredFired = false;
        System.Action<CSteamID> onC = _ => createdFired = true;
        System.Action<CSteamID> onE = _ => enteredFired = true;
        LobbyManager.OnLobbyCreated += onC;
        LobbyManager.OnLobbyEntered += onE;
        LobbyManager.CreateLobby();
        result.Log("CreateLobby called. Re-run this MCP cmd in ~2s to see if events fired.");
        // First call won't see results (Steam callback is async). Run twice.
        result.Log("createdFired={0} enteredFired={1} currentLobby={2}", createdFired, enteredFired, LobbyManager.CurrentLobby);
    }
}
```

Expected after second run (if Steam connected): `createdFired=True enteredFired=True currentLobby=<some nonzero ID>`. If Steam isn't running, you'll see an OnError instead.

If Steam is connected but you can't poll-and-wait in one call, an alternative is to register a one-shot listener that logs once invoked. Either is acceptable evidence the API path works.

Exit play after.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Lobby/LobbyManager.cs
git commit -m "feat(lobby): CreateLobby + LobbyCreated/Entered callbacks"
```

---

## Task 3: Browse Lobbies

**Files:**
- Modify: `Assets/Scripts/Lobby/LobbyManager.cs`

- [ ] **Step 1: Add LobbyMatchList callback field + register**

Add field:

```csharp
private CallResult<LobbyMatchList_t> _crLobbyList;
```

In OnEnable (after the existing two):

```csharp
_crLobbyList = CallResult<LobbyMatchList_t>.Create(OnSteamLobbyMatchList);
```

**Note:** `CallResult` (not `Callback`) — `RequestLobbyList` returns a `SteamAPICall_t` we have to bind to a CallResult instance.

- [ ] **Step 2: Implement RequestLobbyList + callback**

Replace the `RequestLobbyList()` stub:

```csharp
public static void RequestLobbyList()
{
    if (_instance == null) return;
    if (!SteamManager.Initialized) { RaiseError("Steam not running"); return; }
    SteamMatchmaking.AddRequestLobbyListStringFilter(
        LobbyDataGameVersion, GameVersion, ELobbyComparison.k_ELobbyComparisonEqual);
    var call = SteamMatchmaking.RequestLobbyList();
    _instance._crLobbyList.Set(call);
}
```

Add the callback handler:

```csharp
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
```

- [ ] **Step 3: Compile check via MCP**

Expected: clean.

- [ ] **Step 4: Verify list call returns**

```csharp
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        System.Action<List<LobbyInfo>> handler = l => {
            UnityEngine.Debug.Log("[Test] Received list with " + l.Count + " lobbies");
            foreach (var i in l) UnityEngine.Debug.Log("  - " + i.HostName + " " + i.CurrentMembers + "/" + i.MaxMembers);
        };
        LobbyManager.OnLobbyListReceived += handler;
        LobbyManager.RequestLobbyList();
        result.Log("Requested. Check console after ~2s for [Test] logs.");
    }
}
```

Check Unity Console after a few seconds (use `mcp__unity-mcp__Unity_ReadConsole` with FilterText="[Test]"). With your own active lobby running from Task 2's test, you should see at least 1 entry.

Exit play after.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Lobby/LobbyManager.cs
git commit -m "feat(lobby): RequestLobbyList + LobbyMatchList filtered by game_version"
```

---

## Task 4: Join Lobby

**Files:**
- Modify: `Assets/Scripts/Lobby/LobbyManager.cs`

- [ ] **Step 1: Implement JoinLobby (LobbyEnter callback already exists from Task 2)**

Replace the `JoinLobby(CSteamID)` stub:

```csharp
public static void JoinLobby(CSteamID id)
{
    if (_instance == null) return;
    if (!SteamManager.Initialized) { RaiseError("Steam not running"); return; }
    if (id == CSteamID.Nil) { RaiseError("Invalid lobby id"); return; }
    SteamMatchmaking.JoinLobby(id);
    // LobbyEnter_t fires asynchronously
}
```

The existing `OnSteamLobbyEntered` handles success/failure for both Create and Join paths.

- [ ] **Step 2: Compile check via MCP**

Expected: clean.

- [ ] **Step 3: Verify by joining own existing lobby**

```csharp
using UnityEngine;
using UnityEditor;
using Steamworks;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        if (!LobbyManager.InLobby) { result.LogError("Not in a lobby — run Task 2 verify first"); return; }
        // Leaving + rejoining own lobby tests the JoinLobby path
        var id = LobbyManager.CurrentLobby;
        result.Log("Current lobby: {0}. Calling JoinLobby on same id (no-op-ish).", id);
        LobbyManager.JoinLobby(id);
    }
}
```

Expected: no error in console. The OnLobbyEntered event fires again (or Steam returns kAlreadyInChat).

Exit play after.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Lobby/LobbyManager.cs
git commit -m "feat(lobby): JoinLobby wrapper (shares LobbyEnter callback)"
```

---

## Task 5: Leave Lobby + Member Updates

**Files:**
- Modify: `Assets/Scripts/Lobby/LobbyManager.cs`

- [ ] **Step 1: Add LobbyChatUpdate callback registration**

Add field:

```csharp
private Callback<LobbyChatUpdate_t> _cbLobbyChat;
```

In OnEnable:

```csharp
_cbLobbyChat = Callback<LobbyChatUpdate_t>.Create(OnSteamLobbyChat);
```

- [ ] **Step 2: Implement LeaveLobby + LobbyChatUpdate handler**

Replace the `LeaveLobby()` stub:

```csharp
public static void LeaveLobby()
{
    if (_instance == null) return;
    if (_instance._currentLobby == CSteamID.Nil) return;
    SteamMatchmaking.LeaveLobby(_instance._currentLobby);
    _instance._currentLobby = CSteamID.Nil;
    RaiseLobbyLeft();
}
```

Add the chat-update handler:

```csharp
private void OnSteamLobbyChat(LobbyChatUpdate_t e)
{
    if ((CSteamID)e.m_ulSteamIDLobby != _currentLobby) return;
    var change = (EChatMemberStateChange)e.m_rgfChatMemberStateChange;
    var changedUser = new CSteamID(e.m_ulSteamIDUserChanged);

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
```

- [ ] **Step 3: Add a helper to enumerate members (used by the LobbyPanel UI later)**

Add a public static helper to LobbyManager:

```csharp
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
```

- [ ] **Step 4: Compile check via MCP**

Expected: clean.

- [ ] **Step 5: Verify Leave fires OnLobbyLeft**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        bool left = false;
        System.Action handler = () => left = true;
        LobbyManager.OnLobbyLeft += handler;
        var before = LobbyManager.InLobby;
        LobbyManager.LeaveLobby();
        result.Log("InLobby before={0} after={1} leftEventFired={2}", before, LobbyManager.InLobby, left);
    }
}
```

Expected: `before=True after=False leftEventFired=True` (if you were in a lobby from prior tasks).

Exit play after.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Lobby/LobbyManager.cs
git commit -m "feat(lobby): LeaveLobby + LobbyChatUpdate (members + host-left detection)"
```

---

## Task 6: Friends Overlay Invite + Command-Line Parsing

**Files:**
- Modify: `Assets/Scripts/Lobby/LobbyManager.cs`

- [ ] **Step 1: Add GameLobbyJoinRequested callback registration**

Add field:

```csharp
private Callback<GameLobbyJoinRequested_t> _cbJoinRequested;
```

In OnEnable:

```csharp
_cbJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnSteamGameLobbyJoinRequested);
```

- [ ] **Step 2: Implement the handler**

```csharp
private void OnSteamGameLobbyJoinRequested(GameLobbyJoinRequested_t e)
{
    // Triggered when user clicks "Join Game" in Steam Friends overlay while app is running.
    JoinLobby(e.m_steamIDLobby);
}
```

- [ ] **Step 3: Parse `+connect_lobby` command-line argument on Awake**

Add `Awake` method to LobbyManager:

```csharp
private void Awake()
{
    // Steam launches the app with "+connect_lobby <id>" when joining from the friends list while the app isn't running.
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

private CSteamID _pendingJoinId = CSteamID.Nil;

private void Update()
{
    if (_pendingJoinId == CSteamID.Nil) return;
    if (!SteamManager.Initialized) return;
    var id = _pendingJoinId;
    _pendingJoinId = CSteamID.Nil;
    JoinLobby(id);
}
```

- [ ] **Step 4: Compile check via MCP**

Expected: clean.

- [ ] **Step 5: Smoke check — confirm no compile/runtime regression**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.Lobby;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        result.Log("LobbyManager InLobby: {0}", LobbyManager.InLobby);
    }
}
```

Friends-overlay flow can only be verified end-to-end with two Steam clients — defer to manual test.

Exit play after.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Lobby/LobbyManager.cs
git commit -m "feat(lobby): Steam Friends overlay invite + +connect_lobby CLI"
```

---

## Task 7: MainMenu scene scaffold

**Files:**
- Create: `Assets/Scenes/MainMenu.unity` (via MCP)
- Create: `Assets/Scenes/MainMenu.unity.meta` (auto-generated by Unity)
- Modify: `ProjectSettings/EditorBuildSettings.asset` (via MCP)

- [ ] **Step 1: Build MainMenu scene with Canvas + 3 empty panel GameObjects**

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UImage = UnityEngine.UI.Image;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera (UI-only orthographic)
        var camGo = new GameObject("UICamera", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f, 1f);
        cam.orthographic = true;

        // EventSystem
        var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

        // Canvas
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // 3 panels (each fills the canvas)
        foreach (var name in new[] { "MainPanel", "MultiplayerPanel", "LobbyPanel" })
        {
            var p = new GameObject(name, typeof(RectTransform));
            p.transform.SetParent(canvasGo.transform, false);
            var rt = p.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            // Only MainPanel active by default
            p.SetActive(name == "MainPanel");
        }

        bool ok = EditorSceneManager.SaveScene(scene, "Assets/Scenes/MainMenu.unity");
        result.Log("Scene saved: {0}", ok);
    }
}
```

- [ ] **Step 2: Update EditorBuildSettings — MainMenu = index 0, SampleScene = index 1**

```csharp
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", true),
        };
        EditorBuildSettings.scenes = scenes;
        result.Log("Build scenes set: {0}", scenes.Length);
        foreach (var s in EditorBuildSettings.scenes) result.Log("  {0} enabled={1}", s.path, s.enabled);
    }
}
```

Expected log: 2 scenes listed.

- [ ] **Step 3: Compile check via MCP** (no script changes here but refresh imports the new scene)

Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/MainMenu.unity Assets/Scenes/MainMenu.unity.meta ProjectSettings/EditorBuildSettings.asset
git commit -m "feat(menu): MainMenu scene scaffold (Canvas + 3 empty panels) + Build Settings"
```

---

## Task 8: MainPanel + MenuRoot (Play Solo / Multiplayer / Quit)

**Files:**
- Create: `Assets/Scripts/Lobby/MenuRoot.cs`
- Create: `Assets/Scripts/Lobby/MenuRoot.cs.meta`
- Create: `Assets/Scripts/Lobby/MainPanel.cs`
- Create: `Assets/Scripts/Lobby/MainPanel.cs.meta`
- Modify: `Assets/Scenes/MainMenu.unity` (add buttons + wire scripts via MCP)

- [ ] **Step 1: Create MenuRoot.cs**

```csharp
using UnityEngine;

namespace RTSCL.Lobby
{
    /// <summary>Owns the three main-menu panels and switches between them.</summary>
    public sealed class MenuRoot : MonoBehaviour
    {
        [SerializeField] private GameObject _mainPanel;
        [SerializeField] private GameObject _multiplayerPanel;
        [SerializeField] private GameObject _lobbyPanel;

        public void ShowMain()         => Show(_mainPanel);
        public void ShowMultiplayer()  => Show(_multiplayerPanel);
        public void ShowLobby()        => Show(_lobbyPanel);

        private void Show(GameObject panel)
        {
            if (_mainPanel != null)         _mainPanel.SetActive(panel == _mainPanel);
            if (_multiplayerPanel != null)  _multiplayerPanel.SetActive(panel == _multiplayerPanel);
            if (_lobbyPanel != null)        _lobbyPanel.SetActive(panel == _lobbyPanel);
        }
    }
}
```

- [ ] **Step 2: Create MenuRoot.cs.meta**

```yaml
fileFormatVersion: 2
guid: f3c89204b4ef0c95108fb654fbdb6172
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

- [ ] **Step 3: Create MainPanel.cs**

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RTSCL.Lobby
{
    public sealed class MainPanel : MonoBehaviour
    {
        [SerializeField] private MenuRoot _root;
        [SerializeField] private Button _playSoloButton;
        [SerializeField] private Button _multiplayerButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private Text _multiplayerStatusLabel;

        private void OnEnable()
        {
            if (_playSoloButton != null)    _playSoloButton.onClick.AddListener(OnPlaySolo);
            if (_multiplayerButton != null) _multiplayerButton.onClick.AddListener(OnMultiplayer);
            if (_quitButton != null)        _quitButton.onClick.AddListener(OnQuit);

            bool steamOk = SteamManager.Initialized;
            if (_multiplayerButton != null) _multiplayerButton.interactable = steamOk;
            if (_multiplayerStatusLabel != null)
                _multiplayerStatusLabel.text = steamOk ? string.Empty : "Steam not running";
        }

        private void OnDisable()
        {
            if (_playSoloButton != null)    _playSoloButton.onClick.RemoveListener(OnPlaySolo);
            if (_multiplayerButton != null) _multiplayerButton.onClick.RemoveListener(OnMultiplayer);
            if (_quitButton != null)        _quitButton.onClick.RemoveListener(OnQuit);
        }

        private void OnPlaySolo()    => SceneManager.LoadScene("SampleScene");
        private void OnMultiplayer() => _root?.ShowMultiplayer();
        private void OnQuit()
        {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
        }
    }
}
```

- [ ] **Step 4: Create MainPanel.cs.meta**

```yaml
fileFormatVersion: 2
guid: 04d9a315c5f01da6219fc765fcec7283
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

Expected: clean.

- [ ] **Step 6: Populate MainPanel UI + wire MenuRoot via MCP**

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using RTSCL.Lobby;
using UImage = UnityEngine.UI.Image;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");

        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/dogica/TTF/dogicapixel.ttf");
        var uiSprite = (Sprite)EditorGUIUtility.Load("UI/Skin/UISprite.psd");

        GameObject canvasGo = null;
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go.name == "Canvas" && go.scene == scene) { canvasGo = go; break; }
        var mainPanel = canvasGo.transform.Find("MainPanel").gameObject;
        var multiPanel = canvasGo.transform.Find("MultiplayerPanel").gameObject;
        var lobbyPanel = canvasGo.transform.Find("LobbyPanel").gameObject;

        // MenuRoot on the Canvas
        var menuRoot = canvasGo.GetComponent<MenuRoot>() ?? canvasGo.AddComponent<MenuRoot>();
        var soRoot = new SerializedObject(menuRoot);
        soRoot.FindProperty("_mainPanel").objectReferenceValue = mainPanel;
        soRoot.FindProperty("_multiplayerPanel").objectReferenceValue = multiPanel;
        soRoot.FindProperty("_lobbyPanel").objectReferenceValue = lobbyPanel;
        soRoot.ApplyModifiedPropertiesWithoutUndo();

        // Title
        var title = new GameObject("Title", typeof(RectTransform));
        title.transform.SetParent(mainPanel.transform, false);
        var titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0.5f, 1f);
        titleRT.anchorMax = new Vector2(0.5f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0, -120);
        titleRT.sizeDelta = new Vector2(800, 120);
        var titleText = title.AddComponent<Text>();
        titleText.text = "GOBLINO";
        titleText.font = font;
        titleText.fontSize = 64;
        titleText.color = Color.white;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.horizontalOverflow = HorizontalWrapMode.Overflow;

        // Vertical button column container
        var col = new GameObject("Buttons", typeof(RectTransform));
        col.transform.SetParent(mainPanel.transform, false);
        var colRT = col.GetComponent<RectTransform>();
        colRT.anchorMin = new Vector2(0.5f, 0.5f);
        colRT.anchorMax = new Vector2(0.5f, 0.5f);
        colRT.pivot = new Vector2(0.5f, 0.5f);
        colRT.sizeDelta = new Vector2(360, 240);
        var vlg = col.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;

        Button MakeButton(string label, Color tint)
        {
            var go = new GameObject(label.Replace(" ", "") + "Button", typeof(RectTransform));
            go.transform.SetParent(col.transform, false);
            var img = go.AddComponent<UImage>();
            img.sprite = uiSprite; img.type = UImage.Type.Sliced; img.color = tint;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 56;
            // Label
            var t = new GameObject("Label", typeof(RectTransform));
            t.transform.SetParent(go.transform, false);
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = t.AddComponent<Text>();
            txt.text = label; txt.font = font; txt.fontSize = 20; txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter; txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            return btn;
        }

        var bSolo = MakeButton("Play Solo", new Color(0.25f, 0.45f, 0.7f, 1f));
        var bMP   = MakeButton("Multiplayer", new Color(0.25f, 0.45f, 0.7f, 1f));
        var bQuit = MakeButton("Quit", new Color(0.35f, 0.35f, 0.4f, 1f));

        // Status label for "Steam not running"
        var statusGo = new GameObject("MPStatus", typeof(RectTransform));
        statusGo.transform.SetParent(mainPanel.transform, false);
        var srt = statusGo.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.5f, 0.5f); srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.pivot = new Vector2(0.5f, 1f);
        srt.anchoredPosition = new Vector2(0, -140);
        srt.sizeDelta = new Vector2(400, 24);
        var statusTxt = statusGo.AddComponent<Text>();
        statusTxt.text = ""; statusTxt.font = font; statusTxt.fontSize = 14;
        statusTxt.color = new Color(0.9f, 0.5f, 0.4f, 1f);
        statusTxt.alignment = TextAnchor.MiddleCenter;

        // MainPanel script + wiring
        var main = mainPanel.GetComponent<MainPanel>() ?? mainPanel.AddComponent<MainPanel>();
        var soMain = new SerializedObject(main);
        soMain.FindProperty("_root").objectReferenceValue = menuRoot;
        soMain.FindProperty("_playSoloButton").objectReferenceValue = bSolo;
        soMain.FindProperty("_multiplayerButton").objectReferenceValue = bMP;
        soMain.FindProperty("_quitButton").objectReferenceValue = bQuit;
        soMain.FindProperty("_multiplayerStatusLabel").objectReferenceValue = statusTxt;
        soMain.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("MainPanel populated and wired");
    }
}
```

- [ ] **Step 7: Play-mode verify — open MainMenu scene, check buttons appear**

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        result.Log("Active scene: {0} (buildIndex {1})", scene.name, scene.buildIndex);
    }
}
```

Expected: `Active scene: MainMenu (buildIndex 0)`. Visually verify the 3 buttons appear stacked in the centre + GOBLINO title at top. Clicking Play Solo should load SampleScene (visually verify).

Exit play after.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Lobby/MenuRoot.cs Assets/Scripts/Lobby/MenuRoot.cs.meta Assets/Scripts/Lobby/MainPanel.cs Assets/Scripts/Lobby/MainPanel.cs.meta Assets/Scenes/MainMenu.unity
git commit -m "feat(menu): MainPanel with Play Solo / Multiplayer / Quit + MenuRoot panel switcher"
```

---

## Task 9: MultiplayerPanel (Create / Browse / Refresh / Back)

**Files:**
- Create: `Assets/Scripts/Lobby/MultiplayerPanel.cs`
- Create: `Assets/Scripts/Lobby/MultiplayerPanel.cs.meta`
- Modify: `Assets/Scenes/MainMenu.unity` (populate MultiplayerPanel via MCP)

- [ ] **Step 1: Create MultiplayerPanel.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Steamworks;

namespace RTSCL.Lobby
{
    public sealed class MultiplayerPanel : MonoBehaviour
    {
        [SerializeField] private MenuRoot _root;
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _createButton;
        [SerializeField] private Button _refreshButton;
        [SerializeField] private RectTransform _listContent;
        [SerializeField] private Text _emptyLabel;
        [SerializeField] private Text _errorLabel;
        [SerializeField] private Font _rowFont;
        [SerializeField] private Sprite _rowSprite;

        private void OnEnable()
        {
            if (_backButton != null)    _backButton.onClick.AddListener(OnBack);
            if (_createButton != null)  _createButton.onClick.AddListener(OnCreate);
            if (_refreshButton != null) _refreshButton.onClick.AddListener(OnRefresh);
            LobbyManager.OnLobbyListReceived += OnListReceived;
            LobbyManager.OnError += OnError;
            ClearError();
            OnRefresh();
        }

        private void OnDisable()
        {
            if (_backButton != null)    _backButton.onClick.RemoveListener(OnBack);
            if (_createButton != null)  _createButton.onClick.RemoveListener(OnCreate);
            if (_refreshButton != null) _refreshButton.onClick.RemoveListener(OnRefresh);
            LobbyManager.OnLobbyListReceived -= OnListReceived;
            LobbyManager.OnError -= OnError;
        }

        private void OnBack()    => _root?.ShowMain();
        private void OnCreate()  { ClearError(); LobbyManager.CreateLobby(); }
        private void OnRefresh() { ClearError(); LobbyManager.RequestLobbyList(); }

        private void OnError(string msg)
        {
            if (_errorLabel == null) return;
            _errorLabel.text = msg;
            _errorLabel.gameObject.SetActive(true);
        }

        private void ClearError()
        {
            if (_errorLabel == null) return;
            _errorLabel.text = string.Empty;
            _errorLabel.gameObject.SetActive(false);
        }

        private void OnListReceived(List<LobbyInfo> list)
        {
            if (_listContent == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            if (_emptyLabel != null) _emptyLabel.gameObject.SetActive(list.Count == 0);

            foreach (var info in list) BuildRow(info);
        }

        private void BuildRow(LobbyInfo info)
        {
            var row = new GameObject($"Lobby_{info.Id.m_SteamID}", typeof(RectTransform));
            row.transform.SetParent(_listContent, false);
            var bg = row.AddComponent<Image>();
            bg.sprite = _rowSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.18f, 0.18f, 0.22f, 0.95f);
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(12, 12, 8, 8);
            hl.spacing = 10;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childAlignment = TextAnchor.MiddleLeft;
            var rle = row.AddComponent<LayoutElement>();
            rle.preferredHeight = 48;
            rle.flexibleWidth = 1;

            var nameGo = new GameObject("Name", typeof(RectTransform));
            nameGo.transform.SetParent(row.transform, false);
            var nameText = nameGo.AddComponent<Text>();
            nameText.text = $"{info.HostName} — {info.CurrentMembers}/{info.MaxMembers}";
            nameText.font = _rowFont;
            nameText.fontSize = 16;
            nameText.color = Color.white;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var nameLE = nameGo.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1;

            var joinGo = new GameObject("Join", typeof(RectTransform));
            joinGo.transform.SetParent(row.transform, false);
            var joinImg = joinGo.AddComponent<Image>();
            joinImg.sprite = _rowSprite;
            joinImg.type = Image.Type.Sliced;
            joinImg.color = new Color(0.25f, 0.55f, 0.3f, 1f);
            var joinBtn = joinGo.AddComponent<Button>();
            joinBtn.targetGraphic = joinImg;
            var joinLE = joinGo.AddComponent<LayoutElement>();
            joinLE.preferredWidth = 96;
            joinLE.preferredHeight = 32;
            var lbl = new GameObject("Label", typeof(RectTransform));
            lbl.transform.SetParent(joinGo.transform, false);
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
            var lblTxt = lbl.AddComponent<Text>();
            lblTxt.text = "Join"; lblTxt.font = _rowFont; lblTxt.fontSize = 14;
            lblTxt.color = Color.white;
            lblTxt.alignment = TextAnchor.MiddleCenter;

            var capturedId = info.Id;
            joinBtn.onClick.AddListener(() => LobbyManager.JoinLobby(capturedId));
        }
    }
}
```

- [ ] **Step 2: Create MultiplayerPanel.cs.meta**

```yaml
fileFormatVersion: 2
guid: 15eab426d6f12eb732afd876fdfd8394
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

- [ ] **Step 4: Populate MultiplayerPanel UI + wire script via MCP**

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using RTSCL.Lobby;
using UImage = UnityEngine.UI.Image;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/dogica/TTF/dogicapixel.ttf");
        var uiSprite = (Sprite)EditorGUIUtility.Load("UI/Skin/UISprite.psd");

        GameObject mp = null, canvasGo = null, menuRootGo = null;
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.scene != scene) continue;
            if (go.name == "MultiplayerPanel") mp = go;
            if (go.name == "Canvas") canvasGo = go;
        }
        menuRootGo = canvasGo;

        // Header bar (Back + title)
        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(mp.transform, false);
        var hrt = header.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.anchoredPosition = new Vector2(0, -20);
        hrt.sizeDelta = new Vector2(-40, 60);

        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 10; headerLayout.padding = new RectOffset(10, 10, 5, 5);
        headerLayout.childControlWidth = false; headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false; headerLayout.childForceExpandHeight = true;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;

        Button MakeButton(Transform parent, string text, Vector2 size, Color tint)
        {
            var go = new GameObject(text.Replace(" ", "") + "Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<UImage>();
            img.sprite = uiSprite; img.type = UImage.Type.Sliced; img.color = tint;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size.x; le.preferredHeight = size.y;
            var t = new GameObject("Label", typeof(RectTransform));
            t.transform.SetParent(go.transform, false);
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = t.AddComponent<Text>();
            txt.text = text; txt.font = font; txt.fontSize = 16; txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            return btn;
        }

        var bBack = MakeButton(header.transform, "Back", new Vector2(80, 40), new Color(0.35f, 0.35f, 0.4f, 1f));
        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(header.transform, false);
        var titleTxt = titleGo.AddComponent<Text>();
        titleTxt.text = "Multiplayer"; titleTxt.font = font; titleTxt.fontSize = 22;
        titleTxt.color = Color.white; titleTxt.alignment = TextAnchor.MiddleLeft;
        titleTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
        var titleLE = titleGo.AddComponent<LayoutElement>();
        titleLE.preferredWidth = 300; titleLE.preferredHeight = 40;

        // Action row (Create + Refresh)
        var actions = new GameObject("Actions", typeof(RectTransform));
        actions.transform.SetParent(mp.transform, false);
        var art = actions.GetComponent<RectTransform>();
        art.anchorMin = new Vector2(0, 1); art.anchorMax = new Vector2(1, 1);
        art.pivot = new Vector2(0.5f, 1f);
        art.anchoredPosition = new Vector2(0, -90);
        art.sizeDelta = new Vector2(-40, 50);
        var actionsLayout = actions.AddComponent<HorizontalLayoutGroup>();
        actionsLayout.spacing = 10;
        actionsLayout.childControlHeight = true; actionsLayout.childControlWidth = false;
        actionsLayout.childAlignment = TextAnchor.MiddleLeft;
        var bCreate = MakeButton(actions.transform, "Create Lobby", new Vector2(180, 44), new Color(0.25f, 0.55f, 0.3f, 1f));
        var bRefresh = MakeButton(actions.transform, "Refresh", new Vector2(120, 44), new Color(0.25f, 0.45f, 0.7f, 1f));

        // Scroll list
        var scroll = new GameObject("LobbyList", typeof(RectTransform));
        scroll.transform.SetParent(mp.transform, false);
        var srt = scroll.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
        srt.offsetMin = new Vector2(20, 80);
        srt.offsetMax = new Vector2(-20, -160);
        var sBg = scroll.AddComponent<UImage>();
        sBg.sprite = uiSprite; sBg.type = UImage.Type.Sliced;
        sBg.color = new Color(0.05f, 0.05f, 0.07f, 0.9f);
        var sr = scroll.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Mask), typeof(UImage));
        viewport.transform.SetParent(scroll.transform, false);
        var vmask = viewport.GetComponent<Mask>(); vmask.showMaskGraphic = false;
        var vimg = viewport.GetComponent<UImage>(); vimg.color = new Color(1, 1, 1, 0.01f);
        var vrt = viewport.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
        sr.viewport = vrt;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
        crt.sizeDelta = new Vector2(0, 0);
        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 6; contentLayout.padding = new RectOffset(6, 6, 6, 6);
        contentLayout.childForceExpandHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childControlWidth = true;
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = crt;

        // Empty + error labels
        var empty = new GameObject("Empty", typeof(RectTransform));
        empty.transform.SetParent(scroll.transform, false);
        var ert = empty.GetComponent<RectTransform>();
        ert.anchorMin = new Vector2(0.5f, 0.5f); ert.anchorMax = new Vector2(0.5f, 0.5f);
        ert.sizeDelta = new Vector2(500, 40);
        var emptyTxt = empty.AddComponent<Text>();
        emptyTxt.text = "No lobbies found — refresh or create one";
        emptyTxt.font = font; emptyTxt.fontSize = 16;
        emptyTxt.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        emptyTxt.alignment = TextAnchor.MiddleCenter;

        var error = new GameObject("Error", typeof(RectTransform));
        error.transform.SetParent(mp.transform, false);
        var errrt = error.GetComponent<RectTransform>();
        errrt.anchorMin = new Vector2(0, 0); errrt.anchorMax = new Vector2(1, 0);
        errrt.pivot = new Vector2(0.5f, 0f);
        errrt.anchoredPosition = new Vector2(0, 20);
        errrt.sizeDelta = new Vector2(-40, 30);
        var errTxt = error.AddComponent<Text>();
        errTxt.text = ""; errTxt.font = font; errTxt.fontSize = 14;
        errTxt.color = new Color(0.95f, 0.5f, 0.4f, 1f);
        errTxt.alignment = TextAnchor.MiddleCenter;
        error.SetActive(false);

        // Attach script + wire
        var mpScript = mp.GetComponent<MultiplayerPanel>() ?? mp.AddComponent<MultiplayerPanel>();
        var menuRoot = canvasGo.GetComponent<MenuRoot>();
        var so = new SerializedObject(mpScript);
        so.FindProperty("_root").objectReferenceValue = menuRoot;
        so.FindProperty("_backButton").objectReferenceValue = bBack;
        so.FindProperty("_createButton").objectReferenceValue = bCreate;
        so.FindProperty("_refreshButton").objectReferenceValue = bRefresh;
        so.FindProperty("_listContent").objectReferenceValue = crt;
        so.FindProperty("_emptyLabel").objectReferenceValue = emptyTxt;
        so.FindProperty("_errorLabel").objectReferenceValue = errTxt;
        so.FindProperty("_rowFont").objectReferenceValue = font;
        so.FindProperty("_rowSprite").objectReferenceValue = uiSprite;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("MultiplayerPanel populated and wired");
    }
}
```

- [ ] **Step 5: Play-mode visual verify**

Enter play mode → click Multiplayer button on MainPanel → MultiplayerPanel appears with Back + Create + Refresh buttons + (empty) list. Click Create → on success the LobbyPanel switch will be wired in Task 11; for now you'll just see no UI change. Click Back returns to Main.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Lobby/MultiplayerPanel.cs Assets/Scripts/Lobby/MultiplayerPanel.cs.meta Assets/Scenes/MainMenu.unity
git commit -m "feat(menu): MultiplayerPanel — create/browse/join via Steam matchmaking"
```

---

## Task 10: LobbyPanel (Header + member list + Leave + Start disabled)

**Files:**
- Create: `Assets/Scripts/Lobby/LobbyPanel.cs`
- Create: `Assets/Scripts/Lobby/LobbyPanel.cs.meta`
- Modify: `Assets/Scenes/MainMenu.unity` (populate LobbyPanel via MCP)

- [ ] **Step 1: Create LobbyPanel.cs**

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
            // Start is intentionally disabled in this sub-project.
            if (_startButton != null) _startButton.interactable = false;
            LobbyManager.OnLobbyMembersChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (_leaveButton != null) _leaveButton.onClick.RemoveListener(OnLeave);
            LobbyManager.OnLobbyMembersChanged -= Refresh;
        }

        private void OnLeave() => LobbyManager.LeaveLobby();

        private void Refresh()
        {
            if (!LobbyManager.InLobby) return;

            // Header: "{HostName}'s Lobby"
            string host = SteamMatchmaking.GetLobbyData(LobbyManager.CurrentLobby, LobbyManager.LobbyDataHostName);
            if (string.IsNullOrEmpty(host)) host = "Unknown";
            if (_headerLabel != null) _headerLabel.text = $"{host}'s Lobby";

            // Member list
            if (_memberListContent == null) return;
            for (int i = _memberListContent.childCount - 1; i >= 0; i--)
                Destroy(_memberListContent.GetChild(i).gameObject);

            foreach (var (id, name, isHost) in LobbyManager.GetCurrentMembers())
                BuildRow(name, isHost);
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

- [ ] **Step 2: Create LobbyPanel.cs.meta**

```yaml
fileFormatVersion: 2
guid: 26fbc537e702bfc843bfe987feee94a5
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

- [ ] **Step 4: Populate LobbyPanel UI + wire script via MCP**

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using RTSCL.Lobby;
using UImage = UnityEngine.UI.Image;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/dogica/TTF/dogicapixel.ttf");
        var uiSprite = (Sprite)EditorGUIUtility.Load("UI/Skin/UISprite.psd");

        GameObject lobby = null, canvasGo = null;
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.scene != scene) continue;
            if (go.name == "LobbyPanel") lobby = go;
            if (go.name == "Canvas") canvasGo = go;
        }

        Button MakeButton(Transform parent, string text, Vector2 size, Color tint)
        {
            var go = new GameObject(text.Replace(" ", "") + "Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<UImage>();
            img.sprite = uiSprite; img.type = UImage.Type.Sliced; img.color = tint;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size.x; le.preferredHeight = size.y;
            var t = new GameObject("Label", typeof(RectTransform));
            t.transform.SetParent(go.transform, false);
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = t.AddComponent<Text>();
            txt.text = text; txt.font = font; txt.fontSize = 16; txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            return btn;
        }

        // Header
        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(lobby.transform, false);
        var hrt = header.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.anchoredPosition = new Vector2(0, -30);
        hrt.sizeDelta = new Vector2(-40, 60);
        var headerTxt = header.AddComponent<Text>();
        headerTxt.text = "Lobby"; headerTxt.font = font; headerTxt.fontSize = 28;
        headerTxt.color = Color.white;
        headerTxt.alignment = TextAnchor.MiddleCenter;
        headerTxt.horizontalOverflow = HorizontalWrapMode.Overflow;

        // Member list panel
        var listGo = new GameObject("MemberList", typeof(RectTransform));
        listGo.transform.SetParent(lobby.transform, false);
        var lrt = listGo.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 1);
        lrt.offsetMin = new Vector2(20, 100);
        lrt.offsetMax = new Vector2(-20, -110);
        var lbg = listGo.AddComponent<UImage>();
        lbg.sprite = uiSprite; lbg.type = UImage.Type.Sliced;
        lbg.color = new Color(0.05f, 0.05f, 0.07f, 0.9f);

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(listGo.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
        crt.offsetMin = new Vector2(8, 8); crt.offsetMax = new Vector2(-8, -8);
        var cLayout = content.AddComponent<VerticalLayoutGroup>();
        cLayout.spacing = 6; cLayout.padding = new RectOffset(4, 4, 4, 4);
        cLayout.childForceExpandWidth = true; cLayout.childForceExpandHeight = false;
        cLayout.childControlWidth = true; cLayout.childControlHeight = true;
        cLayout.childAlignment = TextAnchor.UpperLeft;

        // Bottom buttons (Leave + Start)
        var bottom = new GameObject("Bottom", typeof(RectTransform));
        bottom.transform.SetParent(lobby.transform, false);
        var brt = bottom.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 0);
        brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0, 30);
        brt.sizeDelta = new Vector2(-40, 50);
        var bLayout = bottom.AddComponent<HorizontalLayoutGroup>();
        bLayout.spacing = 12;
        bLayout.childControlWidth = false; bLayout.childControlHeight = true;
        bLayout.childAlignment = TextAnchor.MiddleCenter;
        var bLeave = MakeButton(bottom.transform, "Leave Lobby", new Vector2(180, 44), new Color(0.6f, 0.3f, 0.3f, 1f));
        var bStart = MakeButton(bottom.transform, "Networking coming soon", new Vector2(280, 44), new Color(0.3f, 0.3f, 0.35f, 1f));
        bStart.interactable = false;

        // Script + wire
        var lp = lobby.GetComponent<LobbyPanel>() ?? lobby.AddComponent<LobbyPanel>();
        var menuRoot = canvasGo.GetComponent<MenuRoot>();
        var so = new SerializedObject(lp);
        so.FindProperty("_root").objectReferenceValue = menuRoot;
        so.FindProperty("_headerLabel").objectReferenceValue = headerTxt;
        so.FindProperty("_memberListContent").objectReferenceValue = crt;
        so.FindProperty("_leaveButton").objectReferenceValue = bLeave;
        so.FindProperty("_startButton").objectReferenceValue = bStart;
        so.FindProperty("_rowFont").objectReferenceValue = font;
        so.FindProperty("_rowSprite").objectReferenceValue = uiSprite;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("LobbyPanel populated and wired");
    }
}
```

- [ ] **Step 5: Play-mode visual verify**

Enter play → no easy way to see LobbyPanel until navigation glue lands in Task 11. Confirm by force-activating in editor: in the scene window, select LobbyPanel → set Active → enter Play → you should see header, empty member list, Leave + greyed Start button.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Lobby/LobbyPanel.cs Assets/Scripts/Lobby/LobbyPanel.cs.meta Assets/Scenes/MainMenu.unity
git commit -m "feat(menu): LobbyPanel — header + member list + Leave + Start (disabled)"
```

---

## Task 11: Glue — LobbyManager events → MenuRoot navigation

**Files:**
- Modify: `Assets/Scripts/Lobby/MenuRoot.cs` (subscribe to LobbyManager events)

- [ ] **Step 1: Extend MenuRoot to listen to LobbyManager events**

Replace the contents of MenuRoot.cs:

```csharp
using UnityEngine;

namespace RTSCL.Lobby
{
    /// <summary>Owns the three main-menu panels and switches between them in response
    /// to button clicks and LobbyManager events.</summary>
    public sealed class MenuRoot : MonoBehaviour
    {
        [SerializeField] private GameObject _mainPanel;
        [SerializeField] private GameObject _multiplayerPanel;
        [SerializeField] private GameObject _lobbyPanel;

        public void ShowMain()         => Show(_mainPanel);
        public void ShowMultiplayer()  => Show(_multiplayerPanel);
        public void ShowLobby()        => Show(_lobbyPanel);

        private void OnEnable()
        {
            LobbyManager.OnLobbyEntered += OnLobbyEntered;
            LobbyManager.OnLobbyLeft    += OnLobbyLeft;
        }

        private void OnDisable()
        {
            LobbyManager.OnLobbyEntered -= OnLobbyEntered;
            LobbyManager.OnLobbyLeft    -= OnLobbyLeft;
        }

        private void OnLobbyEntered(Steamworks.CSteamID _) => ShowLobby();
        private void OnLobbyLeft() => ShowMain();

        private void Show(GameObject panel)
        {
            if (_mainPanel != null)         _mainPanel.SetActive(panel == _mainPanel);
            if (_multiplayerPanel != null)  _multiplayerPanel.SetActive(panel == _multiplayerPanel);
            if (_lobbyPanel != null)        _lobbyPanel.SetActive(panel == _lobbyPanel);
        }
    }
}
```

- [ ] **Step 2: Compile check via MCP**

Expected: clean.

- [ ] **Step 3: End-to-end smoke test (requires Steam)**

Enter Play mode. From MainPanel:
1. Click Multiplayer → MultiplayerPanel appears, list refreshes (probably empty)
2. Click Create Lobby → after Steam round-trip (~1s), UI switches to LobbyPanel showing your name + (host)
3. Click Leave Lobby → returns to MainPanel
4. Verify the Steam "Multiplayer" button is greyed out if you launch without Steam running

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Lobby/MenuRoot.cs
git commit -m "feat(menu): wire LobbyManager events to MenuRoot panel navigation"
```

---

## Self-Review Checklist

- [x] **Spec coverage:**
  - Scenes + Build Settings → Task 7
  - LobbyManager (skeleton, create, browse, join, leave, members, friends-invite, CLI) → Tasks 1-6
  - 3 Panels (Main/Multiplayer/Lobby) → Tasks 8, 9, 10
  - Navigation glue + Steam-not-running guard → Tasks 8 (guard), 11 (events)
  - Edge cases (Steam offline, empty list, join failure, host left) → covered by event paths in tasks 5 + 11 and error labels in tasks 9
- [x] **No placeholders** — every code block is complete
- [x] **Type consistency** — `LobbyInfo`, `MenuRoot`, `LobbyManager.GetCurrentMembers()`, `LobbyDataHostName`, `OnLobbyEntered`, `OnLobbyLeft`, `OnLobbyListReceived` used uniformly across tasks
- [x] **Each task is a single focused commit**
- [x] **Manual verification steps are concrete + runnable via MCP where Steam-independent; manual-with-Steam clearly flagged**

## Open verification gaps (acknowledged)

- Full multi-client lobby flow (host on one Steam account, join from another) needs two real Steam clients. Per-task verification establishes single-client correctness; the cross-client test happens after Task 11 or during sub-project #3.
- Friends-overlay invite cannot be triggered headlessly — verified by smoke (compile + handler registration) in Task 6; end-to-end deferred to manual.

# Main Menu + Steam Lobby

Date: 2026-05-24
Status: Approved for implementation

## Purpose

Replace the direct-to-SampleScene boot with a proper Main Menu scene. Add a Steam-backed public-lobby browser so players can host or join multiplayer lobbies (up to 4 players). Singleplayer remains available and bypasses Steam entirely. Game-state networking is out of scope — the lobby's "Start Game" button is disabled in this sub-project and will be wired up in the next one.

## Scope

In scope:
- New scene `Assets/Scenes/MainMenu.unity` (Build index 0)
- 3 UI panels in MainMenu: Main, Multiplayer, Lobby
- `LobbyManager` MonoBehaviour wrapping Steamworks.NET matchmaking
- Create / browse (public) / join / leave lobbies
- Lobby member list view with host indicator
- Steam Friends overlay "Join Game" auto-handling
- Graceful degradation when Steam is not running

Out of scope (later sub-projects):
- Game-state networking / world-seed sync (#3)
- Player ownership / per-player input filtering (#4)
- Command-sync over the network (#5)
- Multiplayer combat (#6)
- Ready-state toggles, lobby chat, player color/team selection
- ESC-back-to-menu from SampleScene

## Architecture

### Scenes

| Build Index | Scene | Purpose |
|---|---|---|
| 0 | `Assets/Scenes/MainMenu.unity` | New — main menu + lobby UI |
| 1 | `Assets/Scenes/SampleScene.unity` | Existing game scene (solo for now) |

App boot lands in MainMenu (Unity Build Settings update required). "Play Solo" loads SampleScene via `SceneManager.LoadScene(1)` — solo play continues to behave like today.

### Persistent Managers

- **SteamManager** (existing): auto-bootstraps Steam API via `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`. Unchanged.
- **LobbyManager** (new): auto-spawned the same way, marked `DontDestroyOnLoad`. Holds current `CSteamID` lobby reference, owns Steamworks `Callback<>` registrations, exposes events for the UI to subscribe to:
  - `OnLobbyCreated(CSteamID lobby)`
  - `OnLobbyEntered(CSteamID lobby)`
  - `OnLobbyListReceived(List<LobbyInfo>)`
  - `OnLobbyMembersChanged()`
  - `OnLobbyLeft()`
  - `OnError(string message)`
- The UI subscribes to events; LobbyManager has no UI dependencies.

### Data Types

- `LobbyInfo`: `CSteamID Id; string HostName; int CurrentMembers; int MaxMembers`. Used by the lobby-list UI.
- All other state held inside LobbyManager.

## Steam Lobby Wiring

### Create

- `SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, maxMembers: 4)`
- On `LobbyCreated_t` (success): set custom lobby data:
  - `game_version = "0.1"`
  - `host_name = SteamFriends.GetPersonaName()`
- `LobbyEnter_t` auto-fires for the creator → triggers `OnLobbyEntered`

### Browse

- Set filter: `SteamMatchmaking.AddRequestLobbyListStringFilter("game_version", "0.1", ELobbyComparison.k_ELobbyComparisonEqual)`
- `SteamMatchmaking.RequestLobbyList()` → callback `LobbyMatchList_t` returns count
- For each `i` in `[0, count)`: `SteamMatchmaking.GetLobbyByIndex(i)` → read `host_name` via `GetLobbyData` + member count via `GetNumLobbyMembers` + `GetLobbyMemberLimit`
- Emit `OnLobbyListReceived(List<LobbyInfo>)`

### Join

- `SteamMatchmaking.JoinLobby(targetId)`
- On `LobbyEnter_t`:
  - If `m_EChatRoomEnterResponse == k_EChatRoomEnterResponseSuccess` → emit `OnLobbyEntered`
  - Else → emit `OnError("Could not join: {reason}")`

### Leave

- `SteamMatchmaking.LeaveLobby(currentLobbyId)` → reset internal state → emit `OnLobbyLeft`

### Friends overlay invite

- Register `GameLobbyJoinRequested_t` callback → `LobbyManager` auto-calls `JoinLobby(target.m_steamIDLobby)` → UI navigates to LobbyPanel even if app was on MainPanel
- App launched via `+connect_lobby <lobbyId>` command line: parse on boot, schedule join after Steam init

### Member updates

- `LobbyChatUpdate_t` fires on join/leave/disconnect → emit `OnLobbyMembersChanged`
- If the update indicates the owner left and lobby is empty/disbanded → emit `OnError("Host left the lobby")` + auto-`LeaveLobby`

## UI Structure

One Canvas in MainMenu.unity, screen-space-overlay. Three sibling panels under it; only one active at a time.

### MainPanel (default on scene load)

- Title text "GOBLINO" — dogica pixel font, ~64px, top-center
- Vertical layout of 3 buttons (each ~48px tall):
  - `Play Solo` → `SceneManager.LoadScene(1)`
  - `Multiplayer` → switch to MultiplayerPanel (disabled when `SteamManager.Initialized == false`, tooltip "Steam not running")
  - `Quit` → `Application.Quit()` (and `EditorApplication.isPlaying = false` in editor)

### MultiplayerPanel

- Top row: "Multiplayer" header + Back button
- "Create Lobby" button → `LobbyManager.CreateLobby()`
- "Browse Lobbies" section:
  - Refresh button → `LobbyManager.RequestLobbyList()`
  - Scrollable list of dynamic rows: `{HostName} — {N}/4 players` + per-row "Join" button
  - Empty state: "No lobbies found — refresh or create one"

### LobbyPanel

- Header: `"{HostName}'s Lobby"`
- Vertical member list, one row per player: name + `(host)` marker for the lobby owner
- "Leave Lobby" button → `LobbyManager.LeaveLobby()`
- "Start Game" button — host-only visible, `interactable = false`, label "Networking coming soon"

### Styling

- Dogica pixel font everywhere (already in project at `Assets/Fonts/dogica/TTF/dogicapixel.ttf`)
- Unity built-in `UISprite` + dark-grey semi-transparent panel backgrounds (consistent with existing HUD/Inspector popup)
- White text on dark, blue accent on primary buttons

## Edge Cases

| Scenario | Handling |
|---|---|
| Steam not running on app start | `LobbyManager` no-ops all methods; UI disables Multiplayer button with tooltip |
| Lobby browser returns 0 results | List shows "No lobbies found — refresh or create one" |
| Join fails (lobby full / closed / not found) | Error banner ("Could not join lobby") in MultiplayerPanel, browser auto-refreshes |
| Host leaves while others present | All clients receive `LobbyChatUpdate_t` → toast "Host left" + auto-Leave → back to MainPanel |
| Steam disconnects mid-session | LobbyManager fires `OnLobbyLeft` → UI returns to MainPanel |
| App quit while in lobby | `SteamAPI.Shutdown()` cleans up lobby membership server-side |
| Invite via Steam Friends overlay (app running) | `GameLobbyJoinRequested_t` triggers auto-join + UI nav to LobbyPanel |
| Invite via Steam Friends overlay (app not running) | Steam launches app with `+connect_lobby` arg; LobbyManager parses on boot and joins after init |

## Open Questions / Future Work

- Ready-state toggle before host can start (deferred)
- Player color / team selection in lobby (deferred to #4)
- Lobby chat (deferred)
- ESC-back-to-menu from SampleScene (deferred)
- Lobby filters in browser (region, version, full/open) — currently just version filter

## Testing

Manual (requires Steam running):
1. Solo path: app boot → MainPanel → Play Solo → SampleScene loads, single-player game proceeds as before.
2. Create lobby: MainPanel → Multiplayer → Create Lobby → UI shows LobbyPanel with self as host.
3. Browse (second client): app boot → Multiplayer → Refresh → see lobby created in step 2 → Join → both clients in LobbyPanel.
4. Leave as guest: Leave Lobby → host's member list updates immediately.
5. Leave as host: guest receives "Host left" toast + returns to MainPanel.
6. Steam offline: launch without Steam running → Multiplayer button greyed.

Automated tests are not feasible for Steam-bound flows. Pure logic (e.g., `LobbyInfo` parsing) can be unit-tested if extracted.

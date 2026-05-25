# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

**Goblino** — a Unity 6 / URP 2D top-down RTS with Steam multiplayer. Repo: `github.com/FelixEllspermann/goblino`. Direct-to-main workflow.

Current playable loop (single-player):
- Main menu → Play Solo → SampleScene with random world
- Auto-generated map (biomes, trees, resources, 2 spawn points)
- Keep at spawn[0] with 5 Farmer Goblins (+ 2 test Club Goblins for combat testing)
- Build Hut (300 wood, +5 pop cap) or Barracks (500 wood) — Farmer selects, right-click building card, click to place, Farmer constructs it
- Train Farmer at Keep or Club at Barracks (wood + pop cost, progress bar)
- Farmers harvest trees + build; Clubs fight (auto-retaliate, hop-arc death + red particle burst, ~5.5× higher hop than original design after balancing)
- Population cap = 20 + 5/Hut. Farmer=1 cost, Club=3.

Multiplayer state:
- Main menu offers `Multiplayer` → public-lobby browser via Steam matchmaking
- Lobby up to 4 players. Host clicks Start → world seed broadcast over Steam P2P → all clients load SampleScene with identical map.
- Game-state sync (units, commands, combat) is **not yet** networked — that's the next sub-project.

## Engine & rendering

- **Unity 6000.4.3f1**. Pinned in `ProjectSettings/ProjectVersion.txt`.
- **Universal Render Pipeline 2D** (`com.unity.render-pipelines.universal` 17.4.0). Renderer/quality at `Assets/Settings/Renderer2D.asset` and `Assets/Settings/UniversalRP.asset`.
- **New Input System** (`com.unity.inputsystem` 1.19.0) — `UnityEngine.Input` is disabled. Bindings in `Assets/InputSystem_Actions.inputactions`. UI scenes use `InputSystemUIInputModule` (NOT the legacy `StandaloneInputModule`).
- **Scenes** (Build Settings):
  - Index 0: `Assets/Scenes/MainMenu.unity` (boot scene — title + Play Solo + Multiplayer + Quit)
  - Index 1: `Assets/Scenes/SampleScene.unity` (the game)

## Assembly structure

| asmdef | Path | Role |
|---|---|---|
| `RTSCL.World` | `Assets/Scripts/World/` | Pure-logic world generation (noise, biome classifier, spawn planner, resource planner, reachability). No Unity deps beyond `Unity.Mathematics`. Unit-testable. |
| `RTSCL.World.Unity` | `Assets/Scripts/World/Unity/` | Unity-bound gameplay (MonoBehaviours, tilemap painting, building/spawning/combat). `autoReferenced: true`. |
| `RTSCL.World.Tests` | `Assets/Tests/Editor/` | Editor-mode NUnit tests, references only `RTSCL.World`. |
| Assembly-CSharp | `Assets/Scripts/`, `Assets/Scripts/Lobby/` | `SteamManager`, lobby + networking, scene loaders. Can reference all asmdefs. |

**Critical invariant:** Unity asmdefs cannot reference Assembly-CSharp. If `RTSCL.World.Unity` needs data from a lobby/network script (e.g., the world seed at game start), **invert the dependency**: world-side class exposes a `public static` field, lobby/network code pushes data into it (consume-on-read). See `WorldGeneratorBootstrap.PendingSeed` ← set by `GameStartLoader`.

## Code conventions

- **Singleton managers** auto-bootstrap via `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` + `DontDestroyOnLoad`, idempotent `_instance != null` guard. See `SteamManager`, `LobbyManager`, `NetworkManager`, `GameStartLoader`.
- **Static events** for cross-cutting state. `ResourceBank.OnWoodChanged`, `PopulationManager.OnChanged`, `BuildingConstruction.OnCompleted`, `GoblinProduction.OnChanged`, `LobbyManager.OnLobbyEntered/Left/...`, `NetworkManager.OnConnected/Disconnected/Error`, `NetworkSession.OnGameStartReceived`. UI subscribes; no service-locator lookups.
- **Per-cell game state** lives in static classes keyed by `Vector2Int`: `TreeHP`, `BuildingHP`, `BuildingConstruction`, `BuildingPlacer._cellOwners`. World reset routes through `MainBaseSetup.OnNewWorld` which clears all of them.
- **Asset-driven definitions**: `BuildingDefinition` (ScriptableObject) carries sprite/footprint/cost/trains-units/pop-provided. `GoblinUnitDefinition` carries icon/wood-cost/pop-cost/spawn-duration/HP/damage/attack-interval/range. `BuildingCatalog.asset` lists reachable buildings.

## Steam integration

- **App ID `4775350`** (`SteamManager.AppId` + `steam_appid.txt`).
- **Steamworks.NET 2025.163.0** via UPM Git URL (`Packages/manifest.json`). Wraps native C++ SDK 1.63. The `sdk/` folder (1.64) is reference-only — never mix native libs.
- `steam_appid.txt` is the dev-only "skip ownership check" file. **Never ship it in a public Steam build.**
- `SteamManager.Update` pumps `SteamAPI.RunCallbacks()` each frame, so any `Callback<>` registered by `LobbyManager` / `NetworkManager` fires correctly without their own pump.
- **Multiplayer transport** uses `SteamNetworkingSockets` (modern API, NAT punch + relay via `SteamNetworkingUtils.InitRelayNetworkAccess`). Host opens a P2P listen socket + poll group when entering a lobby; clients `ConnectP2P` to the lobby owner. Reliable channel only for now.
- **Wire protocol**: `[byte messageType | payload]`. `NetMessageType.GameStart = 1` carries the `int` seed. Defined in `Assets/Scripts/Lobby/NetMessages.cs`. `SendMessageToConnection` in Steamworks.NET takes `IntPtr` — pin the `byte[]` with `GCHandle.Alloc(..., Pinned)` once per batch, reuse, free in `finally`.

## Workflow

- Git repo with remote at `github.com/FelixEllspermann/goblino`. Direct commits to `main`. Git user: "Claude Code".
- **Documentation under `docs/superpowers/`**:
  - `specs/YYYY-MM-DD-<topic>-design.md` — design docs from brainstorming
  - `plans/YYYY-MM-DD-<feature>.md` — implementation plans for subagent-driven execution
- Unity serializes scenes/prefabs as YAML. Hand-edit only when MCP can't do it — broken GUIDs/file IDs corrupt the project.
- `.meta` files are load-bearing. Never delete a `.meta` without its asset and vice versa. Manually-written `.meta` GUIDs must be **exactly 32 hex chars** (not 33).
- Headless invocation pattern when needed:
  `"C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\fe199\RTSCL" -quit -executeMethod <Class>.<Method>`

## Unity MCP

The Unity MCP server (`mcp__unity-mcp__*` tools) is the preferred way to drive the editor. `Unity_RunCommand` compiles and runs an `IRunCommand` C# script. Use it for asset DB refresh, scene manipulation, `SerializedObject` wiring, play-mode probes, running tests.

**Gotchas:**
- Code is wrapped in `namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`. The class must be `internal class CommandScript : IRunCommand`.
- Sandbox blocks `System.Reflection` and some namespace references. If `Image` collides with `Unity.AI.Image`, alias: `using UImage = UnityEngine.UI.Image;`.
- `EditorBuildSettings.scenes` changes need an explicit `AssetDatabase.SaveAssets()` to flush to disk.
- Play-mode probes via `EditorApplication.EnterPlaymode()` are async — re-run the command after a few seconds to read results. Same applies to async Steam callbacks (lobby create/list/join take ~1–3 s).
- `EditorGUIUtility.Load("UI/Skin/UISprite.psd")` and `Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd")` both return `null` in Unity 6 in this project setup. UI built at runtime currently uses `sprite = null` (flat colored rect). Future polish: import a sliced sprite asset for borders.

## Testing

- Pure-logic tests under `Assets/Tests/Editor/` cover the world generator. 32 tests, all passing. Run via the Test Runner window or via Unity MCP's `TestRunnerApi`.
- Unity-bound code (`RTSCL.World.Unity` and `Assembly-CSharp`) is not automatically tested. Verification is per-task via MCP compile checks + manual play-mode validation (see `docs/superpowers/plans/`).
- Multiplayer flows (lobby join, P2P, GameStart sync) need two real Steam clients to verify end-to-end. Host-alone smoke tests cover the single-client path.

## Multiplayer roadmap

1. ✅ Combat foundation (local, Club-vs-Club friendly fire, hop-arc death + particle burst)
2. ✅ Main menu + Steam public-lobby browser
3. ✅ Steam P2P transport + world-seed sync
4. ☐ Player ownership (each unit knows its `OwnerSteamID`; only owner can command) + 2 spawn placements
5. ☐ Command sync (selection / move / harvest / build over the network)
6. ☐ Combat-over-network (uses ownership filter from #4)

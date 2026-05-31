# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

**Goblino** — a Unity 6 / URP 2D top-down RTS with Steam multiplayer. Repo: `github.com/FelixEllspermann/goblino`. Direct-to-main workflow.

Current playable loop (single-player):
- Main menu → Play Solo → **Solo setup panel** (seed + bot count + **map size**) → SampleScene with random world
- Auto-generated map (biomes, trees, resources, up to 4 spawn points; tuned for more mainland, more stone, closer + reachable safe-spawn resources). Map size presets (square edge): Winzig 96 / Klein 144 / Mittel 192 / **Groß 256 (default)** / Gigantisch 384 — `MapSize` enum + `WorldStartContext.SizeToDimension`, applied by `WorldGeneratorBootstrap` (overrides `WorldGenConfig.Width/Height`).
- Keep at spawn[0] with starting Farmer Goblins (+ optional test Club Goblins)
- Build Hut (+pop cap), Barracks (trains Club + Archer + Speargoblin), or **Docks** (coastal, trains Boats) — Farmer selects, right-click building card, click to place, Farmer constructs it. Costs are asset-driven (`BuildingDefinition`).
- Train Farmer at Keep, Club/Archer/Spear at Barracks, Boat at Docks (wood/food + pop cost, progress bar). Buildings have a **rally point** (GUI_33 flag + dashed line; right-click to set).
- Farmers harvest (auto-find same resource kind nearby, fan out via global standing-cell reservations) + build; Clubs melee, **Archers** fire homing arrows (with **armor-piercing**: ×1.5 vs armored units, so they counter Club/Spear), **Speargoblins** are slow tanky melee with **reach** (range 2) that deal **bonus damage vs monsters + enemy melee** (`CombatBonus`, multiplier on `GoblinUnitDefinition`) and a light on-hit knockback — strong vs Clubs, kited by Archers; units **attack & destroy buildings** (melee lunge / ranged arrow + building hit FX). Melee reach units won't strike across un-walkable water/cliff gaps (anti-camping path check).
- **Food sources:** wild **Berry Bushes** (`BerryBush_*` decoration) scatter randomly across the map + a few guaranteed near each spawn — 100 food each. **Wheat Fields are player-built** (200 wood, farmer constructs a `Wheatfield` building); on completion `MainBaseSetup` converts it into a harvestable wheat-field decoration (500 food). Tile→resource mapping lives in `Goblin.IsHarvestable/KindOf/MaxHpFor`.
- **Neutral monsters** (Giant Crab / Mammoth / Slime / Slime Blue) spawn in biome zones, wander + aggro + leash; hidden under fog (also on minimap).
- **On-hit feedback** on every unit (white flash + wobble + red spritz) and buildings (flash + spritz). **No friendly fire** (only cross-owner / neutral-vs-player are hostile).
- **Bot opponents** (1–N, solo only): own non-cheating economy, must scout under its own fog, builds + expands, trains military, runs adaptive attack plans, defends its base, raids spotted monsters, and ferries across water by boat. See "Single-player AI" below.
- **Win/lose:** a faction is eliminated when it owns 0 buildings; `MatchManager` pauses + shows a Victory/Defeat overlay with Back-to-Menu.
- Click a unit (own or enemy/neutral) to inspect a **stat sheet** (HP / damage / attack speed / range, live-updating). Population cap + unit costs are asset-driven.

Multiplayer state:
- Main menu offers `Multiplayer` → public-lobby browser via Steam matchmaking
- Lobby up to 4 players. Host clicks Start → world seed + per-player slot assignment broadcast over Steam P2P → all clients load SampleScene with identical map.
- Each player gets a keep + starting team at their assigned spawn (P0 Blue / P1 Red / P2 Yellow / P3 Green). Enemy units are colour-tinted; own units stay original sprite. Enemy buildings show name + HP but no production cards.
- Commands (Move / Harvest / Build-Assist / Place Building / Train Unit / Attack) sync via local-immediate + host-echo. Per-hit damage syncs via `EvDamage`. HP bars stay in lockstep across clients (~50-150ms Steam relay latency).

## Single-player AI & content

- **Solo is single-client authoritative.** `WorldStartContext.IsSolo` is true when `PendingSlots == null` (Play Solo path). `Goblin.IsOwnedLocally` is true for ALL units in solo, so the one client simulates player, bots, and monsters. `NetCommandBridge.OutgoingSender` is null → wire-sends are no-ops.
- **Owner model:** player = owner 0; bots = owner 1..N; neutral monsters carry the host owner (0 in solo) but `IsNeutral`. Bot factions get colour tints (`MainBaseSetup.SoloBotColor`); the player's units stay untinted.
- **Bot economy is real, not cheating** — `BotEconomy` holds a per-owner `int[6]` resource bank + pop, keyed by owner. Every bot action (train/build) is checked + deducted there. `BotController` drives each bot every tick: harvest round-robin, train farmers (target ~14), build huts early + barracks (then a 2nd), expand outward.
- **Bot fog / scouting:** each bot has its OWN `Explored` map and only "knows" what it has seen. It runs a growing scout team (+1 scout every 3 min until an enemy is found, then released). Land **connected-components** tell it what's reachable on foot.
- **Bot attack plans:** downtime (`_attackCooldown`) → roll small/medium/large via adaptive `PlanWeights` → train to size → march on a discovered enemy base → back to downtime. Defends when a hostile (monster OR enemy combatant) comes within `_defenseRadius` (30) of the keep, and raids neutral monsters it currently sees (training **Speargoblins** specifically for raids — bonus vs monsters; otherwise it trains a random mix of Club/Archer/Spear).
- **Bot naval:** if an enemy base / unexplored land is on another landmass, the bot builds a Dock, trains a Boat, and ferries units across (board → sail → unload → attack). Same `RunNaval` path is used for naval scouting.
- **Transport boats:** water-only movement (`Goblin._waterMode` inverts passability). Right-click own units onto a boat to board (cap 6); right-click the boat onto land to unload. Boat dies → passengers lost. `DockRegistry` maps a dock origin → its water cell (boat spawn point).
- **Anti-stacking:** stationary units (idle / fighting / harvesting / building) gently separate so they never share a cell; moving units path freely (no separation, to avoid chokepoint deadlocks).

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
- **Per-cell / per-owner game state** lives in static classes: `TreeHP`, `BuildingHP`, `BuildingConstruction`, `BuildingPlacer._cellOwners` (keyed by `Vector2Int`); `RallyPoints`, `DockRegistry`, `HarvestReservations` (a GLOBAL set of reserved standing cells so harvesters never share a cell, even across adjacent nodes); `BotEconomy` (keyed by owner `ulong`), `PlayerUpgrades` (purchased upgrades per owner), `TrainingSpeed` + `SightRange` (per-owner production-speed / vision-radius multipliers). World reset routes through `MainBaseSetup.OnNewWorld`, which clears all of them.
- **Asset-driven definitions**: `BuildingDefinition` (ScriptableObject) carries sprite/footprint/cost/trains-units/pop-provided + `WaterSprite` (dock pier) + `Category` (`BuildCategory` enum → build-menu tab) + `Requires` (prereq buildings; empty = always buildable). `GoblinUnitDefinition` carries icon/wood-cost/food-cost/pop-cost/spawn-duration/HP/damage/attack-interval/range + `ProjectileSprite` (set → ranged, fires an `Arrow`), `WorldScale` (monsters scale up), `WaterUnit` (boat: water-only movement), `MoveSpeed` (cells/sec, default 2.0), `BonusVsMonstersAndMelee` (damage multiplier vs monsters + enemy melee, 1 = none — Speargoblin), `KnockbackStrength` (cosmetic on-hit shove, 0 = none), `Armor` (points → diminishing-returns damage reduction `armor/(armor+36)`, capped 80%; Archer 0, Club 4 ≈ 10%, Spear 15 ≈ 29%), `BonusVsArmored` (damage multiplier vs targets with Armor > 0 — Archer = 1.5, armor-piercing; counters Club/Spear). Per-target attack bonuses live in `CombatBonus` (`Effective` = spear bonus vs monsters/melee, `WithArmorPierce` = archer bonus vs armored — both applied attacker-side in `Goblin.EffectiveDamageAgainst`, used by melee AND the ranged Arrow path); defender-side armor reduction in `ArmorMath` (both pure, in `RTSCL.World`, unit-tested). `ArmorMath.Apply` FLOORS the reduced damage (defender-favoured, so even small armor reliably chips integer hits) with a min-1 floor. Damage flow: attacker applies `CombatBonus` → wire/`EvDamage` → defender's `TakeDamage` applies `ArmorMath` (so armor stays in MP lockstep; reduction never reaches 100%). `BuildingCatalog.asset` lists reachable buildings (incl. Docks); unit assets live under `Assets/Generated/Units/`.
- **Upgrades** (one-time, per-owner, paid in iron/gold/crystal): asset-driven via `UpgradeDefinition` (cost + `UpgradeKind`) referenced from `BuildingDefinition.ProvidesUpgrades`; build-menu shows them automatically. Effects in `UpgradeEffects` (per-unit, applied live + retroactively to new spawns via `ApplyExistingTo`) — except owner-level multipliers `UnitTrainSpeed` (`TrainingSpeed`, consumed by `GoblinProduction` + bot timers) and `SightRange` (`SightRange`, consumed by `FogOfWar` + bot vision). `UpgradeEffects.OnPurchased` is the single entry (player via `NetCommandApplier`, bot via `BotController.TryResearchFrom`, which researches from Keep + Workshop + Mill). **12 upgrades:** Keep = Sight Range; Mill (economy) = Bountiful Harvest, Farmer Harvest Speed, Farmer Move Speed, Farmer Carry Capacity, Build Speed; Workshop (military) = Club Damage, Club HP, Melee Armor, Ranged Range, All-Units Damage, Train Speed.
- **Click-selection** uses each unit's padded sprite **`SelectionBounds`** (not a fixed radius), so large/offset sprites (boats, monsters) are pickable across their hull. Enemy/neutral units are click-inspectable (read-only) but never enter the commandable selection.
- **Build UI**: the left **`BuildMenu`** sidebar (shown while a Farmer is selected) groups buildables by `Category` into vertical tabs + a scrollable grid, greying out locked ones via `BuildRequirements.IsUnlocked(def, ownsCompleted)` (tech-tree foundation; prereqs currently empty). New building = just an asset (set Category, optionally Requires). The bottom **`ObjectInspector`** only inspects + shows train/upgrade cards now. Hover tooltips across both use the shared, auto-sized **`UITooltip`** singleton.

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
- **Play-mode time nearly freezes when the editor window is unfocused** (`Time.deltaTime` ≈ 0). So time-based behaviour (bot AI ticks, movement over seconds, attack cooldowns) can't be observed headlessly — verify *deterministic* things via probes (spawns, ownership, reachability/components, asset wiring, registry state, win/lose) and ask for a focused playtest for the rest. Don't blanket-claim "can't test."
- Reliable recompile of a filesystem edit: `AssetDatabase.ImportAsset(path, ForceUpdate | ForceSynchronousImport)` + `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation()` + wait ~13 s, then read the console. A plain `AssetDatabase.Refresh()` does NOT reliably pick up external edits.
- **Wiring an asset ref into a scene component: `LoadAssetAtPath` AFTER `OpenScene`, not before.** A reference loaded before opening the scene goes stale across the load, so `prop.objectReferenceValue = asset` silently becomes null (won't even stick in-memory). Symptom: the field reads NULL after reopen despite a valid asset + GUID. (Nested `[Serializable]`-config object refs persist fine when assigned this way; the earlier "nested doesn't persist" theory was actually this stale-load bug.)
- `EditorGUIUtility.Load("UI/Skin/UISprite.psd")` and `Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd")` both return `null` in Unity 6 in this project setup. UI built at runtime currently uses `sprite = null` (flat colored rect). Future polish: import a sliced sprite asset for borders.

## Testing

- Pure-logic tests under `Assets/Tests/Editor/` cover the world generator (+ `GoblinNetId`, `BuildRequirements`, `CombatBonus`, `ArmorMath`). 68 tests, all passing. The `RTSCL.World.Tests` asmdef references `RTSCL.World` **and** `RTSCL.World.Unity`. Run via the Test Runner window or via Unity MCP's `TestRunnerApi`.
- Unity-bound code (`RTSCL.World.Unity` and `Assembly-CSharp`) is not automatically tested. Verification is per-task via MCP compile checks + manual play-mode validation (see `docs/superpowers/plans/`).
- Multiplayer flows (lobby join, P2P, GameStart sync) need two real Steam clients to verify end-to-end. Host-alone smoke tests cover the single-client path.

## Multiplayer roadmap

1. ✅ Combat foundation (local Club-vs-Club; hop-arc death + particle burst). Friendly fire was later removed — only cross-owner / neutral-vs-player are hostile.
2. ✅ Main menu + Steam public-lobby browser
3. ✅ Steam P2P transport + world-seed sync
4. ✅ Player ownership (each unit knows its `Owner` ulong; only owner can command) + up to 4 spawn placements with faction tints
5. ✅ Command sync (move / harvest / build-assist / place-building / train-unit over the network)
6. ✅ Combat-over-network (per-hit `EvDamage` sync, attacker-owner authoritative)

## Networking architecture

- **Wire protocol:** `[byte messageType | payload]`. 8 message types: `GameStart=1`, `CmdMove=2`, `CmdHarvest=3`, `CmdBuildAssist=4`, `CmdPlaceBuilding=5`, `CmdTrainUnit=6`, `CmdAttack=7`, `EvDamage=8`. Pack/unpack lives in `NetWireFormat` (World.Unity asmdef — Lobby reads `byte[]` opaque).
- **Authority:** Local-immediate + host-echo. Issuer applies locally + sends to host. Host receives, applies, then echoes to all OTHER clients via `SendToOthers(payload, exceptConn)`. No self-echo.
- **Unit identity:** `GoblinNetId(ulong Owner, ushort LocalIndex)` (10 bytes wire). Starting units assigned in deterministic spawn order (matches across clients). Trained units pre-reserve their NetId via `GoblinNetRegistry.NextLocalIndex(owner)` at train time so all clients spawn with the same ID.
- **Building identity:** Keyed by `(Vector2Int origin)` — no separate NetId.
- **Asmdef bridge:** `RTSCL.World.Unity` can't reference Assembly-CSharp (where Steamworks lives). Two-way bridge: `NetCommandBridge.OutgoingSender` (`Action<byte[]>`) is set by `GameStartLoader` to `NetworkManager.SendToAll`. Wire-format pack/unpack lives entirely in `NetWireFormat` (World.Unity side). `NetworkCatalog` maps `defIndex ↔ BuildingDefinition` / `GoblinUnitDefinition` so wire messages stay byte-based.
- **Solo path:** `OutgoingSender` is `null` when `LoadGameScene` isn't run (Play Solo bypasses it). All `NetCommandIssuer` calls apply locally + wire-send becomes no-op. Solo behavior is identical to single-player.
- **Owner validation:** Receiver verifies `sender == attacker.NetId.Owner` (or `payload.owner == sender` for Place/Train). Mismatches drop + warn. Light anti-cheat MVP.
- **Combat gating:** `Goblin.Update` runs on every client (movement + hit anim deterministic). But `IssueDamage` only fires when `IsLocalOwner` — only attacker-owner authoritates per-hit damage and broadcasts `EvDamage`. Remotes apply damage via `NetCommandApplier.ApplyDamage` → `target.TakeDamage`.

## Key files (networking)

| File | Asmdef | Purpose |
|---|---|---|
| `Assets/Scripts/Lobby/SteamManager.cs` | Assembly-CSharp | Steamworks init + frame-pump callback |
| `Assets/Scripts/Lobby/LobbyManager.cs` | Assembly-CSharp | Steam matchmaking, public-lobby browser, join/create/leave |
| `Assets/Scripts/Lobby/NetworkManager.cs` | Assembly-CSharp | P2P sockets, send/receive, host-echo dispatch via `RouteMessage` |
| `Assets/Scripts/Lobby/NetworkSession.cs` | Assembly-CSharp | Cross-scene state (LocalPlayer, HostPlayer, GameSeed, PlayerSlots) |
| `Assets/Scripts/Lobby/NetMessages.cs` | Assembly-CSharp | `NetMessageType` enum + `PlayerSlot` + `PackGameStart` / `TryUnpackGameStart` |
| `Assets/Scripts/Lobby/GameStartLoader.cs` | Assembly-CSharp | Listens for `OnGameStartReceived`, pushes session into `WorldStartContext` + `NetCommandBridge.OutgoingSender`, loads SampleScene |
| `Assets/Scripts/Lobby/PlayerRegistry.cs` | Assembly-CSharp | 4-color faction lookup (`GetColorForPlayer(CSteamID)`) |
| `Assets/Scripts/World/Unity/WorldStartContext.cs` | RTSCL.World.Unity | Bridge: `LocalPlayer (ulong)`, `PendingSlots`, `GetPlayerColor` |
| `Assets/Scripts/World/Unity/NetCommandBridge.cs` | RTSCL.World.Unity | Bridge: `Action<byte[]> OutgoingSender` |
| `Assets/Scripts/World/Unity/NetWireFormat.cs` | RTSCL.World.Unity | Pack/unpack for 7 command/event types |
| `Assets/Scripts/World/Unity/NetCommandIssuer.cs` | RTSCL.World.Unity | 7 IssueX helpers: local-apply + send |
| `Assets/Scripts/World/Unity/NetCommandApplier.cs` | RTSCL.World.Unity | 7 ApplyX local mutations + top-level `Apply(byte[], ulong)` dispatcher |
| `Assets/Scripts/World/Unity/NetworkCatalog.cs` | RTSCL.World.Unity | `defIndex ↔ BuildingDefinition` / `GoblinUnitDefinition` lookups, populated by `MainBaseSetup.OnNewWorld` |
| `Assets/Scripts/World/Unity/GoblinNetId.cs` | RTSCL.World | `readonly struct GoblinNetId(ulong, ushort)` — unit-testable |
| `Assets/Scripts/World/Unity/GoblinNetRegistry.cs` | RTSCL.World.Unity | `Dictionary<GoblinNetId, Goblin>` + per-owner counter |
| `Assets/Scripts/World/Unity/BuildingOwner.cs` | RTSCL.World.Unity | MonoBehaviour: per-building owner + faction tint + selection ring |

## Key files (single-player gameplay & AI)

| File | Asmdef | Purpose |
|---|---|---|
| `Assets/Scripts/World/Unity/Goblin.cs` | RTSCL.World.Unity | Unit FSM (move/harvest/build/attack-unit/attack-building/board/unload/die), A* pathing, hit anim, `_waterMode` boats, `SelectionBounds`, stationary separation |
| `Assets/Scripts/World/Unity/BotController.cs` | RTSCL.World.Unity | Per-bot AI: economy, vision/scouting, components, attack plans, defense, monster raids, naval ferry |
| `Assets/Scripts/World/Unity/BotEconomy.cs` | RTSCL.World.Unity | Per-owner resource bank + pop (so bots can't cheat) |
| `Assets/Scripts/World/Unity/MonsterSpawner.cs` / `MonsterAI.cs` | RTSCL.World.Unity | Neutral monster placement (biome zones, coastal for crabs) + wander/aggro/leash |
| `Assets/Scripts/World/Unity/FogOfWar.cs` | RTSCL.World.Unity | Per-cell visibility; reveals ONLY around the local player's units/buildings |
| `Assets/Scripts/World/Unity/MatchManager.cs` | RTSCL.World.Unity | Win/lose by building ownership + Victory/Defeat overlay |
| `Assets/Scripts/World/Unity/HarvestReservations.cs` | RTSCL.World.Unity | Global standing-cell reservation (no harvester overlap, even across nodes) |
| `Assets/Scripts/World/Unity/RallyPoints.cs` | RTSCL.World.Unity | Per-building rally point + `RallyVisual` (flag + dashed line) |
| `Assets/Scripts/World/Unity/DockRegistry.cs` | RTSCL.World.Unity | Dock origin → water cell (boat spawn) |
| `Assets/Scripts/World/Unity/Arrow.cs` | RTSCL.World.Unity | Homing projectile (unit damage / visual-only building hit) |
| `Assets/Scripts/World/Unity/HitFeedback.cs` / `BuildingHitFeedback.cs` | RTSCL.World.Unity | On-hit flash + wobble + red spritz (units / buildings) |
| `Assets/Scripts/World/Unity/ObjectInspector.cs` | RTSCL.World.Unity | Bottom info panel: building / resource / unit stat sheet (HP live-updates every frame for buildings, own units, and inspected enemies) |
| `Assets/Scripts/World/Unity/GoblinHealthBar.cs` / `BuildingHealthBar.cs` | RTSCL.World.Unity | Floating world-space HP bars (units / buildings); fed by live HP, hidden at full, shown when damaged |
| `Assets/Scripts/Lobby/SoloSetupPanel.cs` | Assembly-CSharp | Solo setup menu (seed + bot count + map size) → `WorldStartContext.SoloBotCount` / `PendingMapSize` |

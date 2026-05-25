# Command Sync Over Network — Design

**Status:** Spec
**Date:** 2026-05-25
**Roadmap item:** Multiplayer #5 — Command sync (move / harvest / build over the network)

## Goal

Propagate unit/building commands (Move, Harvest, Build-Assist, Place-Building, Train-Unit) over the Steam P2P network so all clients see a consistent simulation. Attack and combat damage stay out of scope — those land in roadmap #6.

## Architecture

### Authority Model — Local-Immediate + Host-Echo

- The **issuer** applies a command to its local simulation immediately, then sends the command to the host.
- The **host** receives the command and broadcasts it to all OTHER connected clients (not back to the sender).
- Each **remote client** receives the command and applies it to its local sim.
- No round-trip lag for the issuer; ~50–150 ms before remotes see it (Steam relay latency).

Trade-off: state drift is possible (e.g. two players ordering harvest of the same tree). Acceptable until combat sync (#6) introduces authoritative damage.

### Unit Identity — Owner-Scoped Counter

Every goblin gets a `GoblinNetId = (ulong Owner, ushort LocalIndex)` — 10 bytes on the wire.

- Counter is **per-owner**, starts at 0, increments by 1 per spawn.
- **Starting units** (MainBaseSetup): IDs assigned in deterministic spawn order. All clients run the same loop with the same `PendingSlots` order, so IDs match without explicit sync.
- **Trained units**: the owning client reserves the next ID at train time and embeds it in the `CmdTrainUnit` payload. All clients (including remotes) spawn the unit with that pre-assigned ID when their local production timer completes.

Buildings are addressed by `(Vector2Int origin)` — no separate NetId needed.

### Asmdef Boundary

`RTSCL.World.Unity` must not reference Steamworks or Assembly-CSharp directly. The bridge pattern from sub-project #4 is reused:

- `NetCommandBridge.OutgoingSender` — `Action<byte[]>` delegate, set once by the lobby layer at game start, called by world-side code to send.
- World-side code only touches primitive types and the delegate. Lobby-side code translates to/from Steamworks types.

## Commands In Scope

| Command | Payload (after `[byte type]`) | Notes |
|---|---|---|
| `CmdMove` (2) | `[u16 N][NetId × N][float worldX][float worldY]` | Formation offset computed by issuer pre-send; remotes apply per-unit position as authoritative |
| `CmdHarvest` (3) | `[u16 N][NetId × N][i32 treeX][i32 treeY]` | Worker → tree cell |
| `CmdBuildAssist` (4) | `[u16 N][NetId × N][i32 originX][i32 originY]` | Worker → construction-site origin |
| `CmdPlaceBuilding` (5) | `[byte defIndex][i32 originX][i32 originY][ulong owner]` | Issuer placed a new construction site; remotes call `PlaceForce` with same params |
| `CmdTrainUnit` (6) | `[i32 originX][i32 originY][byte unitDefIndex][ulong owner][u16 reservedLocalIndex]` | All clients start production timer with pre-assigned NetId |

Existing `GameStart = 1` stays. New types start at 2.

**NetworkCatalog** (new ScriptableObject or static lookup) maps `defIndex ↔ BuildingDefinition` and `unitDefIndex ↔ GoblinUnitDefinition`. Identical across clients (asset-driven), populated once at scene load.

## Out of Scope (Deferred to #6)

- `CmdAttack`
- Combat damage application across clients
- Death state sync
- Unit destroyed/removed events

## Owner Validation

When a remote client receives a unit-targeted command, it verifies:

```
all units in payload have NetId.Owner == sender
```

For `CmdPlaceBuilding` and `CmdTrainUnit`, the payload's `owner` field must equal the sender. Mismatch → log warning + drop. This is light anti-cheat protecting against a malicious peer trying to command another player's units.

## Solo Fallback

`NetCommandBridge.OutgoingSender` is `null` in solo. `NetCommandIssuer` checks and short-circuits: applies the command locally without any wire activity. Solo path matches today's behavior exactly.

## Data Flow Example — Player A trains a Club at their Barracks

1. Player A clicks "Train Club" card → `ObjectInspector.OnUnitClicked` triggers
2. `NetCommandIssuer.IssueTrainUnit(buildingOrigin, unitDef, ownerA)`:
   - Reserves next NetId for ownerA: `idx = NextLocalIndex(ownerA)`
   - Calls `GoblinProduction.TryStart(origin, unitDef, idx)` LOCALLY → A sees timer
   - Packs `CmdTrainUnit` payload (with `idx`), sends via `NetCommandBridge`
3. Host (could be A or another player) receives via `NetworkManager`
   - If A is host: skips own self, broadcasts to other clients
   - If A is client: host echoes to all OTHER clients
4. Each remote receives `CmdTrainUnit`:
   - Validates sender owns the building (lookup via `BuildingPlacer.TryGetBuildingOwner`)
   - Calls `GoblinProduction.TryStart(origin, unitDef, idx)` with the SAME idx
5. Production timer counts down independently on each client. When it completes, each spawns the unit with `NetId = (ownerA, idx)`. All clients see the same goblin with the same ID.

## File Plan

### New files

| File | Asmdef | Responsibility |
|---|---|---|
| `Assets/Scripts/World/Unity/GoblinNetId.cs` | RTSCL.World.Unity | `readonly struct GoblinNetId(ulong, ushort)` with Equals/GetHashCode/ToString |
| `Assets/Scripts/World/Unity/GoblinNetRegistry.cs` | RTSCL.World.Unity | Static `Dictionary<GoblinNetId, Goblin>` + Register/Unregister/TryGet + Reset on world change |
| `Assets/Scripts/World/Unity/NetCommandBridge.cs` | RTSCL.World.Unity | Static `Action<byte[]> OutgoingSender` delegate (asmdef boundary) |
| `Assets/Scripts/World/Unity/NetCommandIssuer.cs` | RTSCL.World.Unity | Static helpers: `IssueMove/IssueHarvest/IssueBuildAssist/IssuePlaceBuilding/IssueTrainUnit`. Each: apply locally + serialize + send |
| `Assets/Scripts/World/Unity/NetCommandApplier.cs` | RTSCL.World.Unity | Static `Apply(byte[] payload, ulong sender)`. Decodes type byte, calls per-command handler, validates ownership, applies to local sim |
| `Assets/Scripts/World/Unity/NetworkCatalog.cs` | RTSCL.World.Unity | ScriptableObject or static lookup `defIndex ↔ BuildingDefinition`, `unitDefIndex ↔ GoblinUnitDefinition` |

### Modified files

| File | Change |
|---|---|
| `Assets/Scripts/Lobby/NetMessages.cs` | Add NetMessageType enum values (2-6); add Pack/TryUnpack per command type |
| `Assets/Scripts/Lobby/NetworkManager.cs` | Add `SendToOthers(payload, exceptConn)` host-echo helper; extend `RouteMessage` with 5 new cases → `NetCommandApplier.Apply` |
| `Assets/Scripts/Lobby/GameStartLoader.cs` | Wire `NetCommandBridge.OutgoingSender = NetworkManager.SendToAll` at game start (existing helper: from client it routes to host, from host it broadcasts to all connected clients) |
| `Assets/Scripts/World/Unity/Goblin.cs` | Add `public GoblinNetId NetId { get; private set; }`; `Init` takes netId; register on Init / unregister on OnDestroy |
| `Assets/Scripts/World/Unity/GoblinSpawner.cs` | `SpawnAt`/`SpawnAroundFootprint`/`SpawnByKindAroundFootprint` accept `ushort? reservedIndex`; if null, auto-increment owner's counter |
| `Assets/Scripts/World/Unity/MainBaseSetup.cs` | Starting units pass through GoblinSpawner with auto-counter (deterministic order) |
| `Assets/Scripts/World/Unity/GoblinSelectionController.cs` | `CommandFormation`/`CommandHarvest`/build-right-click route through `NetCommandIssuer` |
| `Assets/Scripts/World/Unity/BuildingPlacer.cs` | `Place` (local click path) routes through `NetCommandIssuer.IssuePlaceBuilding`; `PlaceForce` stays as the local-application entry point used by both issuer and remotes |
| `Assets/Scripts/World/Unity/ObjectInspector.cs` | `OnUnitClicked` (train) reserves NetId + routes through `NetCommandIssuer.IssueTrainUnit` |
| `Assets/Scripts/World/Unity/GoblinProduction.cs` | `TryStart` accepts optional `ushort reservedIndex`; passes to spawner at production complete |

## Testing

- **Unit (pure-logic):** NetMessages Pack/TryUnpack round-trips for all 5 command types (empty unit lists, max units, edge values).
- **Editor-mode:** GoblinNetRegistry add/remove/lookup. Counter increment per owner.
- **Manual smoke (1 client, host-alone):** All commands work locally — solo regression check.
- **Manual smoke (2 clients, Steam):** Player A moves → Player B sees A's units move. Place building → both see it. Train unit → both clients spawn the unit with the same NetId. Worker assigned to construct → both see worker walking + construction progress.

## Risks

| Risk | Mitigation |
|---|---|
| State drift on shared interactions (two players harvesting same tree) | Acceptable for #5. #6 adds authoritative damage which constrains shared state to host. |
| Production timer drift between clients | Each client runs its own timer; spawn happens with pre-assigned NetId, so even if timers complete a few hundred ms apart the resulting state is identical. |
| Malicious peer commanding another player's units | Owner-validation on each receive. Drop + log. |
| NetworkCatalog index mismatch between clients | All clients ship the same asset bundle (same `BuildingCatalog.asset`). If a future build mismatches, the index lookup fails fast. |
| Late-joining clients | Out of scope. Lobby Start button locks the player set; no mid-game join. |

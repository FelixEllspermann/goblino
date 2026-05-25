# Player Ownership + Multi-Spawn Placement

Date: 2026-05-25
Status: Approved for implementation

## Purpose

Establish per-player unit ownership and place all lobby members' starting bases on the map at distinct spawn points. Both clients spawn all teams from the same world seed so each player sees their own units + the enemies (frozen until command sync lands in #5). Selection is restricted to the local player's units. Goblins, buildings, healthbars, and selection rings show owner-faction color.

## Scope

In scope:
- Up to 4 players per lobby (bump `WorldGenConfig.PlayerCount` to 4)
- Static `PlayerRegistry` (slot ↔ SteamID + faction colors)
- Extended GameStart protocol carrying per-player spawn assignment
- `Goblin.Owner` (`CSteamID`) + new `BuildingOwner` component
- Sprite tint, healthbar border, selection ring all reflect owner color
- Selection filter: only local-owner goblins enter the selection list
- Multi-team spawn at game start (all teams' keeps + farmers + test clubs)
- Population tracker only counts local-owner units

Out of scope (later sub-projects):
- Command sync over network (#5) — without it, enemy units stand still
- Per-player resource banks / population caps (#5)
- Combat ownership-filter (#6) — Clubs can still target any team
- Fog-of-war revisions (current local-only fog already correct)
- Team modes / FFA toggles / spectator
- Player nameplates above units

## Data Model

### PlayerRegistry (new static class)

```csharp
public static class PlayerRegistry
{
    private static readonly Color[] _colors =
    {
        new(0.25f, 0.45f, 0.85f, 1f),   // P0 Blue
        new(0.85f, 0.30f, 0.30f, 1f),   // P1 Red
        new(0.95f, 0.85f, 0.25f, 1f),   // P2 Yellow
        new(0.30f, 0.75f, 0.35f, 1f),   // P3 Green
    };

    private static readonly Dictionary<CSteamID, int> _slotByPlayer = new();

    public static void Reset();
    public static void Register(CSteamID id, int slotIndex);
    public static int GetSlot(CSteamID id);
    public static Color GetColor(int slot);
    public static Color GetColorForPlayer(CSteamID id);
    public static IEnumerable<KeyValuePair<CSteamID, int>> All { get; }
}
```

Cleared by `MainBaseSetup.OnNewWorld` before re-population.

### Goblin

- `public CSteamID Owner { get; private set; }` — default `CSteamID.Nil`
- `public void SetOwner(CSteamID id)` — sets the field then calls `ApplyOwnerVisuals()`
- `ApplyOwnerVisuals()` sets `_renderer.color` to white (own / Nil) or `PlayerRegistry.GetColorForPlayer(Owner)` (enemy)

### BuildingOwner (new MonoBehaviour)

Attached to each building GameObject in `BuildingPlacer.PlaceForce`. Carries `CSteamID Owner` and owns the building's selection-ring child sprite.

- `public CSteamID Owner`
- `public void SetOwner(CSteamID id)` — applies tint to the SpriteRenderer
- `public void SetSelected(bool)` — toggles the ring child
- Construction integration: `BuildingConstruction.Register` stores `_targetColor` (read from renderer after `SetOwner` runs); `AddProgress` lerps from gray construction-tint toward that color rather than `Color.white`.

### BuildingPlacer

- New `Dictionary<Vector2Int, CSteamID> _cellToOwner` parallel to `_cellOwners`
- `PlaceForce(BuildingDefinition def, Vector2Int origin, bool charge = true, bool requireConstruction = true, CSteamID owner = default)` — new optional param; attaches `BuildingOwner.SetOwner(owner)` after instantiation
- New public `bool TryGetBuildingOwner(Vector2Int cell, out CSteamID owner)`
- `ClearAllPlaced` clears `_cellToOwner` too

### GoblinSpawner

`SpawnAroundFootprint(origin, footprint, count, kindName, owner = default)` and `SpawnByKindAroundFootprint(kindName, origin, footprint, owner = default)` thread an extra `CSteamID owner` param to `SpawnAt`, which calls `goblin.SetOwner(owner)` immediately after `Init`.

### NetworkSession additions

- `public static List<PlayerSlot> PlayerSlots { get; internal set; }` — null when not in MP game
- `Reset()` also clears `PlayerSlots`

### NetMessages — extended GameStart

Wire format:

```
[ byte type(=1) | int seed | byte playerCount | (ulong steamId | byte spawnIndex) × playerCount ]
```

```csharp
public sealed class PlayerSlot
{
    public CSteamID SteamId;
    public byte SpawnIndex;
}

public static byte[] PackGameStart(int seed, IList<PlayerSlot> slots);
public static bool TryUnpackGameStart(byte[] payload, out int seed, out List<PlayerSlot> slots);
```

Old single-int overload is removed. `NetworkManager.RouteMessage` updates to the new signature, sets both `GameSeed` and `PlayerSlots`, then raises the event.

## Game Start Flow

### Host (LobbyPanel.OnStart)

1. Random seed via `Random.Range(1, int.MaxValue)`.
2. Pull lobby members via `LobbyManager.GetCurrentMembers()`. Host is first (SpawnIndex 0). Sort remaining members ascending by `SteamID.m_SteamID`. Assign sequential SpawnIndices 1..N-1.
3. Build `List<PlayerSlot>` from that ordering.
4. `NetworkSession.GameSeed = seed`; `NetworkSession.PlayerSlots = slots`.
5. `NetworkManager.SendToAll(NetMessages.PackGameStart(seed, slots))`.
6. `SteamMatchmaking.SetLobbyJoinable(currentLobby, false)`.
7. `NetworkSession.RaiseGameStartReceived()` so the host loads through the same path as clients.

### Client (NetworkManager.RouteMessage on GameStart)

1. `TryUnpackGameStart(payload, out seed, out slots)`.
2. Defensive guard: if `NetworkSession.GameSeed != 0`, ignore (duplicate).
3. Set `NetworkSession.GameSeed = seed`, `NetworkSession.PlayerSlots = slots`.
4. `RaiseGameStartReceived()`.

### Scene load (`GameStartLoader`)

- Sets `WorldGeneratorBootstrap.PendingSeed = NetworkSession.GameSeed`.
- `SceneManager.LoadScene("SampleScene")`.

### Per-player spawn (`MainBaseSetup.OnNewWorld`)

```
PlayerRegistry.Reset()

if NetworkSession.PlayerSlots != null:
    for each PlayerSlot s in PlayerSlots:
        PlayerRegistry.Register(s.SteamId, s.SpawnIndex)
        SpawnTeamAtIndex(s.SteamId, s.SpawnIndex)
else (solo):
    var localId = NetworkSession.LocalPlayer  // CSteamID.Nil if Steam offline
    PlayerRegistry.Register(localId, 0)
    SpawnTeamAtIndex(localId, 0)

SpawnTeamAtIndex(ownerSteamId, spawnIndex):
    var cell = world.Spawns[spawnIndex]
    keepOrigin = cell - footprint/2
    BuildingPlacer.PlaceForce(keepDef, keepOrigin, charge=false, requireConstruction=false, owner=ownerSteamId)
    GoblinSpawner.SpawnAroundFootprint(keepOrigin, keepDef.Footprint, 5, "FarmerGoblin", ownerSteamId)
    GoblinSpawner.SpawnAroundFootprint(paddedOrigin, paddedFootprint, 2, "ClubGoblin", ownerSteamId)
    if (ownerSteamId == NetworkSession.LocalPlayer || ownerSteamId == CSteamID.Nil):
        PopulationManager.AddUsed(5 * 1 + 2 * 3)   // local pop only
```

### `WorldGenConfig`

- `PlayerCount = 4` (was 2). `SpawnPlanner` already uses farthest-point sampling so it handles up to 4 well-separated spawns.

## Visuals

### Sprite tint (Goblin + Building)

```csharp
bool isLocal = Owner == NetworkSession.LocalPlayer || Owner == CSteamID.Nil;
Color tint = isLocal ? Color.white : PlayerRegistry.GetColorForPlayer(Owner);
spriteRenderer.color = tint;
```

Applied via `Goblin.ApplyOwnerVisuals` and `BuildingOwner.ApplyOwnerVisuals` after each `SetOwner` call. For buildings during construction, `BuildingConstruction._targetColor` is captured from the post-tint renderer color so the lerp ends at the right tint.

### Healthbar border (Goblin)

`GoblinHealthBar` adds a third SpriteRenderer behind `_bg`:

- Sprite: same shared 1x1 white sprite cache
- Color: `PlayerRegistry.GetColorForPlayer(_goblin.Owner)` (cached at `AttachTo` time — owner doesn't change mid-game)
- Scale: `(BarWidth + 0.04, BarHeight + 0.04, 1)` — peeks ~0.02 world units around the bg
- SortingOrder: 27 (below bg=28, fill=29)
- Visibility follows the same rule as bg/fill (hidden when full HP and not selected)

### Selection ring (Goblin)

`Goblin.BuildSelectionRing` reads `Owner` and tints the ring:

- Own / Nil-owner → near-white `(0.95, 0.95, 0.95, 1)` (existing look)
- Enemy → `PlayerRegistry.GetColorForPlayer(Owner)` — but irrelevant since selection-filter prevents enemy rings from showing

### Building selection ring (new)

`BuildingOwner` instantiates a child sprite on `Awake`:

- A flat colored rectangle sized to the building's footprint plus 0.1 world-unit border, semi-transparent
- Color: `PlayerRegistry.GetColorForPlayer(Owner)` (set in `SetOwner`)
- Hidden by default; toggled by `BuildingOwner.SetSelected(true/false)` (see ObjectInspector wiring in the Selection + Command Filters section below)

## Selection + Command Filters

### GoblinSelectionController

```csharp
private bool IsLocal(Goblin g)
    => g != null && (g.Owner == NetworkSession.LocalPlayer || g.Owner == CSteamID.Nil);
```

- `SelectAtPoint`: among all `Goblin.All`, pick the nearest passing `IsLocal`
- `SelectInBox`: filter incoming list to `IsLocal`
- `TryGetGoblinAt` (attack target picker): unchanged — returns any non-selected goblin (including enemies, intentional)
- `CommandHarvest` / `CommandAttack` / build command: keep their existing filters (`IsWorker`, `AttackDamage > 0`). Local-only is implicit because `_selected` only ever contains local goblins.

### ObjectInspector

- `ShowBuilding(def, origin)`: look up owner via `BuildingPlacer.TryGetBuildingOwner(origin, ...)`; if owner != local, set all unit-card buttons' `interactable = false` with label "Enemy building"; still show name/HP/description.
- Tracks the previously-selected `BuildingOwner` in a `_currentBuildingOwner` field. On each `ShowBuilding`, calls `SetSelected(false)` on the old + `SetSelected(true)` on the new. `Hide()` also clears the ring.

## Death + Population Accounting

`Goblin.EnterDying`: only decrement the local `PopulationManager` if `Owner == LocalPlayer || Owner == CSteamID.Nil`. Same guard logic applied at spawn-add (`MainBaseSetup`).

## Edge Cases

| Scenario | Handling |
|---|---|
| Solo (`PlayerSlots == null`) | LocalPlayer is own SteamID or `CSteamID.Nil`. `IsLocal` returns true for both. Single team at spawn[0]. Identical UX to current behavior. |
| 1-player lobby (host alone clicks Start) | `PlayerSlots` has 1 entry. One team at spawn[0]. Indistinguishable from solo in gameplay. |
| Lobby has 2 players, world has 4 spawns | Only spawns 0 and 1 used. Spawns 2 and 3 unused (no keep). |
| Player joins lobby after Start | Blocked by `SetLobbyJoinable(false)` from #3. |
| Enemy unit dies | `EnterDying` ownership guard prevents local pop counter from going negative. |
| Steam offline during MP attempt | Can't happen — MP requires a lobby, lobby requires Steam. |
| Building tinted during construction | `BuildingConstruction` lerps from gray toward stored `_targetColor` (post-tint owner color). |
| Resources stay global | Only LOCAL farmers chop trees (they're the only ones with valid commands), so wood gains automatically attribute correctly. Enemy farmers stand still in #4, no risk of contamination. |

## Testing

Manual (single client, host-alone):
1. Solo: Play Solo → 1 keep at spawn[0], all-white units, no border-color difference. Existing behavior.
2. Create lobby alone → Start → 1 keep at spawn[0] owned by self. White sprite, blue healthbar border (P0 color).

Manual (two clients):
3. Host + guest in lobby. Host starts. Both clients load SampleScene. Each sees:
   - Own keep + 5 farmers + 2 clubs at own spawn (white sprites, blue healthbar border for host, red border for guest)
   - Enemy keep + 5 farmers + 2 clubs at other spawn (tinted with enemy player color)
4. Selection drag-box over enemy units → nothing selected (filter rejects).
5. Click enemy keep → inspector shows name + HP, train-unit cards greyed with "Enemy building".
6. Local clubs can still attack enemy clubs (combat ownership filter is #6).

Automated tests: not feasible for Steam-bound flow. `NetMessages.PackGameStart`/`TryUnpackGameStart` round-trip with multi-slot payload is unit-testable if the test asmdef is extended (deferred).

## Open Questions / Future Work

- Per-player resource banks (#5) — when commands sync, host's wood deductions need to be local to host, not affect guest's bank.
- Per-player population caps — same scoping.
- 4-color visual distinction may be insufficient for color-blind players. Add unit-icon shape or pattern overlay later.
- Building selection ring uses a runtime-built rectangle sprite. If a designer wants pixel-art ring sprite, swap the runtime mesh for an asset reference on `BuildingOwner`.
- `_targetColor` storage in `BuildingConstruction` may interact awkwardly with future "repair" / "damage" tinting — revisit if those land.

# Farmer Inventory + Keep Deposit — Design

**Status:** Spec
**Date:** 2026-05-27

## Goal

Replace the instant-credit harvest model with a carry-and-deposit loop: Farmer chops trees into an internal inventory (max 10), then walks back to the nearest own Keep to deposit. Fixes an MP bug along the way: harvest currently credits wood to every client's ResourceBank, not just the owner's.

## Per-Goblin Inventory

- `int _carriedWood` — 0..MaxCarriedWood, initialized 0
- `int MaxCarriedWood = 10` — constant on Goblin
- Inventory persists across commands (manual move doesn't reset it)
- Inventory lost on death (no drop-on-ground)

## State Machine Changes

### New state
- `WalkingToDeposit` — Farmer walks to nearest own Keep with a non-empty inventory.

### Modified flow

Current `State.Harvesting` chop-tick logic:
- `_harvestTimer >= _chopTickDuration` → `HitTree(cell)` which decrements TreeHP **and** calls `ResourceBank.AddWood(WoodPerHit)`.

New chop-tick logic:
- Only runs if `IsLocalOwner` (gate; remote clients skip the tick — fixes existing per-tick wood-double-credit bug).
- `HitTree(cell)` no longer calls `ResourceBank.AddWood`. It still decrements TreeHP.
- After `HitTree`, `_carriedWood += WoodPerHit` (capped at MaxCarriedWood).
- If `_carriedWood >= MaxCarriedWood` OR the tree was destroyed by that hit → transition to `WalkingToDeposit` (see below).

### WalkingToDeposit

Entry conditions:
- Inventory full, OR
- Tree destroyed and inventory non-empty.

Behavior:
- Find the nearest own-owned Keep (see "Keep lookup" below). If none found, drop to `State.Idle` with inventory preserved.
- Set `_moveTarget` to the Keep's edge cell (adjacent passable cell, similar to `FindAdjacentStandingSpot` for trees).
- Issue a `NetCommandIssuer.IssueMove(this, target)` so MP remotes see the farmer walk — without this internal issue, only the owner-client would see the movement.
- Tick: `StepToward(_moveTarget)`. On arrival (`StepToward` returns true):
  - `ResourceBank.AddWood(_carriedWood)` (local credit only — this is the local owner's bank)
  - `_carriedWood = 0`
  - Resume:
    - If `_treeCell` still references a live tree → set `_moveTarget = FindAdjacentStandingSpot(_treeCell)`, set `_state = State.MovingToTree`
    - Else → `FindNextTreeOrIdle()`
  - Same outbound `IssueMove` for the resume-walk so remotes see it.

### Manual Move Command During Carry

If the player right-clicks an empty cell while a carrying farmer is in `WalkingToDeposit` or `Harvesting`:
- Current `SetMoveCommand` transitions to `MovingToPoint`. Inventory **stays** (the farmer just stops working).
- A subsequent harvest or build command continues with inventory intact.

### Manual Harvest Command During Carry

`SetHarvestCommand` is called while carrying:
- Updates `_treeCell` and `_moveTarget` to the new tree as normal.
- Inventory preserved. The harvest tick uses the same `_carriedWood`.
- If already at cap, the first tick after arrival will immediately trigger `WalkingToDeposit` (no chop performed if full).

## Keep Lookup

Add `BuildingPlacer.TryFindNearestBuildingByName(string name, ulong owner, Vector2Int from, out Vector2Int origin)`:
- Iterates `_cellOwners` (existing dictionary) to find entries where:
  - `def.name == name` (e.g. "Keep_0")
  - `_cellToOwner[cell] == owner`
- Returns the origin (via existing `_cellToOrigin`) of the nearest match by Chebyshev distance.

The Goblin gets its `BuildingPlacer` reference via `NetCommandApplier.Placer` (already wired by `MainBaseSetup` at scene start).

## Visual: Carry-Count Text

Above each Farmer's health bar, a small text label shows the carried count:
- Visible only when `_carriedWood > 0`
- Visible only when `IsLocalOwner` (enemies' inventory isn't relevant)
- Text: just the integer (e.g. "7")
- Font: `dogica` (already used elsewhere)
- Position: 0.3 world units above the health bar
- Z-order: above health bar (sortingOrder 30+)

New component: `GoblinCarryText` (small MonoBehaviour parallel to `GoblinHealthBar`). Attached during `Goblin.Init`. Polls `_carriedWood` in `LateUpdate` for visibility and label refresh.

## Multiplayer Sync

**Owner-only harvest tick.** Gate the chop-tick branch (entire `State.Harvesting` body) behind `if (!IsLocalOwner) break;`. Non-owners skip the tick entirely — they still see the idle frame and the deposit-walk path (via the `IssueMove` calls).

**Deposit walks via IssueMove.** When transitioning to `WalkingToDeposit` and when resuming after deposit, the owner-client calls `NetCommandIssuer.IssueMove(this, target)`. This adds local-immediate state plus a `CmdMove` broadcast. Remote clients receive `CmdMove` and apply the same target via the existing `ApplyMove` → `SetMoveCommand` path.

Caveat: `SetMoveCommand` transitions the unit to `MovingToPoint`, not `WalkingToDeposit`. On the remote side that's fine — the farmer walks to the same destination and stops there. The fact that the remote doesn't know "why" the farmer is walking is invisible to the player. When the owner finishes deposit and issues the next IssueMove (back to the tree), the remote also follows.

**No new wire messages.** Inventory is owner-local. ResourceBank is already owner-local. The existing `CmdMove` covers both the deposit-walk and resume-walk visually for remotes.

## Solo Path

`NetCommandIssuer.IssueMove` is null-safe via `NetCommandBridge.Send` (no-op when `OutgoingSender == null`). In solo, only the local-apply path runs — identical to today's behavior plus the inventory bookkeeping.

## File Plan

### Modified files

| File | Change |
|---|---|
| `Goblin.cs` | Add `_carriedWood`, `MaxCarriedWood`, new `WalkingToDeposit` enum value, owner-gate on `State.Harvesting` tick body, modified `HitTree` (no ResourceBank), inventory cap-check + tree-destroyed transition logic, new `State.WalkingToDeposit` case, helper `TryStartDepositRun()` that calls `IssueMove`. |
| `BuildingPlacer.cs` | Add public method `TryFindNearestBuildingByName(string name, ulong owner, Vector2Int from, out Vector2Int origin)`. |

### New files

| File | Purpose |
|---|---|
| `GoblinCarryText.cs` | Per-farmer floating text showing carried wood. Auto-attached at `Goblin.Init` (like `GoblinHealthBar.AttachTo`). |

## Out of Scope

- Drop-on-ground when farmer dies
- Wood-pickup-from-corpse mechanic
- Inventory visible on enemy farmers
- Wood-deposit animation / sound effects
- Multiple resource types (still just wood)

## Testing

- **Edit-mode tests:** none needed (Goblin is MonoBehaviour-bound; BuildingPlacer keep-search is fine to verify by inspection + play-mode).
- **Solo smoke:**
  - 1 Farmer harvests a tree → see carry-text count up 1 / 2 / ... / 10
  - At 10: farmer walks to Keep
  - On arrival: wood UI jumps by 10, carry-text disappears
  - Farmer walks back to the tree, resumes chopping (if tree still there) or finds nearest (if destroyed)
  - Tree destroyed mid-load (e.g., at 4 wood): farmer walks to Keep with 4, deposits, finds new tree
- **2-client smoke:**
  - Each player's farmer harvests independently
  - Wood UI only increases for the owning player (existing bug fixed)
  - Both clients see the farmer walking to the Keep and back

## Risks

| Risk | Mitigation |
|---|---|
| MP move-spam when farmers cycle to Keep | Each deposit run issues 2 CmdMove messages (~50 bytes each). With 5–10 farmers in continuous loop, that's negligible. |
| Remote drift if `IssueMove` lands late | Worst case: remote farmer arrives at Keep position with a slight delay. No state divergence — both clients converge on idle frame at the same target. |
| No Keep exists yet (e.g., destroyed by enemy) | Farmer goes Idle with inventory preserved. Next time a Keep exists, manual harvest command restarts the loop. (Acceptable for MVP.) |

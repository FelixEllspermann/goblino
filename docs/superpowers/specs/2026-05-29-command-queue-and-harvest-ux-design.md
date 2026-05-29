# Command Queue & Harvest/Build UX — Design

**Date:** 2026-05-29
**Status:** Approved

## Goal

Four gameplay/UX improvements:

1. **Info box bigger + cost layout fix** — the bottom info popup is too small and card cost strings (e.g. "200 Iron, 150 Gold") bleed past the card's right edge.
2. **Offset harvesting** — multiple farmers sent to the *same* resource node must stand on *different* adjacent cells instead of stacking on one spot.
3. **Auto-build on place** — placing a building while farmers are selected immediately sends those farmers to construct it.
4. **Shift command queue** — Shift+right-click appends commands (Move / Harvest / Build / Attack) to a per-unit queue; without Shift the queue is replaced and the command runs immediately.

## Constraints

- **No wire-format change.** The shift queue lives owner-local; each queued step, when it activates, fires the existing single-unit net command (`IssueMove` / `IssueHarvest` / `IssueBuildAssist` / `IssueAttack`). Remotes mirror per step.
- Harvest standing-spot reservation is owner-local. Cross-client divergence is cosmetic (matches the existing "pathfinding divergence is cosmetic" stance in CLAUDE.md) and never affects resource totals (owner-authoritative).
- Solo path unchanged: `NetCommandBridge.OutgoingSender == null` → broadcasts are no-ops.

## Part 1 — Info box bigger + cost layout

**Files:** `Assets/Scenes/SampleScene.unity` (popup RectTransform via MCP), `Assets/Scripts/World/Unity/ObjectInspector.cs` (card text wrap).

- Widen + heighten `_popupRoot`'s RectTransform so two-resource cost strings fit on one line. Exact sizeDelta read live from the scene via MCP, then enlarged (target: ~30–40 % wider, taller enough for description + cards).
- In `CreateCard`, change both `nameText.horizontalOverflow` and `costLabel.horizontalOverflow` from `HorizontalWrapMode.Overflow` to `HorizontalWrapMode.Wrap`. Long cost strings then wrap inside the card's text column instead of spilling right.

**Acceptance:** Workshop upgrade cards ("200 Iron, 150 Gold" / "200 Gold, 150 Crystal") show their full cost inside the card with no horizontal overflow; building cards ("120 Wood, 60 Stone") fit on one line.

## Part 2 — Offset harvesting

**Files:** `Assets/Scripts/World/Unity/GoblinSelectionController.cs`, `Assets/Scripts/World/Unity/Goblin.cs`, new `Assets/Scripts/World/Unity/HarvestReservations.cs`.

- `GoblinSelectionController.CommandHarvest`: send **all** selected workers to the single clicked cell (one `IssueHarvest` for the whole worker group on that cell). Remove the "spread across nearby trees" logic (`FindNearbyHarvestables` no longer used for this path).
- New static `HarvestReservations`:
  - `Vector2Int Reserve(Vector3Int node, Goblin g, Func<Vector3Int,bool> passable, Vector3 from)` — returns the nearest *free* passable adjacent cell (8-neighborhood) to `node`, marking it taken for `g`. If all 8 are taken/impassable, returns the closest passable one anyway (stacking fallback) or the node center.
  - `void Release(Goblin g)` — frees whatever cell `g` held.
  - `void Clear()` — wipes all reservations (called from `MainBaseSetup.OnNewWorld`).
  - Internally: `Dictionary<Goblin, (Vector3Int node, Vector2Int cell)>` plus `Dictionary<Vector3Int, HashSet<Vector2Int>>`.
- `Goblin.SetHarvestCommand`: release any prior reservation, then call `HarvestReservations.Reserve(treeCell, this, IsCellPassable-equivalent, transform.position)` to pick the standing spot instead of `FindAdjacentStandingSpot`. Keep `FindAdjacentStandingSpot` as the single-cell fallback inside the reservation helper, or fold its logic in.
- `Goblin` releases its reservation on: entering `Dying`, switching to a non-harvest command (Move/Build/Attack), and when the node depletes and it stops harvesting.
- `MainBaseSetup.OnNewWorld`: add `HarvestReservations.Clear();` alongside the other static resets.

**Acceptance:** Select 5 farmers, right-click one tree/wheatfield → they fan out to 5 distinct adjacent cells and chop the same node. With >8 farmers, the extra ones stack on the closest spot.

## Part 3 — Auto-build on place

**Files:** `Assets/Scripts/World/Unity/BuildingPlacer.cs`, `Assets/Scenes/SampleScene.unity` (wire `_selectionController`).

- Add `[SerializeField] private GoblinSelectionController _selectionController;` to `BuildingPlacer`.
- In `Place(origin)`, after `NetCommandIssuer.IssuePlaceBuilding(...)`: gather selected `FarmerGoblin` workers from `_selectionController.Selection`; if any, call `NetCommandIssuer.IssueBuildAssist(workers, origin)`.
- Wire `_selectionController` in the scene via MCP.

**Acceptance:** Select farmers → click a building card → place ghost → left-click → the selected farmers walk over and construct it without a separate right-click.

## Part 4 — Shift command queue

**Files:** `Assets/Scripts/World/Unity/Goblin.cs`, `Assets/Scripts/World/Unity/GoblinSelectionController.cs`, `Assets/Scripts/World/Unity/BuildingPlacer.cs` (shift-aware placement, optional), new struct in `Goblin.cs`.

### Data
- Nested `public struct GoblinCommand { public CommandType Type; public Vector3 Point; public Vector3Int Cell; public Vector2Int Origin; public Goblin Target; }` with `public enum CommandType { Move, Harvest, BuildAssist, Attack }`.
- `Goblin`: `private readonly List<GoblinCommand> _commandQueue = new();`
- `Goblin.EnqueueCommand(GoblinCommand c)` — adds to the list. If currently `Idle`, the Update drain picks it up next frame.
- `Goblin.ClearQueue()` — clears the list.

### Input (GoblinSelectionController)
- Read shift: `bool shift = Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);`
- On right-click, resolve the command kind exactly as today (harvestable → Harvest; construction → BuildAssist; enemy goblin → Attack; else → Move).
- **Without shift:** `foreach (g in _selected) g.ClearQueue();` then issue immediately via the existing `NetCommandIssuer.IssueX` calls (unchanged behavior).
- **With shift:** enqueue one `GoblinCommand` per relevant unit (no immediate Issue, no ClearQueue):
  - Move: compute the same square-formation offset as `IssueMove` so queued formation matches; enqueue `{Move, Point = center+offset}` per unit.
  - Harvest: enqueue `{Harvest, Cell = treeCell}` for each worker (offset handled by Part 2 at activation).
  - BuildAssist: enqueue `{BuildAssist, Origin = origin}` for each worker.
  - Attack: enqueue `{Attack, Target = enemy}` for each combatant (`AttackDamage > 0`).
- ClickFeedback marker still spawns on each shift-click (existing per-click feedback).

### Activation (Goblin.Update, owner-local)
- At the end of `Update`, after the state switch:
  ```
  if (_state == State.Idle && _commandQueue.Count > 0) ActivateNextQueued();
  ```
- `ActivateNextQueued()` pops `_commandQueue[0]`, removes it, validates, and fires the matching **single-unit** net command (sets local state + broadcasts so remotes mirror the step):
  - Move → `NetCommandIssuer.IssueMove(new List<Goblin>{this}, cmd.Point)`
  - Harvest → if cell still harvestable: `NetCommandIssuer.IssueHarvest(new List<Goblin>{this}, cmd.Cell)`, else skip to next.
  - BuildAssist → if `BuildingConstruction.IsUnderConstruction(cmd.Origin)`: `IssueBuildAssist(new List<Goblin>{this}, cmd.Origin)`, else skip.
  - Attack → if `cmd.Target != null && cmd.Target.CurrentHp > 0 && AttackDamage > 0`: `IssueAttack(this, cmd.Target)`, else skip.
  - "Skip to next" = loop popping until a valid command runs or the queue empties.

### Queue-vs-auto-behavior guards
- `FindNextTreeOrIdle()`: at the top, `if (_commandQueue.Count > 0) { _state = State.Idle; return; }` — let the queue advance instead of auto-finding another tree.
- The deposit-resume block in `WalkingToDeposit`: when the inventory is dropped and `_commandQueue.Count > 0`, go `Idle` (queue takes over) instead of auto-resuming the last tree.

### MP correctness
- Remotes never populate a queue (only the local selection controller enqueues), so their `_commandQueue` stays empty and the drain is a no-op. Each owner-side activation broadcasts a normal single-unit command, which remotes apply via the existing `NetCommandApplier` path.

**Acceptance:** Shift+right-click a sequence of points → unit walks them in order. Shift-queue harvest→move→attack executes in order. A plain (no-shift) right-click clears the queue and overrides immediately. Multiple selected units each run their own queue.

## Out of scope
- Persistent on-screen waypoint lines/numbers (per-click ClickFeedback is the only feedback).
- Queue sync as a first-class wire message (owner-local + per-step broadcast is sufficient).
- Shift-queue for train-unit/upgrade purchases (building-side actions, not unit commands).

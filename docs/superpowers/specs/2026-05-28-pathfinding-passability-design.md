# Pathfinding + Passability (Sub-project B) — Design

**Status:** Spec
**Date:** 2026-05-28

## Goal

Goblins navigate around impassable terrain (deep water, cliffs) and harvestable resource cells (trees/rocks/wheat/ore) using grid A* pathfinding, instead of walking in straight lines. Buildings and other goblins do NOT block. Movement is 8-directional with diagonal corner-cut prevention.

## Context

- `Goblin` currently moves via `StepToward(Vector3 target)` — a straight-line lerp toward a single world point. States MovingToPoint/MovingToTree/WalkingToDeposit/MovingToBuild/MovingToAttack all call `StepToward(_moveTarget)` (attack uses the live target position).
- Commands set `_moveTarget` then a state: `SetMoveCommand`, `SetHarvestCommand` (→ `FindAdjacentStandingSpot(treeCell)`, already adjacent so works for any resource cell), `SetBuildCommand` (→ building origin center), `SetAttackCommand` (chases live target).
- `Goblin` holds `_terrainMap` + `_decorationMap` (Tilemaps). Terrain tile names: `DeepWater`, `Shore`, `Cliff`, biomes. Decoration tile names classify via `Goblin.IsHarvestable` (trees/wheat/rocks/ore) vs cosmetics (cactus/tumbleweed).
- Existing `IsTerrainPassable(cell)` returns `tile != null && name != "DeepWater" && name != "Cliff"` (Shore IS walkable).
- MP: movement targets sync via `CmdMove`/`CmdHarvest`/etc. Each client already runs the FSM locally.

## Passability

A cell `(x,y)` is **passable** iff:
1. Terrain tile is non-null AND its name is not `DeepWater` and not `Cliff`. (Shore = walkable beach.)
2. Decoration tile is null OR not harvestable (`!Goblin.IsHarvestable(name)`). Harvestable resources block; cosmetics (cactus, tumbleweed) do not.

Buildings are NOT consulted → buildings never block. Goblins do not block each other.

The passability check reads the two tilemaps live, so a depleted resource (tile removed) immediately frees its cell.

## Pathfinder (pure, testable)

New `Pathfinder` static class in `RTSCL.World.Unity`, decoupled from Unity tilemaps via an injected passability delegate so it is unit-testable on a synthetic grid:

```csharp
public static class Pathfinder
{
    // Returns a list of cells from start to goal (inclusive of goal, excludes start),
    // or null if unreachable / cap exceeded. Diagonal moves require both shared
    // orthogonal neighbors passable (no corner cutting).
    public static List<Vector2Int> FindPath(
        Vector2Int start, Vector2Int goal,
        int width, int height,
        System.Func<int,int,bool> passable,
        int maxExpansions = 6000);
}
```

- A*, 8-neighbor, octile heuristic (`D=1`, `D2=√2`).
- Diagonal `(±1,±1)` allowed only if `passable(x±1, y)` AND `passable(x, y±1)` (the two orthogonal cells the diagonal "cuts" past).
- If `goal` itself is impassable, returns null (callers path to an already-adjacent passable standing spot, so goal is passable).
- `maxExpansions` cap → returns null if exceeded (caller falls back to straight-line).
- Reusable static scratch buffers (`gScore[]`, `cameFrom[]`, a visited-generation stamp array, a binary min-heap) sized `width*height`, re-used across calls; a generation counter avoids clearing 65k entries each call. Indexed `y*width + x`.

## WorldGrid (dimensions)

Pathfinder needs width/height. Add a tiny static `WorldGrid { public static int Width, Height; }` set in `MainBaseSetup.OnNewWorld` (which already receives `WorldData`). Goblin reads `WorldGrid.Width/Height`. `Reset` on new world is implicit (overwritten each generation).

## Goblin Integration

### Waypoint following

Goblin gains:
```csharp
private readonly List<Vector3> _path = new();   // world-space waypoints, last = exact destination
private int _pathIndex;
```

`RepathTo(Vector3 worldTarget)`:
- `_moveTarget = worldTarget`.
- `start = Cell(transform.position)`, `goal = Cell(worldTarget)`.
- `cells = Pathfinder.FindPath(start, goal, WorldGrid.Width, WorldGrid.Height, IsCellPassable, ...)`.
- Build `_path`: for each path cell add its center; replace the final entry with the exact `worldTarget`. If `cells` is null or empty, `_path` = just `{ worldTarget }` (straight-line fallback).
- `_pathIndex = 0`.

`MoveAlongPath()` → bool (true when final destination reached):
- If `_path` empty → `return StepToward(_moveTarget)`.
- Step toward `_path[_pathIndex]`; when `StepToward` returns true, advance `_pathIndex`; when past the end → return true.

`IsCellPassable(int x, int y)`:
```
if out of [0,W)×[0,H) → false
terrain = _terrainMap.GetTile(cell); if null → false; name DeepWater/Cliff → false
deco = _decorationMap.GetTile(cell); if deco != null && Goblin.IsHarvestable(deco.name) → false
return true
```

### Command wiring

Replace direct `_moveTarget = X` + straight `StepToward` with `RepathTo(X)` + `MoveAlongPath()`:
- `SetMoveCommand(worldTarget)` → `RepathTo(worldTarget)`, state MovingToPoint.
- `SetHarvestCommand(cell)` → standing spot = `FindAdjacentStandingSpot(cell)`; `RepathTo(spot)`, state MovingToTree. (Resource cell itself is now impassable; standing spot is adjacent + passable.)
- `SetBuildCommand(origin)` → `RepathTo(origin center)`, state MovingToBuild. (Buildings don't block, so the origin cell is reachable.)
- `WalkingToDeposit` start (`TryStartDepositRun`) → `RepathTo(keep edge)`.
- Resume-to-tree after deposit + `FindNextTreeOrIdle` → `RepathTo(standing spot)`.
- The movement-state `case`s call `MoveAlongPath()` instead of `StepToward(_moveTarget)`.

### Attack movement (moving target)

`SetAttackCommand(target)` → `RepathTo(target.transform.position)`, state MovingToAttack. In the MovingToAttack case, recompute the path only when the target's CELL changed since the last repath (cheap `Cell(target.pos) != _lastAttackGoalCell`), not every frame. Otherwise `MoveAlongPath()`. This keeps melee chasing cheap while still routing around water.

### Standing-spot for build/keep

`FindAdjacentStandingSpot` already returns a passable adjacent cell. Keep `IsTerrainPassable` aligned with the new rule (DeepWater/Cliff impassable). The keep-edge target in `TryStartDepositRun` already picks a cell beside the keep; if that cell happens to be a resource/water, the pathfinder routes to the nearest reachable approach (path ends at the keep-edge cell center which is adjacent to the keep — keep cells are passable since buildings don't block).

## MP / Determinism

No wire-protocol change. Commands still send target cells/points; each client computes its own path locally. Minor per-client path divergence is cosmetic — units converge on the same destination (authoritative). Owner-gating of harvest/damage is unchanged.

## Performance

- A* runs on command issue + (attack) when the target changes cell — not per frame.
- Reusable scratch buffers + generation-stamp visited array → no per-call 65k clears, minimal GC.
- `maxExpansions` cap (6000) bounds worst-case; unreachable/over-cap → straight-line fallback (rare).

## File Plan

### New
| File | Purpose |
|---|---|
| `Assets/Scripts/World/Unity/Pathfinder.cs` | A* 8-dir with injected passability delegate + reusable buffers |
| `Assets/Scripts/World/Unity/WorldGrid.cs` | static Width/Height |
| `Assets/Tests/Editor/PathfinderTests.cs` | A* unit tests on synthetic grids |

### Modified
| File | Change |
|---|---|
| `Goblin.cs` | `_path`/`_pathIndex`, `RepathTo`, `MoveAlongPath`, `IsCellPassable`, `Cell` helper; movement states use path; commands call `RepathTo`; attack repath-on-cell-change; align `IsTerrainPassable` |
| `MainBaseSetup.cs` | set `WorldGrid.Width/Height` in `OnNewWorld` |

## Testing

- **Edit-mode (Pathfinder):**
  - Straight horizontal/vertical path on an empty grid → expected length.
  - Path around a wall of impassable cells → routes around, reaches goal.
  - Unreachable goal (walled off) → returns null.
  - Diagonal corner not cut: a 1-cell diagonal gap with both orthogonals blocked → path does NOT pass diagonally through.
  - Goal == start → empty/trivial path.
  - Expect total RTSCL.World.Tests = 36 + new Pathfinder tests.
- **Solo smoke:**
  - Order a goblin across a lake → it walks around the water, never onto deep water.
  - Order a goblin past a forest/rock cluster → routes around the resource cells.
  - Harvest a tree/rock/ore → goblin paths to an adjacent cell and harvests (resource cell never walked onto).
  - Build on the far side of an obstacle → goblin routes around; buildings themselves are walkable (units pass through placed buildings).
  - Deposit run routes around obstacles back to the keep.
  - Combat: a club chases a target around water rather than through it.
- **2-client smoke:** units reach the same destinations; minor path-shape divergence is acceptable.

## Risks
| Risk | Mitigation |
|---|---|
| A* cost with many units | Path computed only on command / attack-cell-change; reusable buffers; expansion cap. |
| Goblin stuck if standing spot unreachable | Fallback straight-line if no path; `FindAdjacentStandingSpot` already prefers passable cells. |
| Resource depletion mid-path leaves stale waypoints | Waypoints only get MORE passable as resources vanish; stale path still valid (never newly blocked, since buildings don't block + resources only disappear). |
| Diagonal squeezing through water corner | Corner-cut rule requires both orthogonals passable. |
| WorldGrid not set (e.g. menu scene) | Pathfinder treats out-of-range as impassable; if W/H are 0, FindPath returns null → straight-line fallback. Set in OnNewWorld before any unit moves. |
| Shore classification | Shore treated as walkable (beach); only DeepWater + Cliff block. Flip easily if undesired. |

## Out of Scope

- Unit-vs-unit collision / flow fields / local avoidance
- Path smoothing / funnel
- Flying or amphibious units
- Repath on dynamic blockers (none exist — buildings don't block, resources only unblock)

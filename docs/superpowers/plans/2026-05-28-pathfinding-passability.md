# Pathfinding + Passability (Sub-project B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Goblins pathfind (A*, 8-dir) around impassable terrain (deep water, shore, cliffs) and harvestable resource cells; buildings and other goblins never block. Straight-line `StepToward` is replaced by waypoint following.

**Architecture:** Pure `Pathfinder` (RTSCL.World, `int2` + injected passability delegate, unit-testable). A `WorldGrid` static holds map W/H. `Goblin` computes a waypoint path on each command via `RepathTo`, follows it via `MoveAlongPath`, and supplies an `IsCellPassable` delegate reading the terrain + decoration tilemaps.

**Tech Stack:** Unity 6 / C# / Unity.Mathematics / Tilemaps / RTSCL.World(.Unity) asmdefs

**Spec:** `docs/superpowers/specs/2026-05-28-pathfinding-passability-design.md`

---

## File Structure

### New
| File | Asmdef | Purpose |
|---|---|---|
| `Assets/Scripts/World/Pathfinder.cs` | RTSCL.World | A* 8-dir on `int2` grid via passability delegate + reusable buffers |
| `Assets/Scripts/World/Unity/WorldGrid.cs` | RTSCL.World.Unity | static `Width`/`Height` |
| `Assets/Tests/Editor/PathfinderTests.cs` | RTSCL.World.Tests | A* unit tests on synthetic grids |

### Modified
| File | Change |
|---|---|
| `Goblin.cs` | `_path`/`_pathIndex`/`_lastAttackGoalCell`; `RepathTo`/`MoveAlongPath`/`IsCellPassable`/`CellOfPos`; movement states use path; commands call `RepathTo`; attack repath-on-cell-change; `IsTerrainPassable` excludes Shore |
| `MainBaseSetup.cs` | set `WorldGrid.Width/Height` in `OnNewWorld` |

**Asmdef note:** `Pathfinder` lives in **RTSCL.World** (uses `Unity.Mathematics.int2`, no UnityEngine types) so `RTSCL.World.Tests` (which references only RTSCL.World) can test it. `Goblin` (RTSCL.World.Unity) converts `Vector2Int`↔`int2`.

---

## Task 1: Pathfinder + tests (TDD)

**Files:**
- Create: `Assets/Scripts/World/Pathfinder.cs`
- Create: `Assets/Tests/Editor/PathfinderTests.cs`

- [ ] **Step 1: Write Pathfinder.cs**

```csharp
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    /// <summary>Grid A* (8-directional, octile heuristic, no diagonal corner-cutting).
    /// Passability is injected so this is decoupled from Unity tilemaps and unit-testable.</summary>
    public static class Pathfinder
    {
        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private const float SQRT2 = 1.41421356f;

        private static int _w, _h, _curGen;
        private static float[] _g;
        private static int[] _from;
        private static int[] _gen;
        private static float[] _heapF;
        private static int[] _heapI;
        private static int _heapCount;

        /// <summary>Cells from start (exclusive) to goal (inclusive), or null if unreachable / cap exceeded.</summary>
        public static List<int2> FindPath(int2 start, int2 goal, int width, int height,
                                          Func<int, int, bool> passable, int maxExpansions = 6000)
        {
            if (passable == null || width <= 0 || height <= 0) return null;
            if (!InBounds(start.x, start.y, width, height) || !InBounds(goal.x, goal.y, width, height)) return null;
            if (!passable(goal.x, goal.y)) return null;
            if (start.x == goal.x && start.y == goal.y) return new List<int2>();

            EnsureBuffers(width, height);
            _curGen++;
            _heapCount = 0;

            int si = start.y * _w + start.x;
            int gi = goal.y * _w + goal.x;
            _g[si] = 0f; _from[si] = -1; _gen[si] = _curGen;
            HeapPush(Heuristic(start.x, start.y, goal.x, goal.y), si);

            int expansions = 0;
            while (_heapCount > 0)
            {
                int cur = HeapPop();
                if (cur == gi) return Reconstruct(cur, si);
                if (++expansions > maxExpansions) return null;

                int cx = cur % _w, cy = cur / _w;
                float cg = _g[cur];
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (!InBounds(nx, ny, _w, _h)) continue;
                    if (!passable(nx, ny)) continue;
                    bool diagonal = d >= 4;
                    if (diagonal && (!passable(cx, ny) || !passable(nx, cy))) continue; // no corner cut
                    int ni = ny * _w + nx;
                    float ng = cg + (diagonal ? SQRT2 : 1f);
                    if (_gen[ni] != _curGen || ng < _g[ni])
                    {
                        _g[ni] = ng;
                        _from[ni] = cur;
                        _gen[ni] = _curGen;
                        HeapPush(ng + Heuristic(nx, ny, goal.x, goal.y), ni);
                    }
                }
            }
            return null;
        }

        private static bool InBounds(int x, int y, int w, int h) => x >= 0 && x < w && y >= 0 && y < h;

        private static float Heuristic(int ax, int ay, int bx, int by)
        {
            int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
            int mn = Math.Min(dx, dy), mx = Math.Max(dx, dy);
            return (mx - mn) + SQRT2 * mn;
        }

        private static List<int2> Reconstruct(int goalIdx, int startIdx)
        {
            var rev = new List<int2>();
            int cur = goalIdx;
            while (cur != startIdx && cur != -1)
            {
                rev.Add(new int2(cur % _w, cur / _w));
                cur = _from[cur];
            }
            rev.Reverse();
            return rev;
        }

        private static void EnsureBuffers(int w, int h)
        {
            if (_g != null && _w == w && _h == h) return;
            _w = w; _h = h;
            int n = w * h;
            _g = new float[n];
            _from = new int[n];
            _gen = new int[n];
            _heapF = new float[1024];
            _heapI = new int[1024];
            _curGen = 0;
        }

        private static void HeapPush(float f, int idx)
        {
            if (_heapCount + 1 >= _heapF.Length)
            {
                Array.Resize(ref _heapF, _heapF.Length * 2);
                Array.Resize(ref _heapI, _heapI.Length * 2);
            }
            int i = ++_heapCount;
            _heapF[i] = f; _heapI[i] = idx;
            while (i > 1)
            {
                int p = i >> 1;
                if (_heapF[p] <= _heapF[i]) break;
                Swap(p, i); i = p;
            }
        }

        private static int HeapPop()
        {
            int top = _heapI[1];
            _heapF[1] = _heapF[_heapCount];
            _heapI[1] = _heapI[_heapCount];
            _heapCount--;
            int i = 1;
            while (true)
            {
                int l = i << 1, r = l + 1, m = i;
                if (l <= _heapCount && _heapF[l] < _heapF[m]) m = l;
                if (r <= _heapCount && _heapF[r] < _heapF[m]) m = r;
                if (m == i) break;
                Swap(m, i); i = m;
            }
            return top;
        }

        private static void Swap(int a, int b)
        {
            (_heapF[a], _heapF[b]) = (_heapF[b], _heapF[a]);
            (_heapI[a], _heapI[b]) = (_heapI[b], _heapI[a]);
        }
    }
}
```

- [ ] **Step 2: Write PathfinderTests.cs**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class PathfinderTests
    {
        // All-passable grid.
        private static System.Func<int, int, bool> Open() => (x, y) => true;

        [Test]
        public void StraightHorizontal()
        {
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(4, 0), 5, 5, Open());
            Assert.IsNotNull(path);
            Assert.AreEqual(4, path.Count);
            Assert.AreEqual(new int2(4, 0), path[path.Count - 1]);
        }

        [Test]
        public void DiagonalShortcut()
        {
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(2, 2), 5, 5, Open());
            Assert.IsNotNull(path);
            Assert.AreEqual(2, path.Count); // two diagonal steps
            Assert.AreEqual(new int2(2, 2), path[1]);
        }

        [Test]
        public void GoalEqualsStart()
        {
            var path = Pathfinder.FindPath(new int2(2, 2), new int2(2, 2), 5, 5, Open());
            Assert.IsNotNull(path);
            Assert.AreEqual(0, path.Count);
        }

        [Test]
        public void RoutesAroundWall()
        {
            // Wall on column x=2 for y=0..3, gap at y=4.
            System.Func<int, int, bool> passable = (x, y) => !(x == 2 && y <= 3);
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(4, 0), 5, 5, passable);
            Assert.IsNotNull(path);
            Assert.AreEqual(new int2(4, 0), path[path.Count - 1]);
            foreach (var c in path) Assert.IsFalse(c.x == 2 && c.y <= 3, "path crossed the wall");
        }

        [Test]
        public void UnreachableReturnsNull()
        {
            // Full wall column x=2 isolates the right half.
            System.Func<int, int, bool> passable = (x, y) => x != 2;
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(4, 0), 5, 5, passable);
            Assert.IsNull(path);
        }

        [Test]
        public void NoDiagonalCornerCut()
        {
            // (1,0) and (0,1) blocked → (0,0) cannot reach (1,1) diagonally (corner cut forbidden),
            // and no orthogonal route exists → null.
            System.Func<int, int, bool> passable = (x, y) => !((x == 1 && y == 0) || (x == 0 && y == 1));
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(1, 1), 3, 3, passable);
            Assert.IsNull(path);
        }
    }
}
```

- [ ] **Step 3: Refresh + run RTSCL.World.Tests via Unity MCP**

Refresh:
```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```
`Unity_ReadConsole` Types=["Error"] → 0. Then run EditMode tests (TestRunnerApi + Temp file callback). Expect 36 + 6 new = 42 pass.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Pathfinder.cs Assets/Scripts/World/Pathfinder.cs.meta Assets/Tests/Editor/PathfinderTests.cs Assets/Tests/Editor/PathfinderTests.cs.meta
git commit -m "feat(path): grid A* Pathfinder (8-dir, octile, no corner-cut) + edit-mode tests"
```

---

## Task 2: WorldGrid static + MainBaseSetup wiring

**Files:**
- Create: `Assets/Scripts/World/Unity/WorldGrid.cs`
- Modify: `Assets/Scripts/World/Unity/MainBaseSetup.cs`

- [ ] **Step 1: Create WorldGrid.cs**

```csharp
namespace RTSCL.World.Unity
{
    /// <summary>Current world grid dimensions, set when a world is generated.
    /// Consumed by Goblin pathfinding.</summary>
    public static class WorldGrid
    {
        public static int Width;
        public static int Height;
    }
}
```

- [ ] **Step 2: Set WorldGrid in MainBaseSetup.OnNewWorld**

In `MainBaseSetup.cs`, find the net-state setup block in `OnNewWorld`:

```csharp
            // Net-state setup for this world.
            GoblinNetRegistry.Reset();
            PlayerUpgrades.Reset();
            NetworkCatalog.PopulateFromCatalog(_catalog);
            NetCommandApplier.Placer = _buildingPlacer;
            NetCommandApplier.Spawner = _goblinSpawner;
```

Right after it, add:

```csharp
            WorldGrid.Width = world.Width;
            WorldGrid.Height = world.Height;
```

- [ ] **Step 3: Refresh + compile check**

`Unity_ReadConsole` Types=["Error"] → 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/WorldGrid.cs Assets/Scripts/World/Unity/WorldGrid.cs.meta Assets/Scripts/World/Unity/MainBaseSetup.cs
git commit -m "feat(path): WorldGrid dimensions set on new world"
```

---

## Task 3: Goblin pathfinding integration

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Add `using Unity.Mathematics;`**

At the top of `Goblin.cs`, after the existing usings (it already has `using RTSCL.World;`), add:

```csharp
using Unity.Mathematics;
```

- [ ] **Step 2: Add path state fields**

Find:
```csharp
        private Vector3 _moveTarget;
```
Right after it, add:
```csharp
        private readonly List<Vector3> _path = new();
        private int _pathIndex;
        private Vector2Int _lastAttackGoalCell = new Vector2Int(int.MinValue, int.MinValue);
```

(`List<Vector3>` needs `System.Collections.Generic` — already imported at top of Goblin.cs.)

- [ ] **Step 3: Add helpers (pathing + passability)**

Place these private methods near `StepToward` (e.g. directly above it):

```csharp
        private static Vector2Int CellOfPos(Vector3 p) =>
            new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));

        private bool IsCellPassable(int x, int y)
        {
            if (_terrainMap == null) return false;
            var t = _terrainMap.GetTile(new Vector3Int(x, y, 0));
            if (t == null) return false;
            if (t.name == "DeepWater" || t.name == "Cliff" || t.name == "Shore") return false;
            if (_decorationMap != null)
            {
                var d = _decorationMap.GetTile(new Vector3Int(x, y, 0));
                if (d != null && IsHarvestable(d.name)) return false;
            }
            return true;
        }

        private void RepathTo(Vector3 worldTarget)
        {
            _moveTarget = worldTarget;
            _path.Clear();
            _pathIndex = 0;
            var start = CellOfPos(transform.position);
            var goal = CellOfPos(worldTarget);
            var cells = Pathfinder.FindPath(new int2(start.x, start.y), new int2(goal.x, goal.y),
                                            WorldGrid.Width, WorldGrid.Height, IsCellPassable);
            if (cells != null && cells.Count > 0)
            {
                for (int i = 0; i < cells.Count; i++)
                    _path.Add(new Vector3(cells[i].x + 0.5f, cells[i].y + 0.5f, 0f));
                _path[_path.Count - 1] = worldTarget; // final waypoint = exact destination
            }
            else
            {
                _path.Add(worldTarget); // straight-line fallback (no path / out of bounds)
            }
        }

        private bool MoveAlongPath()
        {
            if (_path.Count == 0) return StepToward(_moveTarget);
            if (_pathIndex >= _path.Count) return true;
            if (StepToward(_path[_pathIndex]))
            {
                _pathIndex++;
                if (_pathIndex >= _path.Count) return true;
            }
            return false;
        }
```

- [ ] **Step 4: Route commands through RepathTo**

In `SetMoveCommand`, replace:
```csharp
            _moveTarget = worldTarget;
            _state = State.MovingToPoint;
```
with:
```csharp
            RepathTo(worldTarget);
            _state = State.MovingToPoint;
```

In `SetHarvestCommand`, replace the tail:
```csharp
            _treeCell = treeCell;
            _moveTarget = FindAdjacentStandingSpot(treeCell);
            _state = State.MovingToTree;
            _harvestTimer = 0f;
```
with:
```csharp
            _treeCell = treeCell;
            RepathTo(FindAdjacentStandingSpot(treeCell));
            _state = State.MovingToTree;
            _harvestTimer = 0f;
```

In `SetBuildCommand`, replace:
```csharp
            _moveTarget = new Vector3(buildingOrigin.x + 0.5f, buildingOrigin.y + 0.5f, 0f);
            _state = State.MovingToBuild;
            _buildTimer = 0f;
```
with:
```csharp
            RepathTo(new Vector3(buildingOrigin.x + 0.5f, buildingOrigin.y + 0.5f, 0f));
            _state = State.MovingToBuild;
            _buildTimer = 0f;
```

In `SetAttackCommand`, after the existing `_state = State.MovingToAttack;` line add the initial repath. The method currently ends:
```csharp
            _attackTarget = target;
            _attackTimer = 0f;
            _state = State.MovingToAttack;
```
Replace with:
```csharp
            _attackTarget = target;
            _attackTimer = 0f;
            _state = State.MovingToAttack;
            _lastAttackGoalCell = CellOfPos(target.transform.position);
            RepathTo(target.transform.position);
```

- [ ] **Step 5: Route TryStartDepositRun through RepathTo**

In `TryStartDepositRun`, replace:
```csharp
            ResetHitAnim();
            _moveTarget = target;
            _state = State.WalkingToDeposit;
            SendMoveWireOnly(target);
```
with:
```csharp
            ResetHitAnim();
            RepathTo(target);
            _state = State.WalkingToDeposit;
            SendMoveWireOnly(target);
```

- [ ] **Step 6: Route the WalkingToDeposit resume through RepathTo**

In the `State.WalkingToDeposit` case, replace:
```csharp
                        if (IsHarvestableStillThere(_treeCell))
                        {
                            _moveTarget = FindAdjacentStandingSpot(_treeCell);
                            _state = State.MovingToTree;
                            SendMoveWireOnly(_moveTarget);
                        }
```
with:
```csharp
                        if (IsHarvestableStillThere(_treeCell))
                        {
                            RepathTo(FindAdjacentStandingSpot(_treeCell));
                            _state = State.MovingToTree;
                            SendMoveWireOnly(_moveTarget);
                        }
```

(`FindNextTreeOrIdle` calls `SetHarvestCommand`, which now repaths — no change needed there.)

- [ ] **Step 7: Movement states follow the path**

In the Update switch:

`MovingToPoint`:
```csharp
                case State.MovingToPoint:
                    if (MoveAlongPath()) _state = State.Idle;
                    break;
```

`MovingToTree` — replace `if (StepToward(_moveTarget))` with `if (MoveAlongPath())`:
```csharp
                    if (MoveAlongPath())
                    {
                        _state = State.Harvesting;
                        _harvestTimer = 0f;
                    }
```

`WalkingToDeposit` — replace `if (StepToward(_moveTarget))` with `if (MoveAlongPath())` (keep the deposit body).

`MovingToBuild` — replace `if (StepToward(_moveTarget))` with `if (MoveAlongPath())`.

`MovingToAttack` — replace the body:
```csharp
                case State.MovingToAttack:
                {
                    if (_attackTarget == null || _attackTarget.CurrentHp <= 0) { _state = State.Idle; break; }
                    if (ChebyshevDistance(transform.position, _attackTarget.transform.position) <= AttackRange)
                    {
                        _state = State.Attacking;
                        _attackTimer = AttackInterval; // first hit immediately
                        break;
                    }
                    StepToward(_attackTarget.transform.position);
                    break;
                }
```
with:
```csharp
                case State.MovingToAttack:
                {
                    if (_attackTarget == null || _attackTarget.CurrentHp <= 0) { _state = State.Idle; break; }
                    if (ChebyshevDistance(transform.position, _attackTarget.transform.position) <= AttackRange)
                    {
                        _state = State.Attacking;
                        _attackTimer = AttackInterval; // first hit immediately
                        break;
                    }
                    var goalCell = CellOfPos(_attackTarget.transform.position);
                    if (goalCell != _lastAttackGoalCell)
                    {
                        _lastAttackGoalCell = goalCell;
                        RepathTo(_attackTarget.transform.position);
                    }
                    MoveAlongPath();
                    break;
                }
```

- [ ] **Step 8: Align IsTerrainPassable with the new rule (exclude Shore)**

Find `IsTerrainPassable`:
```csharp
        private bool IsTerrainPassable(Vector3Int cell)
        {
            if (_terrainMap == null) return true;
            var t = _terrainMap.GetTile(cell);
            if (t == null) return false;
            var n = t.name;
            return n != "DeepWater" && n != "Cliff";
        }
```
Replace the return with:
```csharp
            return n != "DeepWater" && n != "Cliff" && n != "Shore";
```

This keeps `FindAdjacentStandingSpot` (which uses `IsTerrainPassable`) consistent so harvest standing spots are always reachable cells.

- [ ] **Step 9: Refresh + compile check via Unity MCP**

`Unity_ReadConsole` Types=["Error"] → 0.

- [ ] **Step 10: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(path): Goblin follows A* waypoints + cell passability (water/shore/cliff/resources)"
```

---

## Task 4: Final compile + smoke verification

**Files:** none changed.

- [ ] **Step 1: Full refresh + console check via Unity MCP**

`Unity_ReadConsole` Types=["Error"] + FilterText="CS". Expect 0.

- [ ] **Step 2: Run RTSCL.World.Tests**

TestRunnerApi + Temp file callback. Expect 42/42 pass (36 + 6 Pathfinder).

- [ ] **Step 3: Solo smoke (user manually)**

User runs Play Solo:
- Move a goblin across a lake → it routes around water, never steps on DeepWater/Shore.
- Move past a forest/rock cluster → routes around the resource cells.
- Harvest a tree/rock/ore → goblin stops on an adjacent cell and harvests (never stands on the resource).
- Build across an obstacle → routes around; placed buildings are walkable (units pass through them).
- Deposit run routes back to the keep around obstacles.
- Combat: a club chases a target around water, not through it.
- Goblins don't block each other (overlap is fine).

- [ ] **Step 4: No commit unless a fix was needed**

If smoke passes: no further commits.

---

## Plan Self-Review Notes

**Spec coverage:**
- A* 8-dir, octile, corner-cut prevention, cap, reusable buffers (Task 1 `Pathfinder`)
- Pure + testable (Task 1 `int2` + delegate + `PathfinderTests`)
- Passability: DeepWater/Cliff/Shore + harvestable resources block; buildings/units don't (Task 3 `IsCellPassable`)
- WorldGrid dims (Task 2)
- Goblin waypoint following + command repath + attack repath-on-cell-change (Task 3)
- `IsTerrainPassable` aligned (Task 3 step 8)
- MP: no wire change (commands already sync targets; paths computed locally)

**Placeholder scan:** none.

**Type consistency:**
- `Pathfinder.FindPath(int2, int2, int, int, Func<int,int,bool>, int=6000) → List<int2>` — Task 1; called in Task 3 `RepathTo` with `int2`/`WorldGrid.Width/Height`/`IsCellPassable`.
- `WorldGrid.Width/Height` (int) — Task 2; read in Task 3.
- `Goblin._path` (List<Vector3>), `_pathIndex` (int), `_lastAttackGoalCell` (Vector2Int) — Task 3.
- `CellOfPos`, `IsCellPassable`, `RepathTo`, `MoveAlongPath` — Task 3, used within Task 3.
- `IsHarvestable` (existing public static) used by `IsCellPassable`.
- `int2` from `Unity.Mathematics` (added using in Task 3; RTSCL.World already references Unity.Mathematics).

**Asmdef:** Pathfinder in RTSCL.World (int2, no UnityEngine) → testable from RTSCL.World.Tests. WorldGrid in RTSCL.World.Unity (consumed by Goblin/MainBaseSetup). Goblin bridges Vector2Int↔int2.

**Compile-clean per commit:** Task 1 standalone (pure + tests). Task 2 standalone (static + one setter). Task 3 uses Task 1 + Task 2 (both already committed). No interim broken state.

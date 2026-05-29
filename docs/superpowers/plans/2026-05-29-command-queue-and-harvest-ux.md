# Command Queue & Harvest/Build UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bigger info box with non-overflowing cost text, farmers fanning out around a shared resource, auto-build when placing with workers selected, and a Shift+right-click command queue.

**Architecture:** All four changes live in `RTSCL.World.Unity` MonoBehaviours plus one new static helper (`HarvestReservations`). The shift queue is owner-local: each queued step fires the existing single-unit `NetCommandIssuer.IssueX` call on activation, so the wire protocol is unchanged and solo behaves identically (broadcasts are no-ops when `NetCommandBridge.OutgoingSender == null`).

**Tech Stack:** Unity 6000.4.3f1, URP 2D, New Input System, C#. No pure-logic units to test here — verification is via Unity MCP compile-check (`Unity_RunCommand` → `AssetDatabase.Refresh()` + read console) and manual play-mode smoke. Editor tests (currently 42) must still pass after every task as a regression gate.

**Verification primitives used throughout:**
- **Compile check:** `mcp__unity-mcp__Unity_RunCommand` running `UnityEditor.AssetDatabase.Refresh();` then `mcp__unity-mcp__Unity_ReadConsole` filtered to Errors. Expected: 0 compile errors.
- **Test regression gate:** run the editor test suite via `TestRunnerApi` (the established pattern: a static `ICallbacks` writing `Temp/<name>_test_results.txt`, polled with Bash `until [ -f Temp/... ]`). Expected: `pass=42 fail=0 skip=0`.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `Assets/Scripts/World/Unity/HarvestReservations.cs` | **Create** | Static per-resource standing-cell reservation so farmers fan out around one node. |
| `Assets/Scripts/World/Unity/Goblin.cs` | Modify | Use reservations for harvest standing spot; release on state change/death; add `GoblinCommand`/`CommandType`, `_commandQueue`, `EnqueueCommand`, `ClearQueue`, `ActivateNextQueued`, Update-drain, queue-vs-auto guards. |
| `Assets/Scripts/World/Unity/GoblinSelectionController.cs` | Modify | Send all workers to the one clicked node (remove neighbor-spread); read Shift; enqueue vs immediate per command type. |
| `Assets/Scripts/World/Unity/BuildingPlacer.cs` | Modify | `_selectionController` ref; auto build-assist selected workers after placing. |
| `Assets/Scripts/World/Unity/MainBaseSetup.cs` | Modify | `HarvestReservations.Clear()` in `OnNewWorld`. |
| `Assets/Scripts/World/Unity/ObjectInspector.cs` | Modify | Card name/cost text `Wrap` instead of `Overflow`. |
| `Assets/Scenes/SampleScene.unity` | Modify (via MCP) | Enlarge `_popupRoot` RectTransform; wire `BuildingPlacer._selectionController`. |

---

## Task 1: Card cost text wrap (Part 1a)

**Files:**
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs` (in `CreateCard`, the `nameText` block ~line 374 and `costLabel` block ~line 384)

- [ ] **Step 1: Switch name + cost overflow to Wrap**

In `CreateCard`, find:

```csharp
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
```
change to:
```csharp
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
```

And find:
```csharp
            costLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
```
change to:
```csharp
            costLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
```

- [ ] **Step 2: Compile check**

Run via `mcp__unity-mcp__Unity_RunCommand`:
```csharp
internal class CommandScript : IRunCommand
{
    public object Execute() { UnityEditor.AssetDatabase.Refresh(); return "refreshed"; }
}
```
Then `mcp__unity-mcp__Unity_ReadConsole` (types: Error). Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "fix(ui): wrap card name/cost text so costs don't bleed right"
```

---

## Task 2: Enlarge info popup (Part 1b)

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity` (via MCP — `_popupRoot` RectTransform)

- [ ] **Step 1: Read the current popup RectTransform**

Use `mcp__unity-mcp__Unity_RunCommand` to open SampleScene (if not open) and log the popup size. The popup is the GameObject referenced by `ObjectInspector._popupRoot`. Script:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public object Execute()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var inspector = UnityEngine.Object.FindFirstObjectByType<RTSCL.World.Unity.ObjectInspector>();
        var so = new SerializedObject(inspector);
        var popup = so.FindProperty("_popupRoot").objectReferenceValue as GameObject;
        var rt = popup.GetComponent<RectTransform>();
        return $"size={rt.sizeDelta} anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} anchoredPos={rt.anchoredPosition} pivot={rt.pivot}";
    }
}
```
Record the returned `sizeDelta`. (Note: if the popup uses stretch anchors, width/height live in offsetMin/offsetMax rather than sizeDelta — log those too and adjust accordingly.)

- [ ] **Step 2: Enlarge the popup**

Increase width by ~35% and height enough to clear description + cards (target description region + a 64px card row + padding). Set the new size on the RectTransform and save the scene. Example (adjust numbers to the values read in Step 1 — here assuming a fixed-anchor popup originally 360×180):

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public object Execute()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var inspector = UnityEngine.Object.FindFirstObjectByType<RTSCL.World.Unity.ObjectInspector>();
        var so = new SerializedObject(inspector);
        var popup = so.FindProperty("_popupRoot").objectReferenceValue as GameObject;
        var rt = popup.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(480f, 240f); // <-- replace with read-value * (1.35 width, +60 height)
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"new size={rt.sizeDelta}";
    }
}
```

- [ ] **Step 3: Verify in play mode (manual)**

Enter play mode, select the Workshop, confirm the upgrade cards show full cost text inside the enlarged box with no overflow. (If the popup is anchored to a screen edge, confirm it doesn't run off-screen.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "feat(ui): enlarge object-inspector info popup"
```

---

## Task 3: HarvestReservations helper (Part 2a)

**Files:**
- Create: `Assets/Scripts/World/Unity/HarvestReservations.cs`

- [ ] **Step 1: Create the static reservation class**

Create `Assets/Scripts/World/Unity/HarvestReservations.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Owner-local registry that assigns each harvesting goblin a distinct standing
    /// cell adjacent to a shared resource node, so farmers fan out instead of stacking.
    /// Reservations never affect resource totals (owner-authoritative); cross-client
    /// divergence is purely cosmetic.</summary>
    public static class HarvestReservations
    {
        private static readonly Dictionary<Goblin, (Vector3Int node, Vector2Int cell)> _byGoblin = new();
        private static readonly Dictionary<Vector3Int, HashSet<Vector2Int>> _takenByNode = new();

        private static readonly Vector3Int[] Offsets =
        {
            new( 1, 0, 0), new(-1, 0, 0), new( 0, 1, 0), new( 0,-1, 0),
            new( 1, 1, 0), new( 1,-1, 0), new(-1, 1, 0), new(-1,-1, 0),
        };

        /// <summary>Reserve the nearest free passable adjacent cell of <paramref name="node"/> for
        /// <paramref name="g"/>. Releases any prior reservation held by g first. Falls back to the
        /// closest passable adjacent cell (stacking) and finally the node center if none is passable.</summary>
        public static Vector2Int Reserve(Vector3Int node, Goblin g, Func<int, int, bool> passable, Vector3 from)
        {
            Release(g);

            if (!_takenByNode.TryGetValue(node, out var taken))
            {
                taken = new HashSet<Vector2Int>();
                _takenByNode[node] = taken;
            }

            Vector2Int bestFree = default; float bestFreeDist = float.MaxValue; bool foundFree = false;
            Vector2Int bestAny = default;  float bestAnyDist  = float.MaxValue; bool foundAny = false;

            foreach (var off in Offsets)
            {
                var nc = node + off;
                if (passable != null && !passable(nc.x, nc.y)) continue;
                var cell = new Vector2Int(nc.x, nc.y);
                float d = (new Vector3(nc.x + 0.5f, nc.y + 0.5f, 0f) - from).sqrMagnitude;
                if (d < bestAnyDist) { bestAnyDist = d; bestAny = cell; foundAny = true; }
                if (!taken.Contains(cell) && d < bestFreeDist) { bestFreeDist = d; bestFree = cell; foundFree = true; }
            }

            Vector2Int chosen;
            if (foundFree)      chosen = bestFree;
            else if (foundAny)  chosen = bestAny;          // all free taken → stack on closest passable
            else                chosen = new Vector2Int(node.x, node.y); // nothing passable → node center

            taken.Add(chosen);
            _byGoblin[g] = (node, chosen);
            return chosen;
        }

        /// <summary>Free whatever cell g currently holds.</summary>
        public static void Release(Goblin g)
        {
            if (g == null) return;
            if (!_byGoblin.TryGetValue(g, out var held)) return;
            _byGoblin.Remove(g);
            if (_takenByNode.TryGetValue(held.node, out var taken))
            {
                taken.Remove(held.cell);
                if (taken.Count == 0) _takenByNode.Remove(held.node);
            }
        }

        /// <summary>Wipe all reservations (new world).</summary>
        public static void Clear()
        {
            _byGoblin.Clear();
            _takenByNode.Clear();
        }
    }
}
```

- [ ] **Step 2: Compile check**

`Unity_RunCommand` → `AssetDatabase.Refresh()`, then `Unity_ReadConsole` (Errors). Expected: 0 errors. (The `.meta` for the new file is generated by the refresh.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/HarvestReservations.cs Assets/Scripts/World/Unity/HarvestReservations.cs.meta
git commit -m "feat(harvest): HarvestReservations — distinct standing cells per node"
```

---

## Task 4: Goblin uses reservations for harvest standing spot (Part 2b)

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs` (`SetHarvestCommand` ~line 134; add release calls in `SetMoveCommand`, `SetBuildCommand`, `SetAttackCommand`, `EnterDying`; `FindNextTreeOrIdle` and the deposit-resume path)

- [ ] **Step 1: Use reservation in `SetHarvestCommand`**

Replace the body of `SetHarvestCommand` (currently picks `FindAdjacentStandingSpot(treeCell)`) so the standing spot comes from `HarvestReservations`. Find:

```csharp
            _treeCell = treeCell;
            RepathTo(FindAdjacentStandingSpot(treeCell));
            _state = State.MovingToTree;
            _harvestTimer = 0f;
```
replace with:
```csharp
            _treeCell = treeCell;
            var spot = HarvestReservations.Reserve(treeCell, this, IsCellPassable, transform.position);
            RepathTo(new Vector3(spot.x + 0.5f, spot.y + 0.5f, 0f));
            _state = State.MovingToTree;
            _harvestTimer = 0f;
```

- [ ] **Step 2: Release reservation when switching to a non-harvest command**

At the top of `SetMoveCommand` (after the `if (_state == State.Dying) return;`), add:
```csharp
            HarvestReservations.Release(this);
```
Do the same as the first line of the bodies of `SetBuildCommand` and `SetAttackCommand` (after their `if (_state == State.Dying) return;` guards).

- [ ] **Step 3: Release on death**

In `EnterDying`, after `ResetHitAnim();`, add:
```csharp
            HarvestReservations.Release(this);
```

- [ ] **Step 4: Release when the node depletes (no longer harvesting)**

In `FindNextTreeOrIdle`, add `HarvestReservations.Release(this);` as the **first line** (before the `_decorationMap == null` check). This frees the spot whenever the goblin stops working its current node — `SetHarvestCommand` re-reserves if it picks a new one.

- [ ] **Step 5: Compile check**

`AssetDatabase.Refresh()` → `Unity_ReadConsole` (Errors). Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(harvest): goblins reserve distinct standing cells around a node"
```

---

## Task 5: Send all workers to the clicked node (Part 2c)

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSelectionController.cs` (`CommandHarvest` ~line 118)

- [ ] **Step 1: Replace neighbor-spread with single-node group harvest**

Replace the whole `CommandHarvest` method body. Find:

```csharp
        private void CommandHarvest(Vector3Int clickedTree)
        {
            // Only worker units (Farmer Goblins) can harvest
            var workers = new List<Goblin>();
            foreach (var g in _selected) if (IsWorker(g)) workers.Add(g);
            if (workers.Count == 0) return;

            // Find up to N nearest trees around the click (one per worker)
            var trees = FindNearbyHarvestables(clickedTree, workers.Count, _harvestSpreadRadius);
            if (trees.Count == 0) return;
            // Group workers by their assigned tree, then issue one network command per tree.
            var byTree = new Dictionary<Vector3Int, List<Goblin>>();
            for (int i = 0; i < workers.Count; i++)
            {
                var assigned = trees[i % trees.Count];
                if (!byTree.TryGetValue(assigned, out var list))
                { list = new List<Goblin>(); byTree[assigned] = list; }
                list.Add(workers[i]);
            }
            foreach (var kvp in byTree)
                NetCommandIssuer.IssueHarvest(kvp.Value, kvp.Key);
        }
```
replace with:
```csharp
        private void CommandHarvest(Vector3Int clickedNode)
        {
            // Only worker units (Farmer Goblins) can harvest. All selected workers go to the
            // SAME clicked node; HarvestReservations fans them out onto distinct adjacent cells.
            var workers = new List<Goblin>();
            foreach (var g in _selected) if (IsWorker(g)) workers.Add(g);
            if (workers.Count == 0) return;
            NetCommandIssuer.IssueHarvest(workers, clickedNode);
        }
```

`FindNearbyHarvestables` and `_harvestSpreadRadius` are now unused by this path but other code/serialized fields may reference them; leave them in place (removing the serialized field would dirty the scene). No further change.

- [ ] **Step 2: Compile check**

`AssetDatabase.Refresh()` → `Unity_ReadConsole` (Errors). Expected: 0 errors (an unused-private-method warning for `FindNearbyHarvestables` is acceptable; if the build treats warnings as errors, prefix the method with `// ReSharper disable once UnusedMember.Local` — not the case in this project).

- [ ] **Step 3: Manual play-mode smoke**

Enter play mode, select 5 farmers, right-click one wheatfield → confirm they fan out to distinct adjacent cells and all chop the same node.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSelectionController.cs
git commit -m "feat(harvest): send all selected workers to the one clicked node"
```

---

## Task 6: HarvestReservations.Clear on new world (Part 2d)

**Files:**
- Modify: `Assets/Scripts/World/Unity/MainBaseSetup.cs` (`OnNewWorld`, the static-reset block ~line 47-53)

- [ ] **Step 1: Add the clear call**

In `OnNewWorld`, find:
```csharp
            GoblinNetRegistry.Reset();
            PlayerUpgrades.Reset();
```
add immediately after:
```csharp
            HarvestReservations.Clear();
```

- [ ] **Step 2: Compile check**

`AssetDatabase.Refresh()` → `Unity_ReadConsole` (Errors). Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/MainBaseSetup.cs
git commit -m "feat(harvest): clear reservations on new world"
```

---

## Task 7: Auto-build on place (Part 3)

**Files:**
- Modify: `Assets/Scripts/World/Unity/BuildingPlacer.cs` (`_selectionController` field ~line 15; `Place` ~line 168)
- Modify: `Assets/Scenes/SampleScene.unity` (wire `_selectionController` via MCP)

- [ ] **Step 1: Add the selection-controller field**

In `BuildingPlacer`, in the `[Header("References")]` block, after the `_worldSource` field, add:
```csharp
        [SerializeField] private GoblinSelectionController _selectionController;
```

- [ ] **Step 2: Auto-assign workers after placing**

Replace `Place`:
```csharp
        private void Place(Vector2Int origin)
        {
            if (_selected == null) return;
            ulong owner = WorldStartContext.LocalPlayer;
            NetCommandIssuer.IssuePlaceBuilding(_selected, origin, owner);
            Cancel();
        }
```
with:
```csharp
        private void Place(Vector2Int origin)
        {
            if (_selected == null) return;
            ulong owner = WorldStartContext.LocalPlayer;
            NetCommandIssuer.IssuePlaceBuilding(_selected, origin, owner);

            // Auto-build: send the currently-selected worker goblins to construct it.
            if (_selectionController != null)
            {
                var workers = new System.Collections.Generic.List<Goblin>();
                foreach (var g in _selectionController.Selection)
                    if (g != null && g.Kind == "FarmerGoblin") workers.Add(g);
                if (workers.Count > 0) NetCommandIssuer.IssueBuildAssist(workers, origin);
            }

            Cancel();
        }
```

- [ ] **Step 3: Wire `_selectionController` in the scene (MCP)**

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public object Execute()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var placer = UnityEngine.Object.FindFirstObjectByType<RTSCL.World.Unity.BuildingPlacer>();
        var sel = UnityEngine.Object.FindFirstObjectByType<RTSCL.World.Unity.GoblinSelectionController>();
        var so = new SerializedObject(placer);
        so.FindProperty("_selectionController").objectReferenceValue = sel;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sel != null ? "wired" : "selection controller NOT found";
    }
}
```
Expected return: `wired`.

- [ ] **Step 4: Compile check + manual smoke**

`AssetDatabase.Refresh()` → 0 errors. Then play mode: select farmers, pick a building card, place → the farmers walk over and build it with no extra right-click.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildingPlacer.cs Assets/Scenes/SampleScene.unity
git commit -m "feat(build): selected farmers auto-build a placed building"
```

---

## Task 8: GoblinCommand data + queue storage (Part 4a)

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs` (add type + fields + public methods near the other public API, e.g. after `IsIdle` ~line 41 for the type, and after the state fields for the queue)

- [ ] **Step 1: Add the command type + struct**

In `Goblin.cs`, inside the `Goblin` class (top, near other public members — placing it right after the `public bool IsIdle => _state == State.Idle;` line is fine), add:

```csharp
        public enum CommandType { Move, Harvest, BuildAssist, Attack }

        public struct GoblinCommand
        {
            public CommandType Type;
            public Vector3 Point;       // Move
            public Vector3Int Cell;     // Harvest
            public Vector2Int Origin;   // BuildAssist
            public Goblin Target;       // Attack
        }

        private readonly List<GoblinCommand> _commandQueue = new();

        public void EnqueueCommand(GoblinCommand c) => _commandQueue.Add(c);
        public void ClearQueue() => _commandQueue.Clear();
```

(`System.Collections.Generic` and `UnityEngine` are already imported at the top of the file.)

- [ ] **Step 2: Compile check**

`AssetDatabase.Refresh()` → `Unity_ReadConsole` (Errors). Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(queue): GoblinCommand type + per-unit command queue storage"
```

---

## Task 9: Queue activation + Update drain + auto-behavior guards (Part 4b)

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs` (`Update` end ~line 415; new `ActivateNextQueued`; `FindNextTreeOrIdle` ~line 653; deposit-resume in `WalkingToDeposit` ~line 305)

- [ ] **Step 1: Drain the queue when idle**

In `Update`, the final lines currently are:
```csharp
            // Hit animations (chop / build / attack) keep playing across state transitions
            // so the killing blow's lunge finishes after the target dies. Dying state owns
            // the transform itself, so we skip there.
            if (_state != State.Dying) UpdateHitAnim();
        }
```
Insert the drain **before** the hit-anim line:
```csharp
            // Drain the command queue: whenever idle with queued commands, start the next.
            // Queue is owner-local (remotes never enqueue), so this is a no-op on remotes.
            if (_state == State.Idle && _commandQueue.Count > 0) ActivateNextQueued();

            // Hit animations (chop / build / attack) keep playing across state transitions
            // so the killing blow's lunge finishes after the target dies. Dying state owns
            // the transform itself, so we skip there.
            if (_state != State.Dying) UpdateHitAnim();
        }
```

- [ ] **Step 2: Add `ActivateNextQueued`**

Add this method to the `Goblin` class (e.g. right after `Update`):
```csharp
        /// <summary>Pop and run the next queued command (owner-local). Invalid commands
        /// (depleted node, dead target, finished construction) are skipped. Each activated
        /// command fires the normal single-unit net command so remotes mirror the step.</summary>
        private void ActivateNextQueued()
        {
            while (_commandQueue.Count > 0)
            {
                var cmd = _commandQueue[0];
                _commandQueue.RemoveAt(0);
                var solo = new List<Goblin> { this };
                switch (cmd.Type)
                {
                    case CommandType.Move:
                        NetCommandIssuer.IssueMove(solo, cmd.Point);
                        return;
                    case CommandType.Harvest:
                        if (IsHarvestableStillThere(cmd.Cell))
                        {
                            NetCommandIssuer.IssueHarvest(solo, cmd.Cell);
                            return;
                        }
                        break; // depleted — try next
                    case CommandType.BuildAssist:
                        if (BuildingConstruction.IsUnderConstruction(cmd.Origin))
                        {
                            NetCommandIssuer.IssueBuildAssist(solo, cmd.Origin);
                            return;
                        }
                        break; // already built — try next
                    case CommandType.Attack:
                        if (cmd.Target != null && cmd.Target.CurrentHp > 0 && AttackDamage > 0)
                        {
                            NetCommandIssuer.IssueAttack(this, cmd.Target);
                            return;
                        }
                        break; // dead target — try next
                }
            }
        }
```

- [ ] **Step 3: Guard `FindNextTreeOrIdle` against the queue**

`FindNextTreeOrIdle` currently begins (after Task 4 added the Release line):
```csharp
        private void FindNextTreeOrIdle()
        {
            HarvestReservations.Release(this);
            if (_decorationMap == null) { _state = State.Idle; return; }
```
Insert the queue guard right after the Release line:
```csharp
        private void FindNextTreeOrIdle()
        {
            HarvestReservations.Release(this);
            if (_commandQueue.Count > 0) { _state = State.Idle; return; } // let the queue advance
            if (_decorationMap == null) { _state = State.Idle; return; }
```

- [ ] **Step 4: Guard the deposit-resume against the queue**

In the `WalkingToDeposit` case, after depositing, the code resumes the last tree / auto-finds. Find:
```csharp
                        // Resume: walk back to last tree if still alive, else find nearest, else idle.
                        if (IsHarvestableStillThere(_treeCell))
```
insert before it:
```csharp
                        // If commands are queued, let them take over instead of auto-resuming.
                        if (_commandQueue.Count > 0) { _state = State.Idle; break; }

                        // Resume: walk back to last tree if still alive, else find nearest, else idle.
                        if (IsHarvestableStillThere(_treeCell))
```

- [ ] **Step 5: Compile check**

`AssetDatabase.Refresh()` → `Unity_ReadConsole` (Errors). Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(queue): activate queued commands when idle + defer auto-harvest to queue"
```

---

## Task 10: Shift input → enqueue vs immediate (Part 4c)

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSelectionController.cs` (right-click block ~line 65-96; new enqueue helpers)

- [ ] **Step 1: Read Shift and branch the right-click handler**

Replace the right-click block. Find:
```csharp
            // Right mouse: command selected goblins (harvest if tree, otherwise move)
            if (Mouse.current.rightButton.wasPressedThisFrame
                && _selected.Count > 0 && !IsOverUI())
            {
                Vector2 mp = Mouse.current.position.ReadValue();
                Vector3 worldTarget = _camera.ScreenToWorldPoint(
                    new Vector3(mp.x, mp.y, -_camera.transform.position.z));

                if (TryGetHarvestableAt(worldTarget, out var treeCell))
                {
                    Vector3 treeCenter = _decorationMap.CellToWorld(treeCell) + new Vector3(0.5f, 0.5f, 0f);
                    ClickFeedback.Spawn(treeCenter, new Color(0.4f, 1f, 0.4f, 0.85f));  // green = harvest
                    CommandHarvest(treeCell);
                }
                else if (TryGetConstructionAt(worldTarget, out var buildOrigin))
                {
                    Vector3 c = new(buildOrigin.x + 0.5f, buildOrigin.y + 0.5f, 0f);
                    ClickFeedback.Spawn(c, new Color(1f, 0.7f, 0.2f, 0.9f));  // orange = build
                    var workers = new List<Goblin>();
                    foreach (var g in _selected) if (IsWorker(g)) workers.Add(g);
                    if (workers.Count > 0) NetCommandIssuer.IssueBuildAssist(workers, buildOrigin);
                }
                else if (TryGetGoblinAt(worldTarget, out var enemy))
                {
                    ClickFeedback.Spawn(enemy.transform.position, new Color(1f, 0.3f, 0.3f, 0.9f)); // red = attack
                    CommandAttack(enemy);
                }
                else
                {
                    ClickFeedback.Spawn(worldTarget, new Color(1f, 1f, 1f, 0.85f));    // white = move
                    CommandFormation(worldTarget);
                }
            }
```
replace with:
```csharp
            // Right mouse: command selected goblins. Shift held → append to queue; else immediate.
            if (Mouse.current.rightButton.wasPressedThisFrame
                && _selected.Count > 0 && !IsOverUI())
            {
                Vector2 mp = Mouse.current.position.ReadValue();
                Vector3 worldTarget = _camera.ScreenToWorldPoint(
                    new Vector3(mp.x, mp.y, -_camera.transform.position.z));

                bool shift = Keyboard.current != null
                    && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

                if (TryGetHarvestableAt(worldTarget, out var treeCell))
                {
                    Vector3 treeCenter = _decorationMap.CellToWorld(treeCell) + new Vector3(0.5f, 0.5f, 0f);
                    ClickFeedback.Spawn(treeCenter, new Color(0.4f, 1f, 0.4f, 0.85f));  // green = harvest
                    if (shift) EnqueueHarvest(treeCell); else { ClearQueues(); CommandHarvest(treeCell); }
                }
                else if (TryGetConstructionAt(worldTarget, out var buildOrigin))
                {
                    Vector3 c = new(buildOrigin.x + 0.5f, buildOrigin.y + 0.5f, 0f);
                    ClickFeedback.Spawn(c, new Color(1f, 0.7f, 0.2f, 0.9f));  // orange = build
                    if (shift) EnqueueBuildAssist(buildOrigin); else { ClearQueues(); CommandBuildAssist(buildOrigin); }
                }
                else if (TryGetGoblinAt(worldTarget, out var enemy))
                {
                    ClickFeedback.Spawn(enemy.transform.position, new Color(1f, 0.3f, 0.3f, 0.9f)); // red = attack
                    if (shift) EnqueueAttack(enemy); else { ClearQueues(); CommandAttack(enemy); }
                }
                else
                {
                    ClickFeedback.Spawn(worldTarget, new Color(1f, 1f, 1f, 0.85f));    // white = move
                    if (shift) EnqueueMove(worldTarget); else { ClearQueues(); CommandFormation(worldTarget); }
                }
            }
```

Note this introduces a `CommandBuildAssist(buildOrigin)` helper (Step 2) to mirror the inline build logic, keeping the immediate-path symmetric with the others.

- [ ] **Step 2: Add the queue helpers**

Add these methods to `GoblinSelectionController` (e.g. after `CommandAttack`):
```csharp
        private void ClearQueues()
        {
            foreach (var g in _selected) if (g != null) g.ClearQueue();
        }

        private void CommandBuildAssist(Vector2Int buildOrigin)
        {
            var workers = new List<Goblin>();
            foreach (var g in _selected) if (IsWorker(g)) workers.Add(g);
            if (workers.Count > 0) NetCommandIssuer.IssueBuildAssist(workers, buildOrigin);
        }

        private void EnqueueMove(Vector3 worldCenter)
        {
            // Same square-formation offset IssueMove computes, so queued moves keep formation.
            int n = _selected.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
            int rows = Mathf.CeilToInt((float)n / cols);
            const float spacing = 1.0f;
            for (int i = 0; i < n; i++)
            {
                var g = _selected[i];
                if (g == null) continue;
                int col = i % cols, row = i / cols;
                Vector3 offset = new(
                    (col - (cols - 1) * 0.5f) * spacing,
                    (row - (rows - 1) * 0.5f) * spacing, 0f);
                g.EnqueueCommand(new Goblin.GoblinCommand
                {
                    Type = Goblin.CommandType.Move,
                    Point = worldCenter + offset,
                });
            }
        }

        private void EnqueueHarvest(Vector3Int cell)
        {
            foreach (var g in _selected)
            {
                if (!IsWorker(g)) continue;
                g.EnqueueCommand(new Goblin.GoblinCommand { Type = Goblin.CommandType.Harvest, Cell = cell });
            }
        }

        private void EnqueueBuildAssist(Vector2Int origin)
        {
            foreach (var g in _selected)
            {
                if (!IsWorker(g)) continue;
                g.EnqueueCommand(new Goblin.GoblinCommand { Type = Goblin.CommandType.BuildAssist, Origin = origin });
            }
        }

        private void EnqueueAttack(Goblin target)
        {
            foreach (var g in _selected)
            {
                if (g == null || g.AttackDamage <= 0) continue;
                g.EnqueueCommand(new Goblin.GoblinCommand { Type = Goblin.CommandType.Attack, Target = target });
            }
        }
```

- [ ] **Step 3: Compile check**

`AssetDatabase.Refresh()` → `Unity_ReadConsole` (Errors). Expected: 0 errors.

- [ ] **Step 4: Manual play-mode smoke**

Play mode:
1. Select 1 farmer, Shift+right-click 3 empty points → it walks them in order.
2. Shift-queue: right-click tree (no shift, starts harvesting) then Shift+right-click a far point → after a chop cycle/deposit it walks to the point.
3. Select a club, Shift+right-click two enemies in sequence → attacks first then second.
4. A plain right-click during a queue clears it and overrides.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSelectionController.cs
git commit -m "feat(queue): Shift+right-click appends commands; plain click replaces"
```

---

## Task 11: Final regression + smoke gate

**Files:** none (verification only)

- [ ] **Step 1: Full compile check**

`Unity_RunCommand` → `AssetDatabase.Refresh()`, `Unity_ReadConsole` (Errors). Expected: 0 errors.

- [ ] **Step 2: Run the editor test suite**

Run the 42-test editor suite via the established `TestRunnerApi` + static `ICallbacks` pattern, writing `Temp/queueux_test_results.txt`, polled with:
```bash
until [ -f Temp/queueux_test_results.txt ]; do sleep 2; done; cat Temp/queueux_test_results.txt
```
Expected: `pass=42 fail=0 skip=0`.

- [ ] **Step 3: Report the solo smoke checklist to the user**

Present this checklist for the user to run:
- Info box is larger; Workshop upgrade card costs ("200 Iron, 150 Gold" etc.) wrap inside the card with no right-edge bleed.
- 5 farmers right-clicked on one wheatfield fan out to distinct adjacent cells.
- Selecting farmers + placing a building → they auto-walk and construct it.
- Shift+right-click queues Move/Harvest/Build/Attack in order; plain right-click clears + overrides.

---

## Self-Review notes

- **Spec coverage:** Part 1a→Task1, 1b→Task2, 2→Tasks3-6, 3→Task7, 4→Tasks8-10, regression→Task11. All spec sections covered.
- **Type consistency:** `Goblin.GoblinCommand` / `Goblin.CommandType` (nested, public) defined in Task 8 and referenced in Tasks 9-10. `EnqueueCommand`/`ClearQueue` (Task 8) used in Tasks 9-10. `HarvestReservations.Reserve/Release/Clear` (Task 3) used in Tasks 4 & 6. `IsCellPassable(int,int)` is the existing private Goblin method passed as the `Func<int,int,bool>` to `Reserve` — signature matches.
- **Order dependency:** Task 4 adds `HarvestReservations.Release(this);` as the first line of `FindNextTreeOrIdle`; Task 9 Step 3 inserts the queue guard immediately after that line — consistent.

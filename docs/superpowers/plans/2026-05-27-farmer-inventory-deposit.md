# Farmer Inventory + Keep Deposit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Farmers carry chopped wood in an internal inventory (max 10), then walk to the nearest own Keep to deposit. Fixes the existing MP wood-double-credit bug as a side effect by gating the chop tick to the local owner.

**Architecture:** Per-Goblin `_carriedWood` field + new `WalkingToDeposit` FSM state. `HitTree` no longer credits `ResourceBank` directly — instead increments inventory. When full or the tree dies, the farmer transitions to `WalkingToDeposit`, sends a `CmdMove` wire-only (no local state change), walks to the keep edge, deposits, then resumes the previous tree (if alive) or finds a new one. Carry-count visualized via a new `GoblinCarryText` component.

**Tech Stack:** Unity 6 / C# / Steamworks.NET / RTSCL.World.Unity asmdef

**Spec:** `docs/superpowers/specs/2026-05-27-farmer-inventory-deposit-design.md`

---

## File Structure

### Modified files

| File | Change |
|---|---|
| `Assets/Scripts/World/Unity/BuildingPlacer.cs` | Add public method `TryFindNearestBuildingByName(string name, ulong owner, Vector2Int from, out Vector2Int origin)` |
| `Assets/Scripts/World/Unity/Goblin.cs` | Add `_carriedWood`, `MaxCarriedWood`, new `WalkingToDeposit` enum value, gate harvest tick to local owner, modify `HitTree` (no ResourceBank), add `TryStartDepositRun`, add `SendMoveWireOnly`, add `WalkingToDeposit` case in switch + GoblinCarryText.AttachTo in Init |

### New files

| File | Purpose |
|---|---|
| `Assets/Scripts/World/Unity/GoblinCarryText.cs` | Per-Farmer floating "X" text above HealthBar showing carried wood count. Auto-attached at `Goblin.Init`. |

---

## Task 1: BuildingPlacer.TryFindNearestBuildingByName

**Files:**
- Modify: `Assets/Scripts/World/Unity/BuildingPlacer.cs`

- [ ] **Step 1: Add the helper method**

After the existing `TryGetBuildingOwner` method, add:

```csharp
/// <summary>Find the nearest building whose underlying BuildingDefinition asset is named `name`
/// AND that is owned by `owner`. Returns false if none exists.</summary>
public bool TryFindNearestBuildingByName(string name, ulong owner, Vector2Int from, out Vector2Int origin)
{
    origin = default;
    int bestDistSq = int.MaxValue;
    bool found = false;
    foreach (var kvp in _cellOwners)
    {
        var def = kvp.Value;
        if (def == null || def.name != name) continue;
        if (!_cellToOwner.TryGetValue(kvp.Key, out ulong cellOwner) || cellOwner != owner) continue;
        if (!_cellToOrigin.TryGetValue(kvp.Key, out var thisOrigin)) continue;
        int dx = thisOrigin.x - from.x;
        int dy = thisOrigin.y - from.y;
        int d = dx * dx + dy * dy;
        if (d < bestDistSq)
        {
            bestDistSq = d;
            origin = thisOrigin;
            found = true;
        }
    }
    return found;
}
```

Note: iterating `_cellOwners` walks every footprint cell. Multiple cells of the same building return the same `origin` via the `_cellToOrigin` lookup — duplicates don't break correctness (same origin, same distance), they just cost a few cycles. The dictionary is small (< 100 building cells in practice), so this is fine.

- [ ] **Step 2: Refresh + compile check via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `Unity_ReadConsole` Types=["Error"]. Expect 0.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildingPlacer.cs
git commit -m "feat(harvest): BuildingPlacer.TryFindNearestBuildingByName for keep lookup"
```

---

## Task 2: Goblin inventory + deposit FSM

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

This is the meat of the feature. All changes are in one file and need to compile together — single task / single commit.

- [ ] **Step 1: Add `WalkingToDeposit` to the State enum**

Find the existing enum (around line 48):

```csharp
private enum State { Idle, MovingToPoint, MovingToTree, Harvesting, MovingToBuild, Building, MovingToAttack, Attacking, Dying }
```

Replace with (adding `WalkingToDeposit` between `Harvesting` and `MovingToBuild`):

```csharp
private enum State { Idle, MovingToPoint, MovingToTree, Harvesting, WalkingToDeposit, MovingToBuild, Building, MovingToAttack, Attacking, Dying }
```

- [ ] **Step 2: Add inventory field + constant**

Find the `WoodPerHit` constant (line 85):

```csharp
private const int WoodPerHit = 1;
```

After it, add:

```csharp
private const int MaxCarriedWood = 10;
public int CarriedWood { get; private set; }
```

`CarriedWood` is public read so `GoblinCarryText` (Task 3) can poll it.

- [ ] **Step 3: Modify HitTree to fill inventory instead of bank**

Find `HitTree(Vector3Int cell)` (around line 363) and replace:

```csharp
private void HitTree(Vector3Int cell)
{
    var tile = _decorationMap.GetTile(cell) as UnityEngine.Tilemaps.Tile;
    var sprite = tile != null ? tile.sprite : null;

    int remaining = TreeHP.Hit(cell, 1);
    ResourceBank.AddWood(WoodPerHit);
    StartHitAnim(cell);
    TreeHitEffect.Spawn(_decorationMap, cell, sprite);

    if (remaining <= 0)
    {
        _decorationMap.SetTile(cell, null);
        // FindNext on next frame via the IsTreeStillThere check
    }
}
```

With (no `ResourceBank.AddWood`, inventory increment instead, return-bool to signal tree-destroyed):

```csharp
/// <summary>Apply one chop to the given tree cell. Returns true if the tree was destroyed by this hit.</summary>
private bool HitTree(Vector3Int cell)
{
    var tile = _decorationMap.GetTile(cell) as UnityEngine.Tilemaps.Tile;
    var sprite = tile != null ? tile.sprite : null;

    int remaining = TreeHP.Hit(cell, 1);
    CarriedWood = Mathf.Min(MaxCarriedWood, CarriedWood + WoodPerHit);
    StartHitAnim(cell);
    TreeHitEffect.Spawn(_decorationMap, cell, sprite);

    if (remaining <= 0)
    {
        _decorationMap.SetTile(cell, null);
        return true;
    }
    return false;
}
```

- [ ] **Step 4: Gate the Harvesting case to local owner + dispatch deposit run on cap/destroy**

Find the `case State.Harvesting:` block (around lines 256-272):

```csharp
case State.Harvesting:
{
    ShowIdleFrame();
    if (!IsTreeStillThere(_treeCell))
    {
        ResetHitAnim();
        FindNextTreeOrIdle();
        break;
    }
    _harvestTimer += Time.deltaTime;
    if (_harvestTimer >= _chopTickDuration)
    {
        _harvestTimer = 0f;
        HitTree(_treeCell);
    }
    break;
}
```

Replace with (gates entire branch + handles cap/destroy):

```csharp
case State.Harvesting:
{
    ShowIdleFrame();
    if (!IsLocalOwner) break;     // remote clients skip the chop tick — owner authorities harvest

    if (!IsTreeStillThere(_treeCell))
    {
        ResetHitAnim();
        if (CarriedWood > 0) TryStartDepositRun();
        else FindNextTreeOrIdle();
        break;
    }
    _harvestTimer += Time.deltaTime;
    if (_harvestTimer >= _chopTickDuration)
    {
        _harvestTimer = 0f;
        bool destroyed = HitTree(_treeCell);
        if (CarriedWood >= MaxCarriedWood || destroyed)
            TryStartDepositRun();
    }
    break;
}
```

- [ ] **Step 5: Add TryStartDepositRun + SendMoveWireOnly helpers**

Place these near `HitTree` (e.g., right after the `HitTree` method):

```csharp
/// <summary>Find the owner's nearest Keep, set move target to its edge, transition to WalkingToDeposit,
/// and broadcast a CmdMove so remotes mirror the walk. Falls back to Idle (inventory preserved) if no
/// Keep exists.</summary>
private void TryStartDepositRun()
{
    if (NetCommandApplier.Placer == null)
    {
        _state = State.Idle;
        return;
    }
    var here = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));
    if (!NetCommandApplier.Placer.TryFindNearestBuildingByName("Keep_0", Owner, here, out var keepOrigin))
    {
        _state = State.Idle;
        return;
    }

    // Pick an adjacent passable cell next to the keep's footprint (approximate — use the cell next to origin).
    // Building footprint is small (Keep is 2x2). Standing one cell to the side of the origin works.
    Vector3 target = new Vector3(keepOrigin.x - 0.5f, keepOrigin.y + 0.5f, 0f);

    ResetHitAnim();
    _moveTarget = target;
    _state = State.WalkingToDeposit;
    SendMoveWireOnly(target);
}

/// <summary>Broadcast a single-unit CmdMove without touching local state (the owner's FSM state
/// is already set; remotes apply via the stock ApplyMove → SetMoveCommand path).</summary>
private void SendMoveWireOnly(Vector3 target)
{
    var wire = new System.Collections.Generic.List<NetWireFormat.WireNetIdLocal>(1)
    {
        new NetWireFormat.WireNetIdLocal(NetId.Owner, NetId.LocalIndex)
    };
    NetCommandBridge.Send(NetWireFormat.PackCmdMove(wire, target.x, target.y));
}
```

- [ ] **Step 6: Add the WalkingToDeposit case to the Update switch**

Insert immediately AFTER the `case State.Harvesting:` block (which ends with `break;`), BEFORE `case State.MovingToBuild:`:

```csharp
case State.WalkingToDeposit:
{
    if (!IsLocalOwner) break;     // remotes are in MovingToPoint via ApplyMove; their walk handles itself
    if (StepToward(_moveTarget))
    {
        // Arrived at keep — deposit.
        ResourceBank.AddWood(CarriedWood);
        CarriedWood = 0;

        // Resume: walk back to last tree if still alive, else find nearest, else idle.
        if (IsTreeStillThere(_treeCell))
        {
            _moveTarget = FindAdjacentStandingSpot(_treeCell);
            _state = State.MovingToTree;
            SendMoveWireOnly(_moveTarget);
        }
        else
        {
            FindNextTreeOrIdle();
            // If FindNextTreeOrIdle picked a tree (state changed to MovingToTree), mirror the move.
            if (_state == State.MovingToTree) SendMoveWireOnly(_moveTarget);
        }
    }
    break;
}
```

- [ ] **Step 7: Attach GoblinCarryText in Init**

Task 3 adds the `GoblinCarryText` component. To avoid a broken interim commit, declare the attach line now as a forward reference. Find the existing `GoblinHealthBar.AttachTo(this)` line in `Init`:

```csharp
BuildSelectionRing();
GoblinHealthBar.AttachTo(this);
```

Replace with:

```csharp
BuildSelectionRing();
GoblinHealthBar.AttachTo(this);
GoblinCarryText.AttachTo(this);
```

This will cause a compile error until Task 3 is done. Do NOT commit Task 2 yet — hold the changes in the working tree.

- [ ] **Step 8: Hold off commit until Task 3 lands**

The single-commit boundary spans Goblin.cs + the new GoblinCarryText.cs. Task 3 finishes both with one commit.

---

## Task 3: GoblinCarryText visual + commit Task 2+3

**Files:**
- Create: `Assets/Scripts/World/Unity/GoblinCarryText.cs`
- (Task 2's Goblin.cs changes are still in the working tree from Task 2.)

- [ ] **Step 1: Create GoblinCarryText.cs**

Write `Assets/Scripts/World/Unity/GoblinCarryText.cs`:

```csharp
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Floating "X" text above a Farmer showing carried wood count.
    /// Visible only when CarriedWood > 0 AND the Goblin is owned locally (or solo).</summary>
    public sealed class GoblinCarryText : MonoBehaviour
    {
        private const float YOffset = 1.05f;
        private const int FontSize = 28;

        private Goblin _goblin;
        private TextMesh _label;
        private MeshRenderer _renderer;

        public static GoblinCarryText AttachTo(Goblin owner)
        {
            var go = new GameObject("CarryText");
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = new Vector3(0f, YOffset, 0f);
            var ct = go.AddComponent<GoblinCarryText>();
            ct._goblin = owner;
            return ct;
        }

        private void Awake()
        {
            _label = gameObject.AddComponent<TextMesh>();
            _label.text = "";
            _label.fontSize = FontSize;
            _label.characterSize = 0.04f;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = new Color(0.95f, 0.85f, 0.45f);
            _renderer = GetComponent<MeshRenderer>();
            _renderer.sortingOrder = 31;
            _renderer.enabled = false;
        }

        private void LateUpdate()
        {
            if (_goblin == null) { _renderer.enabled = false; return; }
            bool isLocal = _goblin.Owner == WorldStartContext.LocalPlayer || _goblin.Owner == 0UL;
            bool show = isLocal && _goblin.CarriedWood > 0;
            if (_renderer.enabled != show) _renderer.enabled = show;
            if (show) _label.text = _goblin.CarriedWood.ToString();
        }
    }
}
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `Unity_ReadConsole` Types=["Error"]. Expect 0 (Task 2's Goblin.cs changes + this new file should now compile together).

- [ ] **Step 3: Commit Tasks 2 + 3 together**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs Assets/Scripts/World/Unity/GoblinCarryText.cs Assets/Scripts/World/Unity/GoblinCarryText.cs.meta
git commit -m "feat(harvest): farmer inventory + keep deposit + carry-count visual"
```

---

## Task 4: Final compile + smoke verification

**Files:** none changed.

- [ ] **Step 1: Full refresh + console clear via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `Unity_ReadConsole` Types=["Error"] + FilterText="CS". Expect 0.

- [ ] **Step 2: Run RTSCL.World.Tests**

Use the test-runner pattern (TestRunnerApi + Temp file callback). Expect 36/36 pass.

- [ ] **Step 3: Solo smoke (user manually)**

User runs Play Solo:
- Farmer right-clicks a tree → walks, chops
- Carry-text "1", "2", ... appears over the farmer's head
- At "10": farmer walks toward the Keep
- On arrival: wood UI jumps by 10, carry-text disappears
- Farmer walks back to the same tree (if still alive) or to nearest free tree
- Repeat loop until tree gone or harvest cancelled
- Tree destroyed mid-load (e.g. at 4): farmer walks to Keep with 4, deposits, finds new tree

- [ ] **Step 4: No commit unless a fix was needed**

If smoke passes: no further commits.

---

## Plan Self-Review Notes

- **Spec coverage:**
  - Per-goblin inventory (Task 2 step 2)
  - WalkingToDeposit state (Task 2 step 1)
  - `HitTree` no longer bank-credits (Task 2 step 3)
  - Owner-only harvest tick (Task 2 step 4)
  - Cap-or-destroy → deposit run (Task 2 step 4 + 5)
  - Keep lookup (Task 1)
  - Deposit + resume + IssueMove-style broadcast (Task 2 step 5+6)
  - Carry visual (Task 3)
  - Solo fallback automatic via `NetCommandBridge.Send` null-safety
  - MP gating + `SendMoveWireOnly` covers the visual sync requirement

- **Placeholder scan:** no TBDs / TODOs / vague items. All code blocks complete.

- **Type consistency:**
  - `CarriedWood` (public int property) used in Task 2 (definition) and Task 3 (read by GoblinCarryText)
  - `MaxCarriedWood = 10` constant used in Task 2 only
  - `IsLocalOwner` (existing private prop) used in Task 2
  - `Owner` (existing public ulong) used in Task 2 + Task 3
  - `NetCommandApplier.Placer` (existing static field) used in Task 2
  - `NetCommandBridge.Send` (existing) used in Task 2
  - `NetWireFormat.WireNetIdLocal` + `PackCmdMove` (existing) used in Task 2
  - `WorldStartContext.LocalPlayer` (existing) used in Task 3
  - `BuildingPlacer.TryFindNearestBuildingByName` (new in Task 1) used in Task 2

- **Compile-break window:** Task 2 leaves a broken state (Goblin.cs references `GoblinCarryText.AttachTo` which doesn't exist yet). Task 3 creates the type and commits both together. No interim commit.

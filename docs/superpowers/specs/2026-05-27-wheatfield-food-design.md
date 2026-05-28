# Wheatfield Food Harvest + Info-Panel Carry Display — Design

**Status:** Spec
**Date:** 2026-05-27

## Goal

Farmers can harvest Wheatfields for a new "Food" resource (500 food per wheatfield, 1 food per chop tick). Trees and wheatfields both burst visibly when destroyed. The per-Farmer carry count moves from an overhead text to the ObjectInspector info panel when a single Farmer is selected.

## Food Resource

- New `int Food` + `Action<int> OnFoodChanged` + `void AddFood(int)` on `ResourceBank`
- `ResourceBank.Reset()` clears Food to 0 too
- `ResourceUI` gains a `_foodLabel` SerializeField + `UpdateFood` subscriber
- SampleScene adds a Food UI label parallel to the Wood label

## Wheatfields as Harvest Targets

**Tile detection.** Wheatfields are decoration tiles whose tile-name starts with `Wheatfield_`. Two helpers:
- `Goblin.IsWheatfieldTile(string tileName)` — `tileName.StartsWith("Wheatfield_")`
- Add `Goblin.IsHarvestable(string tileName)` returning `IsTreeTile(tileName) || IsWheatfieldTile(tileName)` (just a convenience wrapper used by selection code)

**Selection.** `GoblinSelectionController.TryGetTreeAt` is renamed to `TryGetHarvestableAt` (Vector3 → Vector3Int) and uses `Goblin.IsHarvestable`. Same call site `CommandHarvest`. The Goblin's harvest FSM handles tile-type at chop time — no need for a separate command type.

**Find-Nearby.** `GoblinSelectionController.FindNearbyTrees` is renamed to `FindNearbyHarvestables` and accepts both kinds. Used by CommandHarvest to dispatch workers around a clicked harvestable.

## Per-Cell HP with Variable Max

`TreeHP.Hit` gains an optional `maxHp` parameter:

```csharp
public static int Hit(Vector3Int cell, int amount = 1, int maxHp = MaxHP)
{
    int next = Mathf.Max(0, GetHP(cell, maxHp) - amount);
    if (next > 0) _hp[cell] = next;
    else _hp.Remove(cell);
    return next;
}
public static int GetHP(Vector3Int cell, int maxHp = MaxHP) =>
    _hp.TryGetValue(cell, out var hp) ? hp : maxHp;
```

Trees use the default (`MaxHP = 50`). Wheatfields pass `maxHp: 500`.

Name stays `TreeHP` (no rename — affects too many files; not worth the churn).

## Goblin Inventory + Mutual Exclusion

**Two carry slots:**
- `CarriedWood` (int, 0..10) — existing
- `CarriedFood` (int, 0..10) — new

Caps stay at 10 for both. (User can raise later if 50 trips per wheatfield is too tedious.)

**One resource type at a time.** A Farmer cannot simultaneously hold wood and food.

If `SetHarvestCommand(cell)` is called with a tile type opposite to a non-zero carry:
- Mutate `_treeCell` to the new target (so the post-deposit resume in `WalkingToDeposit` lands at it)
- Transition to `WalkingToDeposit` instead of `MovingToTree`
- Walk to keep, deposit, then resume to `_treeCell`

If carry is zero or matches the new tile type: existing behavior (walk to harvestable, chop).

## HitHarvestable

`HitTree(cell)` is renamed to `HitHarvestable(cell)` and branches on tile type:

```csharp
private bool HitHarvestable(Vector3Int cell)
{
    var tile = _decorationMap.GetTile(cell) as UnityEngine.Tilemaps.Tile;
    var sprite = tile != null ? tile.sprite : null;
    string tileName = tile != null ? tile.name : "";

    bool isWheatfield = IsWheatfieldTile(tileName);
    int maxHp = isWheatfield ? 500 : TreeHP.MaxHP;
    int remaining = TreeHP.Hit(cell, 1, maxHp);

    if (isWheatfield) CarriedFood = Mathf.Min(MaxCarriedWood, CarriedFood + 1);
    else              CarriedWood = Mathf.Min(MaxCarriedWood, CarriedWood + 1);

    StartHitAnim(cell);
    TreeHitEffect.Spawn(_decorationMap, cell, sprite);

    if (remaining <= 0)
    {
        _decorationMap.SetTile(cell, null);
        TreeHitEffect.SpawnBurst(_decorationMap, cell, sprite);
        return true;
    }
    return false;
}
```

The cap-or-destroyed deposit-trigger in `State.Harvesting` becomes:
```csharp
if (CarriedWood >= MaxCarriedWood || CarriedFood >= MaxCarriedWood || destroyed)
    TryStartDepositRun();
```

## Destruction Burst

`TreeHitEffect.SpawnBurst(Tilemap deco, Vector3Int cell, Sprite sprite)` — new method that spawns ~4× the particles of `Spawn`, with a larger spread radius and slightly higher velocity. Applied to both trees and wheatfields on destruction.

Implementation reuses the existing `Spawn` particle code with a different count + radius constant.

## Deposit Logic

`State.WalkingToDeposit` arrival deposits both slots:
```csharp
if (CarriedWood > 0) ResourceBank.AddWood(CarriedWood);
if (CarriedFood > 0) ResourceBank.AddFood(CarriedFood);
CarriedWood = 0;
CarriedFood = 0;
```

Resume logic unchanged: if `_treeCell` still references a live harvestable → walk back; else `FindNextTreeOrIdle` (which currently only searches trees — keep that scope; finding wheatfields auto-after deposit is out of scope).

`TryStartDepositRun` unchanged (still requires NetCommandApplier.Placer + Keep_0 by owner).

## Carry Display Move

**Remove overhead text:**
- Delete `Assets/Scripts/World/Unity/GoblinCarryText.cs` + `.meta`
- Remove `GoblinCarryText.AttachTo(this)` line from `Goblin.Init`

**Add to ObjectInspector:**
- When `_selectionController.Selection.Count == 1` AND that selection is a Farmer AND (`CarriedWood > 0` OR `CarriedFood > 0`):
  - Append a new line to the description: `"Carrying: 5 wood"` or `"Carrying: 8 food"`
- Update logic: ObjectInspector polls in its own `Update()` while in `SelKind.Goblins` with a single Farmer selected. Caches last-rendered count; only calls `SetHeader` again when it changes.

This keeps the change local to ObjectInspector — no new event on Goblin.

## File Plan

### Modified

| File | Change |
|---|---|
| `ResourceBank.cs` | Add `Food`, `AddFood`, `OnFoodChanged`. Reset clears Food. |
| `ResourceUI.cs` | Add `_foodLabel` + UpdateFood subscriber. |
| `TreeHP.cs` | Add optional `maxHp` param to `Hit` + `GetHP`. |
| `Goblin.cs` | `CarriedFood` field, `IsWheatfieldTile`, `IsHarvestable`, `HitTree` → `HitHarvestable` (branch on tile kind + destruction burst), mutual-exclusion in `SetHarvestCommand`, deposit both slots, remove `GoblinCarryText.AttachTo`. |
| `GoblinSelectionController.cs` | `TryGetTreeAt` → `TryGetHarvestableAt`, `FindNearbyTrees` → `FindNearbyHarvestables`. |
| `ObjectInspector.cs` | Carry-display in description for single-Farmer selection. Poll in Update; cache last-rendered value. |
| `TreeHitEffect.cs` | New `SpawnBurst` method (bigger version of `Spawn`). |
| `Assets/Scenes/SampleScene.unity` | Add Food UI label + wire to ResourceUI._foodLabel. |

### Deleted

| File | Reason |
|---|---|
| `GoblinCarryText.cs` + `.meta` | Replaced by info-panel display. |

## Multiplayer Sync

No new wire messages.
- `CarriedFood` is owner-local (matches `CarriedWood`)
- `ResourceBank.Food` is owner-local
- Existing `CmdHarvest` covers wheatfield targets identically (cell index doesn't care what's on it)
- Deposit walk-to-keep already uses `SendMoveWireOnly` via the existing FSM

## Solo Fallback

Identical to today: `NetCommandBridge.Send` is null-safe, all FSM logic runs locally with `IsLocalOwner` defaulting true.

## Testing

- **Edit-mode tests:** none required (existing 36 tests still pass).
- **Solo smoke:**
  - Right-click a Wheatfield → Farmer walks to it
  - Per-tick "Food: X" goes up in ObjectInspector description (no overhead text)
  - At 10 carried: walk to Keep, deposit, walk back to same wheatfield
  - Repeat until wheatfield destroyed → big burst visible
  - Tree destruction also shows burst (same effect)
  - Right-click a tree while carrying food → Farmer walks to Keep first, deposits, then walks to tree
- **2-client smoke:** wheatfields harvestable by both players; remote sees the deposit-walk (via CmdMove); food UI only increases for the owning player.

## Risks

| Risk | Mitigation |
|---|---|
| Wheatfield with 500 HP × 50 trips = tedious | Acceptable for MVP. Player feedback decides if cap should go to 50 or 100. |
| Polling carry count every frame in Inspector | Cheap (single field read + string compare per tick during Farmer selection). |
| `FindNextTreeOrIdle` doesn't auto-target wheatfields | Intentional scope cut. Trees are the natural fallback; players manually click a wheatfield to start food chain. |
| `TreeHP.Hit` callers without `maxHp` arg | Default param keeps existing call sites working. |
| Mutual-exclusion edge case: SetHarvestCommand on EMPTY tile | Caller already filters via TryGetHarvestableAt — never happens. |

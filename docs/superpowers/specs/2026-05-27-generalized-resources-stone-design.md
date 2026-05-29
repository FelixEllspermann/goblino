# Generalized Resources + Stone Mining (Phase 1) — Design

**Status:** Spec
**Date:** 2026-05-27

## Goal

Refactor the Wood/Food resource handling into a generalized `ResourceKind`-keyed system, then make Rocks harvestable for a new `Stone` resource. This is **Phase 1** of a two-phase mining feature; Phase 2 (Gold/Iron/Crystal ore deposits) builds on this generalized base and is a separate spec.

## Context

- `ResourceBank` currently has hardcoded `Wood` + `Food` int fields with `AddWood`/`AddFood` + `OnWoodChanged`/`OnFoodChanged` events.
- `Goblin` carries two separate slots `CarriedWood` + `CarriedFood` (cap 10 each), with mutual-exclusion (one resource type at a time) already enforced in `SetHarvestCommand`.
- Harvestables are decoration tiles: Trees (→Wood), Wheatfield (→Food). `Rocks_*` decoration tiles already spawn across many biomes but are inert.
- `ResourceUI` shows fixed Wood + Food labels with tree/wheatfield icons + a Population label.
- World-gen `ResourceCluster`/`ResourceType` (Stone/Food) only feed the reserved-mask + reachability; they are NOT painted. This Phase does not touch them.

## Generalized Resource System

### ResourceKind enum

New `Assets/Scripts/World/Unity/ResourceKind.cs`:

```csharp
namespace RTSCL.World.Unity
{
    public enum ResourceKind { Wood = 0, Food = 1, Stone = 2, Gold = 3, Iron = 4, Crystal = 5 }
}
```

Gold/Iron/Crystal are defined now (Phase 2 uses them) but only Wood/Food/Stone are exercised in Phase 1.

### ResourceBank refactor

`ResourceBank` becomes array-backed with a generic API, keeping `Wood`/`Food` convenience accessors to minimize call-site churn:

```csharp
public static class ResourceBank
{
    private static readonly int[] _amounts = new int[6]; // sized to ResourceKind count

    public static event System.Action<ResourceKind, int> OnChanged;

    public static int Get(ResourceKind k) => _amounts[(int)k];

    public static void Add(ResourceKind k, int amount)
    {
        _amounts[(int)k] += amount;
        OnChanged?.Invoke(k, _amounts[(int)k]);
    }

    public static void Reset()
    {
        for (int i = 0; i < _amounts.Length; i++) _amounts[i] = 0;
        for (int i = 0; i < _amounts.Length; i++) OnChanged?.Invoke((ResourceKind)i, 0);
    }

    // Convenience for existing call sites.
    public static int Wood => Get(ResourceKind.Wood);
    public static int Food => Get(ResourceKind.Food);
    public static void AddWood(int amount) => Add(ResourceKind.Wood, amount);
    public static void AddFood(int amount) => Add(ResourceKind.Food, amount);
}
```

The old `OnWoodChanged`/`OnFoodChanged` events are removed. Subscribers migrate to `OnChanged` (which passes the kind + new value). Subscribers that only care about one kind filter on the kind argument.

**Call sites to migrate:**
- `ObjectInspector.Start`: `OnWoodChanged += _ => Refresh()` and `OnFoodChanged += _ => Refresh()` → single `OnChanged += (_, __) => Refresh()`.
- `ResourceUI`: rebuilt (see UI section) — subscribes to `OnChanged`.
- `BuildingPlacer`, `Goblin` use `ResourceBank.Wood`/`AddWood`/`AddFood`/`Add` — the convenience accessors keep these working; the Goblin deposit path switches to the generic `Add(kind, amount)`.

### Goblin carry refactor

Replace `CarriedWood` + `CarriedFood` with a single typed slot:

```csharp
public ResourceKind CarriedKind { get; private set; }
public int CarriedAmount { get; private set; }
private const int MaxCarried = 10;   // replaces MaxCarriedWood
```

- `HitHarvestable(cell)` determines the tile's `ResourceKind`, and:
  - If `CarriedAmount > 0 && CarriedKind != newKind` — should not happen mid-harvest because `SetHarvestCommand` already routes a type-switch to a deposit run first; defensively, treat as deposit trigger.
  - Else `CarriedKind = newKind; CarriedAmount = Mathf.Min(MaxCarried, CarriedAmount + 1)`.
- Deposit (in `WalkingToDeposit` arrival): `if (CarriedAmount > 0) ResourceBank.Add(CarriedKind, CarriedAmount); CarriedAmount = 0;`
- Cap check in `Harvesting`: `if (CarriedAmount >= MaxCarried || destroyed) TryStartDepositRun();`

### Tile → ResourceKind mapping

Add to `Goblin`:

```csharp
public static bool IsRockTile(string tileName) => tileName.StartsWith("Rocks_");

public static bool IsHarvestable(string tileName) =>
    IsTreeTile(tileName) || IsWheatfieldTile(tileName) || IsRockTile(tileName);

// Used by HitHarvestable to classify the struck tile.
private static ResourceKind KindOf(string tileName)
{
    if (IsWheatfieldTile(tileName)) return ResourceKind.Food;
    if (IsRockTile(tileName)) return ResourceKind.Stone;
    return ResourceKind.Wood; // trees + default
}

private static int MaxHpFor(string tileName)
{
    if (IsWheatfieldTile(tileName)) return 500;
    if (IsRockTile(tileName)) return 100;
    return TreeHP.MaxHP; // 50 for trees
}
```

`HitHarvestable` uses `KindOf` + `MaxHpFor` instead of the current `isWheatfield` bool branch.

### SetHarvestCommand mutual exclusion (generalized)

The current check compares "wheat vs wood". Generalize to compare the target tile's kind against `CarriedKind`:

```csharp
var tile = _decorationMap != null ? _decorationMap.GetTile(treeCell) : null;
if (tile != null && CarriedAmount > 0)
{
    var targetKind = KindOf(tile.name);
    if (targetKind != CarriedKind)
    {
        _treeCell = treeCell;
        TryStartDepositRun();
        return;
    }
}
```

## Stone Harvest

With Rocks in `IsHarvestable` + `KindOf` mapping to `Stone` + HP 100, the existing harvest FSM handles stone identically to wood:
- Right-click a Rock → Farmer walks, chops (1 stone/tick), carry shows "Carrying: N stone".
- At cap 10 or rock destroyed → walk to keep, deposit.
- Rock destroyed → `TreeHitEffect.SpawnBurst` (existing).

Rocks already appear in many biome decoration pools, so Stone is a common resource immediately.

## UI Refactor

`ResourceUI` is rebuilt to be data-driven over `ResourceKind`:

- New serialized mapping: a list of `(ResourceKind kind, Sprite icon)` entries (Inspector-wired), e.g. Wood=Trees_2, Food=Wheatfield_0, Stone=Rocks_6. Phase 2 adds Gold/Iron/Crystal entries.
- On `OnEnable`, for each configured entry, build (or reuse a pre-wired) counter: a small icon Image + a count Text, laid out left-to-right in a container.
- Subscribe to `ResourceBank.OnChanged`; on change, update the matching kind's count text. Kinds without a configured entry are ignored (so Phase 1 can ship with just Wood/Food/Stone visible).
- Population counter stays as-is (separate, not a ResourceKind).

To keep scene wiring simple and avoid a large dynamic-UI build, the implementation builds the per-kind counters at runtime under a `_countersRoot` RectTransform, cloning a style from constants (icon size, font, spacing). The icon sprites come from the serialized `ResourceKind→Sprite` map.

### ObjectInspector carry display

`UpdateCarryUI` switches from wood/food-specific text to generic:

```csharp
if (sel.Count == 1 && sel[0] != null && sel[0].Kind == "FarmerGoblin" && sel[0].CarriedAmount > 0)
    newCarry = $"Carrying: {sel[0].CarriedAmount} {sel[0].CarriedKind.ToString().ToLowerInvariant()}";
```

## File Plan

### New
| File | Purpose |
|---|---|
| `Assets/Scripts/World/Unity/ResourceKind.cs` | enum |

### Modified
| File | Change |
|---|---|
| `ResourceBank.cs` | array-backed + Get/Add/OnChanged + Wood/Food convenience |
| `Goblin.cs` | CarriedKind/CarriedAmount slot, IsRockTile, IsHarvestable += rocks, KindOf, MaxHpFor, HitHarvestable rewrite, SetHarvestCommand generalized, deposit via Add(kind) |
| `ObjectInspector.cs` | OnChanged subscription; generic carry display |
| `ResourceUI.cs` | data-driven counters over ResourceKind→Sprite map + OnChanged |
| `Assets/Scenes/SampleScene.unity` | ResourceUI counters root + kind→sprite wiring (Wood/Food/Stone) |

## MP

Carry slot + ResourceBank are owner-local (unchanged ownership model). Stone behaves like Wood/Food over the network: `CmdMove` covers deposit walks; no new wire messages. Owner-only harvest tick already gates stone mining.

## Testing

- **Edit-mode:** existing 36 tests must still pass (ResourceBank/Goblin changes are MonoBehaviour/static; no new pure-logic tests required, but verify nothing references removed `OnWoodChanged`/`OnFoodChanged`).
- **Solo smoke:**
  - Wood/Food/Stone counters visible with icons.
  - Chop tree → Wood rises on deposit; chop wheatfield → Food; chop rock → Stone.
  - Carry display shows "Carrying: N stone" etc.
  - Carrying wood + right-click rock → deposit wood first, then mine stone (mutual exclusion).
  - Rock destroyed → burst, Farmer finds next or idles.

## Risks
| Risk | Mitigation |
|---|---|
| Removing OnWoodChanged/OnFoodChanged breaks a subscriber | Grep all usages; migrate to OnChanged. Convenience Wood/Food props keep value-reads working. |
| Dynamic UI build complexity | Build counters from a simple style + sprite map; reuse existing label style constants. |
| FindNextTreeOrIdle only finds trees | Existing behavior (out of scope); rocks/wheat require manual click. Acceptable. |
| ResourceBank array index vs enum drift | enum values are explicit 0..5; array sized to 6. |

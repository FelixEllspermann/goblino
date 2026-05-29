# Food Cost for Units + Resource UI Icons — Design

**Status:** Spec
**Date:** 2026-05-27

## Goal

Switch Farmer training cost from Wood to Food. Add Food cost to Club (alongside existing Wood). Add resource sprite icons (tree + wheatfield) in the top UI before each resource count.

## Cost System

### GoblinUnitDefinition

Add new field next to `WoodCost`:

```csharp
public int FoodCost = 0;
```

Default 0 keeps existing assets working without explicit migration.

### Asset Updates

| Asset | Before | After |
|---|---|---|
| `FarmerGoblin.asset` | WoodCost: 50, FoodCost: (n/a) | WoodCost: 0, FoodCost: 50 |
| `ClubGoblin.asset` | WoodCost: 150, FoodCost: (n/a) | WoodCost: 150, FoodCost: 100 |

PopulationCost unchanged (Farmer 1, Club 3).

### Affordability Check

`ObjectInspector.OnUnitClicked` must guard on both resources:

```csharp
if (ResourceBank.Wood < unit.WoodCost) return;
if (ResourceBank.Food < unit.FoodCost) return;
if (!PopulationManager.CanAfford(unit.PopulationCost)) return;
if (unit.WoodCost > 0) ResourceBank.AddWood(-unit.WoodCost);
if (unit.FoodCost > 0) ResourceBank.AddFood(-unit.FoodCost);
```

Building placement (`BuildingPlacer`) and Upgrade purchase (`OnUpgradeClicked`) remain Wood-only — out of scope.

### Cost Display on Cards

`ObjectInspector.CreateCard` currently shows `"X Wood"` from a single `woodCost` int. For unit cards, the cost line must show both resources when present:

- Only wood (`WoodCost > 0 && FoodCost == 0`): `"150 Wood"`
- Only food (`WoodCost == 0 && FoodCost > 0`): `"50 Food"`
- Both (`WoodCost > 0 && FoodCost > 0`): `"150 Wood, 100 Food"`

Implementation: `BuildUnitCards` formats the string with `FormatUnitCost(woodCost, foodCost)` helper before calling `CreateCard`. `CreateCard` keeps its existing single-string `costText` parameter — no signature change.

### CardRefs Extension

Add `public int FoodCost;` next to existing `public int WoodCost;`.

`BuildUnitCards` sets both `c.WoodCost = u.WoodCost` and `c.FoodCost = u.FoodCost`.

### Refresh Logic

`ObjectInspector.Refresh` per-card loop adds a Food affordability check:

```csharp
bool affordable = ResourceBank.Wood >= card.WoodCost
               && ResourceBank.Food >= card.FoodCost;
```

Existing `popOk` + `alreadyOwned` + `busy` gates stay identical.

### Refresh Event Subscription

ObjectInspector's `Start` already subscribes to `ResourceBank.OnWoodChanged`. Add:

```csharp
ResourceBank.OnFoodChanged += _ => Refresh();
```

(Symmetric pattern — keeps the unit cards live as Food changes too.)

## Resource UI Icons

### Sprites

| Resource | Sprite Path | Sub-sprite |
|---|---|---|
| Wood | `Assets/MiniWorldSprites/Nature/Trees.png` | `Trees_0` |
| Food | `Assets/MiniWorldSprites/Nature/Wheatfield.png` | `Wheatfield_0` |

### ResourceUI Field Additions

```csharp
[SerializeField] private Image _woodIcon;
[SerializeField] private Image _foodIcon;
```

No runtime logic change — icons are static. Sprite is set at scene-design time via Inspector wiring (Step in implementation plan). The fields exist so the implementer can verify the wiring happened.

### Scene Wiring

Add two new Image GameObjects under the existing `WoodCounter` parent:

- `WoodIcon` — Image with sprite Trees_0, preserveAspect true, size ~24x24 UI px, anchored to the left of `WoodLabel` (offset ~-30px x)
- `FoodIcon` — Image with sprite Wheatfield_0, preserveAspect true, size ~24x24 UI px, anchored to the left of `FoodLabel` (offset ~-30px x)

Wire `_woodIcon` and `_foodIcon` SerializeFields on the `ResourceUI` component to point at these new Images.

The wood/food labels themselves stay where they are — icons sit to the left.

## Out of Scope

- Building cost in Food
- Upgrade cost in Food
- Population icon
- Animated/glowing icons
- Wood/Food icons elsewhere (e.g., card cost lines)

## Testing

- **Edit-mode tests:** none required (no new pure-logic code).
- **Solo smoke:**
  - UI: Tree icon visible left of Wood count, Wheatfield icon left of Food count.
  - Build Workshop (800 wood — Wood-only path still works).
  - Click Keep → Farmer card shows "50 Food" cost; greyed if Food < 50.
  - Harvest wheatfield → Food climbs to ≥ 50.
  - Train Farmer → 50 Food deducted, 0 Wood deducted, Farmer spawns.
  - Train Club at Barracks → both 150 Wood AND 100 Food deducted.
  - Card greys when either resource is insufficient.
- **2-client smoke:** same affordability gating per local player (existing pattern — each player's own bank, no wire change).

## Risks

| Risk | Mitigation |
|---|---|
| Existing pre-Farmer-Food saves break | No persistent saves yet; asset defaults handle the migration. |
| Cost-text wraps in narrow cards | "150 Wood, 100 Food" is shorter than existing "100 Wood" with longer DisplayName. Test in smoke. |
| Inspector wiring not persisted | Use Unity MCP + `EditorSceneManager.SaveScene` (same pattern as previous Food label task). |
| FoodCost field on existing UpgradeDefinition / BuildingDefinition assets | Out of scope — those types don't have FoodCost. No conflict. |

# Economy Balance — Resource Costs by Category — Design

**Status:** Spec
**Date:** 2026-05-28

## Goal

Re-tier the economy so each thing costs its intended resource category, with upgrades as the most expensive tier. Add the cost dimensions that don't exist yet (Stone on buildings, Iron/Gold/Crystal on upgrades), grant a starting baseline (100 Food + 100 Wood), and apply balanced numbers.

## Cost Model

- **Units** → Food only.
- **Buildings** → Wood + Stone.
- **Upgrades** → Iron / Gold / Crystal (the most expensive tier).
- **Start:** 100 Food, 100 Wood; Stone/Iron/Gold/Crystal start at 0.

## Balance Table

### Units (Food)
| Unit | Food | Pop | Wood (was) |
|---|---|---|---|
| Farmer | 50 | 1 | 0 |
| Club | 100 | 3 | 0 (drop the 150 wood) |

### Buildings (Wood + Stone)
| Building | Wood | Stone |
|---|---|---|
| Hut (+5 pop) | 60 | 20 |
| Barracks | 120 | 60 |
| Workshop | 150 | 100 |

(`Keep_0` is placed free at spawn via `PlaceForce(charge:false)`; its cost fields are irrelevant.)

### Upgrades (Iron / Gold / Crystal)
| Upgrade | Iron | Gold | Crystal |
|---|---|---|---|
| Sharper Tools | 200 | 0 | 0 |
| Heavier Clubs | 200 | 150 | 0 |
| Tough Hide | 0 | 200 | 150 |

Upgrades clearly exceed any building (max ~250 combined) and use the rarer ore resources.

## New Cost Fields

- `BuildingDefinition`: add `public int StoneCost = 0;` (keep existing `WoodCost`).
- `UpgradeDefinition`: replace the single `WoodCost` semantics with three ore costs: `public int IronCost; public int GoldCost; public int CrystalCost;` (keep `WoodCost` field for now but set to 0 on the assets; upgrade affordability/deduct switches to the ore fields).

## Affordability + Deduction

### Buildings (Wood + Stone)
- `BuildingPlacer.IsValid(origin)`: require `ResourceBank.Wood >= def.WoodCost && ResourceBank.Stone >= def.StoneCost`.
- `BuildingPlacer.PlaceForce(... charge:true ...)`: deduct both (`AddWood(-WoodCost)`, `Add(Stone, -StoneCost)`) — only when `charge` is true. Remote-apply (`charge:false`) deducts nothing (unchanged).
- `ObjectInspector.OnBuildingClicked`: re-check wood + stone before selecting the ghost.
- Building card cost text: `"{wood} Wood, {stone} Stone"` (omit stone if 0).
- `Refresh` affordability for building cards: wood + stone.

### Upgrades (Iron / Gold / Crystal)
- `ObjectInspector.OnUpgradeClicked`: require `Iron >= IronCost && Gold >= GoldCost && Crystal >= CrystalCost`; on purchase deduct each (`Add(Iron, -IronCost)` etc.).
- Upgrade card cost text: join the non-zero ore costs, e.g. `"200 Iron"`, `"200 Iron, 150 Gold"`, `"200 Gold, 150 Crystal"`.
- `Refresh` affordability for upgrade cards: all three ore checks (in addition to the existing already-purchased gate).

### CardRefs
Add `int StoneCost`, `int IronCost`, `int GoldCost`, `int CrystalCost` to the `ObjectInspector.CardRefs` struct so `Refresh` can gate building + upgrade cards generically. `BuildUnitCards`/`BuildBuildingCards`/`BuildUpgradeCards` populate the relevant ones.

## Starting Resources

In `MainBaseSetup.OnNewWorld`, after `ResourceBank.Reset()` (which zeroes everything), grant the baseline:
```csharp
ResourceBank.Add(ResourceKind.Wood, 100);
ResourceBank.Add(ResourceKind.Food, 100);
```
This runs once per world load on the local client (ResourceBank is the local player's bank). MP: each client grants its own baseline.

## Cost Text Helper

`ObjectInspector` already has `FormatUnitCost(wood, food)`. Add:
- `FormatBuildingCost(int wood, int stone)` → "60 Wood, 20 Stone" / "60 Wood" if stone 0.
- `FormatUpgradeCost(int iron, int gold, int crystal)` → joins non-zero ore parts.

## Asset Changes

| Asset | Change |
|---|---|
| `Units/FarmerGoblin.asset` | WoodCost 0, FoodCost 50 (already) |
| `Units/ClubGoblin.asset` | WoodCost 0 (was 150), FoodCost 100 |
| `Buildings/Huts_0.asset` | WoodCost 60, StoneCost 20 |
| `Buildings/Barracks_0.asset` | WoodCost 120, StoneCost 60 |
| `Buildings/Workshop.asset` | WoodCost 150, StoneCost 100 |
| `Upgrades/Upgrade_FarmerHarvest.asset` | IronCost 200, WoodCost 0 |
| `Upgrades/Upgrade_ClubDamage.asset` | IronCost 200, GoldCost 150, WoodCost 0 |
| `Upgrades/Upgrade_ClubHp.asset` | GoldCost 200, CrystalCost 150, WoodCost 0 |

## MP

ResourceBank + costs are owner-local. No wire change (train/place/upgrade already issue local-immediate + sync the action, not the cost). Each client deducts its own bank on its own issued actions; remote-apply paths don't deduct (existing behavior).

## File Plan

### Modified
| File | Change |
|---|---|
| `BuildingDefinition.cs` | `+ StoneCost` |
| `UpgradeDefinition.cs` | `+ IronCost/GoldCost/CrystalCost` |
| `BuildingPlacer.cs` | wood+stone affordability + deduct in IsValid/PlaceForce |
| `ObjectInspector.cs` | CardRefs ore/stone fields; FormatBuildingCost/FormatUpgradeCost; building+upgrade affordability in Refresh; OnBuildingClicked stone check; OnUpgradeClicked ore check+deduct; card cost strings |
| `MainBaseSetup.cs` | grant 100 Wood + 100 Food after Reset |
| `Units/*.asset`, `Buildings/*.asset`, `Upgrades/*.asset` | balance numbers (Unity MCP) |

## Testing

- **Edit-mode:** 42 tests still pass (no pure-logic changes).
- **Solo smoke:**
  - Start: Food 100, Wood 100, others 0.
  - Train Farmer → 50 Food deducted; Club → 100 Food. No wood deducted for units.
  - Build Hut → needs 60 Wood + 20 Stone; greyed until both available; deducts both. Same for Barracks (120/60), Workshop (150/100).
  - Workshop upgrades show ore costs; greyed until enough Iron/Gold/Crystal; purchase deducts the ores.
  - Tough Hide (200 Gold + 150 Crystal) is the steepest — needs heavy ore mining.
  - Cards display correct multi-resource cost strings.

## Risks
| Risk | Mitigation |
|---|---|
| Upgrade `WoodCost` field left dangling | Kept on the type, set to 0 on assets; affordability uses the ore fields. Could remove later. |
| New `StoneCost`/ore fields default 0 on already-serialized assets | Set explicitly via Unity MCP per asset. |
| Building deduct double-charges in MP | Only `charge:true` (issuer) deducts; remote `charge:false` unchanged. |
| Starting grant runs per OnNewWorld re-entry | OnNewWorld fires once per world (polled on world change); Reset+grant is idempotent per load. |

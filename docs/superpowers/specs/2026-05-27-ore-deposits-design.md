# Ore Deposits — Gold / Iron / Crystal (Phase 2) — Design

**Status:** Spec
**Date:** 2026-05-27

## Goal

Add three mineable ore deposits — Gold, Iron, Crystal — placed sparsely across the world, harvested like Stone/Wood/Food via the generalized `ResourceKind` system from Phase 1. Builds directly on Phase 1 (`ResourceKind`, `ResourceBank`, single typed carry slot, data-driven `ResourceUI`).

## Context (Phase 1 already done)

- `ResourceKind { Wood, Food, Stone, Gold, Iron, Crystal }` — Gold/Iron/Crystal already declared.
- `ResourceBank.Add/Get/OnChanged` keyed by `ResourceKind` — already handles all 6.
- `Goblin` single typed carry slot + `KindOf(tileName)` + `MaxHpFor(tileName)` + `IsHarvestable` — extend to recognize ore tiles.
- `ResourceUI` data-driven over a `ResourceKind→Sprite` list — extend the scene list with 3 entries.
- Harvestables are decoration-map tiles. `DecorationPlacer.Place` paints them per-biome + builds a reserved mask.

## Ore Sprites

From `Assets/MiniWorldSprites/Buildings/Wood/Resources.png` (user-chosen):
- **Gold** = `Resources_0`
- **Iron** = `Resources_5`
- **Crystal** = `Resources_2`

These sub-sprites become three new Tile assets so they can be painted on the decoration map and classified by name.

## New Tile Assets

Create three `Tile` assets under `Assets/Generated/Tiles/Decorations/`:
- `GoldOre_0.asset` → sprite `Resources_0`
- `IronOre_0.asset` → sprite `Resources_5`
- `CrystalOre_0.asset` → sprite `Resources_2`

Names use distinct prefixes (`GoldOre_`, `IronOre_`, `CrystalOre_`) so `Goblin.KindOf` can classify them. (They intentionally do NOT start with `Rocks_`/`Trees_`/`Wheatfield_`.)

## Goblin Classification (extend)

Add to `Goblin`:

```csharp
public static bool IsOreTile(string tileName) =>
    tileName.StartsWith("GoldOre_") || tileName.StartsWith("IronOre_") || tileName.StartsWith("CrystalOre_");
```

`IsHarvestable` gains `|| IsOreTile(tileName)`.

`KindOf` extends:
```csharp
if (tileName.StartsWith("GoldOre_")) return ResourceKind.Gold;
if (tileName.StartsWith("IronOre_")) return ResourceKind.Iron;
if (tileName.StartsWith("CrystalOre_")) return ResourceKind.Crystal;
```

`MaxHpFor` extends: ore tiles → `200` (slower than stone's 100). Tree 50 / Wheat 500 / Rock 100 / Ore 200 — keep the existing branches, add `if (IsOreTile(tileName)) return 200;`.

Carry cap stays 10; deposit/burst/mutual-exclusion all already generic.

## Placement

Ore deposits are placed by `DecorationPlacer` (deterministic via `world.Seed`, so all MP clients match). After the per-biome decoration pass, run a sparse ore pass:

- `DecorationConfig` gains:
  - `TileBase GoldOreTile; int GoldDeposits = 6;`
  - `TileBase IronOreTile; int IronDeposits = 6;`
  - `TileBase CrystalOreTile; int CrystalDeposits = 3;`
- For each ore type, place `N` single-cell deposits on random cells that are:
  - passable (not DeepWater/Cliff/Shore — reuse the existing passability check via biome),
  - not in the reserved mask (spawn areas + existing decorations/resource clusters),
  - not already holding a decoration/ore.
- Each placement: pick a random cell, validate, `SetTile(cell, oreTile)`, mark occupied. Bounded attempts per deposit (e.g. 40) to avoid infinite loops on a full map.
- Ores override nothing important — they sit on the decoration map like trees/rocks; the reserved mask already excludes spawn + tree/rock cells from the same pass.

Placement uses the same `Random rng` stream seeded from `world.Seed`, sequenced AFTER the decoration loop so existing decoration output is unchanged for a given seed (ore pass appends).

## UI (scene wiring)

Append three `KindIcon` entries to `ResourceUI._kinds`:
- Gold → `Resources_0`
- Iron → `Resources_5`
- Crystal → `Resources_2`

The data-driven `ResourceUI` builds three more counters automatically (icon + count, stacked under the existing Wood/Food/Stone). No code change to ResourceUI.

## File Plan

### New
| File | Purpose |
|---|---|
| `Assets/Generated/Tiles/Decorations/GoldOre_0.asset` (+meta) | Tile → Resources_0 |
| `Assets/Generated/Tiles/Decorations/IronOre_0.asset` (+meta) | Tile → Resources_5 |
| `Assets/Generated/Tiles/Decorations/CrystalOre_0.asset` (+meta) | Tile → Resources_2 |

### Modified
| File | Change |
|---|---|
| `Goblin.cs` | `IsOreTile`, `IsHarvestable += ore`, `KindOf += 3 ores`, `MaxHpFor` ore=200 |
| `DecorationPlacer.cs` | ore tile fields + counts in DecorationConfig; sparse ore-placement pass in `Place` |
| `Assets/Scenes/SampleScene.unity` | wire 3 ore tiles + counts into WorldGenerator's `_decorationConfig`; append Gold/Iron/Crystal to ResourceUI `_kinds` |

## MP

Deterministic placement (seed-driven) → identical deposits on all clients. Carry + bank owner-local (unchanged). No new wire messages.

## Out of Scope

- Building/unit costs in ore (costs stay Wood/Food)
- Ore smelting / processing chains
- Ore-specific worker requirements (any Farmer mines any ore)
- Biome-restricted ore (e.g. crystal only in snow) — uniform random passable placement for MVP
- Multiple sprite variants per ore (one tile each)

## Testing

- **Edit-mode:** existing 36 tests still pass (no pure-logic changes; ore placement is Unity-bound).
- **Solo smoke:**
  - Six counters: Wood, Food, Stone, Gold, Iron, Crystal (icons from chosen sprites).
  - Gold/Iron/Crystal deposits visible scattered on the map (away from spawn).
  - Right-click an ore → Farmer mines it (slower, HP 200), carry shows "Carrying: N gold/iron/crystal".
  - Deposit at keep → matching counter rises.
  - Mutual exclusion: carrying stone + right-click gold → deposit stone first.
  - Ore depleted → particle burst, tile removed.
- **2-client smoke:** both clients see the same ore deposits at the same cells (seed-deterministic); each player's counters independent.

## Risks
| Risk | Mitigation |
|---|---|
| Ore tile name prefix mismatch with KindOf | Tile asset names fixed (`GoldOre_0` etc.); KindOf checks exact prefixes. |
| Sparse placement fails on dense maps | Bounded attempts per deposit; if a deposit can't place, it's skipped (log count placed). |
| Ore overlaps a tree/rock | Placement checks the decoration map cell is empty + not reserved. |
| Resources.png sub-sprite not found | Verify `Resources_0/2/5` exist (count=15, names Resources_0..14 confirmed). |
| Determinism across clients | Single seeded rng, ore pass after decoration loop — same sequence on all clients. |

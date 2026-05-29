# Resource Generation Overhaul (Sub-project A) — Design

**Status:** Spec
**Date:** 2026-05-28

## Goal

Replace the current per-cell random scatter of harvestables with deliberate placement: biome-aware tree **forests** (clustered, frequent), random **stone clusters** (3–8), single scattered ore deposits, and a **guaranteed safe-spawn** zone (radius 25 around each spawn) holding 1× each ore + 5× wheat + 1 forest + 1 stone cluster. Pure-cosmetic decorations (cactus, tumbleweed) stay as low-density random. This is **Sub-project A**; pathfinding + resource-blocking are **Sub-project B** (separate spec).

## Context

- `DecorationPlacer.Place(world, seed)` currently paints, per cell, a biome-weighted random tile from pools that MIX harvestables (Trees, Rocks, Wheatfield) with cosmetics (Cactus, Tumbleweed, DeadTrees). Plus the Phase-2 ore pass.
- Harvestable classification (Goblin): `IsTreeTile` (Trees_/PineTrees_/WinterTrees_/WinterDeadTrees_/DeadTrees_/CoconutTrees_) → Wood; `IsWheatfieldTile` → Food; `IsRockTile` (Rocks_) → Stone; `IsOreTile` (GoldOre_/IronOre_/CrystalOre_) → Gold/Iron/Crystal.
- Determinism: `DecorationPlacer.Place` uses a single seeded `Unity.Mathematics.Random` from `world.Seed` → identical across MP clients.
- `world.Spawns` (int2[]) are the per-player spawn cells. `world.BiomeAt(x,y)`, `world.Width/Height`.

## Placement Model

The `Place` method is restructured into ordered passes over one seeded rng stream:

1. **Reserved mask** (existing): spawn-area footprints + (now) every cell a cluster/guarantee occupies, so passes don't overlap.
2. **Cosmetic scatter** (per cell, low density): ONLY non-harvestable tiles. Harvestables (Trees, Rocks, Wheatfield) are REMOVED from these biome pools — the pools keep cosmetics only (Cactus, Tumbleweed). Biomes whose pool becomes empty simply place nothing here.
3. **Forests** (clustered, biome-aware) — see below.
4. **Scattered single trees** (rare, biome-aware) — low-density singles between forests, biome tree type.
5. **Stone clusters** — N random clusters of 3–8 Rocks on passable land.
6. **Ore deposits** — existing single-deposit pass (Gold 6 / Iron 6 / Crystal 3).
7. **Safe-spawn guarantees** — per spawn, force-place the guaranteed set within radius (runs last so it always succeeds even on a crowded map; it can overwrite cosmetic tiles but not other guarantees).

### Biome-Aware Trees

Trees are NEVER placed biome-agnostically. Each forest/scattered tree uses the tree type matching the cell's biome:

| Biome | Tree tiles | Forests? |
|---|---|---|
| Forest | `Trees_` (deciduous) | yes (dense) |
| Grassland | `Trees_` (deciduous) | yes |
| Snow | `PineTrees_` / `WinterTrees_` | yes |
| TropicalCoast | `CoconutTrees_` | yes |
| DryGrass | `DeadTrees_` | yes (sparse groves) |
| Desert | — | no (cosmetic cactus only) |
| Shore / Cliff / DeepWater | — | no |

A forest cluster picks its center cell, reads that cell's biome, and only proceeds if the biome supports trees; it then paints from that biome's tree-tile array. A cluster does not cross into an unsupported biome (cells whose biome differs from the center's tree-support are skipped).

### Forests

- Count: frequent (config `ForestCount`, default ~22 across a 256² map).
- Size: 15–40 trees (`ForestSizeMin`/`ForestSizeMax`).
- Spread: grown by random-walk/scatter within a radius (`ForestRadius`, ~5) around the center, only on tree-supporting, passable, unreserved, same-tree-type cells.
- Each painted cell marked reserved.

### Scattered Single Trees

- Low per-cell probability (`ScatteredTreeDensity`, ~0.01) on tree-supporting biomes, outside reserved cells, using the biome tree type. Rare — most trees come from forests.

### Stone Clusters

- Count: `StoneClusterCount` (~12) random centers on passable, unreserved land (any non-water/cliff/shore biome).
- Size: 3–8 (`StoneSizeMin`=3 / `StoneSizeMax`=8), random-walk spread like forests.
- Tile: random from `RockTiles` (Rocks_).

### Ore Deposits

- Unchanged single-deposit pass: Gold 6 / Iron 6 / Crystal 3 on passable, unreserved cells.

## Safe-Spawn Guarantee (radius 25)

For each `world.Spawns` cell, place within `SafeSpawnRadius` (default 25), on passable cells outside the spawn footprint reserve, deterministically:
- **1× GoldOre, 1× IronOre, 1× CrystalOre** (single cells)
- **5× Wheatfield** (single cells, spread out)
- **1 forest** (biome tree of the spawn's biome; if spawn biome doesn't support trees — e.g. desert spawn — fall back to deciduous `Trees_` so wood is always reachable)
- **1 stone cluster** (3–8 rocks)

Placement scans candidate cells in the radius (spiral/random with bounded attempts); guaranteed items take priority and may overwrite cosmetic tiles but not each other. If a guarantee can't fit (extremely unlikely at r=25), log a warning.

## Config + Tiles

`DecorationPlacer.DecorationConfig` gains:

```
[Header("Resource Generation")]
TileBase[] DeciduousTreeTiles;   // Trees_*
TileBase[] PineForestTiles;      // PineTrees_*, WinterTrees_*
TileBase[] CoconutForestTiles;   // CoconutTrees_*
TileBase[] DeadTreeTiles;        // DeadTrees_*  (DryGrass groves)
TileBase[] RockTiles;            // Rocks_*
TileBase   WheatTile;            // Wheatfield_0
int ForestCount = 22;
int ForestSizeMin = 15;
int ForestSizeMax = 40;
int ForestRadius = 5;
float ScatteredTreeDensity = 0.01f;
int StoneClusterCount = 12;
int StoneSizeMin = 3;
int StoneSizeMax = 8;
int SafeSpawnRadius = 25;
int SafeSpawnWheat = 5;
```

(Ore tile fields + counts from Phase 2 stay.)

The existing per-biome cosmetic pools (`ForestTiles`, `GrasslandTiles`, …) are repurposed to hold ONLY cosmetics (Cactus, Tumbleweed) — the scene wiring strips Trees/Rocks/Wheatfield out of them. Biome→forest-tile mapping is a method on the placer keyed by `Biome`.

A `BiomeSupportsTrees(Biome)` helper + `ForestTilesFor(Biome)` returns the right array (null = no forest).

## Determinism + MP

All passes consume the single seeded rng in fixed order → identical worlds on all clients. No wire changes (world seed already synced).

## Reachability

The existing `ReachabilityChecker` (pure-logic, on biomes + spawns + resource clusters) is unchanged. Forests/clusters do NOT block movement in Sub-project A (blocking is Sub-project B), so they can't make spawns unreachable. The `WorldData.Resources` clusters (used by the checker) are untouched.

## Out of Scope (→ Sub-project B)

- Pathfinding / water impassability
- Resource cells blocking goblins
- Any movement/collision change

## File Plan

### Modified
| File | Change |
|---|---|
| `DecorationPlacer.cs` | Restructure `Place` into passes; biome-aware forest + scattered-tree + stone-cluster + safe-spawn-guarantee logic; new config fields + biome→tile helpers |
| `Assets/Scenes/SampleScene.unity` | Wire new tile arrays (`DeciduousTreeTiles`, `PineForestTiles`, `CoconutForestTiles`, `DeadTreeTiles`, `RockTiles`, `WheatTile`); strip harvestables from cosmetic pools |

No new scripts strictly required (placer grows); if `Place` becomes unwieldy, the cluster/guarantee helpers may be split into a `ResourceScatter` helper class — decided at plan time.

## Testing

- **Edit-mode:** existing 36 tests still pass (DecorationPlacer is Unity-bound; no pure-logic test changes). World-gen reachability tests unaffected.
- **Solo smoke:**
  - Trees appear as forests (clumps), biome-appropriate (pine in snow, coconut tropical, deciduous green, dead in drygrass, none in desert), only occasional singles.
  - Each spawn has within ~25 cells: 1 gold + 1 iron + 1 crystal, 5 wheat, a forest, a stone cluster.
  - Stone clusters (3–8) scattered out in the world.
  - Cactus/tumbleweed still scattered cosmetically; no random lone trees/rocks/wheat everywhere.
  - New seed each solo run → different but rule-consistent layout.
- **2-client smoke:** identical layout on both clients (seed-deterministic).

## Risks
| Risk | Mitigation |
|---|---|
| Safe-spawn guarantee can't place on a water-locked spawn | Spawns are already placed on passable land with buffer (SpawnPlanner); r=25 gives ample candidates. Bounded attempts + warning log. |
| Forest crossing biome boundary looks wrong | Cluster only paints cells whose biome supports the SAME tree type as the center. |
| Stripping harvestables from cosmetic pools missed in scene | Plan includes an explicit scene pass to rebuild the pools; smoke test checks no random lone trees. |
| Determinism drift if pass order changes | Fixed pass order documented; all passes share one rng stream. |
| Desert spawn has no native trees | Safe-spawn forest falls back to deciduous Trees_ so wood is always reachable. |

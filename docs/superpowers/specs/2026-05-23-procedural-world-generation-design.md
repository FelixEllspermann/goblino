# Procedural World Generation — Design

**Date:** 2026-05-23
**Project:** RTSCL (Unity 6, 2D URP, MiniWorldSprites)
**Status:** Approved for implementation planning

## Goal

Foundation-quality RTS map generator that produces coherent ("sinnvolle") worlds from the MiniWorldSprites tile pack. Deterministic via seed, balanced spawn placement, decoration coherent with biome, extensible to a polished autotile pass later.

## Scope

**V1 (this spec):**
- One-continent-with-coasts topology
- Three-axis biome model (elevation, moisture, temperature)
- Seed-based reproducibility
- N balanced spawn points (default N=2)
- Resource clusters near each spawn + free-roam expansion clusters
- Connected-pathing guarantee between spawns and resources
- Hard biome borders (no soft autotile transitions)
- Decoration scatter (trees, rocks, cactus, etc.) per biome rules
- Camera auto-fit on bootstrap

**Out of scope (Phase 2 / later):**
- Soft edge transitions via RuleTile / 47-tile autotile
- Cliff-water combined transitions
- Rivers, roads, bridges
- Building / champion / unit placement (kept out of the generator entirely)
- Runtime regeneration with animation

## Constraints from MiniWorldSprites pack

Ground tilesets available (under `Assets/MiniWorldSprites/Ground/`):
`Grass`, `DeadGrass`, `TexturedGrass`, `Winter` (snow), `Shore` (sand→water), `Cliff`, `Cliff-Water`.

Decoration sprites (`Nature/`):
`Trees`, `PineTrees`, `CoconutTrees`, `DeadTrees`, `WinterTrees`, `WinterDeadTrees`, `Cactus`, `Tumbleweed`, `Wheatfield`, `Rocks`.

Pack tile size: 16×16 px (standard for MiniWorldSprites packs). Confirmed by inspecting `Boar.png.meta` slice rects (`width: 12, height: 14` for sprites placed on a 16-px grid).

Filter mode for the pack was switched to Point in a prior turn — sprites are pixel-crisp.

## Architecture

Three layers with a strict pure-logic boundary:

### Layer 1 — Pure-logic core

No `UnityEngine.*` references except `Unity.Mathematics` (cross-platform-deterministic Random, plus float types).

```
WorldGenerator
  ├─ NoiseField         (elevation, moisture, temperature)
  ├─ BiomeClassifier    (thresholds → Biome enum)
  ├─ ContinentShaper    (radial falloff + connected-components cleanup)
  ├─ SpawnPlanner       (farthest-point sampling on land)
  ├─ ResourcePlanner    (clusters near spawns + global)
  └─ ReachabilityChecker (flood-fill: all spawns and resources connected)
```

Input: `WorldGenConfig { uint seed; int width=128; int height=128; int playerCount=2; ...thresholds }`.
Output:
```csharp
public sealed class WorldData
{
    public int Width, Height;
    public Biome[,] Biomes;       // [width, height]
    public Vector2Int[] Spawns;   // length = playerCount
    public List<ResourceCluster> Resources;
}
```

Output is a plain data structure — Unity-agnostic. Unit-testable without an Editor.

### Layer 2 — Unity bridge

```
TilePainter            (WorldData → Tilemap, via IBiomeTileResolver)
IBiomeTileResolver     (V1: SingleTileBiomeResolver; V2: RuleTileBiomeResolver)
DecorationPlacer       (paints Nature/* tiles on a separate Decoration Tilemap)
CameraFitter           (frames the generated world)
WorldGeneratorBootstrap (MonoBehaviour entry point + Inspector)
```

`IBiomeTileResolver.GetTile(WorldData world, int x, int y) → TileBase` is the swap point for the Phase 2 autotile upgrade. V1 implementation looks up a single hardcoded sprite per biome; V2 will use neighbor-aware RuleTile assets without any change to the generator or painter.

### Layer 3 — Editor tooling

`MiniWorldSpritesSlicer` (Editor-only): one-shot menu command `Tools → RTSCL → Slice MiniWorldSprites` that:
1. Sets the `TextureImporter` of each Ground PNG to `SpriteImportMode.Multiple` with `SpriteAlignment.Center` and a 16×16 grid slice.
2. Creates a `TileBase` asset per biome under `Assets/Generated/Tiles/<Biome>.asset`, pointing at one chosen sprite index per sheet.
3. Sets the Nature PNGs to `SpriteImportMode.Multiple` with 16×16 slices and generates one `TileBase` per decoration sprite under `Assets/Generated/Tiles/Decorations/`.

Idempotent — re-running re-generates the assets cleanly.

## Algorithm

### Noise generation

Three independent Perlin/Simplex noise fields, all in `[0..1]` after normalization:

```
elevation(x,y)    = noise(x/scale_e, y/scale_e, seed+0)
moisture(x,y)     = noise(x/scale_m, y/scale_m, seed+1)
temperature(x,y)  = noise(x/scale_t, y/scale_t, seed+2) * 0.6 + lat_bias(y) * 0.4
```

Defaults (tunable via `WorldGenConfig`):
- `scale_e = 40` (continent-scale features)
- `scale_m = 30` (biome-scale)
- `scale_t = 60` (slow temperature gradient)
- `lat_bias(y) = abs((y / height) - 0.5) * 2` — poles colder, equator hotter

Implementation note: use `Unity.Mathematics.noise.snoise` for Simplex; it is deterministic across platforms when the seed is folded into the input via offset vectors.

### Continental falloff

To force a continent shape (water at the edges, land in the middle), elevation is multiplied by a radial falloff:

```
nx = (x / width) * 2 - 1    // -1..1
ny = (y / height) * 2 - 1
d  = sqrt(nx² + ny²)         // 0..√2
falloff = clamp01(1 - d² * falloff_strength)
elevation_adjusted = elevation * falloff
```

Default `falloff_strength = 1.2` — produces ~70% land, 30% water with a clear coastline.

### Biome classification

Per cell, with elevation_adjusted `e`, moisture `m`, temperature `t`:

| Condition | Biome |
|---|---|
| `e < 0.30` | DeepWater |
| `e < 0.40` | Shore (sand) |
| `e > 0.85` | Cliff |
| `t < 0.25` | Snow |
| `t > 0.75 ∧ m < 0.30` | Desert |
| `t > 0.75 ∧ m ≥ 0.30 ∧ near_water(x,y,2)` | TropicalCoast |
| `m > 0.65` | Forest |
| `m > 0.35` | Grassland |
| else | DryGrass |

Order matters — first match wins. `near_water(x,y,radius)` is a manhattan-distance check against the in-progress biome map (water cells already classified in a first pass).

### Post-processing

1. **Mini-island removal:** flood-fill on the land set. Components < 10 cells become DeepWater.
2. **Lake-fill:** flood-fill on water; water components < 5 cells that are fully enclosed by land become Grassland.
3. Both passes operate on copies and write back atomically.

### Spawn placement

```
candidates = cells where biome ∈ {Grassland, Forest} AND
             distance_to_nearest({DeepWater, Cliff, Shore}) >= 4
sample = pick min(N*100, |candidates|) random candidates
spawns = farthest_point_sampling(sample, N)
```

`farthest_point_sampling`: pick first candidate randomly, then iteratively pick the candidate maximizing min-distance to all already-chosen. Greedy, O(N·|sample|).

Each spawn reserves a 5×5 block: those cells are marked as "no decoration" so the build area is clear.

### Resource clusters

Two resource types in V1:
- `Stone` → Rocks sprites
- `Food` → Wheatfield sprites

Per spawn: 2 Stone clusters + 2 Food clusters, each 3–5 sprites, placed in an annulus of 6–12 tiles around the spawn, on appropriate biomes (Stone on any land, Food on Grassland/Grassland-adjacent).

Globally: 6–10 additional free-roam clusters scattered across the remaining land (uniform random with rejection for already-occupied cells).

### Reachability check

After all spawns and resources are placed:
```
reachable = flood_fill(spawns[0], passable = !water && !cliff)
if any spawn or resource not in reachable: regenerate with seed+1
max attempts: 5; on failure, throw.
```

This prevents the rare case where a thin water channel splits the continent and isolates a spawn.

## Decoration rules

| Biome | Decoration probability | Sprite source |
|---|---|---|
| Forest | 25% | Trees (60%), Rocks (10%), Wheatfield (5%, near edges) |
| Grassland | 8% | Trees (40%), Wheatfield (35%), Rocks (15%), DeadTrees (10%) |
| DryGrass | 12% | DeadTrees (60%), Rocks (30%), Tumbleweed (10%) |
| Desert | 10% | Cactus (50%), Tumbleweed (30%), Rocks (20%) |
| Snow | 20% | PineTrees (40%), WinterTrees (30%), WinterDeadTrees (20%), Rocks (10%) |
| TropicalCoast | 18% | CoconutTrees (80%), Rocks (20%) |
| Shore | 3% | Rocks only |
| Cliff | 5% | Rocks only |
| DeepWater | 0% | — |

Decorations are placed on a separate Tilemap with higher sorting order so they overlay the ground without affecting the ground tile selection.

## Camera auto-fit

`CameraFitter` runs once after generation:
```csharp
var bounds = new Bounds(
    center: new Vector3(width / 2f, height / 2f, 0) * cellSize,
    size:   new Vector3(width, height, 0) * cellSize);
camera.orthographicSize = max(bounds.size.x / (2 * camera.aspect), bounds.size.y / 2) * 1.05f;
camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10);
```

5% padding so the world isn't flush against the edge.

## Unity scene integration

`SampleScene` layout after first run:
```
Grid (Grid component, cellSize=1)
├── Terrain         (Tilemap, sortingOrder=0)
└── Decorations     (Tilemap, sortingOrder=10)
WorldGenerator      (empty GO with WorldGeneratorBootstrap)
Main Camera         (orthographic, fit by CameraFitter)
```

`WorldGeneratorBootstrap` inspector fields:
- `Seed` (int, -1 = random per run)
- `Width` (default 128)
- `Height` (default 128)
- `PlayerCount` (default 2, range 2–8)
- `Config` (the full `WorldGenConfig` for threshold tuning)
- `Regenerate` button (calls regen with current settings)

Auto-runs on `Start()`. Regenerate button clears both Tilemaps and re-paints.

## Determinism

Single source of randomness: `Unity.Mathematics.Random` constructed from the user's seed. All sampling, noise offsets, and decoration choices draw from this single stream in a fixed order. Future cross-machine multiplayer determinism requires no code changes — `Unity.Mathematics.noise.snoise` and `Random` are explicitly cross-platform.

## Files to create

```
Assets/Scripts/World/
  Biome.cs                          enum: DeepWater, Shore, Cliff, Snow, Desert,
                                          TropicalCoast, Forest, Grassland, DryGrass
  WorldGenConfig.cs                 serializable POCO with all thresholds/scales
  WorldData.cs                      output container (Biomes, Spawns, Resources)
  ResourceCluster.cs                small struct: Type, Center, Cells
  WorldGenerator.cs                 orchestrates the pipeline
  NoiseField.cs                     wraps Unity.Mathematics.noise per channel
  BiomeClassifier.cs                threshold table → Biome
  ContinentShaper.cs                falloff + post-processing
  SpawnPlanner.cs                   farthest-point sampling
  ResourcePlanner.cs                cluster placement
  ReachabilityChecker.cs            flood-fill
  IBiomeTileResolver.cs             V1/V2 swap point
  SingleTileBiomeResolver.cs        V1 impl, ScriptableObject with Tile dict
  TilePainter.cs                    Unity-side painter
  DecorationPlacer.cs               Unity-side decoration scatter (paints tiles on Decorations Tilemap)
  CameraFitter.cs                   frames the world
  WorldGeneratorBootstrap.cs        MonoBehaviour entry + inspector

Assets/Editor/
  MiniWorldSpritesSlicer.cs         one-shot slicing + Tile-asset generation

Assets/Generated/Tiles/             (created by slicer)
  Grass.asset, DeadGrass.asset, Shore.asset, Winter.asset,
  Cliff.asset, DeepWater.asset, TexturedGrass.asset
Assets/Generated/Tiles/Decorations/ (created by slicer)
  Trees_<i>.asset, PineTrees_<i>.asset, Cactus_<i>.asset, ...

docs/superpowers/specs/
  2026-05-23-procedural-world-generation-design.md   (this file)
```

Source-of-truth count: 16 runtime C# files, 1 editor C# file, ~7 generated Tile assets, plus the scene wiring.

## Testing

`WorldGenerator` and its helpers are pure C# — testable in EditMode tests without instantiating a scene:
- Same seed → identical `WorldData` (byte-for-byte)
- Different seed → different `WorldData`
- `playerCount=2..8` → reachability always succeeds within 5 attempts on default config
- Connected-components pass actually removes mini-islands and fills mini-lakes

Unity bridge (`TilePainter`, `DecorationPlacer`, `CameraFitter`) is not unit-tested in V1; verified manually by pressing Play in the Editor.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Generator emits unfair maps (one spawn surrounded by cliffs) | Reachability check + reroll; spawns require ≥4-cell buffer from impassable |
| 128×128 generation is slow in Play | Noise + classification is O(w·h) and trivially fast. Bench on first run; if needed, mark noise sampling as `Burst`-compatible (no API changes). |
| Slicer assumes 16×16 — wrong for some MiniWorldSprites sheets | Verify all Ground sheets visually after first slice. If a sheet uses a different cell size, add an override map in the slicer config. |
| RuleTile Phase 2 needs different `WorldData` info (e.g. neighbor info) | `IBiomeTileResolver.GetTile(world, x, y)` already gets the full `WorldData` — V2 can read neighbors itself. No generator change needed. |

## Open questions (deferred)

- Should resource clusters become destructible / harvestable in Phase 2? (Out of scope — depends on game logic which doesn't exist yet.)
- Should rivers be a Phase 2 addition or Phase 3? (Phase 3 — they need pathfinding-aware generation.)
- Multiplayer seed-sync protocol? (Not now — single-machine generation only in V1.)

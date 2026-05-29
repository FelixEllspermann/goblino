# Building Rally Point — Design

**Date:** 2026-05-29
**Status:** Approved

## Goal

Give unit-training buildings a **rally point**: trained units automatically walk to it and arrange around it (no stacking). While the building is selected, a flag icon (GUI_33) marks the rally point and an animated dashed line ("marching ants" flowing toward the rally) connects the building to it. The player sets the rally by right-clicking the ground while the building is selected.

## Decisions (locked)

- Rally is **per-building**, keyed by footprint origin `Vector2Int`, and **owner-local** (not networked). Only the resulting unit *move* syncs (via existing `IssueMove` → `CmdMove`); no new wire message.
- Set rally = **right-click on ground while the building is selected** (local, unit-training building only).
- Every unit-training building gets a **default rally** just below its footprint at placement, so the icon/line always show something.
- Icon = `GUI_33` from `Assets/2D Casual UI/Sprite/GUI.png` (72×118 @100PPU; rendered at a flag-ish world size).
- Dashed line = `LineRenderer` with a procedurally-baked dash texture, `textureMode = Tile`, scrolling `mainTextureOffset` for marching ants.
- Arrival spread = each finishing unit moves to the **nearest free cell** around the rally (no other goblin within a small radius), filling outward.

## Components

### 1. `RallyPoints` (new static, `RTSCL.World.Unity`)
- `Dictionary<Vector2Int, Vector3> _points` (origin → rally world pos).
- `void Set(Vector2Int origin, Vector3 worldPos)`, `bool TryGet(Vector2Int origin, out Vector3)`, `void Remove(Vector2Int origin)`, `void Clear()`.
- `static Vector3 Default(Vector2Int origin, Vector2Int footprint)` → `(origin.x + footprint.x*0.5, origin.y - 1.5, 0)` (a couple cells below the building, centered).
- Cleared in `MainBaseSetup.OnNewWorld`.

### 2. `RallyVisual` (new MonoBehaviour, `RTSCL.World.Unity`)
- One reusable instance (created lazily; `static RallyVisual Instance`).
- `Show(Vector3 buildingCenter, Vector3 rally, Sprite icon)`: positions an icon `SpriteRenderer` at `rally`, sets `LineRenderer` endpoints `[buildingCenter, rally]`, enables both.
- `Hide()`: disables icon + line.
- `Update()`: if active, scroll the line material's `mainTextureOffset.x` (marching ants toward rally) and keep tiling proportional to line length so dashes stay uniform.
- Dash texture: procedurally baked 1×N (e.g. 8px: 4 opaque white, 4 transparent), `wrapMode = Repeat`. Material uses `Sprites/Default` shader (URP-2D-compatible, supports vertex color + texture) or `Unlit/Transparent`-style; tint to a visible color.
- Sorting: line `sortingOrder = 22` (above terrain/buildings, below units 25); icon `sortingOrder = 24`.
- Icon localScale tuned (~0.7) so the flag reads at goblin scale.

### 3. `ObjectInspector` changes
- New `[SerializeField] private Sprite _rallyIcon;` (wired to GUI_33 in the scene).
- Helper `bool BuildingTakesRally()` = current selection is a Building, local-owned, and `_selDef.TrainsUnits` non-empty.
- In `ShowBuilding`: if `BuildingTakesRally()`, ensure a rally exists (set default via `RallyPoints` if missing) and call `RallyVisual.Instance.Show(buildingCenterWorld, rally, _rallyIcon)`. Otherwise `RallyVisual.Instance.Hide()`.
- In `Hide()` (and when switching to Decoration/Goblins selection): `RallyVisual.Instance.Hide()`.
- In `Update`: when right mouse pressed, not over UI, and `BuildingTakesRally()` → set rally for `_selOrigin` to the clicked world point (snapped to cell center), update the visual. Guarded so it doesn't fire during placement mode.
- Building center world = `_terrainMap.CellToWorld(origin) + footprint*0.5`.

### 4. `BuildingPlacer.PlaceForce`
- After registering a building: if `def.TrainsUnits != null && def.TrainsUnits.Length > 0`, set `RallyPoints.Set(origin, RallyPoints.Default(origin, def.Footprint))` (only if not already set, to preserve a rally across reconstructs).

### 5. `GoblinProductionRunner`
- `SpawnByKindAroundFootprint` returns the spawned `Goblin`. Capture it.
- If owner is local (`owner == WorldStartContext.LocalPlayer || owner == 0UL`) and `RallyPoints.TryGet(origin, out var rally)`: compute the nearest free cell to `rally` and `NetCommandIssuer.IssueMove(new List<Goblin>{unit}, freeCellWorld)`.
- Free-cell finder (in the runner): spiral/ring outward from the rally cell; pick the first passable cell whose center has no other live `Goblin` within ~0.6 units. Fallback to the rally point itself.

### 6. `MainBaseSetup.OnNewWorld`
- Add `RallyPoints.Clear();` alongside the other static resets.

## What already works
- Move sync, formation, pathfinding (units rally via standard `IssueMove`).
- Building selection/deselection drives the visual.

## Out of scope
- Networking the rally point itself (owner-local is sufficient; only moves sync).
- Per-unit rally lines, waypoint chains, or rally on resources (just a ground point).
- Rally for non-training buildings.

## Acceptance
- Select a Keep/Barracks → GUI_33 flag appears below it with a dashed line whose dashes flow toward the flag.
- Right-click ground while it's selected → flag + line jump to the clicked spot.
- Train units → they walk to the rally and fan out around it without overlapping.
- Deselect → flag + line disappear.
- Solo and MP identical (the rally move syncs as a normal move).

# Minimap — Design

**Status:** Spec
**Date:** 2026-05-27

## Goal

A corner minimap that shows the world terrain (biome colors), units + buildings as dots, the camera viewport frame, and respects Fog of War (unexplored = black, explored = dimmed, visible = clear). Clicking the minimap jumps the camera.

## Architecture

New `Minimap` MonoBehaviour drives everything. References (Inspector-wired):
- `WorldGeneratorBootstrap` — source of `CurrentWorld`
- `RawImage _terrainImage` — static biome texture
- `RawImage _fogImage` — fog overlay texture (same rect, stacked above terrain)
- `RectTransform _minimapRect` — the clickable minimap area (the terrain RawImage's RectTransform)
- `RectTransform _dotsRoot` — parent for pooled dot UI Images
- `RectTransform _viewportFrame` — the camera-viewport outline
- `Camera _mainCamera` + `RTSCamera2D _cameraRig`
- Dot prefab settings (sprite, sizes, pool)

Placement: a UI panel anchored bottom-right, ~200×200 px square. World is square (Width == Height, typically 256).

### Coordinate Mapping

World cell `(x, y)` spans `0..Width` / `0..Height`. Minimap local space is the `_minimapRect` rect (origin bottom-left, size `rect.width × rect.height`).

```
WorldToMinimap(Vector2 world) =>
    new Vector2(world.x / Width * rectW, world.y / Height * rectH)   // local, bottom-left origin

MinimapToWorld(Vector2 local) =>
    new Vector2(local.x / rectW * Width, local.y / rectH * Height)
```

Helpers are private methods on `Minimap`. `Width`/`Height` cached from `CurrentWorld`.

## Terrain Layer (static, once per world)

Poll `CurrentWorld` in `Update` (compare against `_knownWorld`, same pattern as `MainBaseSetup` / `FogOfWar`). On a new world:

- Create `Texture2D(Width, Height, RGBA32, false) { filterMode = Point }`
- One pixel per cell, color by biome:

| Biome | Color (RGB 0-255) |
|---|---|
| DeepWater | (30, 55, 120) |
| Shore | (90, 130, 180) |
| Cliff | (110, 110, 115) |
| Snow | (235, 240, 245) |
| Desert | (210, 195, 120) |
| TropicalCoast | (90, 200, 190) |
| Forest | (40, 95, 50) |
| Grassland | (95, 165, 75) |
| DryGrass | (140, 150, 80) |

- Fill a `Color32[]` (row-major, index `y*Width + x`), `SetPixels32`, `Apply()` once.
- Assign to `_terrainImage.texture`.

## Fog Overlay (per-frame, diff-updated)

Second `Texture2D(Width, Height, RGBA32, false) { filterMode = Point }` assigned to `_fogImage.texture`.

`Minimap` keeps a `Visibility[,] _prevFog` (its own mirror). Each frame:
- For every cell, read `FogOfWar.GetVisibility(x, y)`.
- If unchanged vs `_prevFog`, skip.
- Else update `_prevFog` + set the overlay pixel:
  - `Hidden` → `(0,0,0,255)` opaque black
  - `Explored` → `(0,0,0,140)` ~55% black
  - `Visible` → `(0,0,0,0)` transparent
- Track a `dirty` flag; call `Apply()` at most once per frame, only if any pixel changed.

On a new world: re-create the fog texture sized to the new Width/Height + reset `_prevFog` to a sentinel that forces first paint.

If `FogOfWar` isn't present / not yet initialized, the fog overlay stays fully transparent (no-op).

## FogOfWar Extension

Make the visibility enum public and add a query accessor:

```csharp
public enum Visibility : byte { Hidden = 0, Explored = 1, Visible = 2 }

public Visibility GetVisibility(int x, int y)
{
    if (_state == null || x < 0 || y < 0 || x >= _width || y >= _height)
        return Visibility.Hidden;
    return _state[x, y];
}
```

The existing private `Visibility[,] _state` / `_prevState` fields and their usages stay — only the enum's accessibility changes (private nested → public nested) plus the new getter.

## Dots (pooled UI Images)

`Minimap` maintains a pool of `Image` children under `_dotsRoot`. Each frame:
1. Deactivate all pooled dots (or track a cursor and disable the tail).
2. For each `Goblin` in `Goblin.All`:
   - `isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL`
   - If NOT local: skip unless its cell is `Visibility.Visible` (FoW gating — no wallhack).
   - Take a pooled dot, set color (white if local, else `WorldStartContext.GetPlayerColor(Owner)`), size = unit dot (3px), position via `WorldToMinimap(transform.position)`.
3. For each building in `BuildingPlacer.AllOccupied` (iterate origins — dedupe by origin via the `_cellToOrigin`-style key; since AllOccupied yields every footprint cell, track a `HashSet<Vector2Int>` of origins already drawn this frame using `BuildingPlacer.TryGetBuildingOrigin`):
   - Determine owner via `BuildingPlacer.TryGetBuildingOwner(cell)`.
   - `isLocal` check same as units.
   - If NOT local: skip unless the origin cell is `Visibility.Visible`.
   - Building dot: bigger (5px), faction color.
4. Grow the pool on demand (instantiate a new dot Image when the pool is exhausted).

Dot pivot is centered; `anchoredPosition` is the minimap-local position from `WorldToMinimap`. Dots are anchored to `_dotsRoot`'s bottom-left so local coords line up with the mapping.

**Building dedupe:** `AllOccupied` returns one entry per footprint cell. To draw one dot per building, resolve each cell to its origin and only draw the first time an origin is seen this frame.

## Viewport Frame

`_viewportFrame` is a UI element showing the camera's current view rectangle. Each frame:
- Camera center = `_mainCamera.transform.position`
- Half-extents: `halfH = orthographicSize`, `halfW = halfH * aspect`
- World rect corners: `(cx - halfW, cy - halfH)` to `(cx + halfW, cy + halfH)`
- Convert min-corner + size to minimap-local via `WorldToMinimap`
- Set `_viewportFrame.anchoredPosition` (min corner) + `sizeDelta` (mapped size)

`_viewportFrame` visual: a UI Image with a hollow/outline sprite, OR (simpler) an Image using a 1px-border sliced sprite. For MVP, use an Image with `color` set to semi-transparent white and a thin outline via `Image.type = Sliced` with a built-in border sprite; if no border sprite is available, fall back to 4 thin child Images forming a rectangle outline. The implementer picks whichever renders a clean hollow rect.

## Click Navigation

`Minimap` implements `IPointerClickHandler` + `IDragHandler` (from `UnityEngine.EventSystems`):
- On click/drag, convert the pointer's screen position to `_minimapRect` local point via `RectTransformUtility.ScreenPointToLocalPointInRectangle`.
- Normalize to the rect (account for the rect's pivot — local point may be pivot-relative; shift so origin is bottom-left).
- `MinimapToWorld(local)` → world XY.
- Call `_cameraRig.JumpTo(worldXY)`.

### RTSCamera2D Extension

```csharp
/// <summary>Center the camera on a world XY (keeps z + ortho size) and clamp to map bounds.</summary>
public void JumpTo(Vector2 worldXY)
{
    if (_camera == null) return;
    _camera.transform.position = new Vector3(worldXY.x, worldXY.y, -10f);
    _userInteracted = true;   // re-enable clamping
    ClampPosition();
}
```

`ClampPosition` is currently private — `JumpTo` is in the same class so it can call it directly.

## Scene UI

Add under the main Canvas:
- `MinimapPanel` (RectTransform, bottom-right anchor, 200×200)
  - `TerrainImage` (RawImage, fills panel)
  - `FogImage` (RawImage, fills panel, above terrain)
  - `DotsRoot` (RectTransform, fills panel, above fog)
  - `ViewportFrame` (Image, above dots)
- `Minimap` component on `MinimapPanel` with all refs wired.

Built via Unity MCP (create GameObjects + SerializedObject wiring + SaveScene), same pattern as the Food label / resource icons tasks.

## MP Considerations

- `WorldStartContext.LocalPlayer` distinguishes own vs enemy for dot color + FoW gating.
- FoW is owner-local (each client computes its own). Minimap reads the local FogOfWar — correct per client.
- No wire messages. Minimap is pure presentation.

## Out of Scope

- Minimap pings / markers
- Minimap zoom
- Enemy-building "explored memory" dots (only live-Visible gating for MVP)
- Rotating/non-square minimap
- Terrain texture mip/anti-alias (Point filter, crisp pixels)

## Testing

- **Edit-mode tests:** none (MonoBehaviour + UI bound). Existing 36 tests must still pass.
- **Solo smoke:**
  - Minimap visible bottom-right with biome-colored terrain.
  - Unexplored area black; explored area dimmed; area around units/keep clear.
  - Own units = white dots; move them → dots move; FoW around them clears on minimap.
  - Viewport frame matches the on-screen view; pan/zoom main camera → frame resizes/moves.
  - Click minimap → camera jumps to that location (clamped at edges).
  - New solo game (random seed) → minimap re-bakes the new terrain.
- **2-client smoke:**
  - Each player's minimap shows only their own FoW.
  - Enemy units appear on the minimap only when within the local player's vision.
  - Own units always shown; enemy keep hidden until scouted into vision.

## Risks

| Risk | Mitigation |
|---|---|
| Per-frame fog texture upload cost | Diff-update — only changed cells written, single `Apply()` per dirty frame. 256×256 worst-case first frame is a one-time cost. |
| Dot pool churn | Pool reused across frames; grows only to peak unit count. |
| Click coordinate mismatch with rect pivot | Use `RectTransformUtility.ScreenPointToLocalPointInRectangle` + explicit pivot-to-bottom-left shift. |
| Building dedupe (AllOccupied yields per-cell) | Track drawn origins in a per-frame HashSet. |
| FogOfWar absent in some scene | `GetVisibility` returns Hidden; minimap fog stays opaque — degrades gracefully. |
| Non-square world | World is square by config; mapping uses independent x/y scales anyway, so a non-square map still maps correctly (just stretches). |

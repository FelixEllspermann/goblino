# Docks & Transport Boats — Design

**Date:** 2026-05-29
**Status:** Approved (phased)

## Goal

Add coastal **Docks** (Docks_0 on land + Docks_1 on the adjacent water cell) that can only be built where land meets water. Docks train **transport boats** (TransportShip) that move only on water. Boats carry units: select land units → right-click a boat to board (up to 6); select the boat → right-click land to unload passengers at the shore. Boats are killable; if sunk, their passengers are lost.

## Decisions (locked)
- Boats killable (ranged units can sink them); **passengers lost** on sink. Capacity **6**.
- Sprites: `Docks.png` (16 PPU) Docks_0 = land, Docks_1 = water; `Miscellaneous/TransportShip.png` (→ 16 PPU) for the boat.
- Player-only for now (bots don't build docks/boats yet).

## Architecture

### Phase 1 — Dock building (coastal placement)
- `DockRegistry` (new static): `Dictionary<Vector2Int origin, Vector2Int waterCell>` + `Clear()` (new world). Records each dock's adjacent water cell (boat spawn point).
- `BuildingPlacer` dock special-case (detected by `def.name.StartsWith("Docks")`, footprint 1×1 = the land cell):
  - **IsValid:** the land cell is passable + empty (normal), AND has an orthogonally-adjacent **water** cell (`DeepWater`/`Shore`). (Skip the normal "no water/shore in footprint" rejection — the footprint is the land cell only.)
  - **PlaceForce:** place normally on the land cell, then `FindAdjacentWater(origin)` → spawn a child SpriteRenderer with the `Docks_1` sprite at that water cell (sorting 15) and store `DockRegistry[origin] = waterCell`.
- Dock asset `Docks_0` (BuildingDefinition): Sprite = Docks_0, Footprint 1×1, cost (≈80 wood + 40 stone), `TrainsUnits = [Boat]`, PopulationProvided 0. Add to `BuildingCatalog` + the player's farmer-buildables.

### Phase 2 — Boat unit + water movement
- `GoblinUnitDefinition.WaterUnit` (bool). `Goblin._waterMode = def.WaterUnit` in Init.
- `Goblin.IsCellPassable`: when `_waterMode`, **invert** — water (`DeepWater`/`Shore`) is passable, land/`Cliff` blocked. (Harvestable-decoration block only applies to land units.) So boats A*-path on water with the existing pathfinder.
- Boat asset `Boat` (TransportShip frames 0–2, 16 PPU): `WaterUnit=true`, AttackDamage 0 (no attack), MaxHp ~80, WorldScale ~1.4, SpawnerKindName `"Boat"`. Add a `Boat` kind to GoblinSpawner.
- Boat spawn: when a **dock** finishes training a boat, `GoblinProductionRunner` spawns it at `DockRegistry[origin]` (the water cell) instead of around the footprint.
- Player moves a boat by right-clicking water (existing IssueMove + water pathfinding). Right-clicking land with only boats selected → unload (Phase 3).

### Phase 3 — Transport (board / unload)
- Boat `Goblin` holds `List<Goblin> _passengers` (cap 6) + `bool IsBoat`.
- **Board:** `Goblin.SetBoardCommand(Goblin boat)` — land unit paths to the nearest passable **land** cell adjacent to the boat's cell; when within 1 cell, it boards: deactivate its GameObject (leaves `Goblin.All`), add to the boat's passenger list (if < capacity). Re-path if the boat moved.
- **Unload:** `Goblin.SetUnloadCommand(Vector3 landTarget)` on the boat — boat moves to a water cell adjacent to land near the target; on arrival, eject each passenger to the nearest passable land cell (reactivate GO, place, clear from list).
- **Input (GoblinSelectionController):** right-click a friendly **boat** with land units selected → board; right-click a land cell with **only boats** selected → unload there.
- **Sink:** boat `EnterDying` → destroy all passenger GameObjects (passengers lost).

## Routing / reuse
- Boats reuse Goblin movement/HP/health-bar/hop-death/selection. No-friendly-fire already prevents attacking own boats; enemy boats are attackable by ranged units (water-mode boat sits on water; archers on shore within range can hit it — uses existing unit-vs-unit combat since the boat is a Goblin).
- Solo authority covers boats (IsOwnedLocally true in solo).

## Out of scope
- Bots using boats; naval combat units (warships); boats carrying buildings; pathfinding boats around tiny lakes vs ocean (any connected water); animated water wakes.

## Task list (phase 1 first, then checkpoint)
1. **P1:** `DockRegistry`; `BuildingPlacer` dock placement (validity + water-side sprite + registry); `MainBaseSetup` `DockRegistry.Clear`. Dock asset + catalog + farmer-buildables wiring. Verify placement only at coast.
2. **P2:** `GoblinUnitDefinition.WaterUnit`; `Goblin` water-mode passability; Boat asset + kind + 16 PPU; dock-boat spawn on water. Verify boat moves on water only.
3. **P3:** passengers + board/unload commands + input + sink. Verify load → ferry → unload.

## Acceptance (full)
- A Dock can only be placed where its land cell touches water; Docks_1 appears on the water beside it.
- The Dock trains boats that spawn on the water and only travel on water.
- Land units board a boat (≤6), get ferried across water, and unload onto a far shore; sinking the boat loses its passengers.

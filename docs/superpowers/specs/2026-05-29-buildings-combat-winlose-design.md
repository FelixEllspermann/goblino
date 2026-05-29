# Units vs Buildings + Win/Lose — Design

**Date:** 2026-05-29
**Status:** Approved

## Goal

Let goblins attack and destroy buildings, and end the match: a faction is eliminated when **all** its buildings are destroyed; the player wins when every enemy faction is eliminated and loses when its own last building falls. On game over the game pauses and a Victory/Defeat overlay with "Back to Menu" appears.

## Decisions (locked)
- Elimination = a faction owns **0 buildings**.
- Game over = **pause (Time.timeScale = 0) + overlay + Back-to-Menu**.
- Scope = **solo-correct** (player vs bots; all units locally authoritative). Building damage/destruction applies locally — no new wire messages. MP human-vs-human building-combat sync (CmdAttackBuilding + EvBuildingDamage) is **deferred** (MP isn't the active mode and needs 2-client testing).

## Components

### 1. `BuildingPlacer` — single-building removal + queries
- Track `Dictionary<Vector2Int, GameObject> _originToGo` populated in `PlaceForce`.
- `void RemoveBuilding(Vector2Int origin)`:
  - look up def/footprint/owner; if completed and `PopulationProvided > 0`, reduce that owner's pop cap (player → `PopulationManager.AddCap(-n)`, bot → `BotEconomy.AddCap(owner, -n)`);
  - clear `_cellOwners/_cellToOrigin/_cellToOwner` for every footprint cell; `BuildingHP.Remove`; `BuildingConstruction.Remove` (new); destroy the GO; spawn a `DeathBurst` at the center for juice.
- `int BuildingCount(ulong owner)` (distinct origins owned) and `bool HasAnyBuilding(ulong owner)` for the match check.
- `bool TryGetFootprint(Vector2Int origin, out Vector2Int footprint)` (from the def) for range checks.

### 2. `BuildingConstruction` — `Remove(origin)`
- Add `Remove(Vector2Int origin)` to drop an under-construction entry (so destroying a half-built building stops its builders).

### 3. `Goblin` — attack a building
- `void SetAttackBuildingCommand(Vector2Int origin)` (only combatants; respects no-friendly-fire via owner check at the call site).
- New fields `_buildingTarget` + `_hasBuildingTarget`; two new states `MovingToAttackBuilding`, `AttackingBuilding`.
  - **MovingToAttackBuilding:** if the building is gone (`!BuildingHP.TryGet`) → Idle. Path to the nearest passable cell adjacent to the footprint; when Chebyshev distance to the footprint ≤ `AttackRange` → `AttackingBuilding`.
  - **AttackingBuilding:** if gone → Idle. Every `AttackInterval`: lunge toward the building center (melee anim; ranged units just deal damage, no arrow for MVP); if `IsLocalOwner` → `BuildingHP.Damage(origin, AttackDamage)`, and if HP hits 0 → `NetCommandApplier.Placer.RemoveBuilding(origin)` (local destroy).
- Switching to any other command (move/harvest/build/attack-unit) clears `_hasBuildingTarget`.

### 4. `GoblinSelectionController` — order an attack on a building
- On right-click: after the existing harvest / own-construction / enemy-unit checks, add: if the clicked cell holds a building **hostile** to the selection (owner differs / not own) and it's not under-construction-own → command all combatants (`AttackDamage > 0`) to `SetAttackBuildingCommand(origin)` (orange/red click feedback). Shift-queue support optional (skip for MVP).

### 5. `BotController` — raze the enemy
- During an attack, for each military unit: if a hostile **unit** is within `_engageRadius` → attack it (as now); else if a discovered enemy **building** exists → `SetAttackBuildingCommand(nearest discovered base)`; else march toward the base. So the bot army actually destroys the enemy.

### 6. `MatchManager` (new MonoBehaviour) + Game-Over overlay
- `Begin(IEnumerable<ulong> owners)` called by `MainBaseSetup.OnNewWorld` after spawning (records participating factions; solo only — skip in MP for now).
- Each ~0.5 s (and not yet over): a faction is *alive* if `BuildingPlacer.HasAnyBuilding(owner)`. 
  - Player (owner 0) not alive → **Defeat**. 
  - All non-player factions not alive → **Victory**.
- On game over: build a runtime full-screen overlay (dim + big "VICTORY"/"DEFEAT" + "Back to Menu" button), set `Time.timeScale = 0`. Button → restore `Time.timeScale = 1`, `SceneManager.LoadScene("MainMenu")`.
- `MainBaseSetup.OnNewWorld` also resets `Time.timeScale = 1` (in case returning from a paused game).

### 7. `MainBaseSetup`
- After spawning player + bots (solo), call `_matchManager.Begin({0, 1..N})`.

## What already exists
- `BuildingHP.Damage/Remove/TryGet`; hop-death/`DeathBurst`; `IsHostileTo`; solo authority (`IsLocalOwner` true for all in solo).

## Out of scope
- MP building-combat wire sync (deferred); building-attack projectiles for archers (melee-style damage for now); buildings fighting back; rubble/decay visuals beyond a burst.

## Task list
1. `BuildingConstruction.Remove` + `BuildingPlacer` `_originToGo` + `RemoveBuilding` + `HasAnyBuilding`/`BuildingCount`/`TryGetFootprint`.
2. `Goblin` building-attack (fields, states, command, damage+destroy).
3. `GoblinSelectionController` right-click hostile building → attack.
4. `BotController` attack buildings when no unit target.
5. `MatchManager` + overlay; `MainBaseSetup` Begin + timeScale reset.
6. Verify: compile, tests 42/42, play-smoke (raze a building; win/lose overlay).

## Acceptance
- Right-click an enemy building with combat units → they walk over and chip its HP until it's destroyed (HP bar drops, building disappears, burst).
- Destroying a faction's last building eliminates it; all enemies gone → Victory overlay (paused) + Back to Menu; own last building gone → Defeat.
- The bot razes your buildings during its attack waves.

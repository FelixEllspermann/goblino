# Build Menu Sidebar — Design

**Date:** 2026-05-30
**Status:** Approved (brainstorming)

## Problem

The build menu currently lives inside the bottom info bar (`ObjectInspector`), as a single horizontal
row of cards shown when a Farmer is selected. With 6 buildings it's already cramped; a stop-gap
category layer was added (Economy/Military/Naval via hardcoded name-prefix matching). The user wants to
add **30+ buildings** long-term, so the foundation needs to change now rather than be reworked later.

## Goals

- A dedicated, scalable build UI separate from the bottom info bar.
- Data-driven categories + build prerequisites so new buildings need **no code changes** (just an asset).
- Foundation ready for a tech-tree (prerequisites) without a second UI rebuild.

## Non-Goals

- Authoring an actual tech tree (prereqs ship empty — every building buildable as today).
- Changing the ghost-placement flow (`BuildingPlacer.Select` → click to place) — reused unchanged.
- Bot build logic (bots keep their own path; `BuildRequirements` is available to them later).

## Architecture

The build menu is **extracted from the bottom info bar** into its own runtime-built UI: a **left-edge
sidebar** that appears only while a Farmer is selected. `ObjectInspector` reverts to just inspection +
train/upgrade cards. Build flow (Farmer → category → building → ghost placement) moves to a new
`BuildMenu` component. Everything is data-driven: each `BuildingDefinition` gains a `Category` enum and a
`Requires` list; the menu groups by category, builds tabs automatically, and greys out locked buildings.

## Components

- **`BuildCategory` (enum, new)** — `Economy, Military, Defense, Naval, Advanced`. Declaration order =
  top-to-bottom tab order.

- **`BuildingDefinition` (extended)** — two new serialized fields:
  - `BuildCategory Category` (default `Economy`)
  - `BuildingDefinition[] Requires` — empty = always buildable; otherwise every listed building must be
    owned + finished by the player before this one unlocks.

- **`BuildRequirements` (static helper, new)** — `IsUnlocked(BuildingDefinition def, ulong owner)`:
  true if `def.Requires` is empty, or every required building is owned by `owner` and not under
  construction. Single source of truth; reusable by the bot later.

- **`UITooltip` (shared singleton, new)** — the hover tooltip extracted from `ObjectInspector`. One panel
  for the whole UI. **Dynamically sized**: the panel grows to fit its text (ContentSizeFitter + padding),
  no fixed 260×70. Both `ObjectInspector` and `BuildMenu` call `UITooltip.Show(text)` / `Hide()`.

- **`BuildMenu` (MonoBehaviour, new)** — the left sidebar, built at runtime (no prefab):
  - Anchored to the **left edge**, vertically centered; fixed panel height; hidden unless a Farmer is
    selected (subscribes to `GoblinSelectionController.OnSelectionChanged`).
  - Layout: a narrow **vertical tab column** (one button per non-empty category) beside a **scrollable
    grid** (ScrollRect + GridLayoutGroup) of that category's buildings.
  - Each building card: icon + name + cost icons; greyed + non-interactable when `!IsUnlocked`
    (tooltip "Requires: X") or unaffordable. Hover → `UITooltip`. Click on a buildable → existing
    `BuildingPlacer.Select(def)`.
  - Holds the `_buildables` list (moved from `ObjectInspector._farmerBuildables`).

- **`ObjectInspector` (slimmed)** — remove the build-menu code (`BuildCategoryMenu`,
  `BuildBuildingCards`, `_farmerBuildables`, `CategoryOf`, `BuildCategories`) and the inline tooltip
  (now `UITooltip`). Keeps: building / resource / unit inspection + train + upgrade cards.

## Data Flow

`GoblinSelectionController.OnSelectionChanged` → `BuildMenu` checks "is a Farmer selected?":
- yes → build/refresh tabs + grid from `_buildables` (filtered by `Category`, ordered by enum), show panel.
- no → hide panel.

Per-card availability = `BuildRequirements.IsUnlocked(def, LocalPlayer)` && affordable (wood/stone).
Refreshed on `ResourceBank.OnChanged` + `BuildingConstruction.OnCompleted` (so a card unlocks the moment
its prerequisite finishes), plus the cheap per-frame affordability tint already used by cards.

## Migration

- Move `_farmerBuildables` (same 6 asset refs) from `ObjectInspector` to `BuildMenu` in the scene.
- Set each existing building's `Category`: Huts/Workshop/Wheatfield/Mill = Economy, Barracks = Military,
  Docks = Naval. `Requires` left empty for all (everything buildable exactly as today).
- Delete the stop-gap `BuildCategories`/`CategoryOf` prefix logic from `ObjectInspector`.

## Error Handling / Edge Cases

- Empty categories produce no tab.
- A building whose `Requires` references a not-yet-owned building shows greyed with a "Requires: X" tooltip.
- New serialized fields: confirm scene/asset values via MCP after import (Unity fills new fields with the
  code default, but verify — past sessions hit stale/zeroed nested fields).
- Tooltip never eats clicks (raycastTarget off), always drawn on top.

## Verification

- Compile clean (0 errors).
- Deterministic probes: category grouping correct; every buildable maps to a category; `IsUnlocked`
  true with empty Requires, false when a prereq is missing, true once owned.
- Layout, scrolling, and left-edge placement: focused playtest by the user (runtime UI can't be
  asserted headlessly).

## Future (out of scope, enabled by this)
- Populate `Requires` to form a real tech tree — no UI change needed.
- Bot uses `BuildRequirements.IsUnlocked` to respect the tech tree.
- More categories = one enum line; more buildings = one asset each.

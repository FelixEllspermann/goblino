# Goblin Archer (Ranged Unit) — Design

**Date:** 2026-05-29
**Status:** Approved

## Goal

Add a ranged military unit — the **Goblin Archer** — trained at the Barracks alongside the Club Goblin. It holds distance and shoots an arrow projectile that deals damage on impact.

## Decisions (locked)

- **Trained at:** Barracks (second training option next to Club).
- **Damage timing:** on projectile **impact** (arrow flies to target, then damage applies). Owner-authoritative.
- **Stat profile (Glass Cannon):** HP 25, AttackDamage 4, AttackRange 5, AttackInterval 1.8s, WoodCost 0, FoodCost 90, PopulationCost 2, SpawnDuration 7s.
- **Sprites:** unit = `ArcherGoblin.png` frames 0–4 (already wired in GoblinSpawner `_kinds` as kind `"ArcherGoblin"`); projectile = `ArrowLong_0` from `Assets/MiniWorldSprites/Objects/ArrowLong.png` (GUID `1be4c078556799e4e9dcc024310dacb9`).

## Architecture

Data-driven, minimal new code. The combat FSM already supports `AttackRange` (the unit stops `AttackRange` cells away and enters `Attacking`); we add a **ranged branch** in the `Attacking` state plus a cosmetic-but-owner-authoritative projectile.

### 1. `GoblinUnitDefinition` — new field
- Add `public Sprite ProjectileSprite;` (default null). **Null = melee** (current behavior). **Non-null = ranged** — the unit shoots this sprite as a projectile instead of head-butting.

### 2. `Arrow` — new MonoBehaviour (`Assets/Scripts/World/Unity/Arrow.cs`)
- Spawned at the archer's position on every client when an attack swing fires.
- Homes toward the target's current transform (falls back to last-known position if the target dies mid-flight), rotates to face its travel direction, sorting order above units.
- On arrival (within a small epsilon of the target):
  - If `dealsDamage` (owner only) and the target is still alive → `NetCommandIssuer.IssueDamage(target, damage, attacker)` (applies locally + broadcasts `EvDamage`, exactly like melee).
  - Then destroys itself.
- Remotes spawn the same arrow with `dealsDamage = false` (pure visual). The damage they apply comes from the owner's `EvDamage`. Slight visual/HP-timing divergence is cosmetic and acceptable (consistent with the project's existing stance on cosmetic MP divergence). **No new wire message.**
- Speed ~10 units/sec (range 5 → ~0.5 s flight). Tunable constant.

### 3. `Goblin` — ranged branch in `Attacking`
- `Init` reads `def.ProjectileSprite` into a private `_projectileSprite` field.
- In the `Attacking` state's `_attackTimer >= AttackInterval` block:
  - **Ranged** (`_projectileSprite != null`): face the target (set `flipX`), spawn an `Arrow` (`dealsDamage = IsLocalOwner`). Do **not** play the melee lunge. Do **not** call `IssueDamage` here (the arrow does it on impact, owner only).
  - **Melee** (`_projectileSprite == null`): unchanged — `StartHitAnim` + (owner) `IssueDamage`.
- `MovingToAttack` → `Attacking` transition already uses `AttackRange`; with range 5 the archer naturally stops 5 cells out. No change needed.
- Auto-retaliate already works (AttackDamage 4 > 0).

### 4. Assets / wiring
- Create `Assets/Generated/Units/GoblinArcher.asset` (a `GoblinUnitDefinition`) with the stat block above, `Icon = ArcherGoblin_0`, `ProjectileSprite = ArrowLong_0`, `SpawnerKindName = "ArcherGoblin"`.
- Assign this asset to the GoblinSpawner `_kinds` entry whose `Name == "ArcherGoblin"` in `SampleScene` (frames already set; only `Definition` is missing).
- Add the archer def to `Barracks_0.asset`'s `TrainsUnits` (after ClubGoblin). This also auto-registers it in `NetworkCatalog` (populated from `BuildingCatalog` → buildings' `TrainsUnits`), so the train command syncs over the network with no extra step.

## What already works (no change)
- Train card UI (`ObjectInspector.BuildUnitCards`) renders one card per `TrainsUnits` entry with cost + affordability.
- `FormatUnitCost(0, 90)` → "90 Food".
- `PrettyKindName("ArcherGoblin")` → "Archer Goblin".
- Population cap, training progress bar, MP train sync (reserved NetId), upgrades-on-spawn — all generic.

## Out of scope
- Dedicated Archery Range building (chose Barracks).
- Arrow arc/gravity, multi-shot, friendly-fire splash (straight homing line, single target).
- Archer-specific upgrades.

## Acceptance
- Select a Barracks → two train cards (Club, Archer); Archer shows "90 Food", greys out when food < 90 or pop full.
- Train an Archer → spawns near the Barracks, "Archer Goblin" in the info box.
- Right-click an enemy → archer walks to ~5 cells, stops, shoots arrows that fly to the target; target HP drops on impact; target dies → archer stops.
- Solo and MP behave identically (MP: damage syncs via existing `EvDamage`).

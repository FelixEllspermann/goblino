# Neutral Monsters — Design

**Date:** 2026-05-29
**Status:** Approved

## Goal

Add neutral hostile creatures — **Giant Crab, Mammoth, Slime, Slime Blue** — that spawn in themed biome areas, wander a little around their home, and attack nearby player goblins (aggro + chase + leash-back). Killable, no loot. Multiplayer-correct from the start (host-authoritative).

## Decisions (locked)

- **Biomes:** Slime → Forest/Grassland; Slime Blue → Snow/DryGrass; Giant Crab → TropicalCoast (coastal); Mammoth → Snow.
- **Behavior:** idle near a home point with gentle wandering (small radius); aggro any player goblin within an aggro radius; chase; if dragged beyond a leash radius from home, disengage and return home, then resume wandering. No loot on death (hop-death like goblins).
- **MP authority:** monsters are owned by the **host** (solo = local). Only the authoritative client runs monster AI and issues their attacks/damage; everyone else mirrors via the existing command/EvDamage sync. No new wire message.

## Architecture

Monsters reuse the existing `Goblin` MonoBehaviour (movement, A* pathing, combat, hop-death, HP bar, net sync) with monster stats from a `GoblinUnitDefinition`, plus:
- a **neutral flag** so they're untinted, unselectable by players, and don't touch population, and
- a **`MonsterAI`** component that drives wander/aggro/leash on the authoritative client only.

### 1. MP-authority bridge — `WorldStartContext.HostPlayer`
- Add `public static ulong HostPlayer;` to `WorldStartContext` (0 in solo). Reset clears it.
- `GameStartLoader` (Assembly-CSharp) sets `WorldStartContext.HostPlayer = NetworkSession.HostPlayer.m_SteamID` alongside `LocalPlayer` when loading the game scene (solo path leaves it 0).
- Monster owner = `WorldStartContext.PendingSlots != null ? WorldStartContext.HostPlayer : 0UL`. On the host (`LocalPlayer == HostPlayer`) the monster's existing `IsLocalOwner` is true → it authoritatively attacks; on clients it's false → mirror only. Solo: owner 0 → local authoritative.

### 2. `Goblin` — neutral support + per-unit scale
- Add `public bool IsNeutral { get; private set; }` and `public void MarkNeutral() { IsNeutral = true; ApplyOwnerVisuals(); RefreshSelectionRingColor(); }`.
- Add `public bool IsOwnedLocally => Owner == WorldStartContext.LocalPlayer || Owner == 0UL;` (public mirror of the private `IsLocalOwner`, for `MonsterAI`).
- `ApplyOwnerVisuals`: if `IsNeutral`, keep the natural sprite (`Color.white`) — never apply a faction tint (so a host-owned monster isn't drawn in the host's player color on clients).
- `RefreshSelectionRingColor`: neutral → a neutral ring color (e.g. light red/grey) — rings only show if selected, which won't happen for monsters, but keep it consistent.
- `EnterDying`: skip `PopulationManager.RemoveUsed(...)` when `IsNeutral` (monsters never consumed player population).
- `Init`: apply `transform.localScale = Vector3.one * def.WorldScale` (new field below) so big monsters read large.

### 3. `GoblinUnitDefinition` — `WorldScale`
- Add `public float WorldScale = 1f;` (visual size multiplier; slimes 1.0, crab ~1.6, mammoth ~2.2).

### 4. `MonsterAI` (new MonoBehaviour)
Added to each monster GameObject at spawn. Fields: `Vector3 Home`, plus tunable constants `WanderRadius=4`, `AggroRadius=6`, `LeashRadius=12`, `WanderIntervalMin/Max` (~2–5 s).
- `Update` runs decision logic ONLY when the monster is authoritative (`_goblin.IsOwnedLocally`) and alive; otherwise returns (remotes mirror via received commands).
- State:
  - **Returning** (dragged past leash): issue `IssueMove(home)`; once within ~1.5 of home, clear and go idle.
  - **Aggro**: if it has a live target within leash → ensure attacking it (only re-issue `IssueAttack` when target changes, to respect the attack cooldown). If target dies or leaves leash distance from home → drop + Return.
  - **Idle/Wander**: periodically scan `Goblin.All` for the nearest non-neutral, alive player goblin within `AggroRadius` → aggro it. If none, occasionally pick a random reachable point within `WanderRadius` of home and `IssueMove` there.
- All movement/attacks go through `NetCommandIssuer.IssueMove` / `IssueAttack` so they sync (host issues, clients mirror). Target acquisition uses `Goblin.All` (filter `!IsNeutral`, `CurrentHp > 0`, and a real owner).

### 5. `MonsterSpawner` (new)
Seed-deterministic, biome-aware placement, run from `MainBaseSetup.OnNewWorld` AFTER player teams (so spawn order — hence NetIds — is identical on all clients). Uses `Unity.Mathematics.Random(seed)`.
- Config (counts per type, e.g. Slime 6, SlimeBlue 4, Crab 3, Mammoth 2; slimes may spawn in small packs of 2–3).
- For each type: scan/sample cells whose biome matches, that are passable, empty, and at least `MinDistanceFromSpawns` (~22) from every `world.Spawns` point (so early game isn't instant death). Place via `GoblinSpawner.SpawnAt(worldPos, kind, owner)`, then `goblin.MarkNeutral()` and `goblin.gameObject.AddComponent<MonsterAI>().Home = worldPos`.
- Owner per the MP-authority rule above. NetId via the normal reserved/auto index (deterministic given identical spawn order).
- Cleared/re-run on new world (monsters are destroyed by `GoblinSpawner.ClearAllGoblins()` which already runs in `OnNewWorld`).

### 6. `GoblinSelectionController` — exclude neutrals
- `SelectAtPoint` / `SelectInBox`: also require `!g.IsNeutral` so players can't select/command monsters. (Right-click attack targeting via `TryGetGoblinAt` still allows attacking them; auto-retaliate already makes player units fight back.)

### 7. Assets / wiring
- Create 4 `GoblinUnitDefinition` assets (GiantCrab, Mammoth, Slime, SlimeBlue) with stats + `WorldScale` + `SpawnerKindName`.
- Set each monster PNG to **16 PPU** (like goblins) so base frames are ~1 cell; pick 4 walk sub-frames each (avoiding the wide composite strips, per recon).
- Add 4 kinds to the scene's `GoblinSpawner._kinds` (Name + WalkFrames + Definition).
- Wire the `MonsterSpawner` (config + reference) so `MainBaseSetup` runs it.

## Proposed stats (tunable)

| Monster | Biome | HP | Dmg | Interval | Range | Scale | Count |
|---|---|---|---|---|---|---|---|
| Slime | Forest/Grassland | 35 | 3 | 1.5 | 1 | 1.0 | 6 |
| Slime Blue | Snow/DryGrass | 55 | 5 | 1.4 | 1 | 1.0 | 4 |
| Giant Crab | TropicalCoast | 140 | 9 | 1.8 | 1 | 1.6 | 3 |
| Mammoth | Snow | 240 | 16 | 2.0 | 1 | 2.2 | 2 |

Aggro 6 / leash 12 / wander 4 cells; min 22 cells from any player spawn.

## Out of scope
- Loot/XP, monster respawn over time, ranged monsters, boss mechanics.
- Per-monster move speed (uses the goblin default; can add a def field later).
- Monster-vs-monster combat (they only target player goblins).

## Acceptance
- Themed monsters appear in their biomes, away from starting bases, untinted, not player-selectable.
- A monster wanders gently near its home; when a goblin comes within ~6 cells it chases and attacks; HP bars update; dragging the goblin far makes the monster give up and walk home, then resume wandering.
- Player units auto-retaliate; player can right-click a monster to focus-attack it; killing it plays the hop-death, no resources gained.
- Solo and MP consistent (host drives monster AI/damage; clients mirror).

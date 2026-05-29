# Singleplayer Bot Opponent — Design

**Date:** 2026-05-29
**Status:** Approved (phased)

## Goal

Add an AI opponent for singleplayer. Clicking **Play Solo** opens a setup menu (seed + number of bots). Each bot starts identically to the player (a keep, 5 farmers, 100 wood + 100 food), runs a real economy it cannot cheat (no free resources; can only build/train what it can afford), must explore (its own fog of war — it doesn't know where the player is until it scouts), and plays aggressively (grow economy → build army → attack the player once it has found and can reach them).

## Decisions (locked)

- **Aggressive** bot: harvest → economy buildings + farmers → barracks → military → scout → attack.
- **Real bot vision:** each bot has its own explored/visible grid; it only knows enemy units/buildings it has actually seen, and must scout to find the player.
- **Start flow:** Play Solo → Solo Setup panel (seed field, bot count 0–3) → Start.
- Bots are a **solo-only** feature (multiplayer keeps its existing player slots; no bots in MP).

## Core architectural principles

1. **Bots are extra factions** with owner ids 1, 2, 3 (player = 0). They reuse `Goblin`, `BuildingPlacer`, `GoblinSpawner`, pathfinding, combat, FoW-hiding — all already per-owner-aware.
2. **No cheating = a real per-bot economy.** The player keeps using `ResourceBank` / `PopulationManager` untouched. Bots use a new **`BotEconomy`** (per-owner resource pools + population). Every bot build/train checks and deducts from `BotEconomy`; harvest deposits credit it. A bot literally cannot act without the resources.
3. **Solo authority:** in solo (no network), the single client drives ALL units including bots. `Goblin.IsLocalOwner` must return true for every unit in solo (today it's only true for owner 0 / LocalPlayer). Add `WorldStartContext.IsSolo` (PendingSlots == null) and treat solo as locally-authoritative for all owners.
4. **Per-bot vision** is separate from the player's `FogOfWar` (which is the player's screen view). A lightweight `BotVision` per owner tracks explored/visible cells from that bot's units/buildings; the bot AI queries it for "what do I know".

## Routing the shared hooks by owner

A few currently player-hardcoded spots must route by the acting unit's/building's owner:
- **Harvest deposit** (`Goblin.WalkingToDeposit`): `Owner == 0/local → ResourceBank.Add` else `BotEconomy.Add(Owner, …)`.
- **Pop cap on building completion** (`BuildingPlacer.OnConstructionCompleted`): player owner → `PopulationManager.AddCap` else `BotEconomy.AddCap(owner, …)`.
- Player UI/cost paths (ObjectInspector, IssuePlaceBuilding charge, GoblinProduction) stay on `ResourceBank`/`PopulationManager` — only the local player uses them. Bots never call them; they use their own `BotController` action methods against `BotEconomy`.

## Phases (each independently testable)

### Phase 1 — Foundation: setup menu, bot spawn, economy, authority
- **Solo Setup menu** (Assembly-CSharp): Play Solo → a `SoloSetupPanel` with a seed input (blank = random) and a bot-count selector (0–3) + Start. Writes `WorldGeneratorBootstrap.PendingSeed` and a new `WorldStartContext.SoloBotCount`, then loads SampleScene.
- **`BotEconomy`** (new static, RTSCL.World.Unity): per-owner `int[6]` resources + pop used/cap; `Get/Add/CanAfford/AddCap/AddUsed/Reset`. Seeded 100 wood + 100 food, base pop cap 20 per bot.
- **`WorldStartContext.IsSolo`** + make `Goblin.IsLocalOwner`/`IsOwnedLocally` true for all owners in solo.
- **MainBaseSetup**: in solo, after the player team, spawn `SoloBotCount` bot teams at spawns 1..N (owner 1..N), same starting team (keep + 5 farmers) + seed each bot's `BotEconomy` 100/100. Faction colors via a solo `GetPlayerColor` (player white, bots red/yellow/green).
- **Deposit + pop-cap routing** by owner (above).
- *Testable:* start solo with 1 bot → a red enemy keep + 5 farmers at spawn[1]; bot has its own 100/100 (no AI yet — it just sits). Player economy unaffected. Friendly-fire rules already make them enemies.

### Phase 2 — Bot economy AI
- **`BotController`** (new MonoBehaviour, one per bot): periodic tick (e.g. every 0.5 s) that, gated by `BotEconomy` affordability:
  - assigns idle farmers to the nearest known resource node (wood/food/stone) — harvest credits `BotEconomy`;
  - keeps a build order: build Huts until a pop target, train farmers up to a worker target, build a Barracks;
  - bot training is tracked by `BotController` itself (timer per building) deducting `BotEconomy` (resources + pop) and spawning via `GoblinSpawner.SpawnKindAt(owner)` — bypassing `GoblinProduction`/`PopulationManager` so the player's systems stay clean.
- *Testable:* bot grows its base (more farmers, huts) at a believable pace, never exceeding its real resources.

### Phase 3 — Bot vision / scouting
- **`BotVision`** (new, per owner): `bool[,] explored` + `visible`, updated each tick from the bot's unit/building positions (vision radius). Exposes `IsExplored/IsVisible` and a discovered-enemy registry (enemy buildings/units the bot has seen).
- Bot sends a scout (1 farmer or cheap unit) toward unexplored cells (biased toward other spawn points) to find the player. The bot only "knows" enemy positions it has discovered.
- *Testable:* bot units explore outward; it doesn't beeline to the player before scouting reveals them.

### Phase 4 — Bot military + attack
- Bot builds Barracks, trains Clubs/Archers against `BotEconomy`, forms an army, and once it has ≥ threshold military AND a discovered enemy target, sends the army to attack the nearest known enemy building/unit. Retreat/retarget on losses.
- *Testable:* after a few minutes the bot attacks the player's discovered base; combat uses the existing no-friendly-fire rules.

## Out of scope (for now)
- Multiplayer bots; difficulty settings UI (tunable constants only); upgrades by the bot; multiple simultaneous attack waves / advanced strategy; bot using ranged kiting micro.

## Acceptance (full feature)
- Play Solo → setup menu (seed + bots) → game starts with the chosen bots, each a colored enemy faction at its own spawn with the same starting resources/units.
- Bots gather, build, and train strictly within their own resources (verifiable: never spawn/build without affording it).
- Bots explore and only engage what they've discovered.
- Bots eventually attack; player can fight them; killing all of a bot's stuff removes it as a threat.

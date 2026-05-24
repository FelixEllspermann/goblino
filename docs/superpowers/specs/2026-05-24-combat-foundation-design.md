# Combat Foundation (Local, Friendly Fire)

Date: 2026-05-24
Status: Approved for implementation

## Purpose

Establish the local combat infrastructure that future multiplayer steps will build on. Club Goblins can attack other Club Goblins (friendly fire), units take damage, die with a small animation, and free their population cap. This step is fully single-player; ownership-based filtering arrives in a later multiplayer step.

## Scope

In scope:
- HP for goblins (per-kind defaults)
- Combat values on `GoblinUnitDefinition` (damage, interval, range)
- New goblin states: `MovingToAttack`, `Attacking`, `Dying`
- Right-click on enemy → attack command (Clubs only)
- Auto-retaliate when idle and attacked
- Death: hop-arc + ground impact + red particle burst
- Health bar above each goblin (visible when damaged or selected)

Out of scope (later steps):
- Networking / ownership filtering (everyone is "owner" for now → friendly fire)
- Farmer combat or being attacked (Farmers are non-combatants)
- Path-finding (uses existing straight-line movement)
- Building attacks / siege
- Death sounds, particle pooling, advanced VFX

## Data Model

### `GoblinUnitDefinition` additions

```csharp
public int  MaxHp           = 20;
public int  AttackDamage    = 0;     // 0 = non-combatant (Farmer)
public float AttackInterval = 1.5f;  // seconds between hits
public int  AttackRange     = 1;     // cells (Chebyshev distance, 1 = melee)
```

Values:
- **FarmerGoblin**: MaxHp=20, AttackDamage=0 (cannot attack, cannot be attacked in this step)
- **ClubGoblin**: MaxHp=40, AttackDamage=5, AttackInterval=1.5s, AttackRange=1

### `Goblin` additions

```csharp
public int CurrentHp { get; private set; }
public int MaxHp     { get; private set; }
public int PopulationCost { get; private set; }
public int AttackDamage { get; private set; }
public float AttackInterval { get; private set; }
public int AttackRange { get; private set; }
```

Set during `Init()` from the unit definition.

### Spawn pipeline

`GoblinSpawner.SpawnAt` and `SpawnByKindAroundFootprint` must accept (or look up) the `GoblinUnitDefinition` so the spawned `Goblin` can be initialized with HP and combat stats. Lookup table by kind name is acceptable.

## Behavior

### Attack command

- New states added to existing enum: `MovingToAttack`, `Attacking`, `Dying`.
- `Goblin.SetAttackCommand(Goblin target)`:
  - Stores target reference and transitions to `MovingToAttack`.
  - If `AttackDamage == 0`, command is a no-op (non-combatants stay idle).

### `MovingToAttack`

- Walk toward target's current cell (target may move).
- When `Chebyshev distance(self, target) <= AttackRange`, transition to `Attacking`.
- If target dies or becomes null, return to `Idle`.

### `Attacking`

- `_attackTimer` increments by `Time.deltaTime`.
- When `>= AttackInterval`:
  - Reset timer
  - Call `target.TakeDamage(AttackDamage, this)`
  - Play hit animation (reuse existing headbutt anim toward target cell)
- If target moves out of range, transition back to `MovingToAttack`.
- If target dies, return to `Idle`.

### `TakeDamage(int damage, Goblin attacker)`

```
CurrentHp -= damage
if CurrentHp <= 0:
    EnterDying()
    return
// Auto-retaliate: only when idle and capable
if state == Idle and AttackDamage > 0 and attacker.CurrentHp > 0:
    SetAttackCommand(attacker)
```

### Selection controller — right-click priority

Updated order:
1. Tree → `CommandHarvest` (Farmers only — already filtered)
2. Construction site → build (Farmers only — already filtered)
3. **Other Goblin → `CommandAttack`** (Clubs only)
4. Empty terrain → formation move

`CommandAttack`:
- For each selected goblin where `AttackDamage > 0`: `SetAttackCommand(target)`.
- Goblins with `AttackDamage == 0` (Farmers) ignore the command.
- `ClickFeedback.Spawn(targetCell, red)` for visual feedback.

`TryGetGoblinAt(worldPos, out Goblin target)` — picks the nearest goblin within `_clickPickRadius`, excluding self-selection (i.e., not picking one of the selected goblins).

## Death Sequence

State `Dying` replaces the previous instant-destroy path. Sequence:

1. Enter `Dying`:
   - Set `_diePos = transform.position`
   - Set `_dieVelocity = (0, +3, 0)` (slight randomization optional)
   - Disable selection ring
   - Remove from `Goblin.All` and active selection
   - `PopulationManager.RemoveUsed(PopulationCost)`
2. Each frame in `Dying`:
   - `_dieVelocity.y -= 10 * dt` (gravity)
   - `transform.position += _dieVelocity * dt`
   - Optional: rotate sprite slightly for tilt
3. When `transform.position.y <= _diePos.y` (landed):
   - Spawn `DeathBurst` at current position
   - Destroy GameObject

`Dying` cannot be interrupted — no input, no auto-retaliate.

## UI: Health Bar

Component: `GoblinHealthBar` (child of `Goblin`)

- Two stacked `SpriteRenderer`s (bg + fill), each using a 1x1 white sprite
- Background: dark red/brown, full width
- Fill: width scaled by `CurrentHp / MaxHp`
- Color: lerp from green (full) → yellow (half) → red (low)
- Position offset: 0.7 world units above goblin pivot
- Sorting order: above goblin sprite (e.g., 30 if goblin is 25)
- Visibility:
  - Hidden when `CurrentHp == MaxHp` AND not selected
  - Visible otherwise

Sprite dimensions ≈ 14 px wide × 2 px tall (or matching cell width).

## VFX: Death Burst

New component: `DeathBurst` (analogous to existing `ClickFeedback`)

- Spawned at death position
- Creates ~12 small red square sprites (3×3 px) as children
- Each particle:
  - Random outward velocity (radial, ~1.5–2.5 units/s magnitude)
  - Small upward bias (so it looks like a burst, not a circle on ground)
  - Slight gravity (-4 units/s²)
  - Alpha fade: 1 → 0 over 0.4 s
- Self-destroys after 0.5 s

Implementation can reuse the patterns from `ClickFeedback` (single GameObject, manages its children manually in `Update`).

## Testing (manual)

1. Spawn at least 2 Club Goblins (train at Barracks).
2. Select one Club, right-click on another Club → attacker moves to range and starts hitting.
3. Target's health bar appears and depletes; eventually target dies with the hop+burst animation.
4. Population counter decreases by 3 (Club PopCost) when a Club dies.
5. Select target Club FIRST, then attack with another → target auto-retaliates after taking the first hit.
6. Send Farmer to attack a Club → command is no-op (Farmer ignores).
7. Right-click on a Farmer with Clubs selected → command is no-op (Farmers can't be attacked in this step).

## Open Questions / Future Work

- Should Clubs auto-target the closest enemy when idle and one is in range? (Currently strict command + retaliate.)
- Should Farmers be attackable? (No in this step; revisit when ownership exists.)
- Particle pooling / object pooling for death bursts at scale.
- Death sound effect.
- Tooltip on health bar showing exact HP numbers.

# Combat Over Network — Design

**Status:** Spec
**Date:** 2026-05-25
**Roadmap item:** Multiplayer #6 — Combat over network (uses ownership filter from #4 and command/wire infrastructure from #5)

## Goal

Sync combat across all clients so every player sees identical HP bars and unit deaths. Attack commands propagate via the existing local-immediate + host-echo path. Per-hit damage events propagate from the attacker-owner to all other clients.

## Architecture

### Combat Gating per Ownership

The combat simulation already runs on every client because every Goblin has a local `Update` that ticks its own attack timer. To prevent duplicate damage:

- Movement, walk-to-target, hit animation: continue to run on every client (deterministic — driven by `SetAttackCommand` which is already synced via `CmdAttack`).
- **Damage application** (HP reduction + EnterDying transition): gated to the attacker's owner client only. The owner-client fires `EvDamage` on each successful hit, and all other clients receive + apply.

Gate rule (matches existing IsLocalOwner pattern):
```
isLocalOwner(g) = g.Owner == WorldStartContext.LocalPlayer || g.Owner == 0UL
```

### Authority per Attacker

Each goblin's owner is authoritative for that goblin's damage output. Two-attacker overlap (e.g., player A's Club and player B's Club both attacking the same target) works because:
- Each owner broadcasts their own `EvDamage` events.
- All clients apply HP reductions in arrival order.
- HP reduction is commutative — final state is identical regardless of order.

### Attack-Command Sync

`CmdAttack` is broadcast on right-click attack. Payload identifies attacker + target by NetId. All clients call `Goblin.SetAttackCommand(target)` on the attacker, which kicks off walk-to-target. Movement is then locally simulated on every client.

## Wire Protocol

Two new message types in `NetMessageType` enum + `NetWireFormat` constants:

| Type | Value | Payload (after `[byte type]`) | Sender |
|---|---|---|---|
| `CmdAttack` | 7 | `[NetId attacker (10B)][NetId target (10B)]` | Attacker-owner (issuer) |
| `EvDamage`  | 8 | `[NetId target (10B)][i32 damage (4B)][NetId attacker (10B)]` | Attacker-owner only |

Total `CmdAttack` payload: 1 + 10 + 10 = 21 bytes.
Total `EvDamage` payload:  1 + 10 + 4 + 10 = 25 bytes.

## Owner Validation

`CmdAttack`: receiver verifies `attacker.NetId.Owner == sender`. If mismatch → log warning + drop.

`EvDamage`: receiver verifies `attacker.NetId.Owner == sender`. If mismatch → log warning + drop. Only the attacker's owner can authoritate damage.

## Edge Cases

- **Target already destroyed:** `EvDamage` arrives but `GoblinNetRegistry.TryGet(targetId)` returns false → silently drop.
- **Target already in `State.Dying`:** `TakeDamage` early-returns (existing behavior). Second hit ignored.
- **Damage > remaining HP:** existing `TakeDamage` clamps HP to ≥ 0 and triggers `EnterDying`. No change.
- **Concurrent kills (two attackers, simultaneous fatal blows):** first arrival transitions target to Dying; subsequent EvDamage drops at the Dying-state guard.
- **Self-targeting:** Already blocked in `SetAttackCommand` (`if (target == this) return`).
- **Friendly fire:** Still allowed (Club hits any unit regardless of owner). Owner-validation only checks "sender owns attacker," not "victim belongs to different team."

## Solo Fallback

`NetCommandBridge.OutgoingSender` is `null` in solo. `NetCommandIssuer.IssueAttack` / `IssueDamage` apply locally + skip the wire send. Solo combat behavior identical to today.

## Data Flow Example — Player A's Club kills Player B's Club

1. Player A right-clicks Player B's Club → `GoblinSelectionController.CommandAttack` →
   `NetCommandIssuer.IssueAttack(attackerA, targetB)`:
   - Locally: `attackerA.SetAttackCommand(targetB)`
   - Sends `CmdAttack(attackerA.NetId, targetB.NetId)`
2. Player B's client receives `CmdAttack`. Validates owner. Calls `attackerA.SetAttackCommand(targetB)` on its local copy of attackerA.
3. Both clients simulate attackerA walking toward targetB.
4. Player A's client (only — gated by `isLocalOwner`): attack timer fires →
   `NetCommandIssuer.IssueDamage(targetB, damage, attackerA)`:
   - Locally: `targetB.TakeDamage(damage, attackerA)` (HP decreases)
   - Sends `EvDamage(targetB.NetId, damage, attackerA.NetId)`
5. Player B's client receives `EvDamage`. Validates owner. Calls `targetB.TakeDamage(damage, attackerA)` on its local copy. HP matches.
6. Eventually HP ≤ 0 on both clients (because both applied the same EvDamage). Both transition to `State.Dying`. Hop animation plays locally on each client.
7. Hop completes → goblin destroyed → `OnDisable` unregisters from `GoblinNetRegistry`.

## File Plan

### Modified files

| File | Change |
|---|---|
| `Assets/Scripts/Lobby/NetMessages.cs` | Add `CmdAttack = 7, EvDamage = 8` enum values |
| `Assets/Scripts/World/Unity/NetWireFormat.cs` | Add `CmdAttack = 7, EvDamage = 8` byte constants + `PackCmdAttack(WireNetIdLocal attacker, WireNetIdLocal target)` + `PackEvDamage(WireNetIdLocal target, int damage, WireNetIdLocal attacker)`. Receiver unpacks via manual `MemoryStream` + `BinaryReader` (same pattern as `CmdPlaceBuilding`). |
| `Assets/Scripts/World/Unity/NetCommandApplier.cs` | Add `ApplyAttack(GoblinNetId attackerId, GoblinNetId targetId, ulong sender)` + `ApplyDamage(GoblinNetId targetId, int damage, GoblinNetId attackerId, ulong sender)` + 2 new cases in `Apply(byte[], ulong)` dispatcher |
| `Assets/Scripts/World/Unity/NetCommandIssuer.cs` | Add `IssueAttack(Goblin attacker, Goblin target)` + `IssueDamage(Goblin target, int damage, Goblin attacker)` |
| `Assets/Scripts/World/Unity/Goblin.cs` | In the attack-tick branch where damage currently lands: gate by `IsLocalOwner`; replace direct `target.TakeDamage(...)` with `NetCommandIssuer.IssueDamage(target, AttackDamage, this)`. Add `IsLocalOwner` helper if not present. |
| `Assets/Scripts/World/Unity/GoblinSelectionController.cs` | `CommandAttack` foreach: replace `g.SetAttackCommand(target)` with `NetCommandIssuer.IssueAttack(g, target)` |
| `Assets/Scripts/Lobby/NetworkManager.cs` | Add `CmdAttack` and `EvDamage` to the existing command-route fallthrough block |

## Testing

- **Unit (pure-logic):** None — wire format pack/unpack is mechanically same as #5 and can be smoke-tested via the same temporary editor menu pattern if desired.
- **Editor-mode:** N/A (combat is MonoBehaviour-bound).
- **Manual smoke (solo):** Combat works exactly as before. Two test Clubs in MainBaseSetup attack each other; HP decreases on each hit; killing blow plays hop + particle burst.
- **Manual smoke (2 clients, Steam):**
  - Player A right-clicks Player B's Club → on both clients, A's attacker walks toward B's Club.
  - Per-hit: HP bars update on both clients in lockstep (within ~50-150ms relay latency).
  - Kill: both clients see hop arc + particle burst, then the unit despawns.
  - Concurrent: two attackers vs one target — HP decreases via both event streams; final death triggered by whichever hit lands the kill blow first on each client (commutative, so identical end state).

## Risks

| Risk | Mitigation |
|---|---|
| Per-hit EvDamage traffic in a large brawl | At 5-10 events/sec across the lobby with ~25 bytes each, this is < 0.5 KB/s — well within Steam relay throughput. |
| HP drift if EvDamage drops | Channel is Reliable (existing Steam Networking Sockets config) — no drops. |
| Late-arriving EvDamage after target despawned | TryGet returns false → silently drop. |
| Two-attacker race on kill | Dying-state guard in `TakeDamage` handles double-fatal. Outcome: identical death state on all clients (just animation source may differ). |
| Cheater faking damage on someone else's unit | Owner-validation: attacker.NetId.Owner must equal sender. Light anti-cheat (MVP). |

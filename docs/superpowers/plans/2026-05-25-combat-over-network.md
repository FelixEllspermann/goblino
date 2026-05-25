# Combat Over Network Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sync per-hit combat damage + attack commands across Steam P2P so all clients see identical HP bars and unit deaths.

**Architecture:** Each goblin's owner is authoritative for damage events. The combat sim runs on every client (walk-to-target + hit animation), but the actual `target.TakeDamage(...)` call only fires on the attacker's owner client, which broadcasts an `EvDamage` event applied by all remotes. Attack commands sync via `CmdAttack`. Reuses all wire-format / Bridge / Applier infrastructure from sub-project #5.

**Tech Stack:** Unity 6 / C# / Steamworks.NET / RTSCL.World + RTSCL.World.Unity + Assembly-CSharp asmdefs

**Spec:** `docs/superpowers/specs/2026-05-25-combat-over-network-design.md`

---

## File Plan

### Modified files

| File | Change |
|---|---|
| `Assets/Scripts/Lobby/NetMessages.cs` | Add `CmdAttack = 7`, `EvDamage = 8` to enum |
| `Assets/Scripts/World/Unity/NetWireFormat.cs` | Add 2 byte constants + 2 Pack methods |
| `Assets/Scripts/World/Unity/NetCommandApplier.cs` | Add `ApplyAttack`, `ApplyDamage` + 2 dispatcher cases |
| `Assets/Scripts/World/Unity/NetCommandIssuer.cs` | Add `IssueAttack(Goblin, Goblin)`, `IssueDamage(Goblin, int, Goblin)` |
| `Assets/Scripts/World/Unity/Goblin.cs` | Add `IsLocalOwner` helper; gate `Attacking` state's damage to local owner; route damage via Issuer |
| `Assets/Scripts/World/Unity/GoblinSelectionController.cs` | `CommandAttack` routes via `IssueAttack` |
| `Assets/Scripts/Lobby/NetworkManager.cs` | Add `CmdAttack` and `EvDamage` to existing command-route fallthrough |

---

## Task 1: NetMessageType enum extension

**Files:**
- Modify: `Assets/Scripts/Lobby/NetMessages.cs`

- [ ] **Step 1: Add 2 enum values**

Current enum (after sub-project #5):
```csharp
public enum NetMessageType : byte
{
    GameStart = 1,
    CmdMove = 2,
    CmdHarvest = 3,
    CmdBuildAssist = 4,
    CmdPlaceBuilding = 5,
    CmdTrainUnit = 6,
}
```

Replace with:
```csharp
public enum NetMessageType : byte
{
    GameStart = 1,
    CmdMove = 2,
    CmdHarvest = 3,
    CmdBuildAssist = 4,
    CmdPlaceBuilding = 5,
    CmdTrainUnit = 6,
    CmdAttack = 7,
    EvDamage = 8,
}
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `Unity_ReadConsole` Types=["Error"] AND FilterText="CS". Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Lobby/NetMessages.cs
git commit -m "feat(combat): NetMessageType CmdAttack + EvDamage enum values"
```

---

## Task 2: NetWireFormat — Pack methods for CmdAttack + EvDamage

**Files:**
- Modify: `Assets/Scripts/World/Unity/NetWireFormat.cs`

- [ ] **Step 1: Add the 2 byte constants**

Find the existing constants block:
```csharp
public const byte CmdMove = 2;
public const byte CmdHarvest = 3;
public const byte CmdBuildAssist = 4;
public const byte CmdPlaceBuilding = 5;
public const byte CmdTrainUnit = 6;
```

Append:
```csharp
public const byte CmdAttack = 7;
public const byte EvDamage = 8;
```

- [ ] **Step 2: Add `PackCmdAttack`**

Inside the `NetWireFormat` class, after `PackCmdTrainUnit` (the existing last Pack method), add:

```csharp
public static byte[] PackCmdAttack(WireNetIdLocal attacker, WireNetIdLocal target)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write(CmdAttack);
    w.Write(attacker.Owner); w.Write(attacker.LocalIndex);
    w.Write(target.Owner);   w.Write(target.LocalIndex);
    return ms.ToArray();
}
```

Total payload length: 1 + 10 + 10 = 21 bytes.

- [ ] **Step 3: Add `PackEvDamage`**

Below `PackCmdAttack`:

```csharp
public static byte[] PackEvDamage(WireNetIdLocal target, int damage, WireNetIdLocal attacker)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write(EvDamage);
    w.Write(target.Owner);   w.Write(target.LocalIndex);
    w.Write(damage);
    w.Write(attacker.Owner); w.Write(attacker.LocalIndex);
    return ms.ToArray();
}
```

Total payload length: 1 + 10 + 4 + 10 = 25 bytes.

- [ ] **Step 4: Refresh + compile check via Unity MCP**

`AssetDatabase.Refresh()`, then read console. Expect 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/NetWireFormat.cs
git commit -m "feat(combat): NetWireFormat Pack for CmdAttack + EvDamage"
```

---

## Task 3: NetCommandApplier — ApplyAttack + ApplyDamage + dispatcher

**Files:**
- Modify: `Assets/Scripts/World/Unity/NetCommandApplier.cs`

- [ ] **Step 1: Add `ApplyAttack` after `ApplyTrainUnit`**

Inside the `NetCommandApplier` static class, after the existing `ApplyTrainUnit` method, add:

```csharp
public static void ApplyAttack(GoblinNetId attackerId, GoblinNetId targetId, ulong sender)
{
    if (!GoblinNetRegistry.TryGet(attackerId, out var attacker) || attacker == null)
    {
        Debug.LogWarning($"[Net] ApplyAttack dropped: attacker {attackerId} not found");
        return;
    }
    if (sender != 0UL && attacker.NetId.Owner != sender)
    {
        Debug.LogWarning($"[Net] ApplyAttack dropped: sender {sender} != attacker.Owner {attacker.NetId.Owner}");
        return;
    }
    if (!GoblinNetRegistry.TryGet(targetId, out var target) || target == null) return;
    attacker.SetAttackCommand(target);
}
```

- [ ] **Step 2: Add `ApplyDamage` below `ApplyAttack`**

```csharp
public static void ApplyDamage(GoblinNetId targetId, int damage, GoblinNetId attackerId, ulong sender)
{
    if (sender != 0UL && attackerId.Owner != sender)
    {
        Debug.LogWarning($"[Net] ApplyDamage dropped: sender {sender} != attacker.Owner {attackerId.Owner}");
        return;
    }
    if (!GoblinNetRegistry.TryGet(targetId, out var target) || target == null) return;
    // attacker may not exist on this client (already destroyed) — pass null in that case
    GoblinNetRegistry.TryGet(attackerId, out var attacker);
    target.TakeDamage(damage, attacker);
}
```

- [ ] **Step 3: Add 2 new cases to the `Apply(byte[], ulong)` dispatcher**

Find the existing switch in `Apply(byte[] payload, ulong sender)`. After the `CmdTrainUnit` case, add:

```csharp
case NetWireFormat.CmdAttack:
    if (payload.Length >= 21)
    {
        using var ams = new System.IO.MemoryStream(payload, 1, payload.Length - 1);
        using var ar = new System.IO.BinaryReader(ams);
        ulong aOwn = ar.ReadUInt64(); ushort aIdx = ar.ReadUInt16();
        ulong tOwn = ar.ReadUInt64(); ushort tIdx = ar.ReadUInt16();
        ApplyAttack(new GoblinNetId(aOwn, aIdx), new GoblinNetId(tOwn, tIdx), sender);
    }
    break;
case NetWireFormat.EvDamage:
    if (payload.Length >= 25)
    {
        using var dms = new System.IO.MemoryStream(payload, 1, payload.Length - 1);
        using var dr = new System.IO.BinaryReader(dms);
        ulong tOwn = dr.ReadUInt64(); ushort tIdx = dr.ReadUInt16();
        int dmg = dr.ReadInt32();
        ulong aOwn = dr.ReadUInt64(); ushort aIdx = dr.ReadUInt16();
        ApplyDamage(new GoblinNetId(tOwn, tIdx), dmg, new GoblinNetId(aOwn, aIdx), sender);
    }
    break;
```

- [ ] **Step 4: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/NetCommandApplier.cs
git commit -m "feat(combat): NetCommandApplier ApplyAttack + ApplyDamage + dispatcher cases"
```

---

## Task 4: NetCommandIssuer — IssueAttack + IssueDamage

**Files:**
- Modify: `Assets/Scripts/World/Unity/NetCommandIssuer.cs`

- [ ] **Step 1: Add `IssueAttack` after `IssueTrainUnit`**

Inside the `NetCommandIssuer` static class, after the existing `IssueTrainUnit` method, add:

```csharp
public static void IssueAttack(Goblin attacker, Goblin target)
{
    if (attacker == null || target == null || attacker == target) return;
    if (attacker.AttackDamage <= 0) return; // non-combatant
    if (target.CurrentHp <= 0) return;      // already dying/dead

    // Local-immediate: set the attack target on the issuing client.
    attacker.SetAttackCommand(target);

    // Broadcast so all clients drive the same attacker → target assignment.
    var wAtk = new NetWireFormat.WireNetIdLocal(attacker.NetId.Owner, attacker.NetId.LocalIndex);
    var wTgt = new NetWireFormat.WireNetIdLocal(target.NetId.Owner, target.NetId.LocalIndex);
    NetCommandBridge.Send(NetWireFormat.PackCmdAttack(wAtk, wTgt));
}
```

- [ ] **Step 2: Add `IssueDamage` below `IssueAttack`**

```csharp
public static void IssueDamage(Goblin target, int damage, Goblin attacker)
{
    if (target == null || attacker == null) return;
    if (damage <= 0) return;

    // Local-immediate: apply damage on the attacker-owner client.
    target.TakeDamage(damage, attacker);

    // Broadcast so remotes apply the same HP reduction.
    var wTgt = new NetWireFormat.WireNetIdLocal(target.NetId.Owner, target.NetId.LocalIndex);
    var wAtk = new NetWireFormat.WireNetIdLocal(attacker.NetId.Owner, attacker.NetId.LocalIndex);
    NetCommandBridge.Send(NetWireFormat.PackEvDamage(wTgt, damage, wAtk));
}
```

- [ ] **Step 3: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/NetCommandIssuer.cs
git commit -m "feat(combat): NetCommandIssuer IssueAttack + IssueDamage"
```

---

## Task 5: Goblin.cs — Gate damage to local owner + route via Issuer

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Add `IsLocalOwner` private helper**

Near other private helpers (around `ChebyshevDistance`), add:

```csharp
private bool IsLocalOwner =>
    Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
```

- [ ] **Step 2: Replace the damage-dealing line in `State.Attacking`**

Current code at the attack-tick line (around line 316):

```csharp
_attackTimer = 0f;
_attackTarget.TakeDamage(AttackDamage, this);
StartHitAnim(new Vector3Int(
    Mathf.FloorToInt(_attackTarget.transform.position.x),
    Mathf.FloorToInt(_attackTarget.transform.position.y), 0));
```

Replace with:

```csharp
_attackTimer = 0f;
// Hit animation runs on every client (deterministic). Damage only fires on the
// attacker's owner client, which broadcasts the EvDamage that remotes apply.
StartHitAnim(new Vector3Int(
    Mathf.FloorToInt(_attackTarget.transform.position.x),
    Mathf.FloorToInt(_attackTarget.transform.position.y), 0));
if (IsLocalOwner)
    NetCommandIssuer.IssueDamage(_attackTarget, AttackDamage, this);
```

- [ ] **Step 3: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(combat): Goblin gates damage to local owner + routes via NetCommandIssuer"
```

---

## Task 6: GoblinSelectionController — CommandAttack routes via Issuer

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSelectionController.cs`

- [ ] **Step 1: Replace CommandAttack body**

Current:

```csharp
private void CommandAttack(Goblin target)
{
    foreach (var g in _selected)
        if (g != null && g.AttackDamage > 0)
            g.SetAttackCommand(target);
}
```

Replace with:

```csharp
private void CommandAttack(Goblin target)
{
    foreach (var g in _selected)
        if (g != null && g.AttackDamage > 0)
            NetCommandIssuer.IssueAttack(g, target);
}
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSelectionController.cs
git commit -m "feat(combat): selection controller routes attack-command through NetCommandIssuer"
```

---

## Task 7: NetworkManager — Extend RouteMessage

**Files:**
- Modify: `Assets/Scripts/Lobby/NetworkManager.cs`

- [ ] **Step 1: Add CmdAttack + EvDamage to the existing command fallthrough**

Find the existing block in `RouteMessage(CSteamID sender, byte[] payload, HSteamNetConnection senderConn)`:

```csharp
case NetMessageType.CmdMove:
case NetMessageType.CmdHarvest:
case NetMessageType.CmdBuildAssist:
case NetMessageType.CmdPlaceBuilding:
case NetMessageType.CmdTrainUnit:
    RTSCL.World.Unity.NetCommandApplier.Apply(payload, sender.m_SteamID);
    if (NetworkSession.IsHost)
        SendToOthers(payload, senderConn);
    break;
```

Replace with (just add 2 more case labels):

```csharp
case NetMessageType.CmdMove:
case NetMessageType.CmdHarvest:
case NetMessageType.CmdBuildAssist:
case NetMessageType.CmdPlaceBuilding:
case NetMessageType.CmdTrainUnit:
case NetMessageType.CmdAttack:
case NetMessageType.EvDamage:
    RTSCL.World.Unity.NetCommandApplier.Apply(payload, sender.m_SteamID);
    if (NetworkSession.IsHost)
        SendToOthers(payload, senderConn);
    break;
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Lobby/NetworkManager.cs
git commit -m "feat(combat): NetworkManager dispatches CmdAttack + EvDamage"
```

---

## Task 8: Final compile + manual smoke verification

**Files:** none changed.

- [ ] **Step 1: Full refresh + console clear via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

`Unity_ReadConsole` Types=["Error"] AND FilterText="CS". Expected: 0 entries.

- [ ] **Step 2: Run RTSCL.World.Tests**

Via Unity MCP TestRunnerApi. Expected: 36/36 pass (same as after sub-project #5; this sub-project adds no tests).

- [ ] **Step 3: Solo smoke**

User runs Play Solo. Expected behavior unchanged from current:
- 2 test Clubs spawn at game start (per MainBaseSetup)
- The Clubs auto-retaliate when attacked
- A Club kills the other: hop-arc death + red particle burst
- HP bars decrease in lockstep (because solo Owner=0, IsLocalOwner=true, damage fires locally and the wire-send is a no-op)

If any combat regression occurs in solo, the gate-by-IsLocalOwner is the suspect — verify `Owner == 0UL` is matched correctly.

- [ ] **Step 4: 2-client smoke (host + 1 over Steam)**

- Player A and Player B both host/join a Steam lobby; host starts game.
- Each sees 2 keeps + per-team starting units (Farmers + 2 test Clubs).
- Player A right-clicks Player B's Club with one of A's Clubs → both clients see A's Club walking toward B's Club.
- Per-hit: HP bars on B's Club decrease on BOTH clients in near-lockstep (~50-150ms relay latency).
- Killing blow: both clients see hop arc + particle burst; goblin despawns from both.
- Concurrent: A attacks B's Club AND B's other Club retaliates → HP on both attackers decreases via parallel EvDamage streams. Eventually one or both die — same final state on both clients.

If HP drifts visibly across clients (e.g., HP=5 on A but HP=10 on B), the damage-gate is broken: damage is firing on the non-owner client. Confirm `IsLocalOwner` is `false` for remote-owned units on each client.

- [ ] **Step 5: No commit unless a fix was needed**

If smoke passes: no further commit. The 7 previous commits cover the sub-project.

If a fix was needed: capture in a separate commit:

```bash
git commit -m "fix(combat): <specific>"
```

---

## Plan Self-Review Notes

- All 7 spec items in the file plan map to a task (1-7).
- Wire-format sizes: 21 (CmdAttack: 1+10+10), 25 (EvDamage: 1+10+4+10). Dispatcher length-guards in Task 3 match.
- Owner-validation: both `ApplyAttack` and `ApplyDamage` check `sender != 0UL && expected.Owner != sender`. Solo path (sender=0) skips the check (consistent with existing pattern in #5).
- Gating: damage fires only when `IsLocalOwner` is true (Task 5). Hit animation runs on every client (still inside the switch, before the `if (IsLocalOwner)` block).
- `Goblin.TakeDamage` already early-returns on `CurrentHp <= 0` and triggers `EnterDying` at HP ≤ 0. No changes there.
- `SetAttackCommand` already guards self-targeting and dying state. No changes there.
- `NetCommandIssuer.IssueAttack` early-returns on null/self/non-combatant/dead, so `CommandAttack` doesn't need to repeat those guards (they were already absent in the existing code, this is consistent).
- Solo fallback is a no-op on the wire because `NetCommandBridge.OutgoingSender == null`. Local damage application still happens because the issuer applies BEFORE sending.
- The `NetworkManager` dispatch is a 2-line change (add 2 case labels). No other RouteMessage changes.

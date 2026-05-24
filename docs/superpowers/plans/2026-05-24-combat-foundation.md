# Combat Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Local single-player Club-vs-Club melee combat with HP, auto-retaliate, death hop-arc + red particle burst, and floating health bars.

**Architecture:** Combat stats live on `GoblinUnitDefinition` (per-kind). The `Goblin` MonoBehaviour gains HP fields, combat states (`MovingToAttack`, `Attacking`, `Dying`), and a `TakeDamage` API. Auto-retaliate runs inside `TakeDamage`. Death replaces instant-destroy with a `Dying` state that arcs the sprite up, falls under gravity, then spawns a `DeathBurst` particle component on impact. Health bars are a child component (`GoblinHealthBar`) with two stacked `SpriteRenderer`s.

**Tech Stack:** Unity 6 / URP 2D / C# / Unity MCP for editor automation.

**Spec:** `docs/superpowers/specs/2026-05-24-combat-foundation-design.md`

---

## Verification Convention

Most tasks are Unity gameplay code that can't be unit-tested. Each task ends with:
1. **Compile check via MCP** — call `Unity_RunCommand` running `AssetDatabase.Refresh()`, then `Unity_ReadConsole` for errors.
2. **Play-mode verification** — explicit manual steps the engineer/agent executes in the editor (Unity_RunCommand can enter Play mode and capture screenshots if needed).
3. **Commit** — single focused commit per task.

---

## Task 1: Extend GoblinUnitDefinition with combat stats

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinUnitDefinition.cs`
- Modify (via MCP): `Assets/Generated/Units/ClubGoblin.asset`
- Modify (via MCP): `Assets/Generated/Units/FarmerGoblin.asset`

- [ ] **Step 1: Add 4 combat fields to the ScriptableObject**

Edit `Assets/Scripts/World/Unity/GoblinUnitDefinition.cs`. Insert these fields after the existing `PopulationCost`:

```csharp
public int MaxHp = 20;
public int AttackDamage = 0;            // 0 = non-combatant
public float AttackInterval = 1.5f;
public int AttackRange = 1;             // cells, Chebyshev distance
```

The full file becomes:

```csharp
using UnityEngine;

namespace RTSCL.World.Unity
{
    [CreateAssetMenu(menuName = "RTSCL/Goblin Unit", fileName = "Goblin")]
    public sealed class GoblinUnitDefinition : ScriptableObject
    {
        public string DisplayName;
        public Sprite Icon;
        public int WoodCost = 50;
        public int PopulationCost = 1;
        public int MaxHp = 20;
        public int AttackDamage = 0;
        public float AttackInterval = 1.5f;
        public int AttackRange = 1;
        [Tooltip("Seconds to produce one unit at a keep")]
        public float SpawnDuration = 3f;
        [Tooltip("Name must match a GoblinSpawner kind so walk frames are applied")]
        public string SpawnerKindName;
    }
}
```

- [ ] **Step 2: Verify compile via MCP**

```csharp
// Unity_RunCommand body
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        AssetDatabase.Refresh();
        result.Log("Refreshed");
    }
}
```

Then `Unity_ReadConsole` with `Types=["Error"]`. Expected: no errors.

- [ ] **Step 3: Set combat values on the two unit assets via MCP**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.World.Unity;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var club = AssetDatabase.LoadAssetAtPath<GoblinUnitDefinition>("Assets/Generated/Units/ClubGoblin.asset");
        var farmer = AssetDatabase.LoadAssetAtPath<GoblinUnitDefinition>("Assets/Generated/Units/FarmerGoblin.asset");

        var soClub = new SerializedObject(club);
        soClub.FindProperty("MaxHp").intValue = 40;
        soClub.FindProperty("AttackDamage").intValue = 5;
        soClub.FindProperty("AttackInterval").floatValue = 1.5f;
        soClub.FindProperty("AttackRange").intValue = 1;
        soClub.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(club);

        var soFar = new SerializedObject(farmer);
        soFar.FindProperty("MaxHp").intValue = 20;
        soFar.FindProperty("AttackDamage").intValue = 0;
        soFar.FindProperty("AttackInterval").floatValue = 1.5f;
        soFar.FindProperty("AttackRange").intValue = 1;
        soFar.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(farmer);

        AssetDatabase.SaveAssets();
        result.Log("Club: HP=40 dmg=5; Farmer: HP=20 dmg=0");
    }
}
```

Expected console output: `Club: HP=40 dmg=5; Farmer: HP=20 dmg=0`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinUnitDefinition.cs Assets/Generated/Units/ClubGoblin.asset Assets/Generated/Units/FarmerGoblin.asset
git commit -m "feat(world.unity): combat stats on GoblinUnitDefinition (HP, damage, interval, range)"
```

---

## Task 2: Wire GoblinUnitDefinition through GoblinSpawner → Goblin.Init

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSpawner.cs`
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`
- Modify (via MCP): `Assets/Scenes/SampleScene.unity` (wire each kind's `Definition`)

**Approach:** Extend `GoblinSpawner.GoblinKind` with a `Definition` reference. `SpawnAt` passes it to `Goblin.Init`. `Goblin` stores HP and combat stats from the def (with safe defaults if def is null, for backward compat with un-wired kinds like Archer/Spear).

- [ ] **Step 1: Extend GoblinKind with Definition reference**

In `Assets/Scripts/World/Unity/GoblinSpawner.cs`, change the inner `GoblinKind` class:

```csharp
[System.Serializable]
public class GoblinKind
{
    public string Name = "Goblin";
    public Sprite[] WalkFrames;
    public GoblinUnitDefinition Definition;
}
```

- [ ] **Step 2: Pass Definition to Goblin.Init in SpawnAt**

In `GoblinSpawner.SpawnAt`, change the `Init` call to also pass the kind's definition:

```csharp
goblin.Init(kind.Name, kind.WalkFrames, _terrainMap, _decorationMap, kind.Definition);
```

- [ ] **Step 3: Add HP + combat fields to Goblin, update Init signature**

In `Assets/Scripts/World/Unity/Goblin.cs`, add fields after `public string Kind`:

```csharp
public int  CurrentHp { get; private set; }
public int  MaxHp { get; private set; } = 20;
public int  PopulationCost { get; private set; } = 1;
public int  AttackDamage { get; private set; }
public float AttackInterval { get; private set; } = 1.5f;
public int  AttackRange { get; private set; } = 1;
```

Update the `Init` method signature and body:

```csharp
public void Init(string kind, Sprite[] walkFrames, Tilemap terrainMap, Tilemap decorationMap,
                 GoblinUnitDefinition def = null)
{
    Kind = kind;
    _frames = walkFrames;
    _terrainMap = terrainMap;
    _decorationMap = decorationMap;
    _renderer = GetComponent<SpriteRenderer>();
    _renderer.sprite = walkFrames[0];
    _moveTarget = transform.position;

    if (def != null)
    {
        MaxHp = def.MaxHp;
        PopulationCost = def.PopulationCost;
        AttackDamage = def.AttackDamage;
        AttackInterval = def.AttackInterval;
        AttackRange = def.AttackRange;
    }
    CurrentHp = MaxHp;

    BuildSelectionRing();
}
```

- [ ] **Step 4: Register in `Goblin.All` only after Init**

In `Goblin.cs`, find the `OnEnable`/`Awake`/registration. Confirm goblins are added to `Goblin.All` (likely in `OnEnable`). No change needed — `All` already holds the live list.

- [ ] **Step 5: Compile check via MCP**

Run `AssetDatabase.Refresh()` then `Unity_ReadConsole` for errors. Expected: clean.

- [ ] **Step 6: Wire scene — point each GoblinKind's Definition to its asset**

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using RTSCL.World.Unity;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var club = AssetDatabase.LoadAssetAtPath<GoblinUnitDefinition>("Assets/Generated/Units/ClubGoblin.asset");
        var farmer = AssetDatabase.LoadAssetAtPath<GoblinUnitDefinition>("Assets/Generated/Units/FarmerGoblin.asset");
        var spawner = Object.FindFirstObjectByType<GoblinSpawner>(FindObjectsInactive.Include);
        var so = new SerializedObject(spawner);
        var kinds = so.FindProperty("_kinds");
        int wired = 0;
        for (int i = 0; i < kinds.arraySize; i++)
        {
            var elem = kinds.GetArrayElementAtIndex(i);
            string name = elem.FindPropertyRelative("Name").stringValue;
            var defProp = elem.FindPropertyRelative("Definition");
            if (name == "ClubGoblin")   { defProp.objectReferenceValue = club;   wired++; }
            if (name == "FarmerGoblin") { defProp.objectReferenceValue = farmer; wired++; }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("Wired Definition on {0} kinds", wired);
    }
}
```

Expected log: `Wired Definition on 2 kinds`.

- [ ] **Step 7: Play-mode verification via MCP**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.World.Unity;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play mode — re-run after a moment"); return; }
        // Wait a frame, then inspect goblins
        int farmers = 0;
        foreach (var g in Goblin.All)
        {
            if (g.Kind == "FarmerGoblin") farmers++;
            result.Log("{0}: HP={1}/{2} dmg={3}", g.Kind, g.CurrentHp, g.MaxHp, g.AttackDamage);
        }
        result.Log("Farmers spawned: {0}", farmers);
    }
}
```

Run twice if first call entered play mode. Expected output: 5 farmers with HP=20/20, dmg=0.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSpawner.cs Assets/Scripts/World/Unity/Goblin.cs Assets/Scenes/SampleScene.unity
git commit -m "feat(world.unity): Goblin gets HP + combat stats from GoblinUnitDefinition"
```

---

## Task 3: TakeDamage method (HP only, no death yet)

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Add TakeDamage method**

In `Goblin.cs`, add inside the class:

```csharp
public void TakeDamage(int damage, Goblin attacker)
{
    if (CurrentHp <= 0) return;
    CurrentHp = Mathf.Max(0, CurrentHp - damage);
    // Death + retaliate handled in later tasks.
}
```

- [ ] **Step 2: Compile check via MCP**

Same as Task 1 Step 2. Expected: clean.

- [ ] **Step 3: Play-mode verification — damage a goblin from console**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.World.Unity;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play mode"); return; }
        if (Goblin.All.Count == 0) { result.LogError("No goblins alive"); return; }
        var g = Goblin.All[0];
        int before = g.CurrentHp;
        g.TakeDamage(7, null);
        result.Log("Goblin HP: {0} -> {1} (expected -7)", before, g.CurrentHp);
    }
}
```

Expected: HP decreased by 7 (e.g., 20 → 13).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(world.unity): Goblin.TakeDamage subtracts HP"
```

---

## Task 4: Attack states (MovingToAttack, Attacking) + SetAttackCommand

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Add states to the enum and target/timer fields**

In `Goblin.cs`, replace the State enum:

```csharp
private enum State { Idle, MovingToPoint, MovingToTree, Harvesting, MovingToBuild, Building, MovingToAttack, Attacking }
```

Add private fields near other state fields:

```csharp
private Goblin _attackTarget;
private float _attackTimer;
```

- [ ] **Step 2: Add SetAttackCommand**

In `Goblin.cs`, add public method (near `SetHarvestCommand`):

```csharp
public void SetAttackCommand(Goblin target)
{
    if (target == null || target == this) return;
    if (AttackDamage <= 0) return;   // non-combatants (Farmers) ignore
    if (target.CurrentHp <= 0) return;
    ResetHitAnim();
    _attackTarget = target;
    _attackTimer = 0f;
    _state = State.MovingToAttack;
}
```

- [ ] **Step 3: Add a Chebyshev helper**

```csharp
private static int ChebyshevDistance(Vector3 a, Vector3 b)
{
    int dx = Mathf.Abs(Mathf.FloorToInt(a.x) - Mathf.FloorToInt(b.x));
    int dy = Mathf.Abs(Mathf.FloorToInt(a.y) - Mathf.FloorToInt(b.y));
    return Mathf.Max(dx, dy);
}
```

- [ ] **Step 4: Add state handling in the Update switch**

Locate `Goblin.Update` (or wherever the state switch lives). Add two new cases:

```csharp
case State.MovingToAttack:
    if (_attackTarget == null || _attackTarget.CurrentHp <= 0) { _state = State.Idle; break; }
    if (ChebyshevDistance(transform.position, _attackTarget.transform.position) <= AttackRange)
    {
        _state = State.Attacking;
        _attackTimer = AttackInterval; // first hit immediately
        break;
    }
    StepTowards(_attackTarget.transform.position);
    break;

case State.Attacking:
    if (_attackTarget == null || _attackTarget.CurrentHp <= 0) { _state = State.Idle; break; }
    if (ChebyshevDistance(transform.position, _attackTarget.transform.position) > AttackRange)
    {
        _state = State.MovingToAttack;
        break;
    }
    _attackTimer += Time.deltaTime;
    if (_attackTimer >= AttackInterval)
    {
        _attackTimer = 0f;
        _attackTarget.TakeDamage(AttackDamage, this);
        StartHitAnim(new Vector3Int(
            Mathf.FloorToInt(_attackTarget.transform.position.x),
            Mathf.FloorToInt(_attackTarget.transform.position.y), 0));
    }
    UpdateHitAnim();
    break;
```

**Note:** `StepTowards` is the existing per-frame movement helper Goblin uses for MovingToPoint/MovingToTree. If the existing code does movement inline rather than via a helper, follow the existing pattern instead — the key is "move one step toward target each frame". If a helper exists by another name (e.g. `MoveToward`), use that.

- [ ] **Step 5: Compile check via MCP**

Same as Task 1 Step 2. Expected: clean.

- [ ] **Step 6: Play-mode verification — hardcode an attack**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.World.Unity;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play mode"); return; }
        // Find two goblins (any kind) and set one to attack the other.
        // Note: starting goblins are Farmers (dmg=0) so SetAttackCommand will no-op.
        // For a real test, spawn 2 Clubs first via the Keep — skip this verify if no Clubs alive.
        Goblin attacker = null, victim = null;
        foreach (var g in Goblin.All)
        {
            if (g.AttackDamage > 0 && attacker == null) attacker = g;
            else if (g != attacker) victim = g;
        }
        if (attacker == null) { result.LogWarning("No Club Goblin alive to test — train one at the Keep first"); return; }
        attacker.SetAttackCommand(victim);
        result.Log("{0} now attacking {1} (HP {2})", attacker.Kind, victim.Kind, victim.CurrentHp);
    }
}
```

Expected: log shows the assignment; watching the scene, the Club walks toward the target and begins hitting on a 1.5s cadence. Victim HP decreases in the next verify run.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(world.unity): Goblin attack states + SetAttackCommand"
```

---

## Task 5: Selection controller — right-click on Goblin → CommandAttack

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSelectionController.cs`

- [ ] **Step 1: Add TryGetGoblinAt helper**

In `GoblinSelectionController.cs`, add inside the class:

```csharp
private bool TryGetGoblinAt(Vector3 worldPos, out Goblin target)
{
    target = null;
    float bestDistSq = _clickPickRadius * _clickPickRadius;
    foreach (var g in Goblin.All)
    {
        if (g == null) continue;
        // Don't pick a goblin that is currently selected (so right-click on your own selection
        // doesn't accidentally target one of your own as the victim).
        if (_selected.Contains(g)) continue;
        float d = (g.transform.position - worldPos).sqrMagnitude;
        if (d < bestDistSq) { bestDistSq = d; target = g; }
    }
    return target != null;
}
```

- [ ] **Step 2: Add CommandAttack**

```csharp
private void CommandAttack(Goblin target)
{
    foreach (var g in _selected)
        if (g != null && g.AttackDamage > 0)
            g.SetAttackCommand(target);
}
```

- [ ] **Step 3: Update right-click priority in Update**

Locate the right-click handling block (already prioritises tree → construction → move). Insert the Goblin check between construction and move:

```csharp
if (TryGetTreeAt(worldTarget, out var treeCell))
{
    Vector3 treeCenter = _decorationMap.CellToWorld(treeCell) + new Vector3(0.5f, 0.5f, 0f);
    ClickFeedback.Spawn(treeCenter, new Color(0.4f, 1f, 0.4f, 0.85f));
    CommandHarvest(treeCell);
}
else if (TryGetConstructionAt(worldTarget, out var buildOrigin))
{
    Vector3 c = new(buildOrigin.x + 0.5f, buildOrigin.y + 0.5f, 0f);
    ClickFeedback.Spawn(c, new Color(1f, 0.7f, 0.2f, 0.9f));
    foreach (var g in _selected)
        if (IsWorker(g)) g.SetBuildCommand(buildOrigin);
}
else if (TryGetGoblinAt(worldTarget, out var enemy))
{
    ClickFeedback.Spawn(enemy.transform.position, new Color(1f, 0.3f, 0.3f, 0.9f)); // red = attack
    CommandAttack(enemy);
}
else
{
    ClickFeedback.Spawn(worldTarget, new Color(1f, 1f, 1f, 0.85f));
    CommandFormation(worldTarget);
}
```

- [ ] **Step 4: Compile check via MCP**

Same as Task 1 Step 2.

- [ ] **Step 5: Play-mode verification — manual**

Steps for the user (or via MCP entering Play mode):
1. Start Play.
2. Build a Barracks (300 wood — actually 500; check ResourceUI). Wait for completion.
3. Train 2 Club Goblins (150 wood, 3 pop each).
4. Select one Club (left-click drag).
5. Right-click the other Club.
6. **Expected:** red click feedback at target, attacker walks over, target HP starts dropping.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSelectionController.cs
git commit -m "feat(world.unity): right-click on enemy goblin issues attack to selected Clubs"
```

---

## Task 6: Auto-retaliate when idle and attacked

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Expose IsIdle**

In `Goblin.cs`, add a public read-only check (use whatever the enum value is for Idle — likely `State.Idle`):

```csharp
public bool IsIdle => _state == State.Idle;
```

- [ ] **Step 2: Trigger retaliation inside TakeDamage**

Update `TakeDamage`:

```csharp
public void TakeDamage(int damage, Goblin attacker)
{
    if (CurrentHp <= 0) return;
    CurrentHp = Mathf.Max(0, CurrentHp - damage);
    // Death handled in Task 7.
    if (CurrentHp <= 0) return;

    // Auto-retaliate: only when idle and capable
    if (IsIdle && AttackDamage > 0 && attacker != null && attacker.CurrentHp > 0)
        SetAttackCommand(attacker);
}
```

- [ ] **Step 3: Compile check via MCP**

Expected: clean.

- [ ] **Step 4: Play-mode verification — manual**

1. With 2 Clubs alive, select **only the first** and right-click the second.
2. After the first hit lands, the second Club (which was idle) should turn and attack back.
3. Expected: both Clubs trade blows until one dies.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(world.unity): goblins auto-retaliate when idle and attacked"
```

---

## Task 7: Death state (no animation yet) + free population

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Add Dying to the enum**

```csharp
private enum State { Idle, MovingToPoint, MovingToTree, Harvesting, MovingToBuild, Building, MovingToAttack, Attacking, Dying }
```

- [ ] **Step 2: Add EnterDying method**

```csharp
private void EnterDying()
{
    if (_state == State.Dying) return;
    _state = State.Dying;
    // Free population
    PopulationManager.RemoveUsed(PopulationCost);
    // Drop from selection + active list
    Goblin.All.Remove(this);
    SetSelected(false);
    // For now: instant destroy. Animation comes in Task 8.
    Destroy(gameObject);
}
```

**Note:** `Goblin.All.Remove(this)` modifies the static list during enumeration if any code is iterating it. Verify the selection controller's right-click handler tolerates targets disappearing mid-iteration — `TakeDamage` is called during `Attacking` state in the attacker's own Update, which means iteration over `Goblin.All` is not happening there.

- [ ] **Step 3: Wire EnterDying into TakeDamage**

```csharp
public void TakeDamage(int damage, Goblin attacker)
{
    if (CurrentHp <= 0) return;
    CurrentHp = Mathf.Max(0, CurrentHp - damage);
    if (CurrentHp <= 0) { EnterDying(); return; }

    if (IsIdle && AttackDamage > 0 && attacker != null && attacker.CurrentHp > 0)
        SetAttackCommand(attacker);
}
```

- [ ] **Step 4: Compile check via MCP**

Expected: clean.

- [ ] **Step 5: Play-mode verification — manual**

1. With 2 Clubs fighting (from Task 6 setup), watch one die.
2. Expected: dying Club disappears immediately, Population counter decreases by 3.
3. Verify via MCP after a few seconds:

```csharp
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        result.Log("Population: {0} / {1}", PopulationManager.Used, PopulationManager.Cap);
        result.Log("Goblins alive: {0}", Goblin.All.Count);
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(world.unity): goblin death state frees population and despawns"
```

---

## Task 8: Death animation arc (hop + gravity, no burst yet)

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Add death-physics fields**

```csharp
private Vector3 _dieStartPos;
private Vector3 _dieVelocity;
private const float DeathGravity = -10f;
private const float DeathHopVelocity = 3f;
```

- [ ] **Step 2: Replace EnterDying body — launch instead of instant destroy**

```csharp
private void EnterDying()
{
    if (_state == State.Dying) return;
    _state = State.Dying;
    PopulationManager.RemoveUsed(PopulationCost);
    Goblin.All.Remove(this);
    SetSelected(false);

    _dieStartPos = transform.position;
    _dieVelocity = new Vector3(0f, DeathHopVelocity, 0f);
}
```

- [ ] **Step 3: Add Dying case to the Update switch**

```csharp
case State.Dying:
    _dieVelocity.y += DeathGravity * Time.deltaTime;
    transform.position += _dieVelocity * Time.deltaTime;
    if (transform.position.y <= _dieStartPos.y && _dieVelocity.y <= 0f)
    {
        // Snap to ground, destroy. Burst comes in Task 9.
        transform.position = _dieStartPos;
        Destroy(gameObject);
    }
    break;
```

- [ ] **Step 4: Compile check via MCP**

Expected: clean.

- [ ] **Step 5: Play-mode verification — manual**

1. Trigger a fight, watch one Club die.
2. Expected: dying Club hops up ~0.5–1.5 cells, falls back, then disappears.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(world.unity): goblin death hop-arc animation"
```

---

## Task 9: DeathBurst red particle component

**Files:**
- Create: `Assets/Scripts/World/Unity/DeathBurst.cs`
- Create: `Assets/Scripts/World/Unity/DeathBurst.cs.meta`
- Modify: `Assets/Scripts/World/Unity/Goblin.cs` (spawn burst on landing)

- [ ] **Step 1: Create DeathBurst.cs**

```csharp
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Short-lived red particle burst at the given world position.</summary>
    public sealed class DeathBurst : MonoBehaviour
    {
        private const int   ParticleCount = 12;
        private const float BurstLifetime = 0.5f;
        private const float ParticleFadeOver = 0.4f;
        private const float SpeedMin = 1.5f;
        private const float SpeedMax = 2.5f;
        private const float UpwardBias = 0.6f;
        private const float Gravity = -4f;

        private struct Particle
        {
            public Transform T;
            public SpriteRenderer SR;
            public Vector3 Velocity;
        }

        private Particle[] _particles;
        private float _age;
        private static Sprite s_sprite;

        public static DeathBurst Spawn(Vector3 worldPos)
        {
            var go = new GameObject("DeathBurst");
            go.transform.position = worldPos;
            return go.AddComponent<DeathBurst>();
        }

        private void Awake()
        {
            if (s_sprite == null) s_sprite = BuildSquareSprite();
            _particles = new Particle[ParticleCount];
            for (int i = 0; i < ParticleCount; i++)
            {
                var p = new GameObject($"P{i}");
                p.transform.SetParent(transform, false);
                var sr = p.AddComponent<SpriteRenderer>();
                sr.sprite = s_sprite;
                sr.color = new Color(0.95f, 0.15f, 0.15f, 1f);
                sr.sortingOrder = 50;
                p.transform.localScale = Vector3.one * 0.15f;
                float angle = (i / (float)ParticleCount) * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                float speed = Random.Range(SpeedMin, SpeedMax);
                Vector3 v = new(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed + UpwardBias, 0f);
                _particles[i] = new Particle { T = p.transform, SR = sr, Velocity = v };
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= BurstLifetime) { Destroy(gameObject); return; }
            float alpha = Mathf.Clamp01(1f - _age / ParticleFadeOver);
            for (int i = 0; i < _particles.Length; i++)
            {
                var p = _particles[i];
                p.Velocity.y += Gravity * Time.deltaTime;
                p.T.position += p.Velocity * Time.deltaTime;
                var c = p.SR.color; c.a = alpha; p.SR.color = c;
                _particles[i] = p;
            }
        }

        private static Sprite BuildSquareSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
```

- [ ] **Step 2: Create DeathBurst.cs.meta**

```yaml
fileFormatVersion: 2
guid: b9e46073cead8f72e64df210f9f7282f
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Spawn burst on landing in Goblin.cs**

In the `case State.Dying:` block in `Goblin.Update`, replace the `Destroy(gameObject)` line with:

```csharp
case State.Dying:
    _dieVelocity.y += DeathGravity * Time.deltaTime;
    transform.position += _dieVelocity * Time.deltaTime;
    if (transform.position.y <= _dieStartPos.y && _dieVelocity.y <= 0f)
    {
        transform.position = _dieStartPos;
        DeathBurst.Spawn(transform.position);
        Destroy(gameObject);
    }
    break;
```

- [ ] **Step 4: Compile check via MCP**

Expected: clean.

- [ ] **Step 5: Play-mode verification — manual**

1. Trigger a fight, watch a Club die.
2. Expected: hop arc + red particle burst on landing, particles fly outward, fade, gone within ~0.5s.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/DeathBurst.cs Assets/Scripts/World/Unity/DeathBurst.cs.meta Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(world.unity): DeathBurst red particle effect on goblin death"
```

---

## Task 10: GoblinHealthBar component

**Files:**
- Create: `Assets/Scripts/World/Unity/GoblinHealthBar.cs`
- Create: `Assets/Scripts/World/Unity/GoblinHealthBar.cs.meta`
- Modify: `Assets/Scripts/World/Unity/Goblin.cs` (instantiate health bar in Init)

- [ ] **Step 1: Create GoblinHealthBar.cs**

```csharp
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Floating HP bar above a Goblin. Hidden when HP is full and not selected.</summary>
    public sealed class GoblinHealthBar : MonoBehaviour
    {
        private const float BarWidth = 0.75f;
        private const float BarHeight = 0.10f;
        private const float YOffset = 0.85f;

        private Goblin _goblin;
        private SpriteRenderer _bg;
        private SpriteRenderer _fill;
        private Transform _fillT;
        private static Sprite s_sprite;

        public static GoblinHealthBar AttachTo(Goblin owner)
        {
            var go = new GameObject("HealthBar");
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = new Vector3(0f, YOffset, 0f);
            var hb = go.AddComponent<GoblinHealthBar>();
            hb._goblin = owner;
            return hb;
        }

        private void Awake()
        {
            if (s_sprite == null) s_sprite = BuildSquareSprite();

            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(transform, false);
            _bg = bgGo.AddComponent<SpriteRenderer>();
            _bg.sprite = s_sprite;
            _bg.color = new Color(0.12f, 0.05f, 0.05f, 0.9f);
            _bg.sortingOrder = 28;
            bgGo.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            _fill = fillGo.AddComponent<SpriteRenderer>();
            _fill.sprite = s_sprite;
            _fill.sortingOrder = 29;
            _fillT = fillGo.transform;
            _fillT.localScale = new Vector3(BarWidth, BarHeight, 1f);
            // Anchor fill at left edge of background so scaling shrinks from the right.
            _fillT.localPosition = new Vector3(-BarWidth * 0.5f, 0f, 0f);
            // Use a unit-square sprite with bottom-left pivot for left-anchored shrink:
            // implemented via local-position offset on each LateUpdate.
        }

        private void LateUpdate()
        {
            if (_goblin == null) { Destroy(gameObject); return; }
            float frac = _goblin.MaxHp > 0 ? (float)_goblin.CurrentHp / _goblin.MaxHp : 0f;
            bool show = frac < 1f || _goblin.IsSelected;
            _bg.enabled = show;
            _fill.enabled = show;
            if (!show) return;

            // Scale fill width and shift so it stays left-anchored
            _fillT.localScale = new Vector3(BarWidth * frac, BarHeight, 1f);
            _fillT.localPosition = new Vector3(-BarWidth * 0.5f + (BarWidth * frac) * 0.5f, 0f, 0f);

            // Green → yellow → red
            Color c = frac > 0.5f
                ? Color.Lerp(new Color(0.9f, 0.85f, 0.2f), new Color(0.3f, 0.85f, 0.3f), (frac - 0.5f) * 2f)
                : Color.Lerp(new Color(0.85f, 0.2f, 0.2f), new Color(0.9f, 0.85f, 0.2f), frac * 2f);
            _fill.color = c;
        }

        private static Sprite BuildSquareSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
```

- [ ] **Step 2: Create GoblinHealthBar.cs.meta**

```yaml
fileFormatVersion: 2
guid: c0f57184dfbc9e83f75ef321faa8393f
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
```

- [ ] **Step 3: Attach health bar in Goblin.Init**

In `Goblin.cs`, end of `Init`, after `BuildSelectionRing();`:

```csharp
GoblinHealthBar.AttachTo(this);
```

- [ ] **Step 4: Compile check via MCP**

Expected: clean.

- [ ] **Step 5: Play-mode verification — manual**

1. Start Play.
2. Confirm: no health bars visible on full-HP goblins.
3. Select one Farmer → its health bar appears (full green).
4. Start a Club fight → both Clubs show shrinking bars (green → yellow → red as HP drops).
5. Killed goblin: bar disappears with the goblin during the hop arc.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinHealthBar.cs Assets/Scripts/World/Unity/GoblinHealthBar.cs.meta Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(world.unity): floating health bar above goblins"
```

---

## Self-Review Checklist

- [x] All spec sections covered:
  - Data Model → Tasks 1, 2
  - Behavior (attack states, retaliate) → Tasks 4, 5, 6
  - Death sequence → Tasks 7, 8, 9
  - UI health bar → Task 10
- [x] No placeholders (TBD/TODO/etc.)
- [x] Type consistency: `MaxHp`/`CurrentHp`/`AttackDamage`/`AttackInterval`/`AttackRange`/`PopulationCost` used uniformly across tasks
- [x] Each task ends with a single commit
- [x] Manual verification steps are concrete and runnable via MCP

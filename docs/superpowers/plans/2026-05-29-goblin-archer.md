# Goblin Archer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`).

**Goal:** Add a ranged Goblin Archer trained at the Barracks that shoots an arrow projectile dealing damage on impact.

**Architecture:** Data-driven. New `Sprite ProjectileSprite` field on `GoblinUnitDefinition` (non-null = ranged). New `Arrow` MonoBehaviour homing to the target; owner applies damage on impact via existing `IssueDamage`/`EvDamage` (no new wire message). `Goblin.Attacking` branches ranged vs melee. The `ArcherGoblin` sprite kind is already wired in the scene's GoblinSpawner — only its `Definition` asset is missing.

**Tech Stack:** Unity 6000.4.3f1, C#, URP 2D. Verification: Unity MCP `Unity_RunCommand` (`AssetDatabase.Refresh()`) + `Unity_ReadConsole` (Errors == 0); manual play-mode smoke. `IRunCommand` signature is `void Execute(ExecutionResult result)` with `result.Log(...)`. Class MUST be `internal class CommandScript`.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `Assets/Scripts/World/Unity/GoblinUnitDefinition.cs` | Modify | Add `ProjectileSprite` field (ranged marker + projectile art). |
| `Assets/Scripts/World/Unity/Arrow.cs` | **Create** | Flying projectile; owner-authoritative damage on impact. |
| `Assets/Scripts/World/Unity/Goblin.cs` | Modify | Store `_projectileSprite`; ranged branch in `Attacking`; face-target helper. |
| `Assets/Generated/Units/GoblinArcher.asset` | **Create (MCP)** | Archer stat block + icon + projectile + spawner name. |
| `Assets/Scenes/SampleScene.unity` | Modify (MCP) | Assign archer Definition to GoblinSpawner `ArcherGoblin` kind. |
| `Assets/Generated/Buildings/Barracks_0.asset` | Modify (MCP) | Append archer to `TrainsUnits`. |

---

## Task 1: ProjectileSprite field on GoblinUnitDefinition

**Files:** Modify `Assets/Scripts/World/Unity/GoblinUnitDefinition.cs`

- [ ] **Step 1:** Add the field. Find the line declaring `AttackRange` and add after the combat fields (exact placement flexible; put it near the other stats). Add:

```csharp
        [Tooltip("If set, this unit is RANGED: it shoots this sprite as a projectile instead of melee. Null = melee.")]
        public Sprite ProjectileSprite;
```

(`using UnityEngine;` is already present.)

- [ ] **Step 2:** Compile check via `Unity_RunCommand` → `AssetDatabase.Refresh()`, then `Unity_ReadConsole` (Types: Error). Expected: 0 errors.

- [ ] **Step 3:** Commit
```bash
git add Assets/Scripts/World/Unity/GoblinUnitDefinition.cs
git commit -m "feat(archer): GoblinUnitDefinition.ProjectileSprite (ranged marker)"
```

---

## Task 2: Arrow projectile MonoBehaviour

**Files:** Create `Assets/Scripts/World/Unity/Arrow.cs`

- [ ] **Step 1:** Create the file with this exact content:

```csharp
// =============================================================================
// Arrow.cs  —  RTSCL.World.Unity
//
// Cosmetic-but-owner-authoritative projectile fired by ranged goblins (Archer).
// Spawned on every client when an attack swing fires (the Attacking state runs
// on all clients). It homes toward the target's current position, rotating to
// face its travel direction. On impact:
//   - if DealsDamage (owner client only) and the target is still alive, it calls
//     NetCommandIssuer.IssueDamage → applies locally + broadcasts EvDamage, the
//     same path melee uses. Remotes spawn the arrow with DealsDamage=false (pure
//     visual) and receive the HP change via EvDamage. No new wire message.
// To tune: Speed, ArrivalEpsilon, sorting order.
// =============================================================================
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Self-propelled homing arrow. Created via the static <see cref="Spawn"/> factory.</summary>
    public sealed class Arrow : MonoBehaviour
    {
        private const float Speed = 10f;            // world units / second
        private const float ArrivalEpsilon = 0.15f; // distance to target at which it "hits"
        private const float MaxLifetime = 2f;       // safety self-destruct

        private Goblin _target;
        private Vector3 _fallbackPos;   // last-known target pos, used if target dies mid-flight
        private int _damage;
        private Goblin _attacker;
        private bool _dealsDamage;
        private SpriteRenderer _renderer;
        private float _age;

        /// <summary>Spawn an arrow travelling from <paramref name="from"/> toward <paramref name="target"/>.
        /// <paramref name="dealsDamage"/> must be true only on the attacker's owner client.</summary>
        public static void Arrow_Stub() { } // (placeholder removed below)

        public static void Spawn(Vector3 from, Goblin target, int damage, Goblin attacker, bool dealsDamage, Sprite sprite)
        {
            if (target == null || sprite == null) return;
            var go = new GameObject("Arrow");
            go.transform.position = from;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 30; // above goblins (25)
            var a = go.AddComponent<Arrow>();
            a._renderer = sr;
            a._target = target;
            a._fallbackPos = target.transform.position;
            a._damage = damage;
            a._attacker = attacker;
            a._dealsDamage = dealsDamage;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= MaxLifetime) { Destroy(gameObject); return; }

            // Aim point: live target position while alive, else last-known.
            Vector3 aim = (_target != null && _target.CurrentHp > 0) ? _target.transform.position : _fallbackPos;
            Vector3 delta = aim - transform.position;
            float dist = delta.magnitude;

            if (dist <= ArrivalEpsilon)
            {
                // Impact. Owner client applies + broadcasts damage; remotes are visual-only.
                if (_dealsDamage && _target != null && _target.CurrentHp > 0)
                    NetCommandIssuer.IssueDamage(_target, _damage, _attacker);
                Destroy(gameObject);
                return;
            }

            Vector3 dir = delta / dist;
            transform.position += dir * (Speed * Time.deltaTime);
            // Rotate sprite to point along travel direction (ArrowLong_0 points +X).
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, ang);
        }
    }
}
```

NOTE: delete the `Arrow_Stub` line — it is not needed. (Included here only to flag: do not add placeholder methods. The real file should contain only `Spawn` + `Update` + fields.)

- [ ] **Step 2:** Verify the created file does NOT contain `Arrow_Stub` (remove that line if pasted). Then `Unity_RunCommand` → `AssetDatabase.Refresh()` (generates the `.meta`), `Unity_ReadConsole` (Error). Expected: 0 errors.

- [ ] **Step 3:** Commit
```bash
git add Assets/Scripts/World/Unity/Arrow.cs Assets/Scripts/World/Unity/Arrow.cs.meta
git commit -m "feat(archer): Arrow projectile — homing, owner-authoritative impact damage"
```

---

## Task 3: Ranged branch in Goblin

**Files:** Modify `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1:** Add a field to hold the projectile sprite. Near the other private combat fields (e.g. after `_attackTimer`), add:

```csharp
        private Sprite _projectileSprite;   // non-null = ranged unit (shoots Arrow instead of melee lunge)
```

- [ ] **Step 2:** In `Init`, inside the `if (def != null)` block (where MaxHp/AttackDamage/etc. are read), add:

```csharp
                _projectileSprite = def.ProjectileSprite;
```

- [ ] **Step 3:** Branch the `Attacking` state. Find the swing block:

```csharp
                    if (_attackTimer >= AttackInterval)
                    {
                        _attackTimer = 0f;
                        // Hit animation runs on every client (deterministic). Damage only fires
                        // on the attacker's owner client, which broadcasts EvDamage to remotes.
                        StartHitAnim(new Vector3Int(
                            Mathf.FloorToInt(_attackTarget.transform.position.x),
                            Mathf.FloorToInt(_attackTarget.transform.position.y), 0));
                        if (IsLocalOwner)
                            NetCommandIssuer.IssueDamage(_attackTarget, AttackDamage, this);
                    }
```

Replace it with:

```csharp
                    if (_attackTimer >= AttackInterval)
                    {
                        _attackTimer = 0f;
                        if (_projectileSprite != null)
                        {
                            // Ranged: face the target and shoot an arrow. The arrow applies damage
                            // on impact (owner client only); remotes spawn a visual-only arrow.
                            if (_renderer != null)
                                _renderer.flipX = _attackTarget.transform.position.x < transform.position.x;
                            Arrow.Spawn(transform.position, _attackTarget, AttackDamage, this, IsLocalOwner, _projectileSprite);
                        }
                        else
                        {
                            // Melee: lunge animation runs on every client (deterministic). Damage only
                            // fires on the attacker's owner client, which broadcasts EvDamage to remotes.
                            StartHitAnim(new Vector3Int(
                                Mathf.FloorToInt(_attackTarget.transform.position.x),
                                Mathf.FloorToInt(_attackTarget.transform.position.y), 0));
                            if (IsLocalOwner)
                                NetCommandIssuer.IssueDamage(_attackTarget, AttackDamage, this);
                        }
                    }
```

- [ ] **Step 4:** `Unity_RunCommand` → `AssetDatabase.Refresh()`, `Unity_ReadConsole` (Error). Expected: 0 errors.

- [ ] **Step 5:** Commit
```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(archer): ranged branch in Attacking — shoot Arrow instead of melee lunge"
```

---

## Task 4: Create the GoblinArcher definition asset

**Files:** Create `Assets/Generated/Units/GoblinArcher.asset` (via MCP)

- [ ] **Step 1:** Run this `Unity_RunCommand`. It loads the archer icon + arrow sub-sprites by name (robust against fileID changes), creates the ScriptableObject, sets all fields, and saves.

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.World.Unity;

internal class CommandScript : IRunCommand
{
    private static Sprite FindSub(string path, string subName, ExecutionResult result)
    {
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            if (o is Sprite s && s.name == subName) return s;
        // Some sheets expose the sprite as a main asset too:
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is Sprite s && s.name == subName) return s;
        result.LogError($"Sub-sprite '{subName}' not found in {path}");
        return null;
    }

    public void Execute(ExecutionResult result)
    {
        const string iconPath = "Assets/MiniWorldSprites/Characters/Monsters/Orcs/ArcherGoblin.png";
        const string arrowPath = "Assets/MiniWorldSprites/Objects/ArrowLong.png";
        var icon  = FindSub(iconPath, "ArcherGoblin_0", result);
        var arrow = FindSub(arrowPath, "ArrowLong_0", result);

        var def = ScriptableObject.CreateInstance<GoblinUnitDefinition>();
        def.DisplayName = "Archer Goblin";
        def.Icon = icon;
        def.WoodCost = 0;
        def.FoodCost = 90;
        def.PopulationCost = 2;
        def.MaxHp = 25;
        def.AttackDamage = 4;
        def.AttackInterval = 1.8f;
        def.AttackRange = 5;
        def.SpawnDuration = 7f;
        def.SpawnerKindName = "ArcherGoblin";
        def.ProjectileSprite = arrow;

        AssetDatabase.CreateAsset(def, "Assets/Generated/Units/GoblinArcher.asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        result.Log($"Created GoblinArcher.asset  icon={(icon!=null)} arrow={(arrow!=null)} range={def.AttackRange}");
    }
}
```

Expected log: `icon=True arrow=True range=5`. If icon/arrow is False, fix the sub-sprite name/path and re-run.

- [ ] **Step 2:** `Unity_ReadConsole` (Error). Expected: 0 errors.

- [ ] **Step 3:** Commit
```bash
git add Assets/Generated/Units/GoblinArcher.asset Assets/Generated/Units/GoblinArcher.asset.meta
git commit -m "feat(archer): GoblinArcher unit definition asset (glass-cannon stats)"
```

---

## Task 5: Assign the definition to the GoblinSpawner ArcherGoblin kind

**Files:** Modify `Assets/Scenes/SampleScene.unity` (via MCP)

- [ ] **Step 1:** Run this `Unity_RunCommand`. It finds the GoblinSpawner, locates the `_kinds` element whose `Name == "ArcherGoblin"`, and assigns its `Definition` to the new asset.

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var spawner = UnityEngine.Object.FindFirstObjectByType<RTSCL.World.Unity.GoblinSpawner>();
        if (spawner == null) { result.LogError("GoblinSpawner not found"); return; }
        var def = AssetDatabase.LoadAssetAtPath<RTSCL.World.Unity.GoblinUnitDefinition>("Assets/Generated/Units/GoblinArcher.asset");
        if (def == null) { result.LogError("GoblinArcher.asset not found"); return; }

        var so = new SerializedObject(spawner);
        var kinds = so.FindProperty("_kinds");
        bool set = false;
        for (int i = 0; i < kinds.arraySize; i++)
        {
            var el = kinds.GetArrayElementAtIndex(i);
            var name = el.FindPropertyRelative("Name").stringValue;
            if (name == "ArcherGoblin")
            {
                el.FindPropertyRelative("Definition").objectReferenceValue = def;
                set = true;
                break;
            }
        }
        if (!set) { result.LogError("No _kinds entry named 'ArcherGoblin'"); return; }
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("Assigned GoblinArcher definition to ArcherGoblin kind");
    }
}
```

Expected log: `Assigned GoblinArcher definition to ArcherGoblin kind`.

- [ ] **Step 2:** Commit
```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "feat(archer): wire GoblinArcher definition into GoblinSpawner"
```

---

## Task 6: Add the archer to the Barracks training list

**Files:** Modify `Assets/Generated/Buildings/Barracks_0.asset` (via MCP)

- [ ] **Step 1:** Run this `Unity_RunCommand`. It appends the archer def to `Barracks_0`'s `TrainsUnits` (after ClubGoblin), if not already present.

```csharp
using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var barracks = AssetDatabase.LoadAssetAtPath<RTSCL.World.Unity.BuildingDefinition>("Assets/Generated/Buildings/Barracks_0.asset");
        var archer = AssetDatabase.LoadAssetAtPath<RTSCL.World.Unity.GoblinUnitDefinition>("Assets/Generated/Units/GoblinArcher.asset");
        if (barracks == null || archer == null) { result.LogError($"barracks={(barracks!=null)} archer={(archer!=null)}"); return; }

        var so = new SerializedObject(barracks);
        var list = so.FindProperty("TrainsUnits");
        // Skip if already present.
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == archer)
            { result.Log("archer already in TrainsUnits"); return; }

        int idx = list.arraySize;
        list.InsertArrayElementAtIndex(idx);
        list.GetArrayElementAtIndex(idx).objectReferenceValue = archer;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(barracks);
        AssetDatabase.SaveAssets();
        result.Log($"Barracks_0.TrainsUnits now has {list.arraySize} units");
    }
}
```

Expected log: `Barracks_0.TrainsUnits now has 2 units`.

- [ ] **Step 2:** Commit
```bash
git add Assets/Generated/Buildings/Barracks_0.asset
git commit -m "feat(archer): Barracks trains Archer alongside Club"
```

---

## Task 7: Final verification

**Files:** none

- [ ] **Step 1:** Full compile sweep: `Unity_RunCommand` → `AssetDatabase.Refresh()`, `Unity_ReadConsole` (Error). Expected: 0 errors.

- [ ] **Step 2:** Probe NetworkCatalog registration (optional but recommended). Run a `Unity_RunCommand` that enters play mode briefly OR directly calls `NetworkCatalog.PopulateFromCatalog(catalog)` against the `BuildingCatalog.asset` then `TryGetUnitIndex` for the archer def — confirm it returns true (so MP train sync works). If play-mode is needed, follow the async re-run pattern.

- [ ] **Step 3:** Manual play-mode smoke (report checklist to user):
  - Build/select a Barracks → two train cards: **Club Goblin** and **Archer Goblin (90 Food)**; archer greys out when food < 90 or population full.
  - Train an Archer → spawns by the Barracks, shows "Archer Goblin" in the info box.
  - Right-click an enemy unit → archer walks to ~5 cells away, stops, and fires arrows that fly to the target and rotate to face direction; target HP drops on each impact; on target death the archer stops.
  - Club Goblin still melees exactly as before (no projectile).

---

## Self-Review notes
- **Spec coverage:** ProjectileSprite (Task1), Arrow (Task2), ranged branch (Task3), asset (Task4), spawner wiring (Task5), Barracks (Task6), verify (Task7). All covered.
- **Type consistency:** `GoblinUnitDefinition.ProjectileSprite` (Task1) read in `Goblin.Init` (Task3) and set in the asset (Task4). `Arrow.Spawn(Vector3, Goblin, int, Goblin, bool, Sprite)` (Task2) called in Task3 with matching arg order. `SpawnerKindName = "ArcherGoblin"` matches the existing scene kind (Task5).
- **No new wire message:** damage flows through existing `NetCommandIssuer.IssueDamage` → `EvDamage`.
- **Placeholder caution:** Task 2 explicitly instructs removing the `Arrow_Stub` placeholder — the final file must have only real members.

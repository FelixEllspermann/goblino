# Neutral Monsters Implementation Plan

> REQUIRED SUB-SKILL: subagent-driven-development / executing-plans. Checkbox steps.

**Goal:** Themed neutral monsters (Giant Crab, Mammoth, Slime, Slime Blue) that spawn in biomes, wander, aggro+chase+leash player goblins, killable no-loot, host-authoritative.

**Architecture:** Monsters = `Goblin` instances (neutral) + a `MonsterAI` component; placed by a seed-deterministic `MonsterSpawner` after player teams. Host owns monsters (solo = local). Reuses existing combat/move/EvDamage sync — no new wire message.

**Verification:** MCP `AssetDatabase.Refresh()`/import + `Unity_ReadConsole` (Errors==0) per step. **New-file caution:** after creating each new `.cs`, verify its type resolves (compile a tiny snippet referencing it) BEFORE editing files that reference it; if a new file won't register in the headless editor, co-locate its class into an already-registered file.

Order is chosen so every referenced type exists+compiles before its referencer.

---

## Task 1: MP-authority bridge — WorldStartContext.HostPlayer

- Modify `Assets/Scripts/World/Unity/WorldStartContext.cs`: add `public static ulong HostPlayer;` and clear it in `Reset()`.
- Modify `Assets/Scripts/Lobby/GameStartLoader.cs`: where it sets `WorldStartContext.LocalPlayer`, also set `WorldStartContext.HostPlayer = NetworkSession.HostPlayer.m_SteamID;` (solo path that doesn't run this leaves it 0).
- Compile-check. Commit `feat(monsters): WorldStartContext.HostPlayer bridge`.

---

## Task 2: GoblinUnitDefinition.WorldScale

- Modify `Assets/Scripts/World/Unity/GoblinUnitDefinition.cs`: add
```csharp
        [Tooltip("Visual size multiplier applied to the unit transform on spawn (monsters scale up).")]
        public float WorldScale = 1f;
```
- Compile-check. Commit `feat(monsters): GoblinUnitDefinition.WorldScale`.

---

## Task 3: Goblin neutral support + scale + public ownership

- Modify `Assets/Scripts/World/Unity/Goblin.cs`:
  - Add `public bool IsNeutral { get; private set; }` and:
```csharp
        public bool IsOwnedLocally => Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
        public void MarkNeutral() { IsNeutral = true; ApplyOwnerVisuals(); RefreshSelectionRingColor(); }
```
  - In `Init`, inside `if (def != null)`, add `transform.localScale = Vector3.one * Mathf.Max(0.1f, def.WorldScale);`
  - In `ApplyOwnerVisuals`: if `IsNeutral`, force `_renderer.color = Color.white;` and return (no faction tint).
  - In `RefreshSelectionRingColor`: if `IsNeutral`, set ring color to `new Color(1f,0.5f,0.5f,1f)` (neutral) and return.
  - In `EnterDying`: guard the population release — `if (!IsNeutral) PopulationManager.RemoveUsed(PopulationCost);`
- Compile-check. Commit `feat(monsters): Goblin neutral flag, world scale, public IsOwnedLocally`.

---

## Task 4: GoblinSelectionController excludes neutrals

- Modify `Assets/Scripts/World/Unity/GoblinSelectionController.cs`: in `SelectAtPoint` and `SelectInBox`, change the `IsLocalOwner(g)` guard to also require `&& !g.IsNeutral`.
- Compile-check. Commit `feat(monsters): players can't select neutral monsters`.

---

## Task 5: MonsterAI (new component)

Create `Assets/Scripts/World/Unity/MonsterAI.cs` (verify it compiles before Task 7 references it; if it won't register, co-locate into a registered file).

```csharp
using UnityEngine;
using RTSCL.World.Unity;

namespace RTSCL.World.Unity
{
    /// <summary>Drives a neutral monster Goblin: gentle wander around Home, aggro the nearest player
    /// goblin within AggroRadius, chase, and disengage+return when dragged past LeashRadius. Decision
    /// logic runs ONLY on the authoritative client (the monster's owner = host in MP, local in solo);
    /// it issues normal net commands so remotes mirror. Attached at spawn by MonsterSpawner.</summary>
    public sealed class MonsterAI : MonoBehaviour
    {
        public Vector3 Home;

        private const float WanderRadius = 4f;
        private const float AggroRadius = 6f;
        private const float LeashRadius = 12f;
        private const float WanderMinDelay = 2.5f;
        private const float WanderMaxDelay = 5.5f;
        private const float ReturnArrive = 1.5f;

        private Goblin _self;
        private Goblin _target;
        private bool _returning;
        private float _nextWander;
        private float _scanTimer;

        private void Awake() => _self = GetComponent<Goblin>();

        private void Update()
        {
            if (_self == null || _self.CurrentHp <= 0) return;
            if (!_self.IsOwnedLocally) return;   // only the authoritative client decides; others mirror

            float homeDist = Vector2.Distance(transform.position, Home);

            // Returning home after a leash break — ignore aggro until back near home.
            if (_returning)
            {
                if (homeDist <= ReturnArrive) { _returning = false; }
                return;
            }

            // Drag past leash → give up and walk home.
            if (_target != null && homeDist > LeashRadius)
            {
                _target = null;
                _returning = true;
                NetCommandIssuer.IssueMove(new System.Collections.Generic.List<Goblin> { _self }, Home);
                return;
            }

            // Maintain / validate current target.
            if (_target != null && (_target.CurrentHp <= 0 || !_target))
                _target = null;

            // Acquire a target periodically when we have none.
            _scanTimer -= Time.deltaTime;
            if (_target == null && _scanTimer <= 0f)
            {
                _scanTimer = 0.4f;
                _target = FindNearestPlayerGoblin();
                if (_target != null)
                    NetCommandIssuer.IssueAttack(_self, _target);
            }

            // Idle wandering near home when not engaged.
            if (_target == null && !_self.IsIdle == false) // i.e. when idle
            {
                if (Time.time >= _nextWander && _self.IsIdle)
                {
                    _nextWander = Time.time + Random.Range(WanderMinDelay, WanderMaxDelay);
                    Vector2 off = Random.insideUnitCircle * WanderRadius;
                    Vector3 p = new Vector3(Home.x + off.x, Home.y + off.y, 0f);
                    NetCommandIssuer.IssueMove(new System.Collections.Generic.List<Goblin> { _self }, p);
                }
            }
        }

        private Goblin FindNearestPlayerGoblin()
        {
            Goblin best = null;
            float bestSq = AggroRadius * AggroRadius;
            foreach (var g in Goblin.All)
            {
                if (g == null || g.IsNeutral || g.CurrentHp <= 0) continue;
                float d = (g.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = g; }
            }
            return best;
        }
    }
}
```

NOTE the wander guard: simplify to `if (_target == null && _self.IsIdle && Time.time >= _nextWander)` — see Task 5b cleanup.

- Compile-check (Errors==0) + verify `typeof(MonsterAI)` resolves in a snippet. Commit `feat(monsters): MonsterAI — wander/aggro/leash (authoritative-only)`.

### Task 5b: fix the wander condition

The drafted `!_self.IsIdle == false` is confusing; replace that whole wander block with:
```csharp
            if (_target == null && _self.IsIdle && Time.time >= _nextWander)
            {
                _nextWander = Time.time + Random.Range(WanderMinDelay, WanderMaxDelay);
                Vector2 off = Random.insideUnitCircle * WanderRadius;
                Vector3 p = new Vector3(Home.x + off.x, Home.y + off.y, 0f);
                NetCommandIssuer.IssueMove(new System.Collections.Generic.List<Goblin> { _self }, p);
            }
```
Compile-check. (Folded into Task 5's commit if done together.)

---

## Task 6: MonsterSpawner (new)

Create `Assets/Scripts/World/Unity/MonsterSpawner.cs` (verify compiles before Task 7).

```csharp
using System.Collections.Generic;
using RTSCL.World;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace RTSCL.World.Unity
{
    /// <summary>Seed-deterministic, biome-aware placement of neutral monsters. Run from
    /// MainBaseSetup.OnNewWorld AFTER player teams so spawn order (and thus NetIds) is identical on
    /// every client. Monsters are owned by the host (solo = 0). Each gets a MonsterAI with Home set.</summary>
    public sealed class MonsterSpawner : MonoBehaviour
    {
        [System.Serializable]
        public sealed class MonsterType
        {
            public string KindName;       // matches GoblinSpawner kind + GoblinUnitDefinition.SpawnerKindName
            public Biome[] Biomes;        // acceptable spawn biomes
            public int Count = 3;
        }

        [SerializeField] private GoblinSpawner _spawner;
        [SerializeField] private List<MonsterType> _types = new();
        [SerializeField] private int _minDistanceFromSpawns = 22;

        public void SpawnAll(WorldData world, uint seed)
        {
            if (_spawner == null || world == null) return;
            ulong owner = WorldStartContext.PendingSlots != null ? WorldStartContext.HostPlayer : 0UL;
            var rng = new Random(seed == 0 ? 99u : seed * 2654435761u + 1u);

            foreach (var t in _types)
            {
                if (t == null || string.IsNullOrEmpty(t.KindName) || t.Biomes == null || t.Biomes.Length == 0) continue;
                int placed = 0, attempts = 0, maxAttempts = t.Count * 200;
                while (placed < t.Count && attempts < maxAttempts)
                {
                    attempts++;
                    int x = rng.NextInt(0, world.Width);
                    int y = rng.NextInt(0, world.Height);
                    if (!IsBiomeMatch(world, x, y, t.Biomes)) continue;
                    if (TooCloseToSpawn(world, x, y)) continue;
                    var pos = new Vector3(x + 0.5f, y + 0.5f, 0f);
                    var g = _spawner.SpawnAt(pos, FindKind(t.KindName), owner);
                    if (g == null) continue;
                    g.MarkNeutral();
                    g.gameObject.AddComponent<MonsterAI>().Home = pos;
                    placed++;
                }
            }
        }

        private GoblinSpawner.GoblinKind FindKind(string name) => null; // replaced below

        private static bool IsBiomeMatch(WorldData w, int x, int y, Biome[] biomes)
        {
            var b = w.BiomeAt(x, y);
            if (b == Biome.DeepWater || b == Biome.Shore || b == Biome.Cliff) return false;
            foreach (var wanted in biomes) if (b == wanted) return true;
            return false;
        }

        private bool TooCloseToSpawn(WorldData w, int x, int y)
        {
            if (w.Spawns == null) return false;
            int minSq = _minDistanceFromSpawns * _minDistanceFromSpawns;
            foreach (var s in w.Spawns)
            {
                int dx = s.x - x, dy = s.y - y;
                if (dx * dx + dy * dy < minSq) return true;
            }
            return false;
        }
    }
}
```

PROBLEM: `GoblinSpawner.SpawnAt` takes a `GoblinKind` (nested type) which is found via the spawner's private `FindKind`. To avoid exposing internals, add a public spawner overload that takes a kind NAME. So:

- Modify `GoblinSpawner`: add
```csharp
        /// <summary>Spawn a single goblin of the named kind at a world position (used by MonsterSpawner).</summary>
        public Goblin SpawnKindAt(string kindName, Vector3 worldPos, ulong owner)
        {
            var kind = FindKind(kindName);
            if (kind == null) { Debug.LogWarning($"[GoblinSpawner] Kind '{kindName}' not configured"); return null; }
            return SpawnAt(worldPos, kind, owner);
        }
```
- In `MonsterSpawner`, remove the broken `FindKind` stub and the `FindKind(t.KindName)` call; use:
```csharp
                    var g = _spawner.SpawnKindAt(t.KindName, pos, owner);
```

Compile-check (after adding `SpawnKindAt` first, then MonsterSpawner). Commit `feat(monsters): MonsterSpawner (deterministic biome placement) + GoblinSpawner.SpawnKindAt`.

---

## Task 7: Hook MonsterSpawner into MainBaseSetup

- Modify `Assets/Scripts/World/Unity/MainBaseSetup.cs`:
  - Add `[SerializeField] private MonsterSpawner _monsterSpawner;`
  - At the END of `OnNewWorld` (after teams are placed), add:
```csharp
            if (_monsterSpawner != null && _knownWorld != null)
                _monsterSpawner.SpawnAll(_knownWorld, (uint)_knownWorld.Seed);
```
  (Use the same `WorldData` reference used for spawning; `_knownWorld` is set at the top of Update before OnNewWorld — confirm field name; if OnNewWorld receives `world` param, use that.)
- Compile-check. Commit `feat(monsters): spawn monsters after teams in MainBaseSetup`.

---

## Task 8: Monster definition assets (MCP)

For each of the 4 monsters, create `Assets/Generated/Units/<Name>.asset` (GoblinUnitDefinition) with stats from the spec table, `SpawnerKindName` = kind name, `WorldScale` set, `AttackDamage`/`MaxHp`/`AttackInterval`/`AttackRange` set, `Icon` = the monster's frame 0. Wood/Food/Pop = 0, SpawnDuration irrelevant.
- Commit `feat(monsters): GiantCrab/Mammoth/Slime/SlimeBlue unit definitions`.

---

## Task 9: Sprite import (16 PPU) + GoblinSpawner kinds (MCP)

- For each monster PNG set `spritePixelsPerUnit = 16` and reimport.
- For each, load sub-sprites, take the first 4 whose rect width ≤ 18 (skip wide composite strips) as WalkFrames.
- Add 4 entries to the scene `GoblinSpawner._kinds` (Name, WalkFrames, Definition asset). Save scene.
- Wire `MainBaseSetup._monsterSpawner` and `MonsterSpawner._spawner` + `_types` (KindName/Biomes/Count per spec) in the scene.
- Commit `feat(monsters): wire monster sprites, kinds, and spawner config in SampleScene`.

---

## Task 10: Verify

- Full compile (0 errors).
- Probe: generate a world for a few seeds, run a dry biome scan confirming each monster type has ≥Count eligible cells beyond min-distance (so spawning won't starve).
- Manual smoke (report to user): monsters appear in their biomes away from base, untinted, unselectable; approaching with a goblin triggers chase+attack; leashing makes them walk home; killing plays hop-death with no resources.

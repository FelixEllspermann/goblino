# Procedural World Generation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deterministic, seed-based RTS map generator for RTSCL that produces coherent biomes, balanced spawns, and resource clusters from the MiniWorldSprites pack, with hard biome borders in V1 and an extension point for autotile transitions in Phase 2.

**Architecture:** Three layers — (1) pure-logic core in `RTSCL.World` assembly using `Unity.Mathematics` only (no `UnityEngine`), so it is EditMode-testable without scene instantiation; (2) Unity bridge (`TilePainter`, `DecorationPlacer`, `CameraFitter`, `WorldGeneratorBootstrap`) that paints `WorldData` onto two Tilemaps via an `IBiomeTileResolver` swap point; (3) one-shot editor tool that slices the MiniWorldSprites Ground and Nature PNGs and generates the `TileBase` assets the resolver depends on.

**Tech Stack:** Unity 6000.4.3f1, URP 2D, `com.unity.mathematics` (Simplex noise + deterministic Random), `com.unity.test-framework` (NUnit EditMode tests), `com.unity.2d.tilemap` + `com.unity.2d.tilemap.extras`.

**Spec:** `docs/superpowers/specs/2026-05-23-procedural-world-generation-design.md`

---

## File Structure

**Runtime — pure logic (no UnityEngine refs except Unity.Mathematics):**
- `Assets/Scripts/World/RTSCL.World.asmdef`
- `Assets/Scripts/World/Biome.cs` — enum
- `Assets/Scripts/World/WorldGenConfig.cs` — `[Serializable]` POCO
- `Assets/Scripts/World/ResourceCluster.cs` — struct
- `Assets/Scripts/World/WorldData.cs` — output container
- `Assets/Scripts/World/NoiseField.cs` — wraps `noise.snoise`
- `Assets/Scripts/World/ContinentShaper.cs` — falloff + cleanup
- `Assets/Scripts/World/BiomeClassifier.cs` — threshold lookup
- `Assets/Scripts/World/SpawnPlanner.cs` — farthest-point sampling
- `Assets/Scripts/World/ResourcePlanner.cs` — cluster placement
- `Assets/Scripts/World/ReachabilityChecker.cs` — flood-fill
- `Assets/Scripts/World/WorldGenerator.cs` — orchestrator

**Runtime — Unity bridge (depends on UnityEngine + Tilemap):**
- `Assets/Scripts/World/Unity/RTSCL.World.Unity.asmdef`
- `Assets/Scripts/World/Unity/IBiomeTileResolver.cs`
- `Assets/Scripts/World/Unity/SingleTileBiomeResolver.cs` — `ScriptableObject`
- `Assets/Scripts/World/Unity/TilePainter.cs`
- `Assets/Scripts/World/Unity/DecorationPlacer.cs`
- `Assets/Scripts/World/Unity/CameraFitter.cs`
- `Assets/Scripts/World/Unity/WorldGeneratorBootstrap.cs`

**Editor-only tooling:**
- `Assets/Editor/RTSCL.Editor.asmdef`
- `Assets/Editor/MiniWorldSpritesSlicer.cs`

**Tests (EditMode):**
- `Assets/Tests/Editor/RTSCL.World.Tests.asmdef`
- `Assets/Tests/Editor/NoiseFieldTests.cs`
- `Assets/Tests/Editor/ContinentShaperTests.cs`
- `Assets/Tests/Editor/BiomeClassifierTests.cs`
- `Assets/Tests/Editor/SpawnPlannerTests.cs`
- `Assets/Tests/Editor/ResourcePlannerTests.cs`
- `Assets/Tests/Editor/ReachabilityCheckerTests.cs`
- `Assets/Tests/Editor/WorldGeneratorTests.cs`

**Generated assets** (created by slicer in Task 11):
- `Assets/Generated/Tiles/<Biome>.asset` (one per ground biome)
- `Assets/Generated/Tiles/Decorations/<Sheet>_<index>.asset` (one per nature sprite)
- `Assets/Generated/SingleTileBiomeResolver.asset` (the resolver instance)

---

## Task 0: Project setup (git + ignore + verify packages)

**Files:**
- Create: `.gitignore`
- Modify: `Packages/manifest.json` (only if `com.unity.mathematics` is missing)

- [ ] **Step 1: Initialize git repository**

```bash
cd C:\Users\fe199\RTSCL
git init
git branch -M main
```

- [ ] **Step 2: Create Unity-aware `.gitignore`**

Write the following to `C:\Users\fe199\RTSCL\.gitignore`:

```gitignore
# Unity generated
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]ser[Ss]ettings/
[Mm]emoryCaptures/
[Rr]ecordings/

# IDE
*.csproj
*.unityproj
*.sln
*.suo
*.tmp
*.user
*.userprefs
*.pidb
*.booproj
*.svd
*.pdb
*.mdb
*.opendb
*.VC.db
.vs/
.vscode/
.idea/

# OS
.DS_Store
Thumbs.db

# Unity Crashlogs
sysinfo.txt
crashlog.txt

# Generated tile assets (regenerable via slicer)
# (intentionally tracked — leave this rule disabled)

# Steam dev shim (never commit)
steam_appid.txt

# Raw Steamworks SDK sibling folder (large, redownload as needed)
sdk/
```

- [ ] **Step 3: Verify `com.unity.mathematics` is available**

Run:
```powershell
Get-ChildItem "C:\Users\fe199\RTSCL\Library\PackageCache" -Directory -Filter "com.unity.mathematics*"
```
Expected: at least one matching folder.

If empty, add `"com.unity.mathematics": "1.3.2"` to `Packages/manifest.json` dependencies. Otherwise it is a transitive dependency and no change is needed.

- [ ] **Step 4: Commit baseline (entire current project state)**

```bash
git add .gitignore CLAUDE.md docs/ Assets/ Packages/ ProjectSettings/
git status   # verify steam_appid.txt and sdk/ are NOT staged (gitignored)
git commit -m "chore: initial commit — Unity 6 URP 2D, MiniWorldSprites pack, Steamworks.NET binding, world-gen spec"
```

Expected: `steam_appid.txt` and `sdk/` appear in the status output as untracked-and-ignored (not staged). If `steam_appid.txt` is staged, the `.gitignore` rule did not take — re-check it.

---

## Task 1: Create assembly definitions

Assembly definitions isolate the pure-logic World assembly from UnityEngine, so EditMode tests can run without instantiating a scene.

**Files:**
- Create: `Assets/Scripts/World/RTSCL.World.asmdef`
- Create: `Assets/Scripts/World/Unity/RTSCL.World.Unity.asmdef`
- Create: `Assets/Editor/RTSCL.Editor.asmdef`
- Create: `Assets/Tests/Editor/RTSCL.World.Tests.asmdef`

- [ ] **Step 1: Write `RTSCL.World.asmdef`**

```json
{
    "name": "RTSCL.World",
    "rootNamespace": "RTSCL.World",
    "references": ["Unity.Mathematics"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`noEngineReferences: true` is the enforcement: this assembly cannot accidentally reference `UnityEngine.*` types.

- [ ] **Step 2: Write `RTSCL.World.Unity.asmdef`**

```json
{
    "name": "RTSCL.World.Unity",
    "rootNamespace": "RTSCL.World.Unity",
    "references": ["RTSCL.World", "Unity.Mathematics"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: Write `RTSCL.Editor.asmdef`**

```json
{
    "name": "RTSCL.Editor",
    "rootNamespace": "RTSCL.Editor",
    "references": ["RTSCL.World", "RTSCL.World.Unity"],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 4: Write `RTSCL.World.Tests.asmdef`**

```json
{
    "name": "RTSCL.World.Tests",
    "rootNamespace": "RTSCL.World.Tests",
    "references": [
        "RTSCL.World",
        "Unity.Mathematics",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 5: Move `SteamManager.cs` is unaffected — it lives in `Assets/Scripts/` (root), not `Assets/Scripts/World/`**

Verify the path. If `SteamManager.cs` is at `Assets/Scripts/SteamManager.cs`, no action needed. (It remains in the default `Assembly-CSharp`.)

- [ ] **Step 6: Open Unity to let it recompile**

After Unity finishes the recompile, the Console should show no errors. If it complains about `nunit.framework.dll` not found, set `"precompiledReferences": []` and add `"references"` entries with `"UnityEngine.TestRunner"` and `"UnityEditor.TestRunner"` only — Unity's test framework provides NUnit transitively in modern versions.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/World/RTSCL.World.asmdef \
        Assets/Scripts/World/Unity/RTSCL.World.Unity.asmdef \
        Assets/Editor/RTSCL.Editor.asmdef \
        Assets/Tests/Editor/RTSCL.World.Tests.asmdef
git commit -m "chore: add asmdefs for World, World.Unity, Editor, World.Tests"
```

---

## Task 2: Define core types (Biome, WorldGenConfig, ResourceCluster, WorldData)

These are pure data types with no logic — testing them is unnecessary. They go into the pure-logic assembly.

**Files:**
- Create: `Assets/Scripts/World/Biome.cs`
- Create: `Assets/Scripts/World/WorldGenConfig.cs`
- Create: `Assets/Scripts/World/ResourceCluster.cs`
- Create: `Assets/Scripts/World/WorldData.cs`

- [ ] **Step 1: Write `Biome.cs`**

```csharp
namespace RTSCL.World
{
    public enum Biome
    {
        DeepWater = 0,
        Shore = 1,
        Cliff = 2,
        Snow = 3,
        Desert = 4,
        TropicalCoast = 5,
        Forest = 6,
        Grassland = 7,
        DryGrass = 8,
    }
}
```

- [ ] **Step 2: Write `WorldGenConfig.cs`**

```csharp
using System;

namespace RTSCL.World
{
    [Serializable]
    public sealed class WorldGenConfig
    {
        // Map dimensions
        public int Width = 128;
        public int Height = 128;
        public int PlayerCount = 2;

        // Noise scales (larger = smoother / more zoomed-in features)
        public float ElevationScale = 40f;
        public float MoistureScale = 30f;
        public float TemperatureScale = 60f;

        // Continent shaping
        public float FalloffStrength = 1.2f;

        // Biome thresholds (elevation_adjusted)
        public float DeepWaterMax = 0.30f;
        public float ShoreMax = 0.40f;
        public float CliffMin = 0.85f;

        // Biome thresholds (temperature & moisture)
        public float SnowMax = 0.25f;
        public float TropicalMin = 0.75f;
        public float DesertMoistureMax = 0.30f;
        public float ForestMoistureMin = 0.65f;
        public float GrasslandMoistureMin = 0.35f;

        // Post-processing
        public int MiniIslandRemovalThreshold = 10;
        public int LakeFillThreshold = 5;

        // Spawn placement
        public int SpawnBufferToImpassable = 4;
        public int SpawnReservedAreaSize = 5;
        public int CandidatesPerSpawn = 100;

        // Resource clusters
        public int StoneClustersPerSpawn = 2;
        public int FoodClustersPerSpawn = 2;
        public int FreeRoamClusterCountMin = 6;
        public int FreeRoamClusterCountMax = 10;
        public int ClusterSizeMin = 3;
        public int ClusterSizeMax = 5;
        public int ClusterRadiusMin = 6;
        public int ClusterRadiusMax = 12;

        // Reachability retry
        public int MaxRegenerationAttempts = 5;
    }
}
```

- [ ] **Step 3: Write `ResourceCluster.cs`**

```csharp
using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public enum ResourceType
    {
        Stone,
        Food,
    }

    public readonly struct ResourceCluster
    {
        public readonly ResourceType Type;
        public readonly int2 Center;
        public readonly IReadOnlyList<int2> Cells;

        public ResourceCluster(ResourceType type, int2 center, IReadOnlyList<int2> cells)
        {
            Type = type;
            Center = center;
            Cells = cells;
        }
    }
}
```

- [ ] **Step 4: Write `WorldData.cs`**

```csharp
using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public sealed class WorldData
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Biome[,] Biomes;
        public readonly int2[] Spawns;
        public readonly IReadOnlyList<ResourceCluster> Resources;
        public readonly uint Seed;

        public WorldData(int width, int height, Biome[,] biomes, int2[] spawns,
                         IReadOnlyList<ResourceCluster> resources, uint seed)
        {
            Width = width;
            Height = height;
            Biomes = biomes;
            Spawns = spawns;
            Resources = resources;
            Seed = seed;
        }

        public bool InBounds(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height;

        public Biome BiomeAt(int x, int y) => Biomes[x, y];
    }
}
```

- [ ] **Step 5: Save files, let Unity recompile, verify no errors**

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Biome.cs \
        Assets/Scripts/World/WorldGenConfig.cs \
        Assets/Scripts/World/ResourceCluster.cs \
        Assets/Scripts/World/WorldData.cs
git commit -m "feat(world): add core data types — Biome, WorldGenConfig, WorldData, ResourceCluster"
```

---

## Task 3: Implement NoiseField with tests

`NoiseField` wraps `Unity.Mathematics.noise.snoise` with seed-offset injection and normalization to `[0..1]`.

**Files:**
- Create: `Assets/Scripts/World/NoiseField.cs`
- Test: `Assets/Tests/Editor/NoiseFieldTests.cs`

- [ ] **Step 1: Write failing test for determinism**

`Assets/Tests/Editor/NoiseFieldTests.cs`:

```csharp
using NUnit.Framework;
using RTSCL.World;
using Unity.Mathematics;

namespace RTSCL.World.Tests
{
    public class NoiseFieldTests
    {
        [Test]
        public void Sample_SameSeedAndCoord_ReturnsSameValue()
        {
            var a = new NoiseField(seed: 42, channel: 0, scale: 40f);
            var b = new NoiseField(seed: 42, channel: 0, scale: 40f);

            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual(a.Sample(i, i * 3), b.Sample(i, i * 3),
                    1e-6f, $"divergence at i={i}");
            }
        }

        [Test]
        public void Sample_IsInUnitRange()
        {
            var n = new NoiseField(seed: 1, channel: 0, scale: 30f);
            for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
            {
                float v = n.Sample(x, y);
                Assert.GreaterOrEqual(v, 0f);
                Assert.LessOrEqual(v, 1f);
            }
        }

        [Test]
        public void Sample_DifferentChannelsDiffer()
        {
            var a = new NoiseField(seed: 7, channel: 0, scale: 40f);
            var b = new NoiseField(seed: 7, channel: 1, scale: 40f);

            int differing = 0;
            for (int x = 0; x < 32; x++)
            for (int y = 0; y < 32; y++)
                if (math.abs(a.Sample(x, y) - b.Sample(x, y)) > 0.01f)
                    differing++;

            Assert.Greater(differing, 500, "channels should produce mostly different values");
        }
    }
}
```

- [ ] **Step 2: Run tests, verify they fail to compile (NoiseField missing)**

In Unity: `Window → General → Test Runner → EditMode → Run All`.
Expected: compilation error "NoiseField could not be found".

- [ ] **Step 3: Implement `NoiseField.cs`**

```csharp
using Unity.Mathematics;

namespace RTSCL.World
{
    public sealed class NoiseField
    {
        private readonly float2 _offset;
        private readonly float _scale;

        public NoiseField(uint seed, int channel, float scale)
        {
            // Derive a per-channel deterministic offset from seed.
            var rng = new Random(seed == 0 ? 1u : seed);
            for (int i = 0; i < channel; i++) rng.NextFloat2(); // advance per channel
            _offset = new float2(
                rng.NextFloat(-10000f, 10000f),
                rng.NextFloat(-10000f, 10000f));
            _scale = scale <= 0f ? 1f : scale;
        }

        public NoiseField(int seed, int channel, float scale)
            : this(unchecked((uint)seed), channel, scale) { }

        /// <summary>Returns a value in [0..1].</summary>
        public float Sample(int x, int y)
        {
            float2 p = new float2(x / _scale, y / _scale) + _offset;
            // noise.snoise returns roughly [-1..1]. Normalize.
            float raw = noise.snoise(p);
            return math.saturate(raw * 0.5f + 0.5f);
        }
    }
}
```

- [ ] **Step 4: Run tests, verify all three pass**

Run: Test Runner → EditMode → Run All. Expected: 3/3 NoiseFieldTests pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/NoiseField.cs Assets/Tests/Editor/NoiseFieldTests.cs
git commit -m "feat(world): add NoiseField with determinism and range tests"
```

---

## Task 4: Implement ContinentShaper with tests

Applies radial falloff to make a continent shape, then post-processes the biome map to remove mini-islands and fill mini-lakes.

**Files:**
- Create: `Assets/Scripts/World/ContinentShaper.cs`
- Test: `Assets/Tests/Editor/ContinentShaperTests.cs`

- [ ] **Step 1: Write failing tests**

`Assets/Tests/Editor/ContinentShaperTests.cs`:

```csharp
using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class ContinentShaperTests
    {
        [Test]
        public void Falloff_CenterUnchanged_EdgesPushedToZero()
        {
            int w = 64, h = 64;
            var elevation = new float[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                elevation[x, y] = 1f;

            ContinentShaper.ApplyFalloff(elevation, w, h, falloffStrength: 1.2f);

            // Center should still be ~1
            Assert.Greater(elevation[w / 2, h / 2], 0.95f);
            // Corners should be ~0
            Assert.Less(elevation[0, 0], 0.1f);
            Assert.Less(elevation[w - 1, h - 1], 0.1f);
        }

        [Test]
        public void RemoveMiniIslands_SmallIslandBecomesWater()
        {
            int w = 16, h = 16;
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                biomes[x, y] = Biome.DeepWater;

            // Place a 3-cell "island" at (5,5),(5,6),(6,5) — under threshold 10
            biomes[5, 5] = Biome.Grassland;
            biomes[5, 6] = Biome.Grassland;
            biomes[6, 5] = Biome.Grassland;

            ContinentShaper.RemoveMiniIslands(biomes, w, h, threshold: 10);

            Assert.AreEqual(Biome.DeepWater, biomes[5, 5]);
            Assert.AreEqual(Biome.DeepWater, biomes[5, 6]);
            Assert.AreEqual(Biome.DeepWater, biomes[6, 5]);
        }

        [Test]
        public void FillMiniLakes_SmallEnclosedWaterBecomesGrassland()
        {
            int w = 16, h = 16;
            var biomes = new Biome[w, h];
            // Fill with land
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                biomes[x, y] = Biome.Grassland;
            // Carve a 2-cell lake in the middle
            biomes[8, 8] = Biome.DeepWater;
            biomes[8, 9] = Biome.DeepWater;

            ContinentShaper.FillMiniLakes(biomes, w, h, threshold: 5);

            Assert.AreEqual(Biome.Grassland, biomes[8, 8]);
            Assert.AreEqual(Biome.Grassland, biomes[8, 9]);
        }

        [Test]
        public void FillMiniLakes_OceanNotFilled()
        {
            int w = 8, h = 8;
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                biomes[x, y] = Biome.DeepWater;
            biomes[3, 3] = Biome.Grassland;

            // The ocean touches the map edge — must NOT be filled even if "small"
            ContinentShaper.FillMiniLakes(biomes, w, h, threshold: 999);

            Assert.AreEqual(Biome.DeepWater, biomes[0, 0]);
        }
    }
}
```

- [ ] **Step 2: Verify tests fail with "ContinentShaper not found"**

- [ ] **Step 3: Implement `ContinentShaper.cs`**

```csharp
using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public static class ContinentShaper
    {
        public static void ApplyFalloff(float[,] elevation, int w, int h, float falloffStrength)
        {
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float nx = (x / (float)(w - 1)) * 2f - 1f;
                float ny = (y / (float)(h - 1)) * 2f - 1f;
                float d2 = nx * nx + ny * ny;
                float falloff = math.saturate(1f - d2 * falloffStrength);
                elevation[x, y] *= falloff;
            }
        }

        private static bool IsLand(Biome b) =>
            b != Biome.DeepWater && b != Biome.Shore;

        public static void RemoveMiniIslands(Biome[,] biomes, int w, int h, int threshold)
        {
            var visited = new bool[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (visited[x, y]) continue;
                if (!IsLand(biomes[x, y])) { visited[x, y] = true; continue; }

                var component = FloodFill(biomes, visited, w, h, x, y, IsLand);
                if (component.Count < threshold)
                    foreach (var c in component)
                        biomes[c.x, c.y] = Biome.DeepWater;
            }
        }

        public static void FillMiniLakes(Biome[,] biomes, int w, int h, int threshold)
        {
            var visited = new bool[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (visited[x, y]) continue;
                if (biomes[x, y] != Biome.DeepWater) { visited[x, y] = true; continue; }

                var component = FloodFill(biomes, visited, w, h, x, y,
                                          b => b == Biome.DeepWater);
                if (component.Count >= threshold) continue;

                // Touches edge → it's the ocean, do not fill
                bool touchesEdge = false;
                foreach (var c in component)
                    if (c.x == 0 || c.y == 0 || c.x == w - 1 || c.y == h - 1)
                    { touchesEdge = true; break; }
                if (touchesEdge) continue;

                foreach (var c in component)
                    biomes[c.x, c.y] = Biome.Grassland;
            }
        }

        private static List<int2> FloodFill(Biome[,] biomes, bool[,] visited,
                                            int w, int h, int startX, int startY,
                                            System.Func<Biome, bool> predicate)
        {
            var result = new List<int2>();
            var stack = new Stack<int2>();
            stack.Push(new int2(startX, startY));
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p.x < 0 || p.x >= w || p.y < 0 || p.y >= h) continue;
                if (visited[p.x, p.y]) continue;
                if (!predicate(biomes[p.x, p.y])) continue;
                visited[p.x, p.y] = true;
                result.Add(p);
                stack.Push(new int2(p.x + 1, p.y));
                stack.Push(new int2(p.x - 1, p.y));
                stack.Push(new int2(p.x, p.y + 1));
                stack.Push(new int2(p.x, p.y - 1));
            }
            return result;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify 4/4 ContinentShaperTests pass**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/ContinentShaper.cs Assets/Tests/Editor/ContinentShaperTests.cs
git commit -m "feat(world): add ContinentShaper with falloff + mini-island/lake cleanup"
```

---

## Task 5: Implement BiomeClassifier with tests

Per-cell threshold lookup that turns (elevation, moisture, temperature) into a Biome. Order matters — first match wins.

**Files:**
- Create: `Assets/Scripts/World/BiomeClassifier.cs`
- Test: `Assets/Tests/Editor/BiomeClassifierTests.cs`

- [ ] **Step 1: Write failing tests**

`Assets/Tests/Editor/BiomeClassifierTests.cs`:

```csharp
using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class BiomeClassifierTests
    {
        private static WorldGenConfig DefaultConfig() => new WorldGenConfig();

        [Test]
        public void LowElevation_ReturnsDeepWater()
        {
            var b = BiomeClassifier.Classify(0.10f, m: 0.5f, t: 0.5f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.DeepWater, b);
        }

        [Test]
        public void ShoreRange_ReturnsShore()
        {
            var b = BiomeClassifier.Classify(0.35f, m: 0.5f, t: 0.5f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Shore, b);
        }

        [Test]
        public void HighElevation_ReturnsCliff()
        {
            var b = BiomeClassifier.Classify(0.90f, m: 0.5f, t: 0.5f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Cliff, b);
        }

        [Test]
        public void LowTemperature_ReturnsSnow()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.5f, t: 0.10f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Snow, b);
        }

        [Test]
        public void HotAndDry_ReturnsDesert()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.20f, t: 0.80f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Desert, b);
        }

        [Test]
        public void HotMoistNearWater_ReturnsTropicalCoast()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.60f, t: 0.80f, hasNearbyWater: true,
                                             DefaultConfig());
            Assert.AreEqual(Biome.TropicalCoast, b);
        }

        [Test]
        public void HotMoistFarFromWater_FallsBackToGrasslandOrForest()
        {
            var b = BiomeClassifier.Classify(0.50f, m: 0.50f, t: 0.80f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreNotEqual(Biome.TropicalCoast, b);
        }

        [Test]
        public void HighMoisture_ReturnsForest()
        {
            var b = BiomeClassifier.Classify(0.60f, m: 0.80f, t: 0.50f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Forest, b);
        }

        [Test]
        public void MidMoisture_ReturnsGrassland()
        {
            var b = BiomeClassifier.Classify(0.60f, m: 0.50f, t: 0.50f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.Grassland, b);
        }

        [Test]
        public void LowMoisture_ReturnsDryGrass()
        {
            var b = BiomeClassifier.Classify(0.60f, m: 0.10f, t: 0.50f, hasNearbyWater: false,
                                             DefaultConfig());
            Assert.AreEqual(Biome.DryGrass, b);
        }
    }
}
```

- [ ] **Step 2: Verify failure (BiomeClassifier missing)**

- [ ] **Step 3: Implement `BiomeClassifier.cs`**

```csharp
namespace RTSCL.World
{
    public static class BiomeClassifier
    {
        public static Biome Classify(float e, float m, float t, bool hasNearbyWater,
                                     WorldGenConfig c)
        {
            if (e < c.DeepWaterMax) return Biome.DeepWater;
            if (e < c.ShoreMax) return Biome.Shore;
            if (e > c.CliffMin) return Biome.Cliff;
            if (t < c.SnowMax) return Biome.Snow;
            if (t > c.TropicalMin && m < c.DesertMoistureMax) return Biome.Desert;
            if (t > c.TropicalMin && m >= c.DesertMoistureMax && hasNearbyWater)
                return Biome.TropicalCoast;
            if (m > c.ForestMoistureMin) return Biome.Forest;
            if (m > c.GrasslandMoistureMin) return Biome.Grassland;
            return Biome.DryGrass;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify 10/10 BiomeClassifierTests pass**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/BiomeClassifier.cs Assets/Tests/Editor/BiomeClassifierTests.cs
git commit -m "feat(world): add BiomeClassifier with threshold-based biome lookup"
```

---

## Task 6: Implement SpawnPlanner with tests

Greedy farthest-point sampling on land cells, with a buffer to impassable terrain.

**Files:**
- Create: `Assets/Scripts/World/SpawnPlanner.cs`
- Test: `Assets/Tests/Editor/SpawnPlannerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using NUnit.Framework;
using RTSCL.World;
using Unity.Mathematics;

namespace RTSCL.World.Tests
{
    public class SpawnPlannerTests
    {
        private static Biome[,] MakeAllGrassland(int w, int h)
        {
            var b = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                b[x, y] = Biome.Grassland;
            return b;
        }

        [Test]
        public void PlaceSpawns_OnUniformGrass_ReturnsRequestedCount()
        {
            var biomes = MakeAllGrassland(64, 64);
            var rng = new Random(42);
            var spawns = SpawnPlanner.PlaceSpawns(biomes, 64, 64, playerCount: 4,
                                                  buffer: 0, candidatesPerSpawn: 100, ref rng);
            Assert.AreEqual(4, spawns.Length);
        }

        [Test]
        public void PlaceSpawns_AllSpawnsOnEligibleCells()
        {
            var biomes = MakeAllGrassland(32, 32);
            var rng = new Random(7);
            var spawns = SpawnPlanner.PlaceSpawns(biomes, 32, 32, playerCount: 3,
                                                  buffer: 0, candidatesPerSpawn: 100, ref rng);
            foreach (var s in spawns)
            {
                Assert.That(biomes[s.x, s.y], Is.EqualTo(Biome.Grassland)
                                                    .Or.EqualTo(Biome.Forest));
            }
        }

        [Test]
        public void PlaceSpawns_Determinism()
        {
            var biomes = MakeAllGrassland(48, 48);
            var rng1 = new Random(123);
            var rng2 = new Random(123);
            var a = SpawnPlanner.PlaceSpawns(biomes, 48, 48, 2, 0, 50, ref rng1);
            var b = SpawnPlanner.PlaceSpawns(biomes, 48, 48, 2, 0, 50, ref rng2);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].x, b[i].x);
                Assert.AreEqual(a[i].y, b[i].y);
            }
        }

        [Test]
        public void PlaceSpawns_BufferKeepsAwayFromWater()
        {
            int w = 32, h = 32;
            var biomes = MakeAllGrassland(w, h);
            // Make left column water
            for (int y = 0; y < h; y++) biomes[0, y] = Biome.DeepWater;

            var rng = new Random(99);
            var spawns = SpawnPlanner.PlaceSpawns(biomes, w, h, playerCount: 2,
                                                  buffer: 4, candidatesPerSpawn: 100, ref rng);
            foreach (var s in spawns)
                Assert.GreaterOrEqual(s.x, 4, "spawn must be ≥4 cells from water column");
        }
    }
}
```

- [ ] **Step 2: Verify failure**

- [ ] **Step 3: Implement `SpawnPlanner.cs`**

```csharp
using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public static class SpawnPlanner
    {
        private static bool IsImpassable(Biome b) =>
            b == Biome.DeepWater || b == Biome.Cliff || b == Biome.Shore;

        private static bool IsEligibleStart(Biome b) =>
            b == Biome.Grassland || b == Biome.Forest;

        public static int2[] PlaceSpawns(Biome[,] biomes, int w, int h, int playerCount,
                                          int buffer, int candidatesPerSpawn,
                                          ref Random rng)
        {
            var candidates = new List<int2>();
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (!IsEligibleStart(biomes[x, y])) continue;
                if (!HasBuffer(biomes, w, h, x, y, buffer)) continue;
                candidates.Add(new int2(x, y));
            }

            if (candidates.Count == 0)
                return new int2[0];

            // Reservoir-like sample: pick up to playerCount*candidatesPerSpawn from candidates
            int sampleCount = math.min(playerCount * candidatesPerSpawn, candidates.Count);
            var sample = new List<int2>(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                int idx = rng.NextInt(0, candidates.Count);
                sample.Add(candidates[idx]);
            }

            // Greedy farthest-point sampling
            var chosen = new List<int2>(playerCount);
            chosen.Add(sample[rng.NextInt(0, sample.Count)]);
            while (chosen.Count < playerCount && sample.Count > 0)
            {
                int2 best = sample[0];
                int bestMinDist = -1;
                foreach (var cand in sample)
                {
                    int minDist = int.MaxValue;
                    foreach (var c in chosen)
                    {
                        int dx = cand.x - c.x;
                        int dy = cand.y - c.y;
                        int d = dx * dx + dy * dy;
                        if (d < minDist) minDist = d;
                    }
                    if (minDist > bestMinDist) { bestMinDist = minDist; best = cand; }
                }
                chosen.Add(best);
            }
            return chosen.ToArray();
        }

        private static bool HasBuffer(Biome[,] biomes, int w, int h,
                                      int x, int y, int buffer)
        {
            if (buffer <= 0) return true;
            for (int dx = -buffer; dx <= buffer; dx++)
            for (int dy = -buffer; dy <= buffer; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) return false;
                if (IsImpassable(biomes[nx, ny])) return false;
            }
            return true;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify 4/4 SpawnPlannerTests pass**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/SpawnPlanner.cs Assets/Tests/Editor/SpawnPlannerTests.cs
git commit -m "feat(world): add SpawnPlanner with farthest-point sampling"
```

---

## Task 7: Implement ResourcePlanner with tests

Place stone + food clusters around spawns and globally.

**Files:**
- Create: `Assets/Scripts/World/ResourcePlanner.cs`
- Test: `Assets/Tests/Editor/ResourcePlannerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using RTSCL.World;
using Unity.Mathematics;

namespace RTSCL.World.Tests
{
    public class ResourcePlannerTests
    {
        private static Biome[,] MakeGrassland(int w, int h)
        {
            var b = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                b[x, y] = Biome.Grassland;
            return b;
        }

        [Test]
        public void PlaceClusters_ProducesPerSpawnAndFreeRoam()
        {
            var biomes = MakeGrassland(64, 64);
            var spawns = new[] { new int2(16, 16), new int2(48, 48) };
            var cfg = new WorldGenConfig
            {
                StoneClustersPerSpawn = 2,
                FoodClustersPerSpawn = 2,
                FreeRoamClusterCountMin = 6,
                FreeRoamClusterCountMax = 6,
                ClusterSizeMin = 3, ClusterSizeMax = 3,
                ClusterRadiusMin = 6, ClusterRadiusMax = 10,
            };
            var rng = new Random(11);
            var clusters = ResourcePlanner.PlaceClusters(biomes, 64, 64, spawns, cfg, ref rng);
            // 2 spawns * (2 stone + 2 food) = 8, plus 6 free-roam = 14
            Assert.AreEqual(14, clusters.Count);
        }

        [Test]
        public void PlaceClusters_StoneAndFoodBothRepresented()
        {
            var biomes = MakeGrassland(64, 64);
            var spawns = new[] { new int2(32, 32) };
            var rng = new Random(5);
            var clusters = ResourcePlanner.PlaceClusters(biomes, 64, 64, spawns,
                                                          new WorldGenConfig(), ref rng);
            bool hasStone = false, hasFood = false;
            foreach (var c in clusters)
            {
                if (c.Type == ResourceType.Stone) hasStone = true;
                if (c.Type == ResourceType.Food) hasFood = true;
            }
            Assert.IsTrue(hasStone);
            Assert.IsTrue(hasFood);
        }

        [Test]
        public void PlaceClusters_Determinism()
        {
            var biomes = MakeGrassland(48, 48);
            var spawns = new[] { new int2(10, 10), new int2(35, 35) };
            var cfg = new WorldGenConfig();
            var rng1 = new Random(2026);
            var rng2 = new Random(2026);
            var a = ResourcePlanner.PlaceClusters(biomes, 48, 48, spawns, cfg, ref rng1);
            var b = ResourcePlanner.PlaceClusters(biomes, 48, 48, spawns, cfg, ref rng2);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Type, b[i].Type);
                Assert.AreEqual(a[i].Center.x, b[i].Center.x);
                Assert.AreEqual(a[i].Center.y, b[i].Center.y);
            }
        }
    }
}
```

- [ ] **Step 2: Verify failure**

- [ ] **Step 3: Implement `ResourcePlanner.cs`**

```csharp
using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public static class ResourcePlanner
    {
        private static bool IsPassable(Biome b) =>
            b != Biome.DeepWater && b != Biome.Cliff && b != Biome.Shore;

        public static List<ResourceCluster> PlaceClusters(Biome[,] biomes, int w, int h,
                                                          int2[] spawns, WorldGenConfig cfg,
                                                          ref Random rng)
        {
            var result = new List<ResourceCluster>();
            var occupied = new bool[w, h];

            foreach (var spawn in spawns)
            {
                for (int i = 0; i < cfg.StoneClustersPerSpawn; i++)
                    TryPlaceClusterAround(biomes, w, h, spawn, ResourceType.Stone,
                                          cfg, occupied, ref rng, result);
                for (int i = 0; i < cfg.FoodClustersPerSpawn; i++)
                    TryPlaceClusterAround(biomes, w, h, spawn, ResourceType.Food,
                                          cfg, occupied, ref rng, result);
            }

            int freeCount = rng.NextInt(cfg.FreeRoamClusterCountMin,
                                        cfg.FreeRoamClusterCountMax + 1);
            for (int i = 0; i < freeCount; i++)
            {
                var type = rng.NextBool() ? ResourceType.Stone : ResourceType.Food;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    int x = rng.NextInt(0, w);
                    int y = rng.NextInt(0, h);
                    if (!IsPassable(biomes[x, y])) continue;
                    if (occupied[x, y]) continue;
                    PlaceCluster(biomes, w, h, new int2(x, y), type, cfg, occupied,
                                  ref rng, result);
                    break;
                }
            }
            return result;
        }

        private static void TryPlaceClusterAround(Biome[,] biomes, int w, int h, int2 spawn,
                                                  ResourceType type, WorldGenConfig cfg,
                                                  bool[,] occupied, ref Random rng,
                                                  List<ResourceCluster> result)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                int radius = rng.NextInt(cfg.ClusterRadiusMin, cfg.ClusterRadiusMax + 1);
                float angle = rng.NextFloat(0f, math.PI * 2f);
                int x = spawn.x + (int)(math.cos(angle) * radius);
                int y = spawn.y + (int)(math.sin(angle) * radius);
                if (x < 0 || x >= w || y < 0 || y >= h) continue;
                if (!IsPassable(biomes[x, y])) continue;
                if (occupied[x, y]) continue;
                if (type == ResourceType.Food && biomes[x, y] != Biome.Grassland
                                              && biomes[x, y] != Biome.Forest) continue;
                PlaceCluster(biomes, w, h, new int2(x, y), type, cfg, occupied,
                              ref rng, result);
                return;
            }
        }

        private static void PlaceCluster(Biome[,] biomes, int w, int h, int2 center,
                                          ResourceType type, WorldGenConfig cfg,
                                          bool[,] occupied, ref Random rng,
                                          List<ResourceCluster> result)
        {
            int size = rng.NextInt(cfg.ClusterSizeMin, cfg.ClusterSizeMax + 1);
            var cells = new List<int2>();
            cells.Add(center);
            occupied[center.x, center.y] = true;
            int placed = 1;
            int attempt = 0;
            while (placed < size && attempt < size * 8)
            {
                attempt++;
                int dx = rng.NextInt(-1, 2);
                int dy = rng.NextInt(-1, 2);
                int nx = center.x + dx;
                int ny = center.y + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (!IsPassable(biomes[nx, ny])) continue;
                if (occupied[nx, ny]) continue;
                cells.Add(new int2(nx, ny));
                occupied[nx, ny] = true;
                placed++;
            }
            result.Add(new ResourceCluster(type, center, cells));
        }
    }
}
```

- [ ] **Step 4: Run tests, verify 3/3 ResourcePlannerTests pass**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/ResourcePlanner.cs Assets/Tests/Editor/ResourcePlannerTests.cs
git commit -m "feat(world): add ResourcePlanner with per-spawn and free-roam clusters"
```

---

## Task 8: Implement ReachabilityChecker with tests

Flood-fill from spawn 0 across passable cells; report any unreachable spawns or resources.

**Files:**
- Create: `Assets/Scripts/World/ReachabilityChecker.cs`
- Test: `Assets/Tests/Editor/ReachabilityCheckerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using RTSCL.World;
using Unity.Mathematics;

namespace RTSCL.World.Tests
{
    public class ReachabilityCheckerTests
    {
        [Test]
        public void AllReachable_OnOpenMap_ReturnsTrue()
        {
            int w = 16, h = 16;
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++) biomes[x, y] = Biome.Grassland;

            var spawns = new[] { new int2(2, 2), new int2(13, 13) };
            var clusters = new List<ResourceCluster>
            {
                new ResourceCluster(ResourceType.Stone, new int2(8, 8), new[] { new int2(8, 8) })
            };

            Assert.IsTrue(ReachabilityChecker.AllReachable(biomes, w, h, spawns, clusters));
        }

        [Test]
        public void SplitByWaterWall_ReturnsFalse()
        {
            int w = 16, h = 16;
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++) biomes[x, y] = Biome.Grassland;
            // Vertical water wall at x=8
            for (int y = 0; y < h; y++) biomes[8, y] = Biome.DeepWater;

            var spawns = new[] { new int2(2, 2), new int2(13, 13) };
            Assert.IsFalse(ReachabilityChecker.AllReachable(biomes, w, h, spawns,
                                                            new List<ResourceCluster>()));
        }

        [Test]
        public void ResourceOnCliff_ReturnsFalse()
        {
            int w = 8, h = 8;
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++) biomes[x, y] = Biome.Grassland;

            var spawns = new[] { new int2(0, 0) };
            // The cluster center is on a cliff cell, which is impassable
            biomes[4, 4] = Biome.Cliff;
            var clusters = new List<ResourceCluster>
            {
                new ResourceCluster(ResourceType.Stone, new int2(4, 4), new[] { new int2(4, 4) })
            };
            Assert.IsFalse(ReachabilityChecker.AllReachable(biomes, w, h, spawns, clusters));
        }
    }
}
```

- [ ] **Step 2: Verify failure**

- [ ] **Step 3: Implement `ReachabilityChecker.cs`**

```csharp
using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public static class ReachabilityChecker
    {
        private static bool IsPassable(Biome b) =>
            b != Biome.DeepWater && b != Biome.Cliff;

        public static bool AllReachable(Biome[,] biomes, int w, int h,
                                         int2[] spawns,
                                         IReadOnlyList<ResourceCluster> clusters)
        {
            if (spawns.Length == 0) return true;
            var reached = FloodFill(biomes, w, h, spawns[0]);
            for (int i = 1; i < spawns.Length; i++)
                if (!reached[spawns[i].x, spawns[i].y]) return false;
            foreach (var c in clusters)
                if (!reached[c.Center.x, c.Center.y]) return false;
            return true;
        }

        private static bool[,] FloodFill(Biome[,] biomes, int w, int h, int2 start)
        {
            var reached = new bool[w, h];
            if (!IsPassable(biomes[start.x, start.y])) return reached;
            var stack = new Stack<int2>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p.x < 0 || p.x >= w || p.y < 0 || p.y >= h) continue;
                if (reached[p.x, p.y]) continue;
                if (!IsPassable(biomes[p.x, p.y])) continue;
                reached[p.x, p.y] = true;
                stack.Push(new int2(p.x + 1, p.y));
                stack.Push(new int2(p.x - 1, p.y));
                stack.Push(new int2(p.x, p.y + 1));
                stack.Push(new int2(p.x, p.y - 1));
            }
            return reached;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify 3/3 ReachabilityCheckerTests pass**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/ReachabilityChecker.cs Assets/Tests/Editor/ReachabilityCheckerTests.cs
git commit -m "feat(world): add ReachabilityChecker flood-fill"
```

---

## Task 9: Implement WorldGenerator orchestrator with end-to-end test

Wires the pipeline together. Retries with `seed+1` up to `MaxRegenerationAttempts` if reachability fails.

**Files:**
- Create: `Assets/Scripts/World/WorldGenerator.cs`
- Test: `Assets/Tests/Editor/WorldGeneratorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class WorldGeneratorTests
    {
        [Test]
        public void Generate_SameSeed_ProducesIdenticalBiomes()
        {
            var cfg = new WorldGenConfig { Width = 64, Height = 64, PlayerCount = 2 };
            var w1 = new WorldGenerator().Generate(seed: 12345, cfg);
            var w2 = new WorldGenerator().Generate(seed: 12345, cfg);
            for (int x = 0; x < cfg.Width; x++)
            for (int y = 0; y < cfg.Height; y++)
                Assert.AreEqual(w1.Biomes[x, y], w2.Biomes[x, y],
                                $"divergence at ({x},{y})");
        }

        [Test]
        public void Generate_DifferentSeed_ProducesDifferentBiomes()
        {
            var cfg = new WorldGenConfig { Width = 64, Height = 64, PlayerCount = 2 };
            var w1 = new WorldGenerator().Generate(seed: 1, cfg);
            var w2 = new WorldGenerator().Generate(seed: 2, cfg);

            int differing = 0;
            for (int x = 0; x < cfg.Width; x++)
            for (int y = 0; y < cfg.Height; y++)
                if (w1.Biomes[x, y] != w2.Biomes[x, y]) differing++;
            Assert.Greater(differing, 100, "two different seeds should diverge meaningfully");
        }

        [Test]
        public void Generate_ProducesRequestedSpawnCount()
        {
            var cfg = new WorldGenConfig { Width = 96, Height = 96, PlayerCount = 4 };
            var w = new WorldGenerator().Generate(seed: 999, cfg);
            Assert.AreEqual(4, w.Spawns.Length);
        }

        [Test]
        public void Generate_AllSpawnsReachableFromEachOther()
        {
            var cfg = new WorldGenConfig { Width = 96, Height = 96, PlayerCount = 4 };
            var w = new WorldGenerator().Generate(seed: 555, cfg);
            Assert.IsTrue(ReachabilityChecker.AllReachable(w.Biomes, w.Width, w.Height,
                                                            w.Spawns, w.Resources));
        }

        [Test]
        public void Generate_ProducesAtLeastSomeWaterAndSomeLand()
        {
            var cfg = new WorldGenConfig { Width = 96, Height = 96, PlayerCount = 2 };
            var w = new WorldGenerator().Generate(seed: 7, cfg);
            int water = 0, land = 0;
            for (int x = 0; x < w.Width; x++)
            for (int y = 0; y < w.Height; y++)
            {
                if (w.Biomes[x, y] == Biome.DeepWater) water++;
                else land++;
            }
            Assert.Greater(water, 100, "expected meaningful coastline");
            Assert.Greater(land, 100, "expected meaningful land");
        }
    }
}
```

- [ ] **Step 2: Verify failure**

- [ ] **Step 3: Implement `WorldGenerator.cs`**

```csharp
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    public sealed class WorldGenerator
    {
        public WorldData Generate(int seed, WorldGenConfig cfg)
        {
            uint baseSeed = unchecked((uint)seed);
            if (baseSeed == 0) baseSeed = 1u;

            for (int attempt = 0; attempt < cfg.MaxRegenerationAttempts; attempt++)
            {
                uint trySeed = baseSeed + (uint)attempt;
                var result = TryGenerate(trySeed, cfg);
                if (result != null) return result;
            }
            throw new InvalidOperationException(
                $"WorldGenerator failed reachability after {cfg.MaxRegenerationAttempts} attempts " +
                $"(base seed {baseSeed}). Tune WorldGenConfig.");
        }

        private static WorldData TryGenerate(uint seed, WorldGenConfig cfg)
        {
            int w = cfg.Width, h = cfg.Height;

            // 1. Noise channels
            var elevField   = new NoiseField(seed, channel: 0, cfg.ElevationScale);
            var moistField  = new NoiseField(seed, channel: 1, cfg.MoistureScale);
            var tempField   = new NoiseField(seed, channel: 2, cfg.TemperatureScale);

            var elevation   = new float[w, h];
            var moisture    = new float[w, h];
            var temperature = new float[w, h];

            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                elevation[x, y]   = elevField.Sample(x, y);
                moisture[x, y]    = moistField.Sample(x, y);
                float t = tempField.Sample(x, y) * 0.6f;
                float latBias = math.abs(((float)y / h) - 0.5f) * 2f * 0.4f;
                temperature[x, y] = math.saturate(t + latBias);
            }

            // 2. Continental falloff
            ContinentShaper.ApplyFalloff(elevation, w, h, cfg.FalloffStrength);

            // 3. First-pass classification (without TropicalCoast water-proximity)
            var biomes = new Biome[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                biomes[x, y] = BiomeClassifier.Classify(
                    elevation[x, y], moisture[x, y], temperature[x, y],
                    hasNearbyWater: false, cfg);

            // 4. Second pass — upgrade hot+moist land near water to TropicalCoast
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (biomes[x, y] != Biome.Grassland && biomes[x, y] != Biome.Forest) continue;
                if (temperature[x, y] <= cfg.TropicalMin) continue;
                if (HasWaterWithinManhattan(biomes, w, h, x, y, radius: 2))
                    biomes[x, y] = Biome.TropicalCoast;
            }

            // 5. Post-processing
            ContinentShaper.RemoveMiniIslands(biomes, w, h, cfg.MiniIslandRemovalThreshold);
            ContinentShaper.FillMiniLakes(biomes, w, h, cfg.LakeFillThreshold);

            // 6. Spawn placement
            var rng = new Random(seed);
            // burn a few values to decorrelate from noise offsets
            rng.NextUInt(); rng.NextUInt();
            var spawns = SpawnPlanner.PlaceSpawns(biomes, w, h, cfg.PlayerCount,
                                                    cfg.SpawnBufferToImpassable,
                                                    cfg.CandidatesPerSpawn, ref rng);
            if (spawns.Length < cfg.PlayerCount) return null;

            // 7. Reserve spawn areas (set decoration-mask later in DecorationPlacer)

            // 8. Resource clusters
            var clusters = ResourcePlanner.PlaceClusters(biomes, w, h, spawns, cfg, ref rng);

            // 9. Reachability gate
            if (!ReachabilityChecker.AllReachable(biomes, w, h, spawns, clusters))
                return null;

            return new WorldData(w, h, biomes, spawns, clusters, seed);
        }

        private static bool HasWaterWithinManhattan(Biome[,] biomes, int w, int h,
                                                     int cx, int cy, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (math.abs(dx) + math.abs(dy) > radius) continue;
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (biomes[nx, ny] == Biome.DeepWater || biomes[nx, ny] == Biome.Shore)
                    return true;
            }
            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify 5/5 WorldGeneratorTests pass**

If `Generate_ProducesRequestedSpawnCount` fails for size 96×96, increase `cfg.Width/Height` in that test to 128 — the default falloff strength may produce too small a continent at 96×96. Document the minimum supported size in `WorldGenConfig.cs` comments.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/WorldGenerator.cs Assets/Tests/Editor/WorldGeneratorTests.cs
git commit -m "feat(world): add WorldGenerator orchestrator with reachability retry"
```

---

## Task 10: Implement MiniWorldSpritesSlicer (Editor tool)

One-shot editor utility that slices the Ground and Nature PNGs to a 16×16 grid and emits `TileBase` assets the resolver consumes.

**Files:**
- Create: `Assets/Editor/MiniWorldSpritesSlicer.cs`

- [ ] **Step 1: Write the slicer**

```csharp
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.Editor
{
    public static class MiniWorldSpritesSlicer
    {
        private const int CellSize = 16;
        private const string GroundFolder = "Assets/MiniWorldSprites/Ground";
        private const string NatureFolder = "Assets/MiniWorldSprites/Nature";
        private const string OutFolder    = "Assets/Generated/Tiles";
        private const string DecoOutFolder = "Assets/Generated/Tiles/Decorations";

        // For V1 we hardcode which sprite index represents each biome's "base" tile.
        // These indices are into the 16x16 grid in reading order (top-to-bottom, left-to-right
        // as Unity slices). Adjust if a particular sheet's center looks bad.
        private static readonly Dictionary<string, int> GroundBaseIndex = new()
        {
            { "Grass",        12 },
            { "DeadGrass",    12 },
            { "TexturedGrass",12 },
            { "Winter",       12 },
            { "Shore",         5 },
            { "Cliff",        12 },
            // Cliff-Water deferred to Phase 2 (transitions)
        };

        [MenuItem("Tools/RTSCL/Slice MiniWorldSprites")]
        public static void Run()
        {
            EnsureFolder(OutFolder);
            EnsureFolder(DecoOutFolder);

            EnsureDeepWaterTile();

            int sliced = 0, tilesMade = 1;  // 1 = DeepWater already counted

            foreach (var path in EnumeratePngs(GroundFolder))
            {
                if (SliceTo16(path)) sliced++;
                var name = Path.GetFileNameWithoutExtension(path);
                if (GroundBaseIndex.TryGetValue(name, out int idx))
                {
                    if (CreateBaseTile(path, name, idx)) tilesMade++;
                }
            }

            foreach (var path in EnumeratePngs(NatureFolder))
            {
                if (SliceTo16(path)) sliced++;
                tilesMade += CreateDecorationTiles(path);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Slicer] Sliced {sliced} PNGs; generated {tilesMade} TileBase assets.");
        }

        private static void EnsureDeepWaterTile()
        {
            const string tileOut = OutFolder + "/DeepWater.asset";
            if (AssetDatabase.LoadAssetAtPath<Tile>(tileOut) != null) return;

            var pngPath = OutFolder + "/_DeepWaterTexture.png";
            if (!File.Exists(pngPath))
            {
                var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                var color = new Color32(56, 88, 156, 255);
                var pixels = new Color32[16 * 16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
                tex.SetPixels32(pixels);
                tex.Apply();
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(pngPath);
                Object.DestroyImmediate(tex);
            }

            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType         = TextureImporterType.Sprite;
                importer.spriteImportMode    = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 16;
                importer.filterMode          = FilterMode.Point;
                importer.textureCompression  = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            AssetDatabase.CreateAsset(tile, tileOut);
        }

        private static IEnumerable<string> EnumeratePngs(string folder)
        {
            if (!Directory.Exists(folder)) yield break;
            foreach (var p in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
                yield return p.Replace('\\', '/');
        }

        private static bool SliceTo16(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return false;
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 16;
            importer.filterMode          = FilterMode.Point;
            importer.textureCompression  = TextureImporterCompression.Uncompressed;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            // Read texture dims via temporary load
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null) { importer.SaveAndReimport(); return false; }
            int cols = tex.width  / CellSize;
            int rows = tex.height / CellSize;

            var rects = new List<SpriteRect>(cols * rows);
            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            int i = 0;
            for (int row = rows - 1; row >= 0; row--)  // Unity origin is bottom-left;
                                                       // start from top row to match reading order
            for (int col = 0; col < cols; col++)
            {
                rects.Add(new SpriteRect
                {
                    name      = $"{baseName}_{i}",
                    rect      = new Rect(col * CellSize, row * CellSize, CellSize, CellSize),
                    pivot     = new Vector2(0.5f, 0.5f),
                    alignment = SpriteAlignment.Center,
                    spriteID  = GUID.Generate(),
                });
                i++;
            }
            provider.SetSpriteRects(rects.ToArray());

            // Sync name table (required for sprite lookup by name)
            var nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            var pairs = new List<SpriteNameFileIdPair>(rects.Count);
            foreach (var r in rects)
                pairs.Add(new SpriteNameFileIdPair(r.name, r.spriteID));
            nameProvider.SetNameFileIdPairs(pairs);

            provider.Apply();
            importer.SaveAndReimport();
            return true;
        }

        private static bool CreateBaseTile(string sheetAssetPath, string biomeName, int spriteIndex)
        {
            var sprites = LoadSlicedSprites(sheetAssetPath);
            if (spriteIndex < 0 || spriteIndex >= sprites.Count)
            {
                Debug.LogWarning($"[Slicer] '{biomeName}' index {spriteIndex} OOB " +
                                 $"(sheet has {sprites.Count}). Using 0.");
                spriteIndex = 0;
            }
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprites[spriteIndex];
            var outPath = $"{OutFolder}/{biomeName}.asset";
            AssetDatabase.CreateAsset(tile, outPath);
            return true;
        }

        private static int CreateDecorationTiles(string sheetAssetPath)
        {
            var sprites = LoadSlicedSprites(sheetAssetPath);
            string baseName = Path.GetFileNameWithoutExtension(sheetAssetPath);
            int made = 0;
            for (int i = 0; i < sprites.Count; i++)
            {
                // Skip fully-transparent sprites (heuristic: tiny rect won't help in V1; emit all)
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprites[i];
                var outPath = $"{DecoOutFolder}/{baseName}_{i}.asset";
                AssetDatabase.CreateAsset(tile, outPath);
                made++;
            }
            return made;
        }

        private static List<Sprite> LoadSlicedSprites(string sheetAssetPath)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(sheetAssetPath);
            var list = new List<Sprite>();
            foreach (var o in all)
                if (o is Sprite s) list.Add(s);
            return list;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            var parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            var leaf   = Path.GetFileName(assetFolder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
```

- [ ] **Step 2: Open Unity, wait for compile, run the menu item**

In Unity menu: `Tools → RTSCL → Slice MiniWorldSprites`.

Expected console output: `[Slicer] Sliced N PNGs; generated M TileBase assets.`
(N ≈ 17 — the count of PNGs in Ground + Nature; M ≈ 250+ — many decoration tiles.)

- [ ] **Step 3: Verify generated assets**

In the Project window, navigate to `Assets/Generated/Tiles/` — confirm `Grass.asset`, `Shore.asset`, etc. exist with a sprite preview showing a single 16×16 tile.

In `Assets/Generated/Tiles/Decorations/` — many `Trees_0.asset`, `Cactus_0.asset` etc.

- [ ] **Step 4: Commit slicer + generated tiles + modified pack meta files**

The slicer mutates importer settings of every Ground/Nature PNG, so those `.meta` files have new bytes too. Stage them along with the new generated assets:

```bash
git add Assets/Editor/MiniWorldSpritesSlicer.cs \
        Assets/Generated/ \
        Assets/MiniWorldSprites/Ground/ \
        Assets/MiniWorldSprites/Nature/
git commit -m "feat(editor): add MiniWorldSpritesSlicer; slice Ground+Nature; generate tile assets"
```

---

## Task 11: Implement IBiomeTileResolver + SingleTileBiomeResolver

Resolver maps `Biome` enum → `TileBase`. V1: ScriptableObject with a serialized dictionary. V2: a different ScriptableObject implementing the same interface.

**Files:**
- Create: `Assets/Scripts/World/Unity/IBiomeTileResolver.cs`
- Create: `Assets/Scripts/World/Unity/SingleTileBiomeResolver.cs`

- [ ] **Step 1: Write `IBiomeTileResolver.cs`**

```csharp
using RTSCL.World;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    public interface IBiomeTileResolver
    {
        TileBase GetTile(WorldData world, int x, int y);
    }
}
```

- [ ] **Step 2: Write `SingleTileBiomeResolver.cs`**

```csharp
using System;
using System.Collections.Generic;
using RTSCL.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    [CreateAssetMenu(menuName = "RTSCL/Single-Tile Biome Resolver",
                     fileName = "SingleTileBiomeResolver")]
    public sealed class SingleTileBiomeResolver : ScriptableObject, IBiomeTileResolver
    {
        [Serializable]
        public struct Entry
        {
            public Biome Biome;
            public TileBase Tile;
        }

        [SerializeField] private List<Entry> _entries = new();

        private Dictionary<Biome, TileBase> _lookup;

        public TileBase GetTile(WorldData world, int x, int y)
        {
            EnsureLookup();
            return _lookup.TryGetValue(world.BiomeAt(x, y), out var tile) ? tile : null;
        }

        private void EnsureLookup()
        {
            if (_lookup != null && _lookup.Count == _entries.Count) return;
            _lookup = new Dictionary<Biome, TileBase>(_entries.Count);
            foreach (var e in _entries)
                if (e.Tile != null) _lookup[e.Biome] = e.Tile;
        }

        private void OnValidate() => _lookup = null;
    }
}
```

- [ ] **Step 3: Create the resolver asset and populate it**

In Unity:
1. Right-click in `Assets/Generated/` → `Create → RTSCL → Single-Tile Biome Resolver`. Name it `SingleTileBiomeResolver`.
2. In the Inspector, expand `Entries` and add 9 entries — one per `Biome` enum value. Drag the matching tile assets from `Assets/Generated/Tiles/` into each `Tile` slot:

| Biome | Tile |
|---|---|
| DeepWater | `DeepWater.asset` (auto-generated by slicer, solid blue 16×16) |
| Shore | `Shore.asset` |
| Cliff | `Cliff.asset` |
| Snow | `Winter.asset` |
| Desert | `DeadGrass.asset` |
| TropicalCoast | `Grass.asset` |
| Forest | `Grass.asset` |
| Grassland | `Grass.asset` |
| DryGrass | `DeadGrass.asset` |

If `DeepWater.asset` looks visually wrong (e.g., wrong shade of blue), open it and swap its `Sprite` field to a water sprite from the sliced `Shore_*` set. The hex value `(56, 88, 156)` was chosen to read as deep ocean against the pack's pastel palette.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/IBiomeTileResolver.cs \
        Assets/Scripts/World/Unity/SingleTileBiomeResolver.cs \
        Assets/Generated/SingleTileBiomeResolver.asset \
        Assets/Generated/SingleTileBiomeResolver.asset.meta
git commit -m "feat(world.unity): add biome resolver interface + V1 single-tile implementation"
```

---

## Task 12: Implement TilePainter

Paints a `WorldData` onto a `Tilemap` via `IBiomeTileResolver`. Clears the tilemap before painting.

**Files:**
- Create: `Assets/Scripts/World/Unity/TilePainter.cs`

- [ ] **Step 1: Write `TilePainter.cs`**

```csharp
using RTSCL.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    public sealed class TilePainter
    {
        private readonly Tilemap _tilemap;
        private readonly IBiomeTileResolver _resolver;

        public TilePainter(Tilemap tilemap, IBiomeTileResolver resolver)
        {
            _tilemap  = tilemap;
            _resolver = resolver;
        }

        public void Paint(WorldData world)
        {
            _tilemap.ClearAllTiles();
            var positions = new Vector3Int[world.Width * world.Height];
            var tiles     = new TileBase[world.Width * world.Height];
            int i = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                positions[i] = new Vector3Int(x, y, 0);
                tiles[i]     = _resolver.GetTile(world, x, y);
                i++;
            }
            _tilemap.SetTiles(positions, tiles);
            _tilemap.RefreshAllTiles();
        }
    }
}
```

- [ ] **Step 2: Verify Unity compiles cleanly**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/TilePainter.cs
git commit -m "feat(world.unity): add TilePainter that fills a Tilemap from WorldData"
```

---

## Task 13: Implement DecorationPlacer

Scatters decoration tiles on a separate Tilemap based on per-biome probability tables. Honors a "no-decoration" mask around each spawn.

**Files:**
- Create: `Assets/Scripts/World/Unity/DecorationPlacer.cs`

- [ ] **Step 1: Write `DecorationPlacer.cs`**

```csharp
using System;
using System.Collections.Generic;
using RTSCL.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    public sealed class DecorationPlacer
    {
        [Serializable]
        public sealed class DecorationConfig
        {
            // Per-biome decoration probabilities (0..1) and weighted sprite pools.
            public float ForestDensity = 0.25f;
            public TileBase[] ForestTiles;       // Trees_*.asset

            public float GrasslandDensity = 0.08f;
            public TileBase[] GrasslandTiles;    // mix of Trees, Wheatfield, Rocks, DeadTrees

            public float DryGrassDensity = 0.12f;
            public TileBase[] DryGrassTiles;     // DeadTrees, Rocks, Tumbleweed

            public float DesertDensity = 0.10f;
            public TileBase[] DesertTiles;       // Cactus, Tumbleweed, Rocks

            public float SnowDensity = 0.20f;
            public TileBase[] SnowTiles;         // PineTrees, WinterTrees, WinterDeadTrees, Rocks

            public float TropicalDensity = 0.18f;
            public TileBase[] TropicalTiles;     // CoconutTrees, Rocks

            public float ShoreDensity = 0.03f;
            public TileBase[] ShoreTiles;        // Rocks only

            public float CliffDensity = 0.05f;
            public TileBase[] CliffTiles;        // Rocks only

            public int SpawnReservedRadius = 2;  // total reserved area = (2r+1)²
        }

        private readonly Tilemap _decorationMap;
        private readonly DecorationConfig _cfg;

        public DecorationPlacer(Tilemap decorationMap, DecorationConfig cfg)
        {
            _decorationMap = decorationMap;
            _cfg = cfg;
        }

        public void Place(WorldData world, uint seed)
        {
            _decorationMap.ClearAllTiles();
            var rng = new Random(seed == 0 ? 1u : seed);

            var reserved = BuildReservedMask(world);

            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (reserved[x, y]) continue;
                var biome = world.BiomeAt(x, y);
                (float density, TileBase[] pool) = PoolFor(biome);
                if (pool == null || pool.Length == 0) continue;
                if (rng.NextFloat() > density) continue;

                var tile = pool[rng.NextInt(0, pool.Length)];
                _decorationMap.SetTile(new Vector3Int(x, y, 0), tile);
            }
            _decorationMap.RefreshAllTiles();
        }

        private (float, TileBase[]) PoolFor(Biome b) => b switch
        {
            Biome.Forest        => (_cfg.ForestDensity,    _cfg.ForestTiles),
            Biome.Grassland     => (_cfg.GrasslandDensity, _cfg.GrasslandTiles),
            Biome.DryGrass      => (_cfg.DryGrassDensity,  _cfg.DryGrassTiles),
            Biome.Desert        => (_cfg.DesertDensity,    _cfg.DesertTiles),
            Biome.Snow          => (_cfg.SnowDensity,      _cfg.SnowTiles),
            Biome.TropicalCoast => (_cfg.TropicalDensity,  _cfg.TropicalTiles),
            Biome.Shore         => (_cfg.ShoreDensity,     _cfg.ShoreTiles),
            Biome.Cliff         => (_cfg.CliffDensity,     _cfg.CliffTiles),
            _ => (0f, null),
        };

        private bool[,] BuildReservedMask(WorldData world)
        {
            var mask = new bool[world.Width, world.Height];
            int r = _cfg.SpawnReservedRadius;
            foreach (var s in world.Spawns)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int nx = s.x + dx, ny = s.y + dy;
                    if (nx >= 0 && nx < world.Width && ny >= 0 && ny < world.Height)
                        mask[nx, ny] = true;
                }
            }
            // Also mask resource cluster cells so decorations don't overlap them
            foreach (var cluster in world.Resources)
                foreach (var cell in cluster.Cells)
                    mask[cell.x, cell.y] = true;
            return mask;
        }
    }
}
```

- [ ] **Step 2: Verify Unity compiles cleanly**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/DecorationPlacer.cs
git commit -m "feat(world.unity): add DecorationPlacer with per-biome density + spawn-reserved mask"
```

---

## Task 14: Implement CameraFitter

Frames the camera on the generated world bounds.

**Files:**
- Create: `Assets/Scripts/World/Unity/CameraFitter.cs`

- [ ] **Step 1: Write `CameraFitter.cs`**

```csharp
using UnityEngine;

namespace RTSCL.World.Unity
{
    public static class CameraFitter
    {
        public static void Fit(Camera camera, int width, int height, float cellSize = 1f,
                                float padding = 1.05f)
        {
            if (camera == null) return;
            float wWorld = width  * cellSize;
            float hWorld = height * cellSize;
            float orthoFromWidth  = (wWorld / camera.aspect) * 0.5f;
            float orthoFromHeight = hWorld * 0.5f;
            camera.orthographic   = true;
            camera.orthographicSize = Mathf.Max(orthoFromWidth, orthoFromHeight) * padding;
            camera.transform.position = new Vector3(wWorld * 0.5f, hWorld * 0.5f, -10f);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/World/Unity/CameraFitter.cs
git commit -m "feat(world.unity): add CameraFitter to frame the generated world"
```

---

## Task 15: Implement WorldGeneratorBootstrap

MonoBehaviour entry point. Inspector fields + Regenerate button. Auto-runs on `Start`.

**Files:**
- Create: `Assets/Scripts/World/Unity/WorldGeneratorBootstrap.cs`

- [ ] **Step 1: Write `WorldGeneratorBootstrap.cs`**

```csharp
using RTSCL.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    public sealed class WorldGeneratorBootstrap : MonoBehaviour
    {
        [Header("Tilemaps")]
        [SerializeField] private Tilemap _terrainMap;
        [SerializeField] private Tilemap _decorationMap;

        [Header("Resolver")]
        [SerializeField] private SingleTileBiomeResolver _resolver;

        [Header("Decorations")]
        [SerializeField] private DecorationPlacer.DecorationConfig _decorationConfig;

        [Header("Generation")]
        [Tooltip("-1 = pick a fresh random seed each run")]
        [SerializeField] private int _seed = -1;
        [SerializeField] private WorldGenConfig _config = new WorldGenConfig();

        [Header("Camera")]
        [SerializeField] private Camera _cameraToFit;
        [SerializeField] private bool _autoFitCamera = true;

        private void Start() => Regenerate();

        [ContextMenu("Regenerate")]
        public void Regenerate()
        {
            if (_terrainMap == null || _decorationMap == null || _resolver == null)
            {
                Debug.LogError("[WorldGen] Wire up Terrain Tilemap, Decoration Tilemap, " +
                               "and Resolver in the Inspector.", this);
                return;
            }

            int effectiveSeed = _seed < 0 ? Random.Range(1, int.MaxValue) : _seed;
            Debug.Log($"[WorldGen] Generating with seed {effectiveSeed}…");

            WorldData world;
            try { world = new WorldGenerator().Generate(effectiveSeed, _config); }
            catch (System.Exception e)
            {
                Debug.LogError($"[WorldGen] Generation failed: {e.Message}", this);
                return;
            }

            new TilePainter(_terrainMap, _resolver).Paint(world);
            new DecorationPlacer(_decorationMap, _decorationConfig).Place(world, world.Seed);

            if (_autoFitCamera && _cameraToFit != null)
                CameraFitter.Fit(_cameraToFit, world.Width, world.Height);

            Debug.Log($"[WorldGen] Done. Spawns: {world.Spawns.Length}, " +
                      $"Clusters: {world.Resources.Count}.");
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/World/Unity/WorldGeneratorBootstrap.cs
git commit -m "feat(world.unity): add WorldGeneratorBootstrap MonoBehaviour entry point"
```

---

## Task 16: Wire up SampleScene + manual smoke test

Final assembly: GameObjects, Tilemap setup, Resolver and DecorationConfig population, press Play.

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity` (via Editor — never hand-edit)

- [ ] **Step 1: Add Grid + Terrain Tilemap**

In Unity Hierarchy, right-click → `2D Object → Tilemap → Rectangular`. This creates a `Grid` with a child `Tilemap`. Rename the child to `Terrain`. Set its `Tilemap Renderer → Sorting Order` to 0.

- [ ] **Step 2: Add Decoration Tilemap**

Right-click the `Grid` → `2D Object → Tilemap → Rectangular`. Rename the new child to `Decorations`. Set its `Tilemap Renderer → Sorting Order` to 10.

- [ ] **Step 3: Add bootstrap GameObject**

Hierarchy right-click → `Create Empty`, name it `WorldGenerator`. Add the `WorldGeneratorBootstrap` component.

- [ ] **Step 4: Wire bootstrap inspector fields**

In the `WorldGeneratorBootstrap` Inspector:
- `Terrain Map` ← drag `Terrain` Tilemap GameObject
- `Decoration Map` ← drag `Decorations` Tilemap GameObject
- `Resolver` ← drag `Assets/Generated/SingleTileBiomeResolver.asset`
- `Seed` = `-1` (random per run)
- `Config` — leave defaults
- `Camera To Fit` ← drag `Main Camera`
- `Auto Fit Camera` = checked

- [ ] **Step 5: Wire decoration config**

In the same Inspector, expand `Decoration Config` and populate each tile array by dragging from `Assets/Generated/Tiles/Decorations/`:
- `Forest Tiles` ← all `Trees_*.asset` (10–20 of them)
- `Grassland Tiles` ← mix of `Trees_*`, `Wheatfield_*`, `Rocks_*`, `DeadTrees_*`
- `Dry Grass Tiles` ← `DeadTrees_*`, `Rocks_*`, `Tumbleweed_*`
- `Desert Tiles` ← `Cactus_*`, `Tumbleweed_*`, `Rocks_*`
- `Snow Tiles` ← `PineTrees_*`, `WinterTrees_*`, `WinterDeadTrees_*`, `Rocks_*`
- `Tropical Tiles` ← `CoconutTrees_*`, `Rocks_*`
- `Shore Tiles` ← `Rocks_*`
- `Cliff Tiles` ← `Rocks_*`

Tip: select multiple Tile assets in the Project window and drag them onto the array header — Unity appends all of them at once.

- [ ] **Step 6: Press Play**

Expected:
- Console shows `[WorldGen] Generating with seed N…` followed by `[WorldGen] Done. Spawns: 2, Clusters: 14`.
- Scene view shows a 128×128 continent with coastline, multiple visible biomes (grass, forest, cliff, possibly snow at top/bottom), decorations scattered on land.
- Camera framed around the world.

- [ ] **Step 7: Visual sanity checks (manual)**

- One connected continent (no scattered tiny islands)
- Each biome's decorations match (e.g., cactus only in desert, pine trees only in snow)
- Spawn area visibly clear of decorations (5×5 empty patch on Grassland)
- Resource clusters visible (Rocks, Wheatfield bunched together)

If any biome is missing entirely, regenerate a few times — small maps occasionally lack hot/cold extremes. If a biome's decoration array is empty, the Console will be silent — fill in any missing arrays.

- [ ] **Step 8: Commit the scene + onboarding doc updates**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "feat(scene): wire SampleScene to WorldGeneratorBootstrap"
```

- [ ] **Step 9: Final regression — run all EditMode tests**

`Window → General → Test Runner → EditMode → Run All`. Expected: all tests across Tasks 3–9 still pass.

---

## Phase 2 — Out of scope, listed here so it's not forgotten

When V1 is verified visually, the following list captures Phase 2:

- Implement `RuleTileBiomeResolver` (alternate `IBiomeTileResolver`) that picks tiles based on neighbor biomes. Reuse the same `WorldData` — no generator changes.
- Configure `RuleTile` assets per biome with edge/corner sprites from the Ground sheets.
- Cliff-Water composite transitions using `Cliff-Water.png`.
- River carving (Phase 3 — needs A* path between two coast points before biome classification).
- Roads connecting spawn points (Phase 3).

## Notes for the implementer

- **Unity script reload pauses tests.** After saving an asmdef file, wait for the Console "compiled in X.Xs" message before running Test Runner.
- **Pure-logic assembly cannot reference `UnityEngine`.** If a compile error says "UnityEngine could not be resolved" from a `RTSCL.World` file, you accidentally added a `using UnityEngine;` — remove it. `Unity.Mathematics` is intentionally allowed.
- **NUnit assembly resolution.** If the Test Runner cannot find tests after Task 1, try setting `"precompiledReferences": []` and instead add `"UnityEngine.TestRunner"` + `"UnityEditor.TestRunner"` to `references`. Some Unity 6 versions ship NUnit via these instead of as a precompiled reference.
- **Determinism caveat.** `UnityEngine.Random` (used in `WorldGeneratorBootstrap` only for the random-seed picker when `_seed = -1`) is per-session and not cross-platform — but it only picks the seed, not the world data, so this is fine.
- **Performance.** A 128×128 generation should finish in well under 100ms on any modern machine. If it stalls, profile the noise sampling — it's the bulk of the work and is trivially parallelizable with Burst+Jobs in Phase 2 if needed.

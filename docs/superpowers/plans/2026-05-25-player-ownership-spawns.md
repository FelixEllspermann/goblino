# Player Ownership + Multi-Spawn Placement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-player ownership (`CSteamID Owner` on Goblin + new BuildingOwner component), spawn all lobby members' starting bases at distinct world spawn points, and tint sprites + healthbar borders + selection rings with each player's faction color so each client sees their own team (white) and enemy teams (red/yellow/green).

**Architecture:** Static `PlayerRegistry` (lobby side) holds the slot→color map. A new `WorldStartContext` static (world side) bridges Assembly-CSharp → `RTSCL.World.Unity` (asmdefs can't reference Assembly-CSharp). `GameStartLoader` pushes `LocalPlayer`, `PendingSlots`, and a `GetPlayerColor` delegate into the bridge before scene load. Host extends the GameStart message with `(SteamID, SpawnIndex)` per lobby member and broadcasts it. `MainBaseSetup` iterates the slot list and spawns one team per slot. Selection controller filters to local-owner units.

**Tech Stack:** Unity 6 / Steamworks.NET 2025.163.0 / C# / Unity MCP for editor automation.

**Spec:** `docs/superpowers/specs/2026-05-25-player-ownership-spawns-design.md`

---

## File Structure

| File | Path | Role |
|---|---|---|
| `PlayerRegistry.cs` | `Assets/Scripts/Lobby/` (new) | Static slot↔SteamID map + 4 faction colors |
| `WorldStartContext.cs` | `Assets/Scripts/World/Unity/` (new) | Asmdef-boundary bridge: `LocalPlayer` (ulong), `PendingSlots` ((ulong,int)[]), `GetPlayerColor` (Func) |
| `BuildingOwner.cs` | `Assets/Scripts/World/Unity/` (new) | MonoBehaviour on each building: Owner + tint + selection-ring child |
| `NetMessages.cs` | `Assets/Scripts/Lobby/` (modify) | Extended GameStart pack/unpack with PlayerSlot list |
| `NetworkSession.cs` | `Assets/Scripts/Lobby/` (modify) | New `PlayerSlots` field |
| `NetworkManager.cs` | `Assets/Scripts/Lobby/` (modify) | RouteMessage uses new TryUnpackGameStart |
| `LobbyPanel.cs` | `Assets/Scripts/Lobby/` (modify) | OnStart builds PlayerSlot list |
| `GameStartLoader.cs` | `Assets/Scripts/Lobby/` (modify) | Push WorldStartContext fields before SceneManager.LoadScene |
| `Goblin.cs` | `Assets/Scripts/World/Unity/` (modify) | Add `Owner` + `SetOwner` + `ApplyOwnerVisuals`; selection ring color |
| `GoblinSpawner.cs` | `Assets/Scripts/World/Unity/` (modify) | Thread owner param through SpawnAt/Around-Footprint/ByKind |
| `BuildingPlacer.cs` | `Assets/Scripts/World/Unity/` (modify) | Owner param on PlaceForce, `_cellToOwner` dict, `TryGetBuildingOwner` |
| `BuildingConstruction.cs` | `Assets/Scripts/World/Unity/` (modify) | Capture post-tint color in `_targetColor`, lerp toward it |
| `GoblinHealthBar.cs` | `Assets/Scripts/World/Unity/` (modify) | Third SpriteRenderer for owner-colored border |
| `MainBaseSetup.cs` | `Assets/Scripts/World/Unity/` (modify) | Multi-team spawn loop using `WorldStartContext.PendingSlots` |
| `GoblinSelectionController.cs` | `Assets/Scripts/World/Unity/` (modify) | `IsLocal` filter on SelectAtPoint + SelectInBox |
| `ObjectInspector.cs` | `Assets/Scripts/World/Unity/` (modify) | Grey unit cards for enemy buildings, toggle BuildingOwner ring |
| `WorldGenConfig.cs` | `Assets/Scripts/World/` (modify) | `PlayerCount = 4` |

---

## Verification Convention

Each task ends with:
1. **Compile check** via `mcp__unity-mcp__Unity_RunCommand` running `AssetDatabase.Refresh()` → `mcp__unity-mcp__Unity_ReadConsole` Types=["Error"]. Expected: 0.
2. **Behavior verification** where feasible. Multi-client Steam flows (host + remote) need two real clients — defer to manual at the end.
3. **Single commit** per task.

MCP `RunCommand` wraps your code in `namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`. Class must be `internal class CommandScript : IRunCommand`. Alias `using UImage = UnityEngine.UI.Image;` if you reference `Image`. Manually-written `.cs.meta` GUIDs must be **exactly 32 hex chars**.

---

## Task 1: PlayerRegistry + WorldStartContext (asmdef bridge)

**Files:**
- Create: `Assets/Scripts/Lobby/PlayerRegistry.cs`
- Create: `Assets/Scripts/Lobby/PlayerRegistry.cs.meta`
- Create: `Assets/Scripts/World/Unity/WorldStartContext.cs`
- Create: `Assets/Scripts/World/Unity/WorldStartContext.cs.meta`

- [ ] **Step 1: Create PlayerRegistry.cs**

```csharp
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace RTSCL.Lobby
{
    /// <summary>Maps Steam IDs to per-game slot indices and faction colors.
    /// Populated by GameStartLoader from NetworkSession.PlayerSlots, or by
    /// MainBaseSetup in solo mode.</summary>
    public static class PlayerRegistry
    {
        // P0 Blue, P1 Red, P2 Yellow, P3 Green
        private static readonly Color[] _colors =
        {
            new(0.25f, 0.45f, 0.85f, 1f),
            new(0.85f, 0.30f, 0.30f, 1f),
            new(0.95f, 0.85f, 0.25f, 1f),
            new(0.30f, 0.75f, 0.35f, 1f),
        };

        private static readonly Dictionary<CSteamID, int> _slotByPlayer = new();

        public static void Reset() => _slotByPlayer.Clear();
        public static void Register(CSteamID id, int slotIndex) => _slotByPlayer[id] = slotIndex;
        public static int GetSlot(CSteamID id) => _slotByPlayer.TryGetValue(id, out var s) ? s : -1;
        public static Color GetColor(int slot) => slot >= 0 && slot < _colors.Length ? _colors[slot] : Color.gray;
        public static Color GetColorForPlayer(CSteamID id) => GetColor(GetSlot(id));
        public static IEnumerable<KeyValuePair<CSteamID, int>> All => _slotByPlayer;
    }
}
```

- [ ] **Step 2: Create PlayerRegistry.cs.meta**

```yaml
fileFormatVersion: 2
guid: c5d8aa31eb9f2a40c81c2e8746b3d219
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

- [ ] **Step 3: Create WorldStartContext.cs**

```csharp
using System;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Asmdef boundary bridge. The Lobby side (Assembly-CSharp) pushes
    /// session start data into these static fields; world-side scripts read them
    /// without referencing Steamworks types directly.</summary>
    public static class WorldStartContext
    {
        /// <summary>SteamID of the local player as ulong. 0 = solo / Steam offline.</summary>
        public static ulong LocalPlayer;

        /// <summary>Per-player spawn assignment. null = solo (no MP slots).</summary>
        public static (ulong steamId, int spawnIndex)[] PendingSlots;

        /// <summary>Lookup: SteamID-as-ulong → faction color. Default returns gray.</summary>
        public static Func<ulong, Color> GetPlayerColor = _ => Color.gray;

        public static void Reset()
        {
            LocalPlayer = 0UL;
            PendingSlots = null;
            // GetPlayerColor intentionally not reset — it's a stable delegate set once at game start.
        }
    }
}
```

- [ ] **Step 4: Create WorldStartContext.cs.meta**

```yaml
fileFormatVersion: 2
guid: d6e9bb42fca0b540d92d3f9857c4e32a
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

- [ ] **Step 5: Compile check via MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `mcp__unity-mcp__Unity_ReadConsole` Types=["Error"]. Expected: 0.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Lobby/PlayerRegistry.cs Assets/Scripts/Lobby/PlayerRegistry.cs.meta Assets/Scripts/World/Unity/WorldStartContext.cs Assets/Scripts/World/Unity/WorldStartContext.cs.meta
git commit -m "feat(ownership): PlayerRegistry (lobby) + WorldStartContext (world) — asmdef bridge"
```

---

## Task 2: Goblin.Owner + ApplyOwnerVisuals

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Add Owner field + SetOwner method + ApplyOwnerVisuals**

Add near the other public properties (after `Kind`):

```csharp
public ulong Owner { get; private set; }
public void SetOwner(ulong ownerSteamId)
{
    Owner = ownerSteamId;
    ApplyOwnerVisuals();
}
```

Add private helper (place near other private helpers):

```csharp
private void ApplyOwnerVisuals()
{
    if (_renderer == null) return;
    bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
    _renderer.color = isLocal ? Color.white : WorldStartContext.GetPlayerColor(Owner);
}
```

- [ ] **Step 2: Re-apply visuals after Init has set _renderer**

In the existing `Init` method, after `_renderer.sprite = walkFrames[0];` line, add:

```csharp
ApplyOwnerVisuals();
```

(Owner defaults to 0 — Init will tint to white. Later spawn calls set Owner via SetOwner which re-applies.)

- [ ] **Step 3: Compile check via MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Expected: clean.

- [ ] **Step 4: Smoke verify — solo still spawns white goblins**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.World.Unity;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        if (Goblin.All.Count == 0) { result.LogError("Goblins not spawned yet — re-run"); return; }
        var g = Goblin.All[0];
        result.Log("Goblin Owner={0} sprite.color=(r:{1:F2} g:{2:F2} b:{3:F2})", g.Owner,
            g.GetComponent<SpriteRenderer>().color.r,
            g.GetComponent<SpriteRenderer>().color.g,
            g.GetComponent<SpriteRenderer>().color.b);
    }
}
```

Expected: `Owner=0 sprite.color=(r:1.00 g:1.00 b:1.00)` — defaults to white.

Exit play after.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(ownership): Goblin.Owner + SetOwner + ApplyOwnerVisuals (tint enemies)"
```

---

## Task 3: GoblinSpawner threads Owner through SpawnAt + Around-Footprint + ByKind

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSpawner.cs`

- [ ] **Step 1: Add owner param to SpawnAt + set on spawned goblin**

Find `public Goblin SpawnAt(Vector3 worldPos, GoblinKind kind = null)`. Change signature and body:

```csharp
public Goblin SpawnAt(Vector3 worldPos, GoblinKind kind = null, ulong owner = 0UL)
{
    if (_kinds.Count == 0) { Debug.LogWarning("No goblin kinds configured"); return null; }
    kind ??= _kinds[Random.Range(0, _kinds.Count)];
    if (kind.WalkFrames == null || kind.WalkFrames.Length == 0)
    { Debug.LogWarning($"Goblin kind '{kind.Name}' has no frames"); return null; }

    var go = new GameObject($"Goblin_{kind.Name}");
    if (_goblinsRoot != null) go.transform.SetParent(_goblinsRoot, false);
    go.transform.position = SnapToCellCenter(worldPos);

    var sr = go.AddComponent<SpriteRenderer>();
    sr.sortingOrder = 25;

    var goblin = go.AddComponent<Goblin>();
    goblin.Init(kind.Name, kind.WalkFrames, _terrainMap, _decorationMap, kind.Definition);
    goblin.SetOwner(owner);
    return goblin;
}
```

- [ ] **Step 2: Thread owner through SpawnAroundFootprint**

Find `public void SpawnAroundFootprint(Vector2Int origin, Vector2Int footprint, int count, string kindName = null)`. Change signature:

```csharp
public void SpawnAroundFootprint(Vector2Int origin, Vector2Int footprint, int count, string kindName = null, ulong owner = 0UL)
```

Inside the method, find both `SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f), forcedKind)` calls and update them to:

```csharp
SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f), forcedKind, owner);
```

(There are two — one in the `remaining >= available.Count` branch, one in the else branch.)

- [ ] **Step 3: Thread owner through SpawnByKindAroundFootprint**

Find `public Goblin SpawnByKindAroundFootprint(string kindName, Vector2Int origin, Vector2Int footprint)`. Change signature:

```csharp
public Goblin SpawnByKindAroundFootprint(string kindName, Vector2Int origin, Vector2Int footprint, ulong owner = 0UL)
```

Find the `return SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f), kind);` line and update:

```csharp
return SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f), kind, owner);
```

- [ ] **Step 4: Compile check via MCP**

Same as Task 1 Step 5. Expected: clean. (No callers update yet — defaults work.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSpawner.cs
git commit -m "feat(ownership): GoblinSpawner threads owner param through spawn methods"
```

---

## Task 4: BuildingOwner component (tint + selection ring)

**Files:**
- Create: `Assets/Scripts/World/Unity/BuildingOwner.cs`
- Create: `Assets/Scripts/World/Unity/BuildingOwner.cs.meta`

- [ ] **Step 1: Create BuildingOwner.cs**

```csharp
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-building ownership + visuals (sprite tint + selection ring).
    /// Attached by BuildingPlacer.PlaceForce on each placed building.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingOwner : MonoBehaviour
    {
        public ulong Owner { get; private set; }

        private SpriteRenderer _renderer;
        private GameObject _ring;
        private SpriteRenderer _ringRenderer;
        private static Sprite s_sprite;

        public Color CurrentTint { get; private set; } = Color.white;

        public void Initialize(ulong owner, Vector2Int footprint)
        {
            _renderer = GetComponent<SpriteRenderer>();
            BuildRing(footprint);
            SetOwner(owner);
            SetSelected(false);
        }

        public void SetOwner(ulong ownerSteamId)
        {
            Owner = ownerSteamId;
            bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
            CurrentTint = isLocal ? Color.white : WorldStartContext.GetPlayerColor(Owner);
            if (_renderer != null) _renderer.color = CurrentTint;
            if (_ringRenderer != null) _ringRenderer.color = WithAlpha(CurrentTint, 0.35f);
        }

        public void SetSelected(bool selected)
        {
            if (_ring != null) _ring.SetActive(selected);
        }

        private void BuildRing(Vector2Int footprint)
        {
            if (s_sprite == null) s_sprite = BuildSquareSprite();
            _ring = new GameObject("SelectionRing");
            _ring.transform.SetParent(transform, false);
            _ring.transform.localPosition = new Vector3(footprint.x * 0.5f, footprint.y * 0.5f, 0f);
            _ringRenderer = _ring.AddComponent<SpriteRenderer>();
            _ringRenderer.sprite = s_sprite;
            _ringRenderer.sortingOrder = 14;  // below buildings (15)
            _ringRenderer.color = new Color(1, 1, 1, 0.35f);
            _ring.transform.localScale = new Vector3(footprint.x + 0.4f, footprint.y + 0.4f, 1f);
        }

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

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

- [ ] **Step 2: Create BuildingOwner.cs.meta**

```yaml
fileFormatVersion: 2
guid: e7fbcc53fdb1c651ea3e405968d5f43b
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

- [ ] **Step 3: Compile check via MCP**

Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildingOwner.cs Assets/Scripts/World/Unity/BuildingOwner.cs.meta
git commit -m "feat(ownership): BuildingOwner component — Owner + tint + selection ring"
```

---

## Task 5: BuildingPlacer attaches BuildingOwner + owner dict

**Files:**
- Modify: `Assets/Scripts/World/Unity/BuildingPlacer.cs`

- [ ] **Step 1: Add owner storage**

Find the existing dictionaries near the top of the class. Add a parallel dict:

```csharp
private readonly Dictionary<Vector2Int, ulong> _cellToOwner = new();
```

- [ ] **Step 2: Add owner param to PlaceForce + attach BuildingOwner**

Find `public void PlaceForce(BuildingDefinition def, Vector2Int origin, bool charge = true, bool requireConstruction = true)`. Change signature and update body to attach BuildingOwner + populate `_cellToOwner`:

```csharp
public void PlaceForce(BuildingDefinition def, Vector2Int origin,
                       bool charge = true, bool requireConstruction = true,
                       ulong owner = 0UL)
{
    if (def == null || def.Sprite == null) return;

    if (charge && def.WoodCost > 0)
    {
        if (ResourceBank.Wood < def.WoodCost) return;
        ResourceBank.AddWood(-def.WoodCost);
    }

    var go = new GameObject($"Building_{def.name}_{origin.x}_{origin.y}");
    if (_buildingsRoot != null) go.transform.SetParent(_buildingsRoot, false);
    go.transform.position = _terrainMap.CellToWorld(new Vector3Int(origin.x, origin.y, 0));
    var sr = go.AddComponent<SpriteRenderer>();
    sr.sprite = def.Sprite;
    sr.sortingOrder = 15;

    var ownerComp = go.AddComponent<BuildingOwner>();
    ownerComp.Initialize(owner, def.Footprint);

    for (int dy = 0; dy < def.Footprint.y; dy++)
    for (int dx = 0; dx < def.Footprint.x; dx++)
    {
        var c = new Vector2Int(origin.x + dx, origin.y + dy);
        _cellOwners[c] = def;
        _cellToOrigin[c] = origin;
        _cellToOwner[c] = owner;
    }

    BuildingHP.Register(origin, BuildingHP.MaxHpFor(def));
    if (requireConstruction) BuildingConstruction.Register(origin, go);
    else PopulationManager.AddCap(def.PopulationProvided);
}
```

- [ ] **Step 3: Add TryGetBuildingOwner**

Place near `TryGetBuildingAt`:

```csharp
public bool TryGetBuildingOwner(Vector2Int cell, out ulong owner) =>
    _cellToOwner.TryGetValue(cell, out owner);
```

- [ ] **Step 4: Clear new dict in ClearAllPlaced**

Find `public void ClearAllPlaced()`. Add `_cellToOwner.Clear();` alongside the existing clears:

```csharp
public void ClearAllPlaced()
{
    _cellOwners.Clear();
    _cellToOrigin.Clear();
    _cellToOwner.Clear();
    BuildingHP.Clear();
    BuildingConstruction.Clear();
    if (_buildingsRoot == null) return;
    for (int i = _buildingsRoot.childCount - 1; i >= 0; i--)
    {
        var c = _buildingsRoot.GetChild(i);
        if (c.name.StartsWith("Building_")) DestroyImmediate(c.gameObject);
    }
}
```

- [ ] **Step 5: Compile check via MCP**

Expected: clean. (Existing callers compile because owner defaults to 0UL.)

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildingPlacer.cs
git commit -m "feat(ownership): BuildingPlacer attaches BuildingOwner + tracks _cellToOwner"
```

---

## Task 6: BuildingConstruction captures owner-tinted target color

**Files:**
- Modify: `Assets/Scripts/World/Unity/BuildingConstruction.cs`

- [ ] **Step 1: Read existing Register + AddProgress to find where OriginalColor is captured**

The current code captures `OriginalColor = sr.color` when Register is called. With Task 4's BuildingOwner already applied BEFORE BuildingConstruction.Register fires in PlaceForce, the color at that moment is already the owner tint — so existing capture logic is already correct. No code change needed.

- [ ] **Step 2: Verify ordering in BuildingPlacer.PlaceForce**

Read `Assets/Scripts/World/Unity/BuildingPlacer.cs` around the `PlaceForce` method (already modified in Task 5). The order MUST be:

```csharp
ownerComp.Initialize(owner, def.Footprint);   // tints sr.color
// ...dict updates...
if (requireConstruction) BuildingConstruction.Register(origin, go);  // captures sr.color
```

If the order is reversed (Register before Initialize), the captured color is wrong (white instead of owner-tint). Re-order if needed.

The Task 5 spec puts Initialize before Register — this should already be correct. No change expected.

- [ ] **Step 3: Compile check via MCP**

Expected: clean. (No code edits in this task; verification only.)

- [ ] **Step 4: Commit (empty if no change)**

If no change was needed, skip commit. If a re-ordering was applied, commit:

```bash
git add Assets/Scripts/World/Unity/BuildingPlacer.cs
git commit -m "fix(ownership): BuildingOwner.Initialize runs before BuildingConstruction.Register"
```

---

## Task 7: GoblinHealthBar gets faction-colored border

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinHealthBar.cs`

- [ ] **Step 1: Add border SpriteRenderer + use owner color**

Replace the contents of `GoblinHealthBar.cs`:

```csharp
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Floating HP bar above a Goblin. Hidden when HP is full and not selected.
    /// Adds a thin faction-colored border behind the bar.</summary>
    public sealed class GoblinHealthBar : MonoBehaviour
    {
        private const float BarWidth = 0.75f;
        private const float BarHeight = 0.10f;
        private const float BorderPad = 0.04f;
        private const float YOffset = 0.85f;

        private Goblin _goblin;
        private SpriteRenderer _border;
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

            // Owner-colored border (behind bg)
            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(transform, false);
            _border = borderGo.AddComponent<SpriteRenderer>();
            _border.sprite = s_sprite;
            _border.sortingOrder = 27;
            borderGo.transform.localScale = new Vector3(BarWidth + BorderPad, BarHeight + BorderPad, 1f);

            // Dark background
            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(transform, false);
            _bg = bgGo.AddComponent<SpriteRenderer>();
            _bg.sprite = s_sprite;
            _bg.color = new Color(0.12f, 0.05f, 0.05f, 0.9f);
            _bg.sortingOrder = 28;
            bgGo.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);

            // HP fill
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            _fill = fillGo.AddComponent<SpriteRenderer>();
            _fill.sprite = s_sprite;
            _fill.sortingOrder = 29;
            _fillT = fillGo.transform;
            _fillT.localScale = new Vector3(BarWidth, BarHeight, 1f);
            _fillT.localPosition = new Vector3(-BarWidth * 0.5f, 0f, 0f);
        }

        private void Start()
        {
            // Owner color cached at start — owner doesn't change mid-game.
            if (_goblin != null && _border != null)
            {
                bool isLocal = _goblin.Owner == WorldStartContext.LocalPlayer || _goblin.Owner == 0UL;
                Color faction = isLocal ? Color.white : WorldStartContext.GetPlayerColor(_goblin.Owner);
                _border.color = faction;
            }
        }

        private void LateUpdate()
        {
            if (_goblin == null) { Destroy(gameObject); return; }
            float frac = _goblin.MaxHp > 0 ? (float)_goblin.CurrentHp / _goblin.MaxHp : 0f;
            bool show = frac < 1f || _goblin.IsSelected;
            _border.enabled = show;
            _bg.enabled = show;
            _fill.enabled = show;
            if (!show) return;

            _fillT.localScale = new Vector3(BarWidth * frac, BarHeight, 1f);
            _fillT.localPosition = new Vector3(-BarWidth * 0.5f + (BarWidth * frac) * 0.5f, 0f, 0f);

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

- [ ] **Step 2: Compile check via MCP**

Expected: clean.

- [ ] **Step 3: Smoke verify — solo goblins have white border**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.World.Unity;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        if (Goblin.All.Count == 0) { result.LogError("Wait + retry"); return; }
        var g = Goblin.All[0];
        var hb = g.GetComponentInChildren<GoblinHealthBar>();
        result.Log("HealthBar present: {0}", hb != null);
    }
}
```

Expected: `HealthBar present: True`. Visually verify in the editor that the bar still appears above goblins (now with a thin white border).

Exit play after.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinHealthBar.cs
git commit -m "feat(ownership): GoblinHealthBar adds faction-colored border behind the bar"
```

---

## Task 8: Goblin selection ring uses owner color

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Find existing BuildSelectionRing + tint the ring sprite by owner**

In `Goblin.cs`, locate `BuildSelectionRing` (or wherever `_selectionRing` is constructed). After the existing renderer setup, set the color:

```csharp
var sr = _selectionRing.GetComponent<SpriteRenderer>();
bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
sr.color = isLocal
    ? new Color(0.95f, 0.95f, 0.95f, 1f)
    : WorldStartContext.GetPlayerColor(Owner);
```

If `BuildSelectionRing` runs in `Init` BEFORE `SetOwner` is called (Task 2 made SetOwner be called from SpawnAt after Init), the ring will tint to "isLocal" because Owner is still 0UL. We need to re-apply on SetOwner.

- [ ] **Step 2: Update SetOwner to refresh the ring**

In `Goblin.SetOwner`, after `ApplyOwnerVisuals()`:

```csharp
public void SetOwner(ulong ownerSteamId)
{
    Owner = ownerSteamId;
    ApplyOwnerVisuals();
    RefreshSelectionRingColor();
}

private void RefreshSelectionRingColor()
{
    if (_selectionRing == null) return;
    var sr = _selectionRing.GetComponent<SpriteRenderer>();
    if (sr == null) return;
    bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
    sr.color = isLocal
        ? new Color(0.95f, 0.95f, 0.95f, 1f)
        : WorldStartContext.GetPlayerColor(Owner);
}
```

- [ ] **Step 3: Compile check via MCP**

Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(ownership): Goblin selection ring color reflects owner faction"
```

---

## Task 9: NetMessages PlayerSlot + extended GameStart

**Files:**
- Modify: `Assets/Scripts/Lobby/NetMessages.cs`
- Modify: `Assets/Scripts/Lobby/NetworkSession.cs`

- [ ] **Step 1: Add PlayerSlot type + update NetMessages**

Replace the contents of `NetMessages.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using Steamworks;

namespace RTSCL.Lobby
{
    public enum NetMessageType : byte
    {
        GameStart = 1,
    }

    public sealed class PlayerSlot
    {
        public CSteamID SteamId;
        public byte SpawnIndex;
    }

    /// <summary>Binary pack/unpack helpers for the wire protocol. Frame = [byte type | payload].</summary>
    public static class NetMessages
    {
        public static byte[] PackGameStart(int seed, IList<PlayerSlot> slots)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)NetMessageType.GameStart);
            w.Write(seed);
            w.Write((byte)(slots?.Count ?? 0));
            if (slots != null)
            {
                foreach (var s in slots)
                {
                    w.Write(s.SteamId.m_SteamID);
                    w.Write(s.SpawnIndex);
                }
            }
            return ms.ToArray();
        }

        public static bool TryUnpackGameStart(byte[] payload, out int seed, out List<PlayerSlot> slots)
        {
            seed = 0; slots = null;
            if (payload == null || payload.Length < 6) return false;
            if (payload[0] != (byte)NetMessageType.GameStart) return false;
            using var ms = new MemoryStream(payload, 1, payload.Length - 1);
            using var r = new BinaryReader(ms);
            seed = r.ReadInt32();
            int n = r.ReadByte();
            slots = new List<PlayerSlot>(n);
            for (int i = 0; i < n; i++)
            {
                if (ms.Position + 9 > ms.Length) return false;
                slots.Add(new PlayerSlot { SteamId = new CSteamID(r.ReadUInt64()), SpawnIndex = r.ReadByte() });
            }
            return true;
        }
    }
}
```

- [ ] **Step 2: Add PlayerSlots field + Reset to NetworkSession.cs**

Find `NetworkSession.cs`. Add a `PlayerSlots` property near `GameSeed`:

```csharp
public static List<PlayerSlot> PlayerSlots { get; internal set; }
```

(Already has `using System.Collections.Generic;` — if not, add it at the top.)

Update `Reset()`:

```csharp
public static void Reset()
{
    IsHost = false;
    LocalPlayer = CSteamID.Nil;
    HostPlayer = CSteamID.Nil;
    GameSeed = 0;
    PlayerSlots = null;
}
```

- [ ] **Step 3: Compile check via MCP**

Expected: 0 errors. (NetworkManager + LobbyPanel call `TryUnpackGameStart` / `PackGameStart` — they'll temporarily break.)

Actually since this changes the signature of `TryUnpackGameStart` and `PackGameStart`, expect compile errors in NetworkManager and LobbyPanel from this commit until Task 10 lands. To keep main green, fold tasks 9 and 10 into ONE commit. Skip the standalone commit here.

- [ ] **Step 4: Defer commit to Task 10**

Do not commit yet — Tasks 9 and 10 share one commit.

---

## Task 10: NetworkManager.RouteMessage + LobbyPanel.OnStart use new signature

**Files:**
- Modify: `Assets/Scripts/Lobby/NetworkManager.cs`
- Modify: `Assets/Scripts/Lobby/LobbyPanel.cs`

- [ ] **Step 1: Update RouteMessage in NetworkManager.cs**

Find the `RouteMessage` method. Update the GameStart case:

```csharp
case NetMessageType.GameStart:
    if (NetworkSession.GameSeed != 0)
    {
        Debug.LogWarning("[Net] Duplicate GameStart ignored");
        return;
    }
    if (NetMessages.TryUnpackGameStart(payload, out int seed, out var slots))
    {
        NetworkSession.GameSeed = seed;
        NetworkSession.PlayerSlots = slots;
        NetworkSession.RaiseGameStartReceived();
    }
    break;
```

- [ ] **Step 2: Update OnStart in LobbyPanel.cs**

Find the `OnStart` method. Update to build the slot list from lobby members + new PackGameStart signature:

```csharp
private void OnStart()
{
    if (!NetworkSession.IsHost) return;

    // Build slot list: host first (SpawnIndex 0), then others sorted ascending by SteamID.
    var members = LobbyManager.GetCurrentMembers();
    var slots = new System.Collections.Generic.List<PlayerSlot>(members.Count);
    var ordered = new System.Collections.Generic.List<(Steamworks.CSteamID id, string name, bool isHost)>(members);
    ordered.Sort((a, b) =>
    {
        if (a.isHost != b.isHost) return a.isHost ? -1 : 1;
        return a.id.m_SteamID.CompareTo(b.id.m_SteamID);
    });
    for (byte i = 0; i < ordered.Count; i++)
        slots.Add(new PlayerSlot { SteamId = ordered[i].id, SpawnIndex = i });

    var seed = Random.Range(1, int.MaxValue);
    NetworkSession.GameSeed = seed;
    NetworkSession.PlayerSlots = slots;
    NetworkManager.SendToAll(NetMessages.PackGameStart(seed, slots));
    Steamworks.SteamMatchmaking.SetLobbyJoinable(LobbyManager.CurrentLobby, false);
    NetworkSession.RaiseGameStartReceived();
}
```

- [ ] **Step 3: Compile check via MCP**

Expected: 0 errors now.

- [ ] **Step 4: Commit (combines Tasks 9 + 10)**

```bash
git add Assets/Scripts/Lobby/NetMessages.cs Assets/Scripts/Lobby/NetworkSession.cs Assets/Scripts/Lobby/NetworkManager.cs Assets/Scripts/Lobby/LobbyPanel.cs
git commit -m "feat(net): extended GameStart with PlayerSlot list (steamId + spawnIndex per player)"
```

---

## Task 11: GameStartLoader pushes WorldStartContext

**Files:**
- Modify: `Assets/Scripts/Lobby/GameStartLoader.cs`

- [ ] **Step 1: Update LoadGameScene to populate WorldStartContext**

Replace the `LoadGameScene` method:

```csharp
private void LoadGameScene()
{
    // Push session data into the world-side bridge BEFORE the scene loads.
    RTSCL.World.Unity.WorldStartContext.LocalPlayer = NetworkSession.LocalPlayer.m_SteamID;
    RTSCL.World.Unity.WorldStartContext.GetPlayerColor = sid =>
        PlayerRegistry.GetColorForPlayer(new Steamworks.CSteamID(sid));

    // Translate PlayerSlots (List<PlayerSlot> with Steamworks types) into the
    // type-light ValueTuple array the world layer sees.
    var src = NetworkSession.PlayerSlots;
    if (src != null && src.Count > 0)
    {
        var arr = new (ulong steamId, int spawnIndex)[src.Count];
        for (int i = 0; i < src.Count; i++)
        {
            arr[i] = (src[i].SteamId.m_SteamID, src[i].SpawnIndex);
            // Mirror into PlayerRegistry so GetPlayerColor works after this point.
            PlayerRegistry.Register(src[i].SteamId, src[i].SpawnIndex);
        }
        RTSCL.World.Unity.WorldStartContext.PendingSlots = arr;
    }
    else
    {
        RTSCL.World.Unity.WorldStartContext.PendingSlots = null;
    }

    // Push the seed (existing flow).
    RTSCL.World.Unity.WorldGeneratorBootstrap.PendingSeed = NetworkSession.GameSeed;

    SceneManager.LoadScene("SampleScene");
}
```

- [ ] **Step 2: Compile check via MCP**

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Lobby/GameStartLoader.cs
git commit -m "feat(ownership): GameStartLoader pushes WorldStartContext (LocalPlayer + slots + color lookup)"
```

---

## Task 12: MainBaseSetup spawns all teams from PendingSlots

**Files:**
- Modify: `Assets/Scripts/World/Unity/MainBaseSetup.cs`

- [ ] **Step 1: Replace OnNewWorld with multi-team logic**

Replace the existing `OnNewWorld` method body:

```csharp
private void OnNewWorld(WorldData world)
{
    // 1. Wipe state from previous game
    if (_goblinSpawner != null) _goblinSpawner.ClearAllGoblins();
    if (_buildingPlacer != null) _buildingPlacer.ClearAllPlaced();
    ResourceBank.Reset();
    TreeHP.Clear();
    GoblinProduction.Clear();
    PopulationManager.Reset();

    if (world.Spawns == null || world.Spawns.Length == 0) return;
    var def = FindBuildingDefinition(_mainBuildingName);
    if (def == null || _buildingPlacer == null)
    {
        Debug.LogWarning($"[MainBaseSetup] Building '{_mainBuildingName}' not in catalog.");
        return;
    }

    // 2. Determine team list: MP from WorldStartContext.PendingSlots, else solo (LocalPlayer at spawn[0]).
    var slots = WorldStartContext.PendingSlots;
    if (slots == null || slots.Length == 0)
    {
        slots = new[] { (steamId: WorldStartContext.LocalPlayer, spawnIndex: 0) };
    }
    // Consume so a later solo restart doesn't re-use these.
    WorldStartContext.PendingSlots = null;

    // 3. Spawn each team
    foreach (var (steamId, spawnIndex) in slots)
    {
        if (spawnIndex < 0 || spawnIndex >= world.Spawns.Length) continue;
        SpawnTeamAt(world.Spawns[spawnIndex], def, steamId);
    }
}

private void SpawnTeamAt(Unity.Mathematics.int2 spawnCell, BuildingDefinition def, ulong owner)
{
    var keepOrigin = new Vector2Int(spawnCell.x - def.Footprint.x / 2, spawnCell.y - def.Footprint.y / 2);
    _buildingPlacer.PlaceForce(def, keepOrigin, charge: false, requireConstruction: false, owner: owner);

    if (_goblinSpawner != null)
    {
        _goblinSpawner.SpawnAroundFootprint(keepOrigin, def.Footprint, _startingGoblins, "FarmerGoblin", owner);
        bool isLocal = owner == WorldStartContext.LocalPlayer || owner == 0UL;
        int popPerUnit = _startingUnitDef != null ? _startingUnitDef.PopulationCost : 1;
        if (isLocal) PopulationManager.AddUsed(_startingGoblins * popPerUnit);

        if (_testStartingClubs > 0)
        {
            var paddedOrigin = new Vector2Int(keepOrigin.x - 1, keepOrigin.y - 1);
            var paddedFootprint = new Vector2Int(def.Footprint.x + 2, def.Footprint.y + 2);
            _goblinSpawner.SpawnAroundFootprint(paddedOrigin, paddedFootprint, _testStartingClubs, "ClubGoblin", owner);
            if (isLocal) PopulationManager.AddUsed(_testStartingClubs * _testClubPopCost);
        }
    }
}
```

Make sure `using` statements at the top of MainBaseSetup.cs cover `Unity.Mathematics` (likely already present via `using RTSCL.World;` chain). If not, add `using Unity.Mathematics;`.

- [ ] **Step 2: Compile check via MCP**

Expected: clean.

- [ ] **Step 3: Smoke verify solo — single team, unchanged behavior**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.World.Unity;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        result.Log("Goblins spawned: {0}", Goblin.All.Count);
        // Expected: 7 (5 farmers + 2 test clubs) for solo
    }
}
```

Expected: `Goblins spawned: 7`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/MainBaseSetup.cs
git commit -m "feat(ownership): MainBaseSetup spawns one team per WorldStartContext slot"
```

---

## Task 13: GoblinSelectionController filters to local-owner units

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSelectionController.cs`

- [ ] **Step 1: Add IsLocal helper + apply filter to selection methods**

Add a private helper:

```csharp
private static bool IsLocal(Goblin g)
    => g != null && (g.Owner == WorldStartContext.LocalPlayer || g.Owner == 0UL);
```

In `SelectAtPoint(Vector2 screenPos)`, change the inner loop's `if (g == null) continue;` to also skip enemy goblins:

```csharp
foreach (var g in Goblin.All)
{
    if (!IsLocal(g)) continue;
    float d = Vector2.Distance(g.transform.position, world);
    if (d < bestDist) { bestDist = d; best = g; }
}
```

In `SelectInBox(Vector2 a, Vector2 b)`, change the inner check similarly:

```csharp
foreach (var g in Goblin.All)
{
    if (!IsLocal(g)) continue;
    Vector3 sp = _camera.WorldToScreenPoint(g.transform.position);
    if (sp.x >= minX && sp.x <= maxX && sp.y >= minY && sp.y <= maxY)
        hit.Add(g);
}
```

`TryGetGoblinAt` (right-click attack target) is unchanged — it intentionally returns enemy goblins.

- [ ] **Step 2: Compile check via MCP**

Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSelectionController.cs
git commit -m "feat(ownership): selection filters to local-owner goblins only"
```

---

## Task 14: ObjectInspector grays enemy-building train cards + toggles ring

**Files:**
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs`

- [ ] **Step 1: Track current building's BuildingOwner for ring toggling**

Add a private field:

```csharp
private BuildingOwner _currentBuildingOwner;
```

- [ ] **Step 2: Update ShowBuilding to toggle ring + greying**

Find the `ShowBuilding(BuildingDefinition def, Vector2Int origin)` method. At the start, toggle the old ring off; at the end, toggle the new ring on and grey enemy cards.

```csharp
private void ShowBuilding(BuildingDefinition def, Vector2Int origin)
{
    // Hide old ring
    if (_currentBuildingOwner != null) _currentBuildingOwner.SetSelected(false);

    _selKind = SelKind.Building;
    _selDef = def;

    string desc = DescribeBuilding(def);
    if (BuildingHP.TryGet(origin, out int cur, out int max))
        desc += $"\nHP: {cur} / {max}";
    SetHeader(def.DisplayName, desc);

    if (def.TrainsUnits != null && def.TrainsUnits.Length > 0)
        BuildUnitCards(def.TrainsUnits);
    else
        ClearCards();

    _popupRoot?.SetActive(true);

    // Owner check — grey unit cards on enemy buildings
    ulong owner = 0UL;
    _placer?.TryGetBuildingOwner(origin, out owner);
    bool isLocalBuilding = owner == WorldStartContext.LocalPlayer || owner == 0UL;
    if (!isLocalBuilding && _cards.Count > 0)
    {
        foreach (var card in _cards)
        {
            card.Button.interactable = false;
            if (card.Name != null) card.Name.text = card.Def != null ? card.Def.DisplayName : "(enemy)";
            if (card.Cost != null) card.Cost.text = "Enemy building";
        }
    }
    else
    {
        Refresh();
    }

    // Show new ring (find BuildingOwner on the placed building GameObject)
    _currentBuildingOwner = FindBuildingOwnerAtOrigin(origin);
    _currentBuildingOwner?.SetSelected(true);
}

private BuildingOwner FindBuildingOwnerAtOrigin(Vector2Int origin)
{
    // BuildingOwner doesn't expose a per-cell lookup; iterate scene-loaded owners.
    foreach (var bo in UnityEngine.Object.FindObjectsByType<BuildingOwner>(UnityEngine.FindObjectsSortMode.None))
    {
        // Match by position: building's transform = terrainMap.CellToWorld(origin)
        if (_terrainMap == null) break;
        var pos = _terrainMap.CellToWorld(new Vector3Int(origin.x, origin.y, 0));
        if ((bo.transform.position - pos).sqrMagnitude < 0.01f) return bo;
    }
    return null;
}
```

- [ ] **Step 3: Update Hide() to clear the ring**

Find `Hide()`. Add a ring-clear at the top:

```csharp
private void Hide()
{
    if (_currentBuildingOwner != null) _currentBuildingOwner.SetSelected(false);
    _currentBuildingOwner = null;
    if (_popupRoot != null) _popupRoot.SetActive(false);
    _selKind = SelKind.None;
    _selDef = null;
    ClearCards();
}
```

- [ ] **Step 4: Compile check via MCP**

Expected: clean.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "feat(ownership): ObjectInspector toggles BuildingOwner ring + greys enemy unit cards"
```

---

## Task 15: WorldGenConfig.PlayerCount = 4

**Files:**
- Modify: `Assets/Scripts/World/WorldGenConfig.cs`

- [ ] **Step 1: Bump PlayerCount default**

Find `public int PlayerCount = 2;` in `Assets/Scripts/World/WorldGenConfig.cs`. Change to:

```csharp
public int PlayerCount = 4;
```

- [ ] **Step 2: Compile check via MCP**

Expected: clean.

- [ ] **Step 3: Smoke verify — world still generates with 4 spawns**

```csharp
using UnityEngine;
using UnityEditor;
using RTSCL.World.Unity;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!Application.isPlaying) { EditorApplication.EnterPlaymode(); result.Log("Entering play"); return; }
        var w = Object.FindFirstObjectByType<WorldGeneratorBootstrap>()?.CurrentWorld;
        result.Log("Spawns generated: {0}", w?.Spawns.Length ?? 0);
    }
}
```

Expected: `Spawns generated: 4`. SpawnPlanner uses farthest-point sampling; if the map can't fit 4 distant eligible cells, it may fall back to fewer — flagged as DONE_WITH_CONCERNS if so.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/WorldGenConfig.cs
git commit -m "feat(world): WorldGenConfig.PlayerCount = 4 (was 2) for 4-player lobbies"
```

---

## Task 16: End-to-end host-alone smoke test

**Files:** None (verification only)

- [ ] **Step 1: Final compile check via MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

ReadConsole Types=["Error"]. Expected: 0.

- [ ] **Step 2: Manual smoke checklist**

In the Unity Editor, perform these checks (no commits expected unless a bug is found):

1. **Solo path**: Click Play → MainMenu shows → click "Play Solo" → SampleScene loads → see one Keep + 5 Farmers + 2 Clubs all WHITE-tinted with WHITE healthbar borders. Selection works. Combat (Club vs Club) works.
2. **Host-alone MP path**: Restart → Multiplayer → Create Lobby → Start Game → SampleScene loads with one team at spawn[0]. Host SteamID is mapped to slot 0 (Blue) — own units stay WHITE (because isLocal), but healthbar borders are BLUE.
3. **Build Settings**: Verify MainMenu is scene 0 (`File → Build Settings`).

If any check fails: STOP, file as a follow-up task, do not declare complete.

- [ ] **Step 3: No commit needed (verification only)**

Multi-client validation (2 Steam clients seeing each other's enemy teams) requires manual cross-client testing.

---

## Self-Review Checklist

- [x] **Spec coverage:**
  - PlayerRegistry (lobby colors) → Task 1
  - WorldStartContext (asmdef bridge) → Task 1, populated in Task 11
  - Goblin.Owner + tint → Tasks 2, 3 (spawn threading)
  - BuildingOwner + tint + ring → Tasks 4, 5 (BuildingPlacer wiring), 6 (construction tint order)
  - GoblinHealthBar border → Task 7
  - Goblin selection ring color → Task 8
  - Extended GameStart protocol → Tasks 9-10
  - Multi-team spawn → Task 12
  - Selection filter → Task 13
  - Enemy-building UI → Task 14
  - 4-player support → Task 15
- [x] **No placeholders** — every code block is complete
- [x] **Type consistency**: `Owner` as `ulong` across Goblin + BuildingOwner; `PlayerSlot` shared between NetMessages + NetworkSession + LobbyPanel; `WorldStartContext` field names consistent
- [x] **Single commit per task** (except Tasks 9+10 share to keep main green)
- [x] **MCP-driven verification per task**; multi-client manual flow flagged

## Acknowledged verification gaps

- Cross-client validation (2 Steam clients seeing distinct-colored enemy teams + selection-filter rejecting enemies) needs two physical Steam logins on different machines. Per-task structural correctness + host-alone smoke (Task 16) is the bar; cross-client validation deferred to manual.
- `BuildingOwner` selection ring uses runtime-built 1x1 sprite scaled to footprint. Pixel-art-style ring sprite is future polish.
- Healthbar border color uses the goblin's Owner at `Start()` time and caches it. If an Owner ever changes mid-game (not in this sub-project), refresh needed.

# Building Rally Point Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: subagent-driven-development / executing-plans. Steps use checkboxes.

**Goal:** Per-building rally point: trained units auto-walk to it and fan out; while the building is selected a GUI_33 flag + animated dashed line mark it; right-click sets it.

**Architecture:** Owner-local `RallyPoints` store keyed by origin. `RallyVisual` MonoBehaviour draws the flag + marching-ants LineRenderer. `ObjectInspector` shows/hides it and sets the rally on right-click. `GoblinProductionRunner` sends finished units to the nearest free cell around the rally via the existing `IssueMove` (only the owner issues → MP mirrors). No new wire message.

**Verification:** Unity MCP `AssetDatabase.Refresh()` + `Unity_ReadConsole` (Errors==0) per task; manual play smoke. `IRunCommand` = `void Execute(ExecutionResult result)`, class `internal class CommandScript`.

---

## Task 1: RallyPoints store + reset

**Create** `Assets/Scripts/World/Unity/RallyPoints.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Owner-local store of each building's rally point (world position), keyed by footprint
    /// origin cell. Not networked — only the unit MOVE that results from a rally is synced. Cleared on
    /// a new world via MainBaseSetup.OnNewWorld.</summary>
    public static class RallyPoints
    {
        private static readonly Dictionary<Vector2Int, Vector3> _points = new();

        public static void Set(Vector2Int origin, Vector3 worldPos) => _points[origin] = worldPos;
        public static bool TryGet(Vector2Int origin, out Vector3 worldPos) => _points.TryGetValue(origin, out worldPos);
        public static void Remove(Vector2Int origin) => _points.Remove(origin);
        public static void Clear() => _points.Clear();

        /// <summary>Default rally: a couple of cells below the footprint, centered on its width.</summary>
        public static Vector3 Default(Vector2Int origin, Vector2Int footprint) =>
            new Vector3(origin.x + footprint.x * 0.5f, origin.y - 1.5f, 0f);
    }
}
```

**Modify** `Assets/Scripts/World/Unity/MainBaseSetup.cs` — after `HarvestReservations.Clear();` add:
```csharp
            RallyPoints.Clear();
```

Compile-check (0 errors). Commit: `feat(rally): RallyPoints store + clear on new world`.

---

## Task 2: RallyVisual (flag + animated dashed line)

**Create** `Assets/Scripts/World/Unity/RallyVisual.cs`:

```csharp
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Single reusable visual for the currently-selected building's rally point: a flag icon at
    /// the rally and a dashed LineRenderer from the building to it, with dashes that flow ("marching ants")
    /// toward the rally. Created lazily; driven by ObjectInspector.Show/Hide.</summary>
    public sealed class RallyVisual : MonoBehaviour
    {
        private const float LineWidth = 0.09f;
        private const float ScrollSpeed = 2.0f;   // marching-ants speed (texture units / sec)
        private const float IconScale = 0.7f;
        private const float DashesPerUnit = 1.5f;  // dash texture repeats per world unit of line length

        private static RallyVisual _instance;
        public static RallyVisual Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("RallyVisual");
                    _instance = go.AddComponent<RallyVisual>();
                    _instance.Build();
                    _instance.HideNow();
                }
                return _instance;
            }
        }

        private LineRenderer _line;
        private SpriteRenderer _icon;
        private Material _lineMat;
        private bool _active;
        private float _offset;

        private void Build()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.widthMultiplier = LineWidth;
            _line.numCapVertices = 0;
            _line.numCornerVertices = 0;
            _line.useWorldSpace = true;
            _line.textureMode = LineTextureMode.Tile;
            _line.sortingOrder = 22;
            _line.alignment = LineAlignment.TransformZ;  // face the camera (flat top-down)
            _lineMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = DashTex() };
            _lineMat.color = new Color(1f, 1f, 0.6f, 0.9f);
            _line.material = _lineMat;

            var iconGo = new GameObject("RallyIcon");
            iconGo.transform.SetParent(transform, false);
            _icon = iconGo.AddComponent<SpriteRenderer>();
            _icon.sortingOrder = 24;
            iconGo.transform.localScale = Vector3.one * IconScale;
        }

        public void Show(Vector3 buildingCenter, Vector3 rally, Sprite icon)
        {
            _active = true;
            if (_icon.sprite != icon) _icon.sprite = icon;
            _icon.gameObject.SetActive(true);
            _icon.transform.position = rally;
            _line.enabled = true;
            _line.SetPosition(0, new Vector3(buildingCenter.x, buildingCenter.y, 0f));
            _line.SetPosition(1, new Vector3(rally.x, rally.y, 0f));
            // Keep dash density uniform regardless of line length.
            float len = Vector2.Distance(buildingCenter, rally);
            _lineMat.mainTextureScale = new Vector2(Mathf.Max(1f, len * DashesPerUnit), 1f);
        }

        public void Hide()
        {
            if (_instance == null) return;
            HideNow();
        }

        private void HideNow()
        {
            _active = false;
            if (_line != null) _line.enabled = false;
            if (_icon != null) _icon.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_active || _lineMat == null) return;
            // Scroll the dash texture so the dashes march toward the rally point.
            _offset -= ScrollSpeed * Time.deltaTime;
            _lineMat.mainTextureOffset = new Vector2(_offset, 0f);
        }

        // 8px horizontal dash strip: 4 opaque + 4 transparent, repeated.
        private static Texture2D s_dash;
        private static Texture2D DashTex()
        {
            if (s_dash != null) return s_dash;
            const int W = 8, H = 1;
            s_dash = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[W];
            for (int x = 0; x < W; x++) px[x] = x < 4 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            s_dash.SetPixels32(px);
            s_dash.Apply();
            return s_dash;
        }
    }
}
```

Compile-check. Commit: `feat(rally): RallyVisual — flag icon + marching-ants dashed line`.

---

## Task 3: Default rally on placement

**Modify** `Assets/Scripts/World/Unity/BuildingPlacer.cs` in `PlaceForce`, after the footprint registration loop / `BuildingHP.Register` block, add:
```csharp
            // Give unit-training buildings a default rally point (below the footprint) if none set yet.
            if (def.TrainsUnits != null && def.TrainsUnits.Length > 0 && !RallyPoints.TryGet(origin, out _))
                RallyPoints.Set(origin, RallyPoints.Default(origin, def.Footprint));
```

Compile-check. Commit: `feat(rally): default rally point for unit-training buildings`.

---

## Task 4: Trained units walk to rally + spread

**Modify** `Assets/Scripts/World/Unity/GoblinProductionRunner.cs`:
- Add `using System.Collections.Generic;`.
- Capture the spawned goblin and send it to the rally if owner-local:

Replace the spawn line:
```csharp
                _spawner.SpawnByKindAroundFootprint(def.SpawnerKindName, origin, building.Footprint, owner, reservedIndex);
```
with:
```csharp
                var unit = _spawner.SpawnByKindAroundFootprint(def.SpawnerKindName, origin, building.Footprint, owner, reservedIndex);

                // Send the new unit to the building's rally point (owner issues → syncs as a normal move).
                bool isLocal = owner == WorldStartContext.LocalPlayer || owner == 0UL;
                if (unit != null && isLocal && RallyPoints.TryGet(origin, out var rally))
                {
                    Vector3 dest = NearestFreeCell(rally, unit);
                    NetCommandIssuer.IssueMove(new List<Goblin> { unit }, dest);
                }
```

Add the helper method to the class:
```csharp
        // Spiral out from the rally cell; return the first cell-center with no OTHER live goblin within
        // ~0.6 units, so rallied units fan out instead of stacking. Falls back to the rally point.
        private static Vector3 NearestFreeCell(Vector3 rally, Goblin self)
        {
            int rx = Mathf.FloorToInt(rally.x), ry = Mathf.FloorToInt(rally.y);
            for (int ring = 0; ring <= 6; ring++)
            for (int dy = -ring; dy <= ring; dy++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue; // ring edge only
                var c = new Vector3(rx + dx + 0.5f, ry + dy + 0.5f, 0f);
                bool occupied = false;
                foreach (var g in Goblin.All)
                {
                    if (g == null || g == self) continue;
                    if ((g.transform.position - c).sqrMagnitude < 0.36f) { occupied = true; break; }
                }
                if (!occupied) return c;
            }
            return rally;
        }
```

Compile-check. Commit: `feat(rally): trained units walk to rally + fan out (no stacking)`.

---

## Task 5: ObjectInspector — show/hide visual + right-click sets rally

**Modify** `Assets/Scripts/World/Unity/ObjectInspector.cs`:

- Add field after `_cardFont`/style fields (or near other SerializeFields):
```csharp
        [Header("Rally")]
        [SerializeField] private Sprite _rallyIcon;   // GUI_33
```

- Add helper:
```csharp
        // True when the current selection is a local, unit-training building (eligible for a rally point).
        private bool BuildingTakesRally(out Vector3 buildingCenter)
        {
            buildingCenter = default;
            if (_selKind != SelKind.Building || _selDef == null) return false;
            if (_selDef.TrainsUnits == null || _selDef.TrainsUnits.Length == 0) return false;
            if (_placer != null && _placer.TryGetBuildingOwner(_selOrigin, out ulong owner)
                && owner != WorldStartContext.LocalPlayer && owner != 0UL) return false;
            if (_terrainMap == null) return false;
            var w = _terrainMap.CellToWorld(new Vector3Int(_selOrigin.x, _selOrigin.y, 0));
            buildingCenter = w + new Vector3(_selDef.Footprint.x * 0.5f, _selDef.Footprint.y * 0.5f, 0f);
            return true;
        }

        private void ShowRallyVisual()
        {
            if (!BuildingTakesRally(out var center)) { RallyVisual.Instance.Hide(); return; }
            if (!RallyPoints.TryGet(_selOrigin, out var rally))
            {
                rally = RallyPoints.Default(_selOrigin, _selDef.Footprint);
                RallyPoints.Set(_selOrigin, rally);
            }
            RallyVisual.Instance.Show(center, rally, _rallyIcon);
        }
```

- In `ShowBuilding`, at the end (after cards built) add: `ShowRallyVisual();`
- In `Hide()` add: `RallyVisual.Instance.Hide();`
- In `ShowGoblinSelection` and `ShowResourceNode` (decoration), add `RallyVisual.Instance.Hide();` near the top so selecting a unit/resource clears it.
- In `Update`, add right-click handling (after the left-click block, before the early `return`s are an issue — place it so it runs when a building is selected). Insert near the top of Update after the placement guard:
```csharp
            // Right-click while a unit-training building is selected → set its rally point.
            if (Mouse.current.rightButton.wasPressedThisFrame
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                && BuildingTakesRally(out var bc))
            {
                Vector2 rmp = Mouse.current.position.ReadValue();
                Vector3 rworld = _camera.ScreenToWorldPoint(new Vector3(rmp.x, rmp.y, -_camera.transform.position.z));
                var rally = new Vector3(Mathf.FloorToInt(rworld.x) + 0.5f, Mathf.FloorToInt(rworld.y) + 0.5f, 0f);
                RallyPoints.Set(_selOrigin, rally);
                RallyVisual.Instance.Show(bc, rally, _rallyIcon);
                return;
            }
```

Compile-check. Commit: `feat(rally): ObjectInspector shows rally visual + right-click sets it`.

---

## Task 6: Wire GUI_33 into ObjectInspector + verify

- MCP: set `ObjectInspector._rallyIcon` to sub-sprite `GUI_33` from `Assets/2D Casual UI/Sprite/GUI.png`, save scene.
- Full compile (0 errors).
- Manual smoke (report to user): select Keep → flag + flowing dashed line below it; right-click ground → flag jumps there; train a Farmer → it walks to the flag and fans out with others; deselect → flag/line vanish.

Commit (scene): `feat(rally): wire GUI_33 rally icon in SampleScene`.

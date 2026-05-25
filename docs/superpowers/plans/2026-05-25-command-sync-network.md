# Command Sync Over Network Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Propagate Move/Harvest/Build-Assist/Place-Building/Train-Unit commands across all clients via Steam P2P so all players see a consistent simulation.

**Architecture:** Local-immediate + host-echo. Each client applies its own commands instantly, then sends to the host. The host echoes to all OTHER clients. Units use owner-scoped NetIds `(ulong owner, ushort localIndex)` that match across clients because of deterministic starting-spawn order plus pre-reserved IDs for trained units.

**Tech Stack:** Unity 6 / C# / Steamworks.NET / RTSCL.World + RTSCL.World.Unity + Assembly-CSharp asmdefs

**Spec:** `docs/superpowers/specs/2026-05-25-command-sync-network-design.md`

---

## File Structure

### New files

| File | Asmdef | Purpose |
|---|---|---|
| `Assets/Scripts/World/GoblinNetId.cs` | RTSCL.World | Pure struct `(ulong Owner, ushort LocalIndex)`. Lives in World (not World.Unity) so it's testable. |
| `Assets/Scripts/World/Unity/GoblinNetRegistry.cs` | RTSCL.World.Unity | Static `Dictionary<GoblinNetId, Goblin>` + per-owner counter + Register/Unregister/TryGet/NextLocalIndex |
| `Assets/Scripts/World/Unity/NetCommandBridge.cs` | RTSCL.World.Unity | Static `Action<byte[]> OutgoingSender` delegate — asmdef bridge to Lobby |
| `Assets/Scripts/World/Unity/NetworkCatalog.cs` | RTSCL.World.Unity | Static lookups `defIndex ↔ BuildingDefinition`, `unitDefIndex ↔ GoblinUnitDefinition`. Populated by `WorldGeneratorBootstrap` at scene start. |
| `Assets/Scripts/World/Unity/NetCommandApplier.cs` | RTSCL.World.Unity | Per-command pure-local Apply helpers + top-level `Apply(payload, sender)` dispatch |
| `Assets/Scripts/World/Unity/NetCommandIssuer.cs` | RTSCL.World.Unity | Per-command Issue helpers — apply locally + serialize + send via Bridge |
| `Assets/Tests/Editor/GoblinNetIdTests.cs` | RTSCL.World.Tests | Equals/GetHashCode/Comparable tests for the NetId struct |

### Modified files

| File | Change |
|---|---|
| `Assets/Scripts/Lobby/NetMessages.cs` | Add 5 new `NetMessageType` values + Pack/TryUnpack for each |
| `Assets/Scripts/Lobby/NetworkManager.cs` | `SendToOthers(payload, exceptConn)` host-echo helper + dispatch new types in `RouteMessage` to `NetCommandApplier.Apply` |
| `Assets/Scripts/Lobby/GameStartLoader.cs` | After scene load, wire `NetCommandBridge.OutgoingSender = NetworkManager.SendToAll` |
| `Assets/Scripts/World/Unity/Goblin.cs` | Add `public GoblinNetId NetId`; `Init` takes `GoblinNetId`; register on Init + unregister on OnDestroy |
| `Assets/Scripts/World/Unity/GoblinSpawner.cs` | All Spawn methods accept owner; SpawnAt assigns NetId via `GoblinNetRegistry.Allocate(owner, reservedIndex)` |
| `Assets/Scripts/World/Unity/MainBaseSetup.cs` | No code change beyond what's already there — starting-unit IDs flow through the auto-counter |
| `Assets/Scripts/World/Unity/GoblinSelectionController.cs` | `CommandFormation` / `CommandHarvest` / build-right-click route through `NetCommandIssuer` |
| `Assets/Scripts/World/Unity/BuildingPlacer.cs` | `Place` (local click path) routes through `NetCommandIssuer.IssuePlaceBuilding`; `PlaceForce` remains the local-application entry point |
| `Assets/Scripts/World/Unity/ObjectInspector.cs` | `OnUnitClicked` reserves NetId + routes through `NetCommandIssuer.IssueTrainUnit` |
| `Assets/Scripts/World/Unity/GoblinProduction.cs` | `TryStart` accepts `ushort reservedIndex` and stores it on the Slot |
| `Assets/Scripts/World/Unity/GoblinProductionRunner.cs` | Pass slot's `reservedIndex` to spawner when production completes |
| `Assets/Scripts/World/Unity/WorldGeneratorBootstrap.cs` | Populate `NetworkCatalog` from its `BuildingCatalog` + unit defs at world generate; reset `GoblinNetRegistry` on new world |

---

## Task 1: GoblinNetId struct

**Files:**
- Create: `Assets/Scripts/World/GoblinNetId.cs`
- Create: `Assets/Tests/Editor/GoblinNetIdTests.cs`

- [ ] **Step 1: Create the struct**

Write `Assets/Scripts/World/GoblinNetId.cs`:

```csharp
using System;

namespace RTSCL.World
{
    /// <summary>Owner-scoped unit identifier. Same value on all clients for a given goblin.</summary>
    public readonly struct GoblinNetId : IEquatable<GoblinNetId>
    {
        public readonly ulong Owner;
        public readonly ushort LocalIndex;

        public GoblinNetId(ulong owner, ushort localIndex)
        {
            Owner = owner;
            LocalIndex = localIndex;
        }

        public bool Equals(GoblinNetId other) => Owner == other.Owner && LocalIndex == other.LocalIndex;
        public override bool Equals(object obj) => obj is GoblinNetId other && Equals(other);
        public override int GetHashCode() => unchecked((Owner.GetHashCode() * 397) ^ LocalIndex);
        public override string ToString() => $"({Owner}:{LocalIndex})";

        public static bool operator ==(GoblinNetId a, GoblinNetId b) => a.Equals(b);
        public static bool operator !=(GoblinNetId a, GoblinNetId b) => !a.Equals(b);
    }
}
```

- [ ] **Step 2: Create the .meta sibling**

Use Unity MCP to refresh assets so the .meta is generated:

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Verify `Assets/Scripts/World/GoblinNetId.cs.meta` exists with a 32-char hex GUID.

- [ ] **Step 3: Write Edit-mode tests**

Write `Assets/Tests/Editor/GoblinNetIdTests.cs`:

```csharp
using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class GoblinNetIdTests
    {
        [Test]
        public void EqualForSameValues()
        {
            var a = new GoblinNetId(1234UL, 5);
            var b = new GoblinNetId(1234UL, 5);
            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void DifferentOwnerNotEqual()
        {
            var a = new GoblinNetId(1234UL, 5);
            var b = new GoblinNetId(9999UL, 5);
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void DifferentIndexNotEqual()
        {
            var a = new GoblinNetId(1234UL, 5);
            var b = new GoblinNetId(1234UL, 6);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void ZeroOwnerIsValid()
        {
            var a = new GoblinNetId(0UL, 0);
            var b = new GoblinNetId(0UL, 0);
            Assert.AreEqual(a, b);
        }
    }
}
```

- [ ] **Step 4: Run tests via Unity MCP**

Via `mcp__unity-mcp__Unity_RunCommand` run the test runner against `RTSCL.World.Tests`. Expected: 4 new tests pass, all existing tests still pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/GoblinNetId.cs Assets/Scripts/World/GoblinNetId.cs.meta Assets/Tests/Editor/GoblinNetIdTests.cs Assets/Tests/Editor/GoblinNetIdTests.cs.meta
git commit -m "feat(net): GoblinNetId struct + edit-mode tests"
```

---

## Task 2: GoblinNetRegistry

**Files:**
- Create: `Assets/Scripts/World/Unity/GoblinNetRegistry.cs`

- [ ] **Step 1: Create the registry**

Write `Assets/Scripts/World/Unity/GoblinNetRegistry.cs`:

```csharp
using System.Collections.Generic;
using RTSCL.World;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks live goblins by NetId and hands out owner-scoped LocalIndex values.</summary>
    public static class GoblinNetRegistry
    {
        private static readonly Dictionary<GoblinNetId, Goblin> _byId = new();
        private static readonly Dictionary<ulong, ushort> _nextIndex = new();

        /// <summary>Reserve the next available LocalIndex for the given owner.</summary>
        public static ushort NextLocalIndex(ulong owner)
        {
            _nextIndex.TryGetValue(owner, out ushort idx);
            _nextIndex[owner] = (ushort)(idx + 1);
            return idx;
        }

        public static void Register(GoblinNetId id, Goblin g)
        {
            _byId[id] = g;
        }

        public static void Unregister(GoblinNetId id)
        {
            _byId.Remove(id);
        }

        public static bool TryGet(GoblinNetId id, out Goblin g) =>
            _byId.TryGetValue(id, out g);

        public static IEnumerable<Goblin> All => _byId.Values;

        public static void Reset()
        {
            _byId.Clear();
            _nextIndex.Clear();
        }
    }
}
```

- [ ] **Step 2: Refresh + compile check**

Via Unity MCP run `AssetDatabase.Refresh()`. Read console for errors: expect 0.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinNetRegistry.cs Assets/Scripts/World/Unity/GoblinNetRegistry.cs.meta
git commit -m "feat(net): GoblinNetRegistry static lookup + owner-scoped counter"
```

---

## Task 3: NetCommandBridge

**Files:**
- Create: `Assets/Scripts/World/Unity/NetCommandBridge.cs`

- [ ] **Step 1: Create the bridge**

Write `Assets/Scripts/World/Unity/NetCommandBridge.cs`:

```csharp
using System;

namespace RTSCL.World.Unity
{
    /// <summary>Asmdef-boundary bridge for outgoing network commands.
    /// The Lobby side sets <see cref="OutgoingSender"/> at game start; the World side calls it to
    /// send packed command payloads without referencing Steamworks types directly.
    /// Null in solo (commands are applied locally without a wire send).</summary>
    public static class NetCommandBridge
    {
        public static Action<byte[]> OutgoingSender;

        public static void Send(byte[] payload)
        {
            OutgoingSender?.Invoke(payload);
        }

        public static void Reset()
        {
            OutgoingSender = null;
        }
    }
}
```

- [ ] **Step 2: Refresh + compile check**

Via Unity MCP `AssetDatabase.Refresh()`. Console errors: 0.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/NetCommandBridge.cs Assets/Scripts/World/Unity/NetCommandBridge.cs.meta
git commit -m "feat(net): NetCommandBridge delegate (asmdef boundary)"
```

---

## Task 4: NetworkCatalog

**Files:**
- Create: `Assets/Scripts/World/Unity/NetworkCatalog.cs`

- [ ] **Step 1: Create the catalog**

Write `Assets/Scripts/World/Unity/NetworkCatalog.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Static index ↔ definition lookups. Populated once per world by
    /// <see cref="WorldGeneratorBootstrap"/>. All clients see the same indices because
    /// they all run against the same shipped assets.</summary>
    public static class NetworkCatalog
    {
        private static readonly List<BuildingDefinition> _buildings = new();
        private static readonly List<GoblinUnitDefinition> _units = new();
        private static readonly Dictionary<BuildingDefinition, byte> _buildingToIdx = new();
        private static readonly Dictionary<GoblinUnitDefinition, byte> _unitToIdx = new();

        public static void PopulateFromCatalog(BuildingCatalog catalog)
        {
            _buildings.Clear();
            _buildingToIdx.Clear();
            _units.Clear();
            _unitToIdx.Clear();

            if (catalog == null) return;

            for (int i = 0; i < catalog.Buildings.Count && i < 256; i++)
            {
                var b = catalog.Buildings[i];
                if (b == null) continue;
                byte idx = (byte)_buildings.Count;
                _buildings.Add(b);
                _buildingToIdx[b] = idx;

                if (b.TrainsUnits == null) continue;
                foreach (var u in b.TrainsUnits)
                {
                    if (u == null) continue;
                    if (_unitToIdx.ContainsKey(u)) continue;
                    if (_units.Count >= 256) continue;
                    byte uidx = (byte)_units.Count;
                    _units.Add(u);
                    _unitToIdx[u] = uidx;
                }
            }
        }

        public static bool TryGetBuildingIndex(BuildingDefinition def, out byte idx) =>
            _buildingToIdx.TryGetValue(def, out idx);

        public static BuildingDefinition GetBuilding(byte idx) =>
            idx < _buildings.Count ? _buildings[idx] : null;

        public static bool TryGetUnitIndex(GoblinUnitDefinition def, out byte idx) =>
            _unitToIdx.TryGetValue(def, out idx);

        public static GoblinUnitDefinition GetUnit(byte idx) =>
            idx < _units.Count ? _units[idx] : null;
    }
}
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/NetworkCatalog.cs Assets/Scripts/World/Unity/NetworkCatalog.cs.meta
git commit -m "feat(net): NetworkCatalog index ↔ BuildingDefinition / GoblinUnitDefinition"
```

---

## Task 5: Goblin.NetId integration

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Read Goblin.cs Init signature + OnDestroy**

```bash
# Use Read tool. The current Init signature is:
# public void Init(string kind, Sprite[] frames, Tilemap terrainMap, Tilemap decorationMap, GoblinUnitDefinition def = null)
```

- [ ] **Step 2: Add NetId field**

Near other public properties in Goblin.cs (e.g. near `Kind`), add:

```csharp
public GoblinNetId NetId { get; private set; }
```

- [ ] **Step 3: Extend Init**

Change Init signature to accept a NetId. New signature:

```csharp
public void Init(GoblinNetId netId, string kind, Sprite[] frames, Tilemap terrainMap, Tilemap decorationMap, GoblinUnitDefinition def = null)
{
    NetId = netId;
    GoblinNetRegistry.Register(netId, this);
    // ... rest of existing Init body unchanged
}
```

- [ ] **Step 4: Unregister in OnDestroy**

Find the existing `OnDestroy` (or add one if missing). Inside, before any existing teardown:

```csharp
GoblinNetRegistry.Unregister(NetId);
```

If `OnDestroy` doesn't exist:

```csharp
private void OnDestroy()
{
    GoblinNetRegistry.Unregister(NetId);
}
```

- [ ] **Step 5: Compile check via Unity MCP**

`AssetDatabase.Refresh()`, then read console. Expect: errors at every Init call site (GoblinSpawner). That's the next task — leave it broken for now.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(net): Goblin.NetId + auto-register/unregister"
```

---

## Task 6: GoblinSpawner threads NetId

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSpawner.cs`

- [ ] **Step 1: Update SpawnAt to allocate and pass NetId**

Replace the existing `SpawnAt` method body. New signature includes an optional reserved index:

```csharp
public Goblin SpawnAt(Vector3 worldPos, GoblinKind kind = null, ulong owner = 0UL, ushort? reservedIndex = null)
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

    ushort idx = reservedIndex ?? GoblinNetRegistry.NextLocalIndex(owner);
    var netId = new GoblinNetId(owner, idx);

    var goblin = go.AddComponent<Goblin>();
    goblin.Init(netId, kind.Name, kind.WalkFrames, _terrainMap, _decorationMap, kind.Definition);
    goblin.SetOwner(owner);
    return goblin;
}
```

- [ ] **Step 2: Update SpawnAroundFootprint / SpawnByKindAroundFootprint to optionally pass reservedIndex**

`SpawnAroundFootprint` is used for batch spawning starting units — let it auto-allocate (no reservedIndex param).

`SpawnByKindAroundFootprint` (used by `GoblinProductionRunner`) needs a `ushort? reservedIndex = null` parameter. New signature:

```csharp
public Goblin SpawnByKindAroundFootprint(string kindName, Vector2Int origin, Vector2Int footprint, ulong owner = 0UL, ushort? reservedIndex = null)
{
    // ... existing body unchanged except the final SpawnAt call:
    return SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f), kind, owner, reservedIndex);
    // ... that one return statement inside the loop
}
```

- [ ] **Step 3: Compile check via Unity MCP**

`AssetDatabase.Refresh()`. Expect: 0 errors now that Init signature is satisfied at the call sites.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSpawner.cs
git commit -m "feat(net): GoblinSpawner allocates NetIds (auto-counter or reserved)"
```

---

## Task 7: NetWireFormat (wire-format helpers in RTSCL.World.Unity)

**Why this asmdef:** `RTSCL.World.Unity` cannot reference `Assembly-CSharp` (where the Lobby's `NetMessages` lives), so the actual pack/unpack must live on the World.Unity side. Lobby code routes payloads as opaque `byte[]` — only the first-byte enum is checked there.

**Files:**
- Create: `Assets/Scripts/World/Unity/NetWireFormat.cs`

- [ ] **Step 1: Create NetWireFormat**

Write `Assets/Scripts/World/Unity/NetWireFormat.cs`:

```csharp
using System.Collections.Generic;
using System.IO;

namespace RTSCL.World.Unity
{
    /// <summary>Pack/unpack helpers for the 5 command types. Lives in World.Unity so
    /// the asmdef boundary is preserved (no Steamworks dependency). The byte constants
    /// here mirror RTSCL.Lobby.NetMessageType values.</summary>
    internal static class NetWireFormat
    {
        public const byte CmdMove = 2;
        public const byte CmdHarvest = 3;
        public const byte CmdBuildAssist = 4;
        public const byte CmdPlaceBuilding = 5;
        public const byte CmdTrainUnit = 6;

        public readonly struct WireNetIdLocal
        {
            public readonly ulong Owner;
            public readonly ushort LocalIndex;
            public WireNetIdLocal(ulong o, ushort i) { Owner = o; LocalIndex = i; }
        }

        public static byte[] PackCmdMove(IList<WireNetIdLocal> ids, float x, float y) =>
            PackUnitsCmd(CmdMove, ids, w => { w.Write(x); w.Write(y); });

        public static byte[] PackCmdHarvest(IList<WireNetIdLocal> ids, int tx, int ty) =>
            PackUnitsCmd(CmdHarvest, ids, w => { w.Write(tx); w.Write(ty); });

        public static byte[] PackCmdBuildAssist(IList<WireNetIdLocal> ids, int ox, int oy) =>
            PackUnitsCmd(CmdBuildAssist, ids, w => { w.Write(ox); w.Write(oy); });

        public static byte[] PackCmdPlaceBuilding(byte defIndex, int ox, int oy, ulong owner)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(CmdPlaceBuilding); w.Write(defIndex); w.Write(ox); w.Write(oy); w.Write(owner);
            return ms.ToArray();
        }

        public static byte[] PackCmdTrainUnit(int ox, int oy, byte unitDefIndex, ulong owner, ushort reservedIndex)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(CmdTrainUnit); w.Write(ox); w.Write(oy); w.Write(unitDefIndex); w.Write(owner); w.Write(reservedIndex);
            return ms.ToArray();
        }

        private static byte[] PackUnitsCmd(byte type, IList<WireNetIdLocal> ids, System.Action<BinaryWriter> writeTail)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(type);
            w.Write((ushort)(ids?.Count ?? 0));
            if (ids != null) foreach (var id in ids) { w.Write(id.Owner); w.Write(id.LocalIndex); }
            writeTail(w);
            return ms.ToArray();
        }

        /// <summary>Begin unpacking a units-list command. Caller reads the trailing payload
        /// from the returned BinaryReader. Returns false on a malformed/short payload.</summary>
        public static bool TryUnpackUnitsCmd(byte[] payload, byte expectedType, int tailBytes,
                                              out List<WireNetIdLocal> ids,
                                              out BinaryReader reader)
        {
            ids = null; reader = null;
            if (payload == null || payload.Length < 3 + tailBytes) return false;
            if (payload[0] != expectedType) return false;
            var ms = new MemoryStream(payload, 1, payload.Length - 1);
            var r = new BinaryReader(ms);
            int n = r.ReadUInt16();
            ids = new List<WireNetIdLocal>(n);
            for (int i = 0; i < n; i++)
            {
                if (ms.Position + 10 > ms.Length) { r.Dispose(); ms.Dispose(); return false; }
                ids.Add(new WireNetIdLocal(r.ReadUInt64(), r.ReadUInt16()));
            }
            if (ms.Position + tailBytes > ms.Length) { r.Dispose(); ms.Dispose(); return false; }
            reader = r;
            return true;
        }
    }
}
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 3: Smoke-test round-trips via a temporary editor MenuItem**

Create `Assets/Scripts/Editor/_NetWireFormatRoundTripCheck.cs` (will be deleted at end of task):

```csharp
using UnityEditor;
using System.Collections.Generic;
using RTSCL.World.Unity;

public static class _NetWireFormatRoundTripCheck
{
    [MenuItem("RTSCL/Verify NetWireFormat")]
    public static void Run()
    {
        var ids = new List<NetWireFormat.WireNetIdLocal> {
            new(1234UL, 0), new(1234UL, 1)
        };

        // CmdMove
        var p = NetWireFormat.PackCmdMove(ids, 12.5f, -3.25f);
        if (!NetWireFormat.TryUnpackUnitsCmd(p, NetWireFormat.CmdMove, 8, out var ids2, out var r1)
            || ids2.Count != 2 || r1.ReadSingle() != 12.5f || r1.ReadSingle() != -3.25f)
        { UnityEngine.Debug.LogError("CmdMove round-trip failed"); return; }

        // CmdHarvest
        p = NetWireFormat.PackCmdHarvest(ids, 7, -4);
        if (!NetWireFormat.TryUnpackUnitsCmd(p, NetWireFormat.CmdHarvest, 8, out _, out var r2)
            || r2.ReadInt32() != 7 || r2.ReadInt32() != -4)
        { UnityEngine.Debug.LogError("CmdHarvest round-trip failed"); return; }

        // CmdBuildAssist
        p = NetWireFormat.PackCmdBuildAssist(ids, 9, 10);
        if (!NetWireFormat.TryUnpackUnitsCmd(p, NetWireFormat.CmdBuildAssist, 8, out _, out var r3)
            || r3.ReadInt32() != 9 || r3.ReadInt32() != 10)
        { UnityEngine.Debug.LogError("CmdBuildAssist round-trip failed"); return; }

        // CmdPlaceBuilding — manual unpack since not a units-list command
        p = NetWireFormat.PackCmdPlaceBuilding(3, 11, 12, 0xDEADBEEFUL);
        if (p.Length != 22 || p[0] != NetWireFormat.CmdPlaceBuilding)
        { UnityEngine.Debug.LogError("CmdPlaceBuilding header wrong"); return; }
        using (var ms = new System.IO.MemoryStream(p, 1, p.Length - 1))
        using (var r = new System.IO.BinaryReader(ms))
        {
            if (r.ReadByte() != 3 || r.ReadInt32() != 11 || r.ReadInt32() != 12 || r.ReadUInt64() != 0xDEADBEEFUL)
            { UnityEngine.Debug.LogError("CmdPlaceBuilding round-trip failed"); return; }
        }

        // CmdTrainUnit
        p = NetWireFormat.PackCmdTrainUnit(13, 14, 5, 0xCAFEBABEUL, 42);
        if (p.Length != 20 || p[0] != NetWireFormat.CmdTrainUnit)
        { UnityEngine.Debug.LogError("CmdTrainUnit header wrong"); return; }
        using (var ms = new System.IO.MemoryStream(p, 1, p.Length - 1))
        using (var r = new System.IO.BinaryReader(ms))
        {
            if (r.ReadInt32() != 13 || r.ReadInt32() != 14 || r.ReadByte() != 5
                || r.ReadUInt64() != 0xCAFEBABEUL || r.ReadUInt16() != 42)
            { UnityEngine.Debug.LogError("CmdTrainUnit round-trip failed"); return; }
        }

        UnityEngine.Debug.Log("[NetWireFormat] All 5 round-trips OK");
    }
}
```

Invoke via Unity MCP: `mcp__unity-mcp__Unity_ManageMenuItem` action "execute" path "RTSCL/Verify NetWireFormat". Expected console: `[NetWireFormat] All 5 round-trips OK`.

- [ ] **Step 4: Delete the temporary verifier**

Delete `Assets/Scripts/Editor/_NetWireFormatRoundTripCheck.cs` and its .meta. Refresh.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/NetWireFormat.cs Assets/Scripts/World/Unity/NetWireFormat.cs.meta
git commit -m "feat(net): NetWireFormat pack/unpack for 5 command types"
```

---

## Task 8: Extend NetMessageType enum

**Files:**
- Modify: `Assets/Scripts/Lobby/NetMessages.cs`

- [ ] **Step 1: Add the 5 new enum values**

Replace the existing enum block in NetMessages.cs:

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

No other changes to NetMessages.cs in this task. The actual pack/unpack lives in `NetWireFormat` (Task 7). Lobby only needs the enum values for the `RouteMessage` switch in Task 12.

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Lobby/NetMessages.cs
git commit -m "feat(net): NetMessageType enum values for 5 command types"
```

---

## Task 9: NetCommandApplier (per-command + dispatcher)

**Files:**
- Create: `Assets/Scripts/World/Unity/NetCommandApplier.cs`

- [ ] **Step 1: Create the applier**

Write `Assets/Scripts/World/Unity/NetCommandApplier.cs`:

```csharp
using System.Collections.Generic;
using RTSCL.World;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-command local mutations used by both the issuer (own commands) and
    /// the incoming network dispatch (remote commands). No wire-format dependency on
    /// Lobby — this side touches local sim state via NetWireFormat directly.</summary>
    public static class NetCommandApplier
    {
        // Set by MainBaseSetup at scene start.
        public static BuildingPlacer Placer;
        public static GoblinSpawner Spawner;

        // ------------- Per-command local mutations -------------

        public static void ApplyMove(IList<GoblinNetId> ids, Vector3 centerTarget, ulong senderOwnerCheck)
        {
            if (ids == null || ids.Count == 0) return;
            // Re-compute the same formation offset the issuer used (deterministic).
            int n = ids.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
            int rows = Mathf.CeilToInt((float)n / cols);
            const float spacing = 1.0f;
            for (int i = 0; i < n; i++)
            {
                var id = ids[i];
                if (!GoblinNetRegistry.TryGet(id, out var g) || g == null) continue;
                if (senderOwnerCheck != 0UL && g.NetId.Owner != senderOwnerCheck)
                {
                    Debug.LogWarning($"[Net] Move on {id} dropped: sender {senderOwnerCheck} != owner {g.NetId.Owner}");
                    continue;
                }
                int col = i % cols;
                int row = i / cols;
                Vector3 offset = new(
                    (col - (cols - 1) * 0.5f) * spacing,
                    (row - (rows - 1) * 0.5f) * spacing, 0f);
                g.SetMoveCommand(centerTarget + offset);
            }
        }

        public static void ApplyHarvest(IList<GoblinNetId> ids, Vector3Int treeCell, ulong senderOwnerCheck)
        {
            if (ids == null) return;
            foreach (var id in ids)
            {
                if (!GoblinNetRegistry.TryGet(id, out var g) || g == null) continue;
                if (senderOwnerCheck != 0UL && g.NetId.Owner != senderOwnerCheck)
                {
                    Debug.LogWarning($"[Net] Harvest on {id} dropped: ownership mismatch");
                    continue;
                }
                g.SetHarvestCommand(treeCell);
            }
        }

        public static void ApplyBuildAssist(IList<GoblinNetId> ids, Vector2Int origin, ulong senderOwnerCheck)
        {
            if (ids == null) return;
            foreach (var id in ids)
            {
                if (!GoblinNetRegistry.TryGet(id, out var g) || g == null) continue;
                if (senderOwnerCheck != 0UL && g.NetId.Owner != senderOwnerCheck)
                {
                    Debug.LogWarning($"[Net] BuildAssist on {id} dropped: ownership mismatch");
                    continue;
                }
                g.SetBuildCommand(origin);
            }
        }

        public static void ApplyPlaceBuilding(BuildingDefinition def, Vector2Int origin, ulong owner)
        {
            if (def == null || Placer == null) return;
            Placer.PlaceForce(def, origin, charge: false, requireConstruction: true, owner: owner);
        }

        public static void ApplyTrainUnit(Vector2Int buildingOrigin, GoblinUnitDefinition def, ulong owner, ushort reservedLocalIndex)
        {
            if (def == null) return;
            GoblinProduction.TryStart(buildingOrigin, def, reservedLocalIndex);
        }

        // ------------- Top-level dispatcher -------------

        public static void Apply(byte[] payload, ulong sender)
        {
            if (payload == null || payload.Length == 0) return;
            switch (payload[0])
            {
                case NetWireFormat.CmdMove:
                    if (NetWireFormat.TryUnpackUnitsCmd(payload, NetWireFormat.CmdMove, 8, out var moveWire, out var mr))
                    {
                        float mx = mr.ReadSingle(); float my = mr.ReadSingle();
                        mr.Dispose();
                        ApplyMove(WireToNetIds(moveWire), new Vector3(mx, my, 0f), sender);
                    }
                    break;
                case NetWireFormat.CmdHarvest:
                    if (NetWireFormat.TryUnpackUnitsCmd(payload, NetWireFormat.CmdHarvest, 8, out var harvWire, out var hr))
                    {
                        int tx = hr.ReadInt32(); int ty = hr.ReadInt32();
                        hr.Dispose();
                        ApplyHarvest(WireToNetIds(harvWire), new Vector3Int(tx, ty, 0), sender);
                    }
                    break;
                case NetWireFormat.CmdBuildAssist:
                    if (NetWireFormat.TryUnpackUnitsCmd(payload, NetWireFormat.CmdBuildAssist, 8, out var bldWire, out var br))
                    {
                        int ox = br.ReadInt32(); int oy = br.ReadInt32();
                        br.Dispose();
                        ApplyBuildAssist(WireToNetIds(bldWire), new Vector2Int(ox, oy), sender);
                    }
                    break;
                case NetWireFormat.CmdPlaceBuilding:
                    if (payload.Length >= 22)
                    {
                        using var pms = new System.IO.MemoryStream(payload, 1, payload.Length - 1);
                        using var pr = new System.IO.BinaryReader(pms);
                        byte pdi = pr.ReadByte();
                        int pox = pr.ReadInt32(); int poy = pr.ReadInt32();
                        ulong pown = pr.ReadUInt64();
                        if (pown != sender) { Debug.LogWarning($"[Net] CmdPlaceBuilding dropped: owner {pown} != sender {sender}"); break; }
                        ApplyPlaceBuilding(NetworkCatalog.GetBuilding(pdi), new Vector2Int(pox, poy), pown);
                    }
                    break;
                case NetWireFormat.CmdTrainUnit:
                    if (payload.Length >= 1 + 4 + 4 + 1 + 8 + 2)
                    {
                        using var tms = new System.IO.MemoryStream(payload, 1, payload.Length - 1);
                        using var tr = new System.IO.BinaryReader(tms);
                        int tox = tr.ReadInt32(); int toy = tr.ReadInt32();
                        byte tui = tr.ReadByte();
                        ulong town = tr.ReadUInt64();
                        ushort tri = tr.ReadUInt16();
                        if (town != sender) { Debug.LogWarning($"[Net] CmdTrainUnit dropped: owner {town} != sender {sender}"); break; }
                        ApplyTrainUnit(new Vector2Int(tox, toy), NetworkCatalog.GetUnit(tui), town, tri);
                    }
                    break;
            }
        }

        private static List<GoblinNetId> WireToNetIds(List<NetWireFormat.WireNetIdLocal> wire)
        {
            var ids = new List<GoblinNetId>(wire.Count);
            foreach (var w in wire) ids.Add(new GoblinNetId(w.Owner, w.LocalIndex));
            return ids;
        }
    }
}
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect ONE compile error: `GoblinProduction.TryStart(Vector2Int, GoblinUnitDefinition, ushort)` doesn't exist yet (existing signature has 2 params). Task 10 adds the third param.

- [ ] **Step 3: Do not commit yet — hold in working tree until Task 10**

Leave the file uncommitted. Task 10's commit covers both this file and the production changes together.

---

## Task 10: GoblinProduction reserved index + spawn pass-through

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinProduction.cs`
- Modify: `Assets/Scripts/World/Unity/GoblinProductionRunner.cs`

- [ ] **Step 1: Add reservedIndex to Slot + TryStart**

In `GoblinProduction.cs`, change the `Slot` inner class to include a `ReservedIndex`:

```csharp
public sealed class Slot
{
    public GoblinUnitDefinition Def;
    public float Elapsed;
    public ushort ReservedIndex;
    public float Progress => Def == null || Def.SpawnDuration <= 0
        ? 1f
        : Mathf.Clamp01(Elapsed / Def.SpawnDuration);
}
```

Extend `TryStart` with an optional `ushort reservedIndex`:

```csharp
public static bool TryStart(Vector2Int origin, GoblinUnitDefinition def, ushort reservedIndex = 0)
{
    if (def == null) return false;
    if (_slots.ContainsKey(origin)) return false;
    if (!PopulationManager.CanAfford(def.PopulationCost)) return false;
    PopulationManager.AddUsed(def.PopulationCost);
    _slots[origin] = new Slot { Def = def, Elapsed = 0f, ReservedIndex = reservedIndex };
    OnChanged?.Invoke();
    return true;
}
```

Change `Tick`'s return tuple to include the reserved index:

```csharp
public static List<(Vector2Int origin, GoblinUnitDefinition def, ushort reservedIndex)> Tick(float dt)
{
    List<(Vector2Int, GoblinUnitDefinition, ushort)> done = null;
    foreach (var kvp in _slots)
    {
        kvp.Value.Elapsed += dt;
        if (kvp.Value.Elapsed >= kvp.Value.Def.SpawnDuration)
        {
            done ??= new List<(Vector2Int, GoblinUnitDefinition, ushort)>();
            done.Add((kvp.Key, kvp.Value.Def, kvp.Value.ReservedIndex));
        }
    }
    if (done != null)
    {
        foreach (var (origin, _, _) in done) _slots.Remove(origin);
        OnChanged?.Invoke();
        return done;
    }
    return null;
}
```

- [ ] **Step 2: Update GoblinProductionRunner**

In `GoblinProductionRunner.cs`, change the foreach to use the reserved index and pass the building owner:

```csharp
foreach (var (origin, def, reservedIndex) in done)
{
    if (!_placer.TryGetBuildingAt(origin, out var building) || building == null) continue;
    ulong owner = _placer.TryGetBuildingOwner(origin, out ulong o) ? o : 0UL;
    _spawner.SpawnByKindAroundFootprint(def.SpawnerKindName, origin, building.Footprint, owner, reservedIndex);
}
```

- [ ] **Step 3: Refresh + compile check via Unity MCP**

Expect 0 errors. All of Tasks 8 / 9 / 10 should now compile together.

- [ ] **Step 4: Commit Tasks 9 + 10 together**

```bash
git add Assets/Scripts/World/Unity/NetCommandApplier.cs Assets/Scripts/World/Unity/NetCommandApplier.cs.meta \
        Assets/Scripts/World/Unity/GoblinProduction.cs \
        Assets/Scripts/World/Unity/GoblinProductionRunner.cs
git commit -m "feat(net): NetCommandApplier + production reserved-index pass-through"
```

---

## Task 11: NetCommandIssuer

**Files:**
- Create: `Assets/Scripts/World/Unity/NetCommandIssuer.cs`

- [ ] **Step 1: Create the issuer**

Write `Assets/Scripts/World/Unity/NetCommandIssuer.cs`:

```csharp
using System.Collections.Generic;
using RTSCL.World;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-command Issue helpers. Issuer applies locally + serializes + sends via Bridge.
    /// Solo (NetCommandBridge.OutgoingSender == null): no wire send, just local application.</summary>
    public static class NetCommandIssuer
    {
        public static void IssueMove(IReadOnlyList<Goblin> units, Vector3 worldTarget)
        {
            if (units == null || units.Count == 0) return;

            // Compute formation offset per unit (matches existing CommandFormation in GoblinSelectionController).
            int n = units.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
            const float spacing = 1.0f;

            // Build wire list and apply per-unit move locally with the same target so remotes
            // can use the per-unit target directly. To keep payload small, we send ONE worldTarget
            // and let receivers re-compute the formation offset.
            // Simpler: just send the per-unit target as the center; receivers reproduce the offset
            // because all clients run the same deterministic formula on the same selection ID list.
            var wire = new List<NetWireFormat.WireNetIdLocal>(n);
            for (int i = 0; i < n; i++)
            {
                var g = units[i];
                if (g == null) continue;
                wire.Add(new NetWireFormat.WireNetIdLocal(g.NetId.Owner, g.NetId.LocalIndex));
            }

            // Apply locally (issuer path): compute per-unit target with formation offset.
            int rowsCount = Mathf.CeilToInt((float)n / cols);
            for (int i = 0; i < n; i++)
            {
                int col = i % cols;
                int row = i / cols;
                Vector3 offset = new(
                    (col - (cols - 1) * 0.5f) * spacing,
                    (row - (rowsCount - 1) * 0.5f) * spacing, 0f);
                if (units[i] != null) units[i].SetMoveCommand(worldTarget + offset);
            }

            // Send center target — remotes apply the same formula deterministically.
            NetCommandBridge.Send(NetWireFormat.PackCmdMove(wire, worldTarget.x, worldTarget.y));
        }

        public static void IssueHarvest(IReadOnlyList<Goblin> workers, Vector3Int treeCell)
        {
            if (workers == null || workers.Count == 0) return;
            var wire = new List<NetWireFormat.WireNetIdLocal>(workers.Count);
            foreach (var w in workers)
            {
                if (w == null) continue;
                wire.Add(new NetWireFormat.WireNetIdLocal(w.NetId.Owner, w.NetId.LocalIndex));
                w.SetHarvestCommand(treeCell);
            }
            NetCommandBridge.Send(NetWireFormat.PackCmdHarvest(wire, treeCell.x, treeCell.y));
        }

        public static void IssueBuildAssist(IReadOnlyList<Goblin> workers, Vector2Int buildOrigin)
        {
            if (workers == null || workers.Count == 0) return;
            var wire = new List<NetWireFormat.WireNetIdLocal>(workers.Count);
            foreach (var w in workers)
            {
                if (w == null) continue;
                wire.Add(new NetWireFormat.WireNetIdLocal(w.NetId.Owner, w.NetId.LocalIndex));
                w.SetBuildCommand(buildOrigin);
            }
            NetCommandBridge.Send(NetWireFormat.PackCmdBuildAssist(wire, buildOrigin.x, buildOrigin.y));
        }

        public static void IssuePlaceBuilding(BuildingDefinition def, Vector2Int origin, ulong owner)
        {
            if (def == null) return;
            if (!NetworkCatalog.TryGetBuildingIndex(def, out byte idx))
            {
                Debug.LogWarning($"[Net] Cannot issue place: {def.name} missing from NetworkCatalog");
                return;
            }
            // Issuer applies locally (charge=true so the wood is deducted on the issuing client).
            if (NetCommandApplier.Placer != null)
                NetCommandApplier.Placer.PlaceForce(def, origin, charge: true, requireConstruction: true, owner: owner);
            NetCommandBridge.Send(NetWireFormat.PackCmdPlaceBuilding(idx, origin.x, origin.y, owner));
        }

        public static void IssueTrainUnit(Vector2Int buildingOrigin, GoblinUnitDefinition def, ulong owner)
        {
            if (def == null) return;
            if (!NetworkCatalog.TryGetUnitIndex(def, out byte idx))
            {
                Debug.LogWarning($"[Net] Cannot issue train: {def.name} missing from NetworkCatalog");
                return;
            }
            ushort reserved = GoblinNetRegistry.NextLocalIndex(owner);
            // Local-immediate: start production with the reserved index.
            GoblinProduction.TryStart(buildingOrigin, def, reserved);
            NetCommandBridge.Send(NetWireFormat.PackCmdTrainUnit(buildingOrigin.x, buildingOrigin.y, idx, owner, reserved));
        }
    }
}
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/NetCommandIssuer.cs Assets/Scripts/World/Unity/NetCommandIssuer.cs.meta
git commit -m "feat(net): NetCommandIssuer — local-immediate + send via Bridge"
```

---

## Task 12: NetworkManager dispatch + host-echo

**Files:**
- Modify: `Assets/Scripts/Lobby/NetworkManager.cs`

- [ ] **Step 1: Add a host-echo helper**

Inside the `NetworkManager` class, near `SendToAll`, add:

```csharp
/// <summary>Host-only: broadcast payload to all connected clients EXCEPT the given one.
/// Used to echo a command back out after the host receives it from a client.</summary>
public static void SendToOthers(byte[] payload, HSteamNetConnection except)
{
    if (_instance == null || payload == null || payload.Length == 0) return;
    _instance.SendToOthersImpl(payload, except);
}

private void SendToOthersImpl(byte[] payload, HSteamNetConnection except)
{
    if (!NetworkSession.IsHost) return;
    int flags = Constants.k_nSteamNetworkingSend_Reliable;
    var handle = System.Runtime.InteropServices.GCHandle.Alloc(
        payload, System.Runtime.InteropServices.GCHandleType.Pinned);
    try
    {
        var ptr = handle.AddrOfPinnedObject();
        foreach (var conn in _connections.Keys)
        {
            if (conn.Equals(except)) continue;
            var res = SteamNetworkingSockets.SendMessageToConnection(
                conn, ptr, (uint)payload.Length, flags, out _);
            if (res != EResult.k_EResultOK)
                Debug.LogWarning($"[Net] SendToOthers failed: {res}");
        }
    }
    finally { handle.Free(); }
}
```

- [ ] **Step 2: Plumb the sender connection through DispatchAndRelease**

Change `DispatchAndRelease` to also pass the originating connection:

```csharp
private void DispatchAndRelease(System.IntPtr ptr)
{
    var msg = System.Runtime.InteropServices.Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptr);
    var data = new byte[msg.m_cbSize];
    System.Runtime.InteropServices.Marshal.Copy(msg.m_pData, data, 0, msg.m_cbSize);
    var sender = msg.m_identityPeer.GetSteamID();
    var senderConn = msg.m_conn;
    RouteMessage(sender, data, senderConn);
    SteamNetworkingMessage_t.Release(ptr);
}
```

Change the existing `RouteMessage(CSteamID sender, byte[] payload)` signature to `RouteMessage(CSteamID sender, byte[] payload, HSteamNetConnection senderConn)`.

- [ ] **Step 3: Extend RouteMessage with the 5 new command types**

Inside the existing switch in `RouteMessage`, add cases. Replace the `default` arm so the new types fall through after dispatching + echoing:

```csharp
case NetMessageType.CmdMove:
case NetMessageType.CmdHarvest:
case NetMessageType.CmdBuildAssist:
case NetMessageType.CmdPlaceBuilding:
case NetMessageType.CmdTrainUnit:
    // Apply on this client.
    RTSCL.World.Unity.NetCommandApplier.Apply(payload, sender.m_SteamID);
    // Host: echo to all OTHER connected clients (so the rest of the lobby sees it).
    if (NetworkSession.IsHost)
        SendToOthers(payload, senderConn);
    break;
```

- [ ] **Step 4: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Lobby/NetworkManager.cs
git commit -m "feat(net): NetworkManager dispatches command messages + host-echoes to non-senders"
```

---

## Task 13: Wire the Bridge at scene start

**Files:**
- Modify: `Assets/Scripts/Lobby/GameStartLoader.cs`
- Modify: `Assets/Scripts/World/Unity/WorldGeneratorBootstrap.cs`

- [ ] **Step 1: Set OutgoingSender at game start**

In `GameStartLoader.LoadGameScene`, before `SceneManager.LoadScene("SampleScene")`, add:

```csharp
// Wire the command-bridge so world-side code can send packed payloads.
RTSCL.World.Unity.NetCommandBridge.OutgoingSender = NetworkManager.SendToAll;
```

- [ ] **Step 2: Reset registry + populate catalog when a new world is generated**

Open `Assets/Scripts/World/Unity/WorldGeneratorBootstrap.cs`. Find where it generates a world (where `CurrentWorld` is assigned). After that assignment, add:

```csharp
GoblinNetRegistry.Reset();
NetworkCatalog.PopulateFromCatalog(/* the BuildingCatalog reference — locate the existing SerializeField on this MonoBehaviour or pass via inspector */);
```

The implementer should grep the file for an existing `BuildingCatalog` SerializeField. If none exists in `WorldGeneratorBootstrap`, find `BuildingPlacer` or `MainBaseSetup` and wire it from there instead — pick whichever already holds a reference to `BuildingCatalog.asset`. The choice criterion: this code must run BEFORE any building is placed by `MainBaseSetup`.

If `MainBaseSetup._catalog` is the closest existing reference, put the catalog-populate + registry-reset at the top of `OnNewWorld` instead. Modify `Assets/Scripts/World/Unity/MainBaseSetup.cs` `OnNewWorld`:

```csharp
private void OnNewWorld(WorldData world)
{
    // Net-state reset must run BEFORE anything spawns.
    GoblinNetRegistry.Reset();
    NetworkCatalog.PopulateFromCatalog(_catalog);

    // ... rest of existing OnNewWorld body unchanged
}
```

Pick the location based on what already exists. Document the choice in the commit message.

- [ ] **Step 3: Hook NetCommandApplier external references**

`NetCommandApplier.Placer` and `NetCommandApplier.Spawner` need to be set so `ApplyPlaceBuilding` and other handlers find their MonoBehaviours.

Edit `MainBaseSetup.cs` to set them in `OnNewWorld` after the catalog populate:

```csharp
NetCommandApplier.Placer = _buildingPlacer;
NetCommandApplier.Spawner = _goblinSpawner;
```

- [ ] **Step 4: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Lobby/GameStartLoader.cs Assets/Scripts/World/Unity/MainBaseSetup.cs
git commit -m "feat(net): wire NetCommandBridge.OutgoingSender + populate NetworkCatalog + reset NetRegistry on new world"
```

---

## Task 14: Route Move / Harvest / Build-Assist through Issuer

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSelectionController.cs`

- [ ] **Step 1: Replace CommandFormation body**

Replace:

```csharp
private void CommandFormation(Vector3 worldCenter)
{
    int n = _selected.Count;
    int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
    int rows = Mathf.CeilToInt((float)n / cols);
    for (int i = 0; i < n; i++)
    {
        int col = i % cols;
        int row = i / cols;
        Vector3 offset = new(
            (col - (cols - 1) * 0.5f) * _formationSpacing,
            (row - (rows - 1) * 0.5f) * _formationSpacing, 0);
        _selected[i].SetMoveCommand(worldCenter + offset);
    }
}
```

With:

```csharp
private void CommandFormation(Vector3 worldCenter)
{
    NetCommandIssuer.IssueMove(_selected, worldCenter);
}
```

- [ ] **Step 2: Replace CommandHarvest worker dispatch**

Inside `CommandHarvest(Vector3Int clickedTree)`, after gathering workers + trees, the existing per-worker `SetHarvestCommand` loop becomes:

```csharp
// Group workers by their assigned tree, then issue one network command per tree.
var byTree = new Dictionary<Vector3Int, List<Goblin>>();
for (int i = 0; i < workers.Count; i++)
{
    var assigned = trees[i % trees.Count];
    if (!byTree.TryGetValue(assigned, out var list))
    { list = new List<Goblin>(); byTree[assigned] = list; }
    list.Add(workers[i]);
}
foreach (var kvp in byTree)
    NetCommandIssuer.IssueHarvest(kvp.Value, kvp.Key);
```

- [ ] **Step 3: Replace BuildAssist worker dispatch**

Find the foreach that calls `g.SetBuildCommand(buildOrigin)` in the right-click branch. Replace with:

```csharp
var workers = new List<Goblin>();
foreach (var g in _selected) if (IsWorker(g)) workers.Add(g);
if (workers.Count > 0) NetCommandIssuer.IssueBuildAssist(workers, buildOrigin);
```

- [ ] **Step 4: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSelectionController.cs
git commit -m "feat(net): selection controller routes move/harvest/build-assist through NetCommandIssuer"
```

---

## Task 15: Route Place Building through Issuer

**Files:**
- Modify: `Assets/Scripts/World/Unity/BuildingPlacer.cs`

- [ ] **Step 1: Change Place to call the Issuer**

Replace the existing `Place(Vector2Int origin)`:

```csharp
private void Place(Vector2Int origin)
{
    if (_selected == null) return;
    ulong owner = WorldStartContext.LocalPlayer;
    NetCommandIssuer.IssuePlaceBuilding(_selected, origin, owner);
    Cancel();
}
```

Note: `IssuePlaceBuilding` already calls `PlaceForce(..., charge: true, ..., owner)` locally on the issuer. The previous `_selected = null` after-place behavior is preserved via `Cancel()`.

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildingPlacer.cs
git commit -m "feat(net): BuildingPlacer.Place routes through NetCommandIssuer"
```

---

## Task 16: Route Train Unit through Issuer

**Files:**
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs`

- [ ] **Step 1: Replace OnUnitClicked production-start path**

Find `OnUnitClicked(GoblinUnitDefinition unit)`. Replace the existing wood-deduct + `GoblinProduction.TryStart` block with:

```csharp
private void OnUnitClicked(GoblinUnitDefinition unit)
{
    if (_selKind != SelKind.Building || _selDef == null) return;
    if (GoblinProduction.IsBusy(_selOrigin)) return;
    if (ResourceBank.Wood < unit.WoodCost) return;
    if (!PopulationManager.CanAfford(unit.PopulationCost)) return;

    // Wood/pop is local to this player (the building's owner).
    ResourceBank.AddWood(-unit.WoodCost);

    ulong owner = WorldStartContext.LocalPlayer;
    NetCommandIssuer.IssueTrainUnit(_selOrigin, unit, owner);

    Refresh();
}
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "feat(net): ObjectInspector routes train-unit through NetCommandIssuer with reserved NetId"
```

---

## Task 17: Final compile + host-alone smoke

**Files:** none changed.

- [ ] **Step 1: Full project refresh + console clear via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

Then `mcp__unity-mcp__Unity_ReadConsole` Types=["Error"]. Expected: 0 entries.

- [ ] **Step 2: Run RTSCL.World.Tests via TestRunnerApi**

Via Unity MCP, run all tests in `RTSCL.World.Tests` assembly. Expected: 32 pre-existing + 4 new GoblinNetId tests = 36 tests pass.

- [ ] **Step 3: Verify solo regression manually**

User runs the game in solo (Play Solo menu). Expected behavior unchanged:
- Keep at spawn[0] with 5 Farmers + 2 Clubs (own faction tint = none / white)
- Right-click a tree → Farmer harvests
- Right-click empty terrain → unit moves
- Place Hut → wood deducted, construction site appears, Farmer walks to build
- Train Farmer at Keep → progress bar, unit spawns

If anything breaks here, the wire path is being taken in solo (it should short-circuit because `OutgoingSender` is `null` when not in a lobby — but `NetCommandBridge.Send` does `OutgoingSender?.Invoke`, so the call is a no-op).

- [ ] **Step 4: Verify 2-client smoke (host + 1 client over Steam)**

Player A (host) and Player B both join a Steam lobby, host starts game. Smoke check:
- Both players see 2 keeps + per-team starting units.
- Player A moves a Farmer → Player B sees A's farmer move with ~50-150ms latency.
- Player A places a Hut → Player B sees the construction site with A's faction tint.
- Player A assigns a worker to build → Player B sees the worker walking + construction progress.
- Player A trains a Club at Barracks → progress bar appears on both clients, and when complete BOTH spawn a Club with the same NetId (same in-world position).
- Attempt: Player B clicks Player A's farmer → selection does nothing (filtered in #4). Right-click on A's unit doesn't issue any command (works because IsLocalOwner blocks selection).

If trained-unit drift is visible (different positions for the same trained Club), the `SpawnByKindAroundFootprint` ring-scan is not deterministic — log and follow up; that's a known #6 risk noted in the spec.

- [ ] **Step 5: Final commit (only if any smoke-test fix changes were needed)**

If no changes: no commit. If a deterministic-spawn fix is needed, capture in a separate commit:

```bash
git commit -m "fix(net): <specific fix>"
```

---

## Plan Self-Review Notes

- All 5 spec command types are covered (Tasks 7 / 8 / 9 / 11).
- All 9 modified files in the spec map to tasks (Tasks 5 / 6 / 10 / 12 / 13 / 14 / 15 / 16).
- All 5 new files in the spec map to tasks (Tasks 1 / 2 / 3 / 4 / 8 / 11). `NetWireFormat.cs` is an additional new file (Task 9 step 2) — surfaced during plan-writing because of the asmdef boundary, not in the original spec.
- Asmdef boundary: `RTSCL.World.Unity` does not reference `Assembly-CSharp` (where Lobby+Steamworks live). `NetCommandApplier` reads payloads via `NetWireFormat` (in the same asmdef). The Lobby's `NetMessages` becomes a thin wrapper delegating to `NetWireFormat` for the 5 new types.
- Solo path is unchanged because `NetCommandBridge.OutgoingSender` stays null in solo (never set).
- TDD where possible: Task 1 has unit tests (pure struct in `RTSCL.World`); other tasks verify via Unity MCP compile + manual smoke (per CLAUDE.md "Unity-bound code is not automatically tested").
- Type consistency: `GoblinNetId(ulong, ushort)`, `WireNetIdLocal(ulong, ushort)`, `NetCommandApplier.Apply(byte[], ulong)`, `GoblinProduction.TryStart(Vector2Int, GoblinUnitDefinition, ushort)` consistent across all referencing tasks.

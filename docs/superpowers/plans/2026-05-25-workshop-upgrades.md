# Workshop + Unit Upgrades Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Workshop building that sells 3 per-player upgrades; sync purchases via the existing NetCommand wire; remove the 2 test Club Goblins from MainBaseSetup.

**Architecture:** Upgrade ScriptableObject + per-owner static `PlayerUpgrades` tracker. Effects applied to both existing owned units AND future-spawned ones (via GoblinSpawner hook). Purchase syncs via new `CmdPurchaseUpgrade` message (type 9), reusing the local-immediate + host-echo pattern.

**Tech Stack:** Unity 6 / C# / Steamworks.NET / RTSCL.World.Unity asmdef

**Spec:** `docs/superpowers/specs/2026-05-25-workshop-upgrades-design.md`

---

## Task 1: UpgradeKind enum + UpgradeDefinition ScriptableObject

**Files:**
- Create: `Assets/Scripts/World/Unity/UpgradeKind.cs`
- Create: `Assets/Scripts/World/Unity/UpgradeDefinition.cs`

- [ ] **Step 1: Create UpgradeKind.cs**

```csharp
namespace RTSCL.World.Unity
{
    public enum UpgradeKind : byte
    {
        FarmerHarvestSpeed = 0,
        ClubAttackDamage = 1,
        ClubMaxHp = 2,
    }
}
```

- [ ] **Step 2: Create UpgradeDefinition.cs**

```csharp
using UnityEngine;

namespace RTSCL.World.Unity
{
    [CreateAssetMenu(menuName = "RTSCL/Upgrade Definition", fileName = "Upgrade")]
    public sealed class UpgradeDefinition : ScriptableObject
    {
        public string DisplayName;
        public Sprite Icon;
        public int WoodCost = 100;
        public UpgradeKind Kind;
    }
}
```

- [ ] **Step 3: Refresh + compile check via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

`Unity_ReadConsole` Types=["Error"]. Expect 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/UpgradeKind.cs Assets/Scripts/World/Unity/UpgradeKind.cs.meta Assets/Scripts/World/Unity/UpgradeDefinition.cs Assets/Scripts/World/Unity/UpgradeDefinition.cs.meta
git commit -m "feat(upgrade): UpgradeKind enum + UpgradeDefinition ScriptableObject"
```

---

## Task 2: PlayerUpgrades tracker

**Files:**
- Create: `Assets/Scripts/World/Unity/PlayerUpgrades.cs`

- [ ] **Step 1: Create PlayerUpgrades.cs**

```csharp
using System.Collections.Generic;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks one-time-purchase upgrades per owner. Synced via CmdPurchaseUpgrade.</summary>
    public static class PlayerUpgrades
    {
        private static readonly HashSet<(ulong owner, UpgradeKind kind)> _purchased = new();

        public static bool IsPurchased(ulong owner, UpgradeKind kind) =>
            _purchased.Contains((owner, kind));

        public static void MarkPurchased(ulong owner, UpgradeKind kind) =>
            _purchased.Add((owner, kind));

        public static void Reset() => _purchased.Clear();

        public static IEnumerable<UpgradeKind> AllFor(ulong owner)
        {
            foreach (var (o, k) in _purchased)
                if (o == owner) yield return k;
        }
    }
}
```

- [ ] **Step 2: Refresh + compile check via Unity MCP**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/PlayerUpgrades.cs Assets/Scripts/World/Unity/PlayerUpgrades.cs.meta
git commit -m "feat(upgrade): PlayerUpgrades per-owner tracker"
```

---

## Task 3: Goblin per-instance HarvestSpeedMul

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

The current code uses `private const float ChopTickDuration = 2.0f;` and references it in the chop-tick check. We replace the const with a per-instance field driven by `HarvestSpeedMul`.

- [ ] **Step 1: Read the current ChopTickDuration usage in Goblin.cs**

It's used in the `State.Harvesting` switch case where `_harvestTimer >= ChopTickDuration` triggers HitTree.

- [ ] **Step 2: Replace the const + add the public mul property**

Find:
```csharp
private const float ChopTickDuration = 2.0f;   // hit every 2s
```

Replace with:
```csharp
private float _chopTickDuration = 2.0f;
public float HarvestSpeedMul { get; private set; } = 1f;
public void SetHarvestSpeedMul(float mul)
{
    HarvestSpeedMul = Mathf.Max(0.01f, mul);
    _chopTickDuration = 2.0f / HarvestSpeedMul;
}
```

- [ ] **Step 3: Replace the ChopTickDuration reference**

In the `State.Harvesting` case, replace:
```csharp
if (_harvestTimer >= ChopTickDuration)
```

With:
```csharp
if (_harvestTimer >= _chopTickDuration)
```

- [ ] **Step 4: Refresh + compile check**

Expect 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(upgrade): Goblin per-instance ChopTickDuration + HarvestSpeedMul"
```

---

## Task 4: UpgradeEffects helper

**Files:**
- Create: `Assets/Scripts/World/Unity/UpgradeEffects.cs`

- [ ] **Step 1: Create UpgradeEffects.cs**

```csharp
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Applies upgrade effects to existing or newly-spawned units.
    /// Constants live here so Issuer and Applier share identical logic.</summary>
    public static class UpgradeEffects
    {
        public const float FarmerHarvestSpeedMul = 1.25f;
        public const float ClubAttackDamageMul = 1.25f;
        public const float ClubMaxHpMul = 1.5f;

        /// <summary>Apply a single upgrade to all currently-living units owned by `owner`.
        /// Called once at purchase time.</summary>
        public static void ApplyToOwnedUnits(ulong owner, UpgradeKind kind)
        {
            foreach (var g in Goblin.All)
            {
                if (g == null || g.Owner != owner) continue;
                ApplyOne(g, kind);
            }
        }

        /// <summary>Apply ALL previously-purchased upgrades for this goblin's owner to it.
        /// Called by GoblinSpawner after a fresh spawn so back-applied upgrades stick.</summary>
        public static void ApplyExistingTo(Goblin g)
        {
            if (g == null) return;
            foreach (var kind in PlayerUpgrades.AllFor(g.Owner))
                ApplyOne(g, kind);
        }

        private static void ApplyOne(Goblin g, UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.FarmerHarvestSpeed:
                    if (g.Kind == "FarmerGoblin")
                        g.SetHarvestSpeedMul(FarmerHarvestSpeedMul);
                    break;
                case UpgradeKind.ClubAttackDamage:
                    if (g.Kind == "ClubGoblin")
                        g.SetAttackDamage(Mathf.RoundToInt(g.AttackDamage * ClubAttackDamageMul));
                    break;
                case UpgradeKind.ClubMaxHp:
                    if (g.Kind == "ClubGoblin")
                    {
                        float ratio = g.MaxHp > 0 ? (float)g.CurrentHp / g.MaxHp : 1f;
                        int newMax = Mathf.RoundToInt(g.MaxHp * ClubMaxHpMul);
                        g.SetMaxHp(newMax, Mathf.RoundToInt(newMax * ratio));
                    }
                    break;
            }
        }
    }
}
```

- [ ] **Step 2: Hold off compile — Goblin doesn't have SetAttackDamage / SetMaxHp yet**

Expected errors: `SetAttackDamage`, `SetMaxHp` not found on Goblin. Task 5 adds those.

- [ ] **Step 3: Don't commit yet — Task 5 commits together.**

---

## Task 5: Goblin SetAttackDamage + SetMaxHp setters

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`

- [ ] **Step 1: Add public setters near the existing properties**

Find the property block in Goblin.cs (around line 22-27):
```csharp
public int  CurrentHp { get; private set; }
public int  MaxHp { get; private set; } = 20;
public int  PopulationCost { get; private set; } = 1;
public int  AttackDamage { get; private set; }
public float AttackInterval { get; private set; } = 1.5f;
public int  AttackRange { get; private set; } = 1;
```

Right after this block, add:

```csharp
public void SetAttackDamage(int newDamage) => AttackDamage = Mathf.Max(0, newDamage);

public void SetMaxHp(int newMax, int newCurrent)
{
    MaxHp = Mathf.Max(1, newMax);
    CurrentHp = Mathf.Clamp(newCurrent, 0, MaxHp);
}
```

- [ ] **Step 2: Refresh + compile check**

Expect 0 errors now (Task 4's UpgradeEffects.cs should now compile).

- [ ] **Step 3: Commit Tasks 4 + 5 together**

```bash
git add Assets/Scripts/World/Unity/UpgradeEffects.cs Assets/Scripts/World/Unity/UpgradeEffects.cs.meta Assets/Scripts/World/Unity/Goblin.cs
git commit -m "feat(upgrade): UpgradeEffects helper + Goblin SetAttackDamage / SetMaxHp"
```

---

## Task 6: BuildingDefinition.ProvidesUpgrades field

**Files:**
- Modify: `Assets/Scripts/World/Unity/BuildingDefinition.cs`

- [ ] **Step 1: Read the current BuildingDefinition.cs**

It currently has fields: DisplayName, Sprite, Footprint, WoodCost, PopulationProvided, TrainsUnits (GoblinUnitDefinition[]).

- [ ] **Step 2: Add ProvidesUpgrades field**

After `public GoblinUnitDefinition[] TrainsUnits;`, add:

```csharp
public UpgradeDefinition[] ProvidesUpgrades;
```

- [ ] **Step 3: Refresh + compile check**

Expect 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildingDefinition.cs
git commit -m "feat(upgrade): BuildingDefinition.ProvidesUpgrades field"
```

---

## Task 7: GoblinSpawner applies existing upgrades on spawn

**Files:**
- Modify: `Assets/Scripts/World/Unity/GoblinSpawner.cs`

- [ ] **Step 1: Add UpgradeEffects.ApplyExistingTo call after SetOwner**

Find the current SpawnAt method body where `goblin.Init(...)` + `goblin.SetOwner(owner)` are called. After SetOwner, add:

```csharp
UpgradeEffects.ApplyExistingTo(goblin);
```

The full sequence becomes:
```csharp
var goblin = go.AddComponent<Goblin>();
goblin.Init(netId, kind.Name, kind.WalkFrames, _terrainMap, _decorationMap, kind.Definition);
goblin.SetOwner(owner);
UpgradeEffects.ApplyExistingTo(goblin);
return goblin;
```

- [ ] **Step 2: Refresh + compile check**

Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/GoblinSpawner.cs
git commit -m "feat(upgrade): GoblinSpawner back-applies purchased upgrades after spawn"
```

---

## Task 8: Wire format + Issuer + Applier

**Files:**
- Modify: `Assets/Scripts/Lobby/NetMessages.cs`
- Modify: `Assets/Scripts/World/Unity/NetWireFormat.cs`
- Modify: `Assets/Scripts/World/Unity/NetCommandIssuer.cs`
- Modify: `Assets/Scripts/World/Unity/NetCommandApplier.cs`
- Modify: `Assets/Scripts/Lobby/NetworkManager.cs`

- [ ] **Step 1: Add `CmdPurchaseUpgrade = 9` to NetMessageType enum**

In `NetMessages.cs`, extend:

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
    CmdPurchaseUpgrade = 9,
}
```

- [ ] **Step 2: Add constant + Pack in NetWireFormat.cs**

In the constants block, append:
```csharp
public const byte CmdPurchaseUpgrade = 9;
```

After `PackEvDamage`, add:
```csharp
public static byte[] PackCmdPurchaseUpgrade(byte upgradeKind, ulong owner)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write(CmdPurchaseUpgrade);
    w.Write(upgradeKind);
    w.Write(owner);
    return ms.ToArray();
}
```

Payload: 1 + 1 + 8 = 10 bytes.

- [ ] **Step 3: Add IssuePurchaseUpgrade in NetCommandIssuer.cs**

After `IssueDamage`:

```csharp
public static void IssuePurchaseUpgrade(UpgradeKind kind, ulong owner)
{
    if (PlayerUpgrades.IsPurchased(owner, kind)) return;

    // Local-immediate: mark + apply to all owned units.
    PlayerUpgrades.MarkPurchased(owner, kind);
    UpgradeEffects.ApplyToOwnedUnits(owner, kind);

    NetCommandBridge.Send(NetWireFormat.PackCmdPurchaseUpgrade((byte)kind, owner));
}
```

- [ ] **Step 4: Add ApplyPurchaseUpgrade + dispatcher case in NetCommandApplier.cs**

After `ApplyDamage`:

```csharp
public static void ApplyPurchaseUpgrade(UpgradeKind kind, ulong owner, ulong sender)
{
    if (sender != 0UL && owner != sender)
    {
        Debug.LogWarning($"[Net] ApplyPurchaseUpgrade dropped: owner {owner} != sender {sender}");
        return;
    }
    if (PlayerUpgrades.IsPurchased(owner, kind)) return;
    PlayerUpgrades.MarkPurchased(owner, kind);
    UpgradeEffects.ApplyToOwnedUnits(owner, kind);
}
```

In the `Apply(byte[], ulong)` dispatcher switch, after the `EvDamage` case, add:

```csharp
case NetWireFormat.CmdPurchaseUpgrade:
    if (payload.Length >= 10)
    {
        using var ums = new System.IO.MemoryStream(payload, 1, payload.Length - 1);
        using var ur = new System.IO.BinaryReader(ums);
        byte ukind = ur.ReadByte();
        ulong uown = ur.ReadUInt64();
        ApplyPurchaseUpgrade((UpgradeKind)ukind, uown, sender);
    }
    break;
```

- [ ] **Step 5: Extend NetworkManager fallthrough case-block**

In `RouteMessage`, add `CmdPurchaseUpgrade` to the existing fallthrough:

```csharp
case NetMessageType.CmdMove:
case NetMessageType.CmdHarvest:
case NetMessageType.CmdBuildAssist:
case NetMessageType.CmdPlaceBuilding:
case NetMessageType.CmdTrainUnit:
case NetMessageType.CmdAttack:
case NetMessageType.EvDamage:
case NetMessageType.CmdPurchaseUpgrade:
    RTSCL.World.Unity.NetCommandApplier.Apply(payload, sender.m_SteamID);
    if (NetworkSession.IsHost)
        SendToOthers(payload, senderConn);
    break;
```

- [ ] **Step 6: Refresh + compile check**

Expect 0 errors.

- [ ] **Step 7: Reset PlayerUpgrades on new world**

In `MainBaseSetup.OnNewWorld`, right next to the existing `GoblinNetRegistry.Reset();` line, add:

```csharp
PlayerUpgrades.Reset();
```

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Lobby/NetMessages.cs Assets/Scripts/World/Unity/NetWireFormat.cs Assets/Scripts/World/Unity/NetCommandIssuer.cs Assets/Scripts/World/Unity/NetCommandApplier.cs Assets/Scripts/Lobby/NetworkManager.cs Assets/Scripts/World/Unity/MainBaseSetup.cs
git commit -m "feat(upgrade): wire-sync purchase via CmdPurchaseUpgrade (type 9) + reset on new world"
```

---

## Task 9: ObjectInspector — upgrade cards

**Files:**
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs`

The current `ShowBuilding` builds unit cards from `def.TrainsUnits`. We need to also handle `def.ProvidesUpgrades`.

- [ ] **Step 1: Extend CardRefs struct**

Find the `CardRefs` struct (private nested) and add a field:

```csharp
public UpgradeDefinition Upgrade;
public UpgradeKind UpgradeKind;
```

- [ ] **Step 2: Update ShowBuilding to build upgrade cards**

Find the section in `ShowBuilding` that currently does:
```csharp
if (isLocal && def.TrainsUnits != null && def.TrainsUnits.Length > 0)
    BuildUnitCards(def.TrainsUnits);
else
    ClearCards();
```

Replace with:
```csharp
if (isLocal && def.TrainsUnits != null && def.TrainsUnits.Length > 0)
    BuildUnitCards(def.TrainsUnits);
else if (isLocal && def.ProvidesUpgrades != null && def.ProvidesUpgrades.Length > 0)
    BuildUpgradeCards(def.ProvidesUpgrades);
else
    ClearCards();
```

- [ ] **Step 3: Add BuildUpgradeCards method**

Below `BuildBuildingCards`:

```csharp
private void BuildUpgradeCards(UpgradeDefinition[] upgrades)
{
    ClearCards();
    if (_unitCardsContainer == null) return;
    _unitCardsContainer.gameObject.SetActive(true);
    foreach (var u in upgrades)
    {
        if (u == null) continue;
        var c = CreateCard(u.DisplayName, u.Icon, u.WoodCost, () => OnUpgradeClicked(u));
        c.Upgrade = u;
        c.UpgradeKind = u.Kind;
        _cards.Add(c);
    }
}
```

- [ ] **Step 4: Add OnUpgradeClicked handler**

After `OnBuildingClicked`:

```csharp
private void OnUpgradeClicked(UpgradeDefinition upgrade)
{
    if (upgrade == null) return;
    ulong owner = WorldStartContext.LocalPlayer;
    if (PlayerUpgrades.IsPurchased(owner, upgrade.Kind)) return;
    if (ResourceBank.Wood < upgrade.WoodCost) return;

    ResourceBank.AddWood(-upgrade.WoodCost);
    NetCommandIssuer.IssuePurchaseUpgrade(upgrade.Kind, owner);
    Refresh();
}
```

- [ ] **Step 5: Update Refresh to disable already-purchased upgrade cards**

Inside the existing `Refresh()` method, modify the per-card loop. Current:

```csharp
foreach (var card in _cards)
{
    bool affordable = ResourceBank.Wood >= card.WoodCost;
    bool popOk = card.Unit == null || PopulationManager.CanAfford(card.Unit.PopulationCost);
    bool enabled = affordable && popOk && !busy;
    ...
}
```

Change `bool enabled` line and add the upgrade-purchased check:

```csharp
foreach (var card in _cards)
{
    bool affordable = ResourceBank.Wood >= card.WoodCost;
    bool popOk = card.Unit == null || PopulationManager.CanAfford(card.Unit.PopulationCost);
    bool alreadyOwned = card.Upgrade != null
        && PlayerUpgrades.IsPurchased(WorldStartContext.LocalPlayer, card.UpgradeKind);
    bool enabled = affordable && popOk && !busy && !alreadyOwned;
    ...
}
```

The rest of the loop (button.interactable, colors, etc.) stays the same.

- [ ] **Step 6: Refresh + compile check**

Expect 0 errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "feat(upgrade): ObjectInspector upgrade cards + purchase routing"
```

---

## Task 10: Workshop + 3 UpgradeDefinition assets

**Files:**
- Create: `Assets/Generated/Upgrades/Upgrade_FarmerHarvest.asset` + .meta
- Create: `Assets/Generated/Upgrades/Upgrade_ClubDamage.asset` + .meta
- Create: `Assets/Generated/Upgrades/Upgrade_ClubHp.asset` + .meta
- Create: `Assets/Generated/Buildings/Workshop.asset` + .meta
- Modify: `Assets/Generated/BuildingCatalog.asset`

Best approach: do this via Unity MCP `Unity_RunCommand` so AssetDatabase generates proper GUIDs.

- [ ] **Step 1: Read existing UpgradeDefinition + BuildingDefinition script GUIDs**

The implementer needs the m_Script GUID for UpgradeDefinition (which Unity assigns when the script is first imported). Check `Assets/Scripts/World/Unity/UpgradeDefinition.cs.meta`. The GUID after `guid:` is the value to use in the new asset's `m_Script` line.

Same for `BuildingDefinition.cs.meta` (this GUID already exists in `Buildings/Hut.asset` — find it via the existing Hut asset's m_Script line).

- [ ] **Step 2: Find Farmer + Club walk-frame Sprite GUIDs to use as icons**

The icons reuse Goblin walk frames. Find the existing GoblinUnitDefinition assets and read their `Icon:` field GUIDs. If those don't exist or look bad, fall back to:

- Farmer icon = Farmer walk frame 0 (find via existing Farmer GoblinUnitDefinition)
- Club icon = Club walk frame 0 (find via existing Club GoblinUnitDefinition)

The implementer should grep for `Icon:` in `Assets/Generated/Units/*.asset` to find the GoblinUnitDefinition that already references those sprites, then use the same Sprite GUID + fileID.

- [ ] **Step 3: Use Unity MCP to create the 3 UpgradeDefinition assets**

```csharp
using UnityEditor;
using UnityEngine;
using RTSCL.World.Unity;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Generated/Upgrades"))
            AssetDatabase.CreateFolder("Assets/Generated", "Upgrades");

        var farmerSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MiniWorldSprites/Characters/SoldiersAndCivilians.png"); // placeholder
        // Implementer note: use the actual Farmer/Club icon paths found in Step 2.
        // Replace these LoadAssetAtPath calls with the correct paths + sub-sprite addressing.

        var u1 = ScriptableObject.CreateInstance<UpgradeDefinition>();
        u1.DisplayName = "Sharper Tools";
        u1.WoodCost = 200;
        u1.Kind = UpgradeKind.FarmerHarvestSpeed;
        AssetDatabase.CreateAsset(u1, "Assets/Generated/Upgrades/Upgrade_FarmerHarvest.asset");

        var u2 = ScriptableObject.CreateInstance<UpgradeDefinition>();
        u2.DisplayName = "Heavier Clubs";
        u2.WoodCost = 300;
        u2.Kind = UpgradeKind.ClubAttackDamage;
        AssetDatabase.CreateAsset(u2, "Assets/Generated/Upgrades/Upgrade_ClubDamage.asset");

        var u3 = ScriptableObject.CreateInstance<UpgradeDefinition>();
        u3.DisplayName = "Tough Hide";
        u3.WoodCost = 400;
        u3.Kind = UpgradeKind.ClubMaxHp;
        AssetDatabase.CreateAsset(u3, "Assets/Generated/Upgrades/Upgrade_ClubHp.asset");

        AssetDatabase.SaveAssets();
        result.Log("Created 3 UpgradeDefinition assets");
    }
}
```

After creating, set the `Icon` field on each via `SerializedObject` (since CreateInstance doesn't accept Sprite refs cleanly). Implementer should do a second `Unity_RunCommand` that LoadAssetAtPath's each upgrade asset and assigns Icon via `SerializedObject.FindProperty("Icon")` and `objectReferenceValue = <sprite>`, then `ApplyModifiedProperties` + `SaveAssets`.

- [ ] **Step 4: Use Unity MCP to create Workshop.asset**

```csharp
using UnityEditor;
using UnityEngine;
using RTSCL.World.Unity;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var sprite = AssetDatabase.LoadAllAssetRepresentationsAtPath(
            "Assets/MiniWorldSprites/Buildings/Wood/Workshops.png");
        Sprite workshop0 = null;
        foreach (var s in sprite)
            if (s is Sprite sp && sp.name == "Workshops_0") { workshop0 = sp; break; }

        var def = ScriptableObject.CreateInstance<BuildingDefinition>();
        def.DisplayName = "Workshop";
        def.Sprite = workshop0;
        def.Footprint = new Vector2Int(1, 1);
        def.WoodCost = 800;
        def.PopulationProvided = 0;
        def.TrainsUnits = new GoblinUnitDefinition[0];

        var u1 = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>("Assets/Generated/Upgrades/Upgrade_FarmerHarvest.asset");
        var u2 = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>("Assets/Generated/Upgrades/Upgrade_ClubDamage.asset");
        var u3 = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>("Assets/Generated/Upgrades/Upgrade_ClubHp.asset");
        def.ProvidesUpgrades = new[] { u1, u2, u3 };

        AssetDatabase.CreateAsset(def, "Assets/Generated/Buildings/Workshop.asset");
        AssetDatabase.SaveAssets();
        result.Log($"Workshop asset created with sprite {workshop0?.name}");
    }
}
```

- [ ] **Step 5: Add Workshop to BuildingCatalog.asset**

```csharp
using UnityEditor;
using RTSCL.World.Unity;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var cat = AssetDatabase.LoadAssetAtPath<BuildingCatalog>("Assets/Generated/BuildingCatalog.asset");
        var workshop = AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Generated/Buildings/Workshop.asset");
        if (cat == null || workshop == null) { result.Log("Missing asset"); return; }
        if (!cat.Buildings.Contains(workshop)) cat.Buildings.Add(workshop);
        EditorUtility.SetDirty(cat);
        AssetDatabase.SaveAssets();
        result.Log("Workshop added to catalog");
    }
}
```

- [ ] **Step 6: Verify assets exist + refresh compile clean**

`Unity_ReadConsole` Types=["Error"]. Expect 0.

- [ ] **Step 7: Commit**

```bash
git add Assets/Generated/Upgrades/ Assets/Generated/Buildings/Workshop.asset Assets/Generated/Buildings/Workshop.asset.meta Assets/Generated/BuildingCatalog.asset
git commit -m "feat(upgrade): Workshop building + 3 UpgradeDefinition assets in catalog"
```

---

## Task 11: Remove starting test Clubs

**Files:**
- Modify: `Assets/Scripts/World/Unity/MainBaseSetup.cs`
- Modify: `Assets/Scenes/SampleScene.unity`

- [ ] **Step 1: Change MainBaseSetup default**

Find:
```csharp
[SerializeField] private int _testStartingClubs = 2;
```

Change to:
```csharp
[SerializeField] private int _testStartingClubs = 0;
```

- [ ] **Step 2: Update the SampleScene serialized value**

The scene has the field serialized. Search SampleScene.unity for `_testStartingClubs:` and change the value to `0`.

- [ ] **Step 3: Refresh + compile check**

Expect 0 errors. The 2 test Clubs no longer spawn.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/MainBaseSetup.cs Assets/Scenes/SampleScene.unity
git commit -m "feat(upgrade): remove 2 starting test Club Goblins (combat-test crutch no longer needed)"
```

---

## Task 12: Final compile + smoke verification

**Files:** none changed.

- [ ] **Step 1: Full refresh + console check via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```

`Unity_ReadConsole` Types=["Error"] + FilterText="CS". Expect 0.

- [ ] **Step 2: Run RTSCL.World.Tests**

Expect 36/36 still pass (no test changes).

- [ ] **Step 3: Solo smoke**

User runs Play Solo:
- No test Clubs spawn (only 5 Farmers + Keep)
- Build palette has 3 entries: Hut / Barracks / Workshop
- Build Workshop (800 wood): Farmer constructs it
- Click Workshop → 3 upgrade cards: "Sharper Tools" (200) / "Heavier Clubs" (300) / "Tough Hide" (400)
- Buy Sharper Tools → wood drops by 200, card greys out + stays disabled
- Have a Farmer harvest a tree → harvest tick visibly faster (~1.6s instead of 2.0s)
- Build Barracks, train a Club, buy Heavier Clubs + Tough Hide → train another Club → both clubs should now have the boosted stats (verify by inspecting in Inspector or via combat)

- [ ] **Step 4: 2-client smoke (host + 1 over Steam)**

- Both players see Workshop in palette + buildable for 800 wood
- Player A buys Sharper Tools → Player A's Farmers harvest faster on both clients
- Player B's Farmers unaffected on both clients
- Card greys on Player A's UI, still active on Player B's UI

- [ ] **Step 5: No commit unless a fix was needed**

If smoke passes: no further commits.

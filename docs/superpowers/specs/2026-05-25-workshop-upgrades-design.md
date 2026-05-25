# Workshop + Unit Upgrades — Design

**Date:** 2026-05-25

## Goal

Add a Workshop building that sells 3 per-player upgrades. Remove the 2 test Clubs from game start (combat-test crutch no longer needed).

## Building

- `Workshop_0` BuildingDefinition asset
- DisplayName: "Workshop"
- Sprite: `Workshops_0` from `Assets/MiniWorldSprites/Buildings/Wood/Workshops.png`
- Footprint: 1×1
- WoodCost: 800
- PopulationProvided: 0
- TrainsUnits: [] (production cards stay empty)
- **ProvidesUpgrades:** 3 UpgradeDefinition assets (new field)
- Added to `BuildingCatalog.asset` (alongside Hut + Barracks)

## Upgrades

3 one-time-purchase-per-player upgrades:

| # | DisplayName | WoodCost | Effect | UpgradeKind enum |
|---|---|---|---|---|
| 1 | "Sharper Tools" | 200 | Farmer ChopTickDuration × 0.8 (≈+25% harvest speed) | `FarmerHarvestSpeed` |
| 2 | "Heavier Clubs" | 300 | Club AttackDamage × 1.25 | `ClubAttackDamage` |
| 3 | "Tough Hide" | 400 | Club MaxHp × 1.5 (CurrentHp ratio-preserved) | `ClubMaxHp` |

**Icons:** reuse existing Goblin Walk-Frame[0] sprites — Farmer-walk frame for upgrade 1, Club-walk frame for upgrades 2 + 3 (placeholders, easy to swap later).

## Per-Player Upgrade Tracking

New `PlayerUpgrades` static class in `RTSCL.World.Unity`:

```csharp
public static class PlayerUpgrades
{
    private static readonly HashSet<(ulong owner, UpgradeKind kind)> _purchased = new();
    public static bool IsPurchased(ulong owner, UpgradeKind kind);
    public static void MarkPurchased(ulong owner, UpgradeKind kind);
    public static void Reset(); // called on new world
    public static IEnumerable<UpgradeKind> AllFor(ulong owner);
}
```

## Effect Application

### Existing units (purchase time)

When a player purchases an upgrade, iterate `Goblin.All` filtered by `(Owner == buyerOwner)` and apply:

- `FarmerHarvestSpeed`: only Farmers → `goblin.HarvestSpeedMul = 1.25f`
- `ClubAttackDamage`: only Clubs → `goblin.AttackDamage = Mathf.RoundToInt(goblin.AttackDamage * 1.25f)`
- `ClubMaxHp`: only Clubs → ratio-preserve `CurrentHp`, then `MaxHp = Mathf.RoundToInt(MaxHp * 1.5f)`, then `CurrentHp = Mathf.RoundToInt(MaxHp * ratio)`

### Future-spawned units (after purchase)

`GoblinSpawner.SpawnAt` after `Init`: query `PlayerUpgrades.AllFor(owner)` and apply each matching kind to the freshly spawned goblin.

### Per-Goblin field

Goblin gains `public float HarvestSpeedMul { get; private set; } = 1f;`

ChopTickDuration becomes per-instance: existing `private const float ChopTickDuration = 2.0f;` → `private float _chopTickDuration = 2.0f;`. The chop-tick check uses the instance field. Upgrade adjusts `_chopTickDuration = 2.0f / HarvestSpeedMul`.

`AttackDamage` and `MaxHp` are already per-instance properties — just mutate them.

## UI (ObjectInspector)

`ShowBuilding(def, origin)`:
- Existing: shows TrainsUnits as cards
- New: also shows ProvidesUpgrades as cards (below or instead of TrainsUnits)
- Workshop has empty TrainsUnits and non-empty ProvidesUpgrades → only upgrade cards show

`BuildUpgradeCards(UpgradeDefinition[])`:
- One card per upgrade, identical visual layout to BuildUnitCards
- Card shows: DisplayName + WoodCost text
- Disabled (greyed) if:
  - Wood < cost
  - Already purchased by the local player (`PlayerUpgrades.IsPurchased(LocalPlayer, upgrade.Kind)`)
  - Not local-owned building (existing isLocal filter from sub-project #4)

`OnUpgradeClicked(UpgradeDefinition upgrade)`:
- Affordability + ownership re-check
- `ResourceBank.AddWood(-cost)` locally
- `NetCommandIssuer.IssuePurchaseUpgrade(upgrade.Kind, LocalPlayer)`

## Network Sync

New message: `CmdPurchaseUpgrade = 9` (NetMessageType + NetWireFormat).

Payload: `[byte type][byte upgradeKind][ulong owner]` = 10 bytes total.

`NetCommandIssuer.IssuePurchaseUpgrade(UpgradeKind, ulong owner)`:
- Locally: `PlayerUpgrades.MarkPurchased(owner, kind)` + apply effect to all owned units
- Sends CmdPurchaseUpgrade

`NetCommandApplier.ApplyPurchaseUpgrade(kind, owner, sender)`:
- Owner-validation: `payload.owner == sender` else drop
- `PlayerUpgrades.MarkPurchased(owner, kind)`
- Apply effect to all `Goblin.All` matching owner

Refactored into a shared internal helper `ApplyUpgradeToOwnedUnits(ulong owner, UpgradeKind kind)` called from both Issuer + Applier.

## Remove Test Clubs

`MainBaseSetup` SerializeField `_testStartingClubs` is already exposed. Set the default to 0 in code (and verify the SampleScene serialized value too — likely 2 right now, override needed).

## Catalog Integration

`NetworkCatalog` already maps BuildingDefinition + GoblinUnitDefinition by index. Extend to also map UpgradeDefinition? Not strictly needed — we sync via the `UpgradeKind` enum byte directly. The catalog stays as-is.

## File Plan

### New files

| File | Purpose |
|---|---|
| `Assets/Scripts/World/Unity/UpgradeKind.cs` | enum (FarmerHarvestSpeed=0, ClubAttackDamage=1, ClubMaxHp=2) |
| `Assets/Scripts/World/Unity/UpgradeDefinition.cs` | ScriptableObject (DisplayName, Icon Sprite, WoodCost, Kind) |
| `Assets/Scripts/World/Unity/PlayerUpgrades.cs` | static per-owner tracker + Reset |
| `Assets/Scripts/World/Unity/UpgradeEffects.cs` | static `ApplyToOwnedUnits(owner, kind)` helper |
| `Assets/Generated/Upgrades/Upgrade_FarmerHarvest.asset` | UpgradeDefinition |
| `Assets/Generated/Upgrades/Upgrade_ClubDamage.asset` | UpgradeDefinition |
| `Assets/Generated/Upgrades/Upgrade_ClubHp.asset` | UpgradeDefinition |
| `Assets/Generated/Buildings/Workshop.asset` | BuildingDefinition (1×1, 800 wood, no train, 3 upgrades) |

### Modified files

| File | Change |
|---|---|
| `BuildingDefinition.cs` | Add `UpgradeDefinition[] ProvidesUpgrades` field |
| `BuildingCatalog.asset` | Append Workshop entry |
| `Goblin.cs` | `_chopTickDuration` per-instance + `HarvestSpeedMul` setter that updates it |
| `GoblinSpawner.cs` | After `Init` + `SetOwner`, call `UpgradeEffects.ApplyExistingTo(goblin)` to back-apply purchased upgrades |
| `ObjectInspector.cs` | `ShowBuilding`: handle ProvidesUpgrades + add `BuildUpgradeCards` + `OnUpgradeClicked` |
| `NetMessages.cs` | enum CmdPurchaseUpgrade = 9 |
| `NetWireFormat.cs` | constant + Pack |
| `NetCommandApplier.cs` | ApplyPurchaseUpgrade + dispatcher case |
| `NetCommandIssuer.cs` | IssuePurchaseUpgrade |
| `NetworkManager.cs` | Add CmdPurchaseUpgrade to existing fallthrough case-block |
| `MainBaseSetup.cs` | `_testStartingClubs` default → 0 in code |
| Scene `SampleScene.unity` | Update serialized `_testStartingClubs` to 0 |

## Solo Fallback

Wire-send is no-op when `OutgoingSender == null` → `IssuePurchaseUpgrade` applies locally + send is silent. Solo path unchanged.

## Testing

- Solo smoke: build Workshop (800 wood), click each upgrade card → wood deducted, effect visible (Farmer harvest faster, Clubs hit harder + tougher).
- 2-client smoke: Player A buys upgrade → Player A's units get the buff on both clients. Player B's units unaffected. Card greys out on Player A's UI but stays available on Player B's.

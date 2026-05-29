# Economy Balance + Resource-Node Info Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-tier costs (units=Food, buildings=Wood+Stone, upgrades=Iron/Gold/Crystal as the priciest tier), grant 100 Food + 100 Wood at start, and show proper names/descriptions + live remaining/max amount when clicking a resource node.

**Architecture:** Add `StoneCost` to BuildingDefinition and `IronCost/GoldCost/CrystalCost` to UpgradeDefinition; affordability/deduction reads the generic `ResourceBank.Get/Add(ResourceKind)`. ObjectInspector gains per-card cost fields + multi-resource cost strings + a resource-node info path (name/desc/amount, polled live). MainBaseSetup grants the starting baseline. Asset numbers applied via Unity MCP.

**Tech Stack:** Unity 6 / C# / RTSCL.World(.Unity) asmdefs

**Spec:** `docs/superpowers/specs/2026-05-28-economy-balance-design.md`

---

## File Structure

### Modified
| File | Change |
|---|---|
| `BuildingDefinition.cs` | `+ int StoneCost` |
| `UpgradeDefinition.cs` | `+ int IronCost/GoldCost/CrystalCost` |
| `BuildingPlacer.cs` | wood+stone affordability (IsValid) + deduct (PlaceForce charge) |
| `Goblin.cs` | `MaxHpFor` → public static |
| `ObjectInspector.cs` | CardRefs ore/stone fields; FormatBuildingCost/FormatUpgradeCost; Refresh affordability per card type; OnBuildingClicked stone check; OnUpgradeClicked ore check+deduct; build-card cost strings; resource-node name/desc/amount display + live poll |
| `MainBaseSetup.cs` | grant 100 Wood + 100 Food after Reset |
| `Units/ClubGoblin.asset`, `Buildings/Huts_0,Barracks_0,Workshop`, `Upgrades/*` | balance numbers (Unity MCP) |

---

## Task 1: New cost fields (BuildingDefinition + UpgradeDefinition)

**Files:**
- Modify: `Assets/Scripts/World/Unity/BuildingDefinition.cs`
- Modify: `Assets/Scripts/World/Unity/UpgradeDefinition.cs`

- [ ] **Step 1: Add StoneCost to BuildingDefinition**

Find:
```csharp
        public int WoodCost = 50;
```
Right after it, add:
```csharp
        public int StoneCost = 0;
```

- [ ] **Step 2: Add ore costs to UpgradeDefinition**

In `UpgradeDefinition.cs` find:
```csharp
        public int WoodCost = 100;
        public UpgradeKind Kind;
```
Replace with:
```csharp
        public int WoodCost = 0;
        public int IronCost = 0;
        public int GoldCost = 0;
        public int CrystalCost = 0;
        public UpgradeKind Kind;
```

- [ ] **Step 3: Refresh + compile check via Unity MCP**

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("Refreshed"); }
}
```
`Unity_ReadConsole` Types=["Error"] → 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildingDefinition.cs Assets/Scripts/World/Unity/UpgradeDefinition.cs
git commit -m "feat(econ): BuildingDefinition.StoneCost + UpgradeDefinition ore costs"
```

---

## Task 2: BuildingPlacer wood+stone affordability + deduct

**Files:**
- Modify: `Assets/Scripts/World/Unity/BuildingPlacer.cs`

- [ ] **Step 1: Wood+Stone in IsValid affordability**

Find:
```csharp
            // Affordability
            if (_selected.WoodCost > 0 && ResourceBank.Wood < _selected.WoodCost) return false;
```
Replace with:
```csharp
            // Affordability (wood + stone)
            if (_selected.WoodCost > 0 && ResourceBank.Wood < _selected.WoodCost) return false;
            if (_selected.StoneCost > 0 && ResourceBank.Get(ResourceKind.Stone) < _selected.StoneCost) return false;
```

- [ ] **Step 2: Deduct wood+stone in PlaceForce**

Find:
```csharp
            if (charge && def.WoodCost > 0)
            {
                if (ResourceBank.Wood < def.WoodCost) return;
                ResourceBank.AddWood(-def.WoodCost);
            }
```
Replace with:
```csharp
            if (charge)
            {
                if (ResourceBank.Wood < def.WoodCost) return;
                if (ResourceBank.Get(ResourceKind.Stone) < def.StoneCost) return;
                if (def.WoodCost > 0) ResourceBank.AddWood(-def.WoodCost);
                if (def.StoneCost > 0) ResourceBank.Add(ResourceKind.Stone, -def.StoneCost);
            }
```

- [ ] **Step 3: Refresh + compile check**

`Unity_ReadConsole` Types=["Error"] → 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Unity/BuildingPlacer.cs
git commit -m "feat(econ): BuildingPlacer requires + deducts Wood and Stone"
```

---

## Task 3: ObjectInspector cost wiring (cards, formats, affordability, click handlers)

**Files:**
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs`

- [ ] **Step 1: Extend CardRefs**

Find:
```csharp
            public int WoodCost;
            public int FoodCost;
            public UpgradeDefinition Upgrade;
```
Replace with:
```csharp
            public int WoodCost;
            public int FoodCost;
            public int StoneCost;
            public int IronCost;
            public int GoldCost;
            public int CrystalCost;
            public UpgradeDefinition Upgrade;
```

- [ ] **Step 2: Add cost-format helpers**

After the existing `FormatUnitCost` method, add:
```csharp
        private static string FormatBuildingCost(int wood, int stone)
        {
            if (wood > 0 && stone > 0) return $"{wood} Wood, {stone} Stone";
            if (wood > 0) return $"{wood} Wood";
            if (stone > 0) return $"{stone} Stone";
            return "Free";
        }

        private static string FormatUpgradeCost(int iron, int gold, int crystal)
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (iron > 0) parts.Add($"{iron} Iron");
            if (gold > 0) parts.Add($"{gold} Gold");
            if (crystal > 0) parts.Add($"{crystal} Crystal");
            return parts.Count == 0 ? "Free" : string.Join(", ", parts);
        }
```

- [ ] **Step 3: BuildBuildingCards — cost string + StoneCost**

Find:
```csharp
                var c = CreateCard(b.DisplayName, b.Sprite, $"{b.WoodCost} Wood", () => OnBuildingClicked(b));
                c.Building = b;
                c.WoodCost = b.WoodCost;
                _cards.Add(c);
```
Replace with:
```csharp
                var c = CreateCard(b.DisplayName, b.Sprite, FormatBuildingCost(b.WoodCost, b.StoneCost), () => OnBuildingClicked(b));
                c.Building = b;
                c.WoodCost = b.WoodCost;
                c.StoneCost = b.StoneCost;
                _cards.Add(c);
```

- [ ] **Step 4: BuildUpgradeCards — cost string + ore costs**

Find:
```csharp
                var c = CreateCard(u.DisplayName, u.Icon, $"{u.WoodCost} Wood", () => OnUpgradeClicked(u));
                c.Upgrade = u;
                c.UpgradeKind = u.Kind;
                c.WoodCost = u.WoodCost;
                _cards.Add(c);
```
Replace with:
```csharp
                var c = CreateCard(u.DisplayName, u.Icon, FormatUpgradeCost(u.IronCost, u.GoldCost, u.CrystalCost), () => OnUpgradeClicked(u));
                c.Upgrade = u;
                c.UpgradeKind = u.Kind;
                c.IronCost = u.IronCost;
                c.GoldCost = u.GoldCost;
                c.CrystalCost = u.CrystalCost;
                _cards.Add(c);
```

- [ ] **Step 5: Refresh affordability per card type**

Find:
```csharp
                bool affordable = ResourceBank.Wood >= card.WoodCost
                               && ResourceBank.Food >= card.FoodCost;
                bool popOk = card.Unit == null || PopulationManager.CanAfford(card.Unit.PopulationCost);
                bool alreadyOwned = card.Upgrade != null
                    && PlayerUpgrades.IsPurchased(WorldStartContext.LocalPlayer, card.UpgradeKind);
                bool enabled = affordable && popOk && !busy && !alreadyOwned;
```
Replace with:
```csharp
                bool affordable = card.Upgrade != null
                    ? ResourceBank.Get(ResourceKind.Iron) >= card.IronCost
                      && ResourceBank.Get(ResourceKind.Gold) >= card.GoldCost
                      && ResourceBank.Get(ResourceKind.Crystal) >= card.CrystalCost
                    : ResourceBank.Wood >= card.WoodCost
                      && ResourceBank.Food >= card.FoodCost
                      && ResourceBank.Get(ResourceKind.Stone) >= card.StoneCost;
                bool popOk = card.Unit == null || PopulationManager.CanAfford(card.Unit.PopulationCost);
                bool alreadyOwned = card.Upgrade != null
                    && PlayerUpgrades.IsPurchased(WorldStartContext.LocalPlayer, card.UpgradeKind);
                bool enabled = affordable && popOk && !busy && !alreadyOwned;
```

- [ ] **Step 6: OnBuildingClicked — wood+stone check**

Find:
```csharp
        private void OnBuildingClicked(BuildingDefinition def)
        {
            if (_placer == null || def == null) return;
            if (ResourceBank.Wood < def.WoodCost) return;
            _placer.Select(def);
        }
```
Replace with:
```csharp
        private void OnBuildingClicked(BuildingDefinition def)
        {
            if (_placer == null || def == null) return;
            if (ResourceBank.Wood < def.WoodCost) return;
            if (ResourceBank.Get(ResourceKind.Stone) < def.StoneCost) return;
            _placer.Select(def);
        }
```

- [ ] **Step 7: OnUpgradeClicked — ore check + deduct**

Find:
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
Replace with:
```csharp
        private void OnUpgradeClicked(UpgradeDefinition upgrade)
        {
            if (upgrade == null) return;
            ulong owner = WorldStartContext.LocalPlayer;
            if (PlayerUpgrades.IsPurchased(owner, upgrade.Kind)) return;
            if (ResourceBank.Get(ResourceKind.Iron) < upgrade.IronCost) return;
            if (ResourceBank.Get(ResourceKind.Gold) < upgrade.GoldCost) return;
            if (ResourceBank.Get(ResourceKind.Crystal) < upgrade.CrystalCost) return;

            if (upgrade.IronCost > 0) ResourceBank.Add(ResourceKind.Iron, -upgrade.IronCost);
            if (upgrade.GoldCost > 0) ResourceBank.Add(ResourceKind.Gold, -upgrade.GoldCost);
            if (upgrade.CrystalCost > 0) ResourceBank.Add(ResourceKind.Crystal, -upgrade.CrystalCost);
            NetCommandIssuer.IssuePurchaseUpgrade(upgrade.Kind, owner);
            Refresh();
        }
```

- [ ] **Step 8: Subscribe Refresh to all resource changes already covered**

`Start` already does `ResourceBank.OnChanged += (_, __) => Refresh();` (covers Stone/Iron/Gold/Crystal too). No change needed — verify the line exists.

- [ ] **Step 9: Refresh + compile check**

`Unity_ReadConsole` Types=["Error"] → 0.

- [ ] **Step 10: Commit**

```bash
git add Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "feat(econ): ObjectInspector multi-resource costs (building wood+stone, upgrade ores)"
```

---

## Task 4: Resource-node info display (names/desc/amount, live)

**Files:**
- Modify: `Assets/Scripts/World/Unity/Goblin.cs`
- Modify: `Assets/Scripts/World/Unity/ObjectInspector.cs`

- [ ] **Step 1: Make Goblin.MaxHpFor public static**

In `Goblin.cs` find:
```csharp
        private static int MaxHpFor(string tileName)
```
Replace `private` with `public`:
```csharp
        public static int MaxHpFor(string tileName)
```

- [ ] **Step 2: Add decoration-selection fields to ObjectInspector**

Find:
```csharp
        private string _lastCarryLine = "";
        private string _lastGoblinDescBase = "";
```
Right after, add:
```csharp
        private Vector3Int _selDecoCell;
        private bool _selDecoHarvestable;
        private string _selDecoBaseDesc = "";
        private string _lastAmountLine = "";
```

- [ ] **Step 3: Replace the decoration-click branch in Update**

Find:
```csharp
            if (_decorationMap != null)
            {
                var deco = _decorationMap.GetTile(cell);
                if (deco != null)
                {
                    _selKind = SelKind.Decoration;
                    _selDef = null;
                    ShowSimple(deco.name, DescribeDecoration(deco.name));
                    return;
                }
            }
```
Replace with:
```csharp
            if (_decorationMap != null)
            {
                var deco = _decorationMap.GetTile(cell);
                if (deco != null)
                {
                    _selKind = SelKind.Decoration;
                    _selDef = null;
                    ShowResourceNode(cell, deco.name);
                    return;
                }
            }
```

- [ ] **Step 4: Add ShowResourceNode + name/desc helpers + amount line builder**

Replace the whole `DescribeDecoration` method with these three methods:
```csharp
        private void ShowResourceNode(Vector3Int cell, string tileName)
        {
            _selDecoCell = cell;
            _selDecoHarvestable = Goblin.IsHarvestable(tileName);
            _selDecoBaseDesc = DecorationDesc(tileName);
            _lastAmountLine = "";
            string desc = _selDecoBaseDesc;
            if (_selDecoHarvestable) desc += "\n" + AmountLine(cell, tileName);
            ShowSimple(DecorationName(tileName), desc);
        }

        private string AmountLine(Vector3Int cell, string tileName)
        {
            int max = Goblin.MaxHpFor(tileName);
            int cur = TreeHP.GetHP(cell, max);
            var kind = Goblin.KindOf(tileName);
            return $"{kind}: {cur} / {max}";
        }

        private static string DecorationName(string tileName)
        {
            if (tileName.StartsWith("Trees_")) return "Oak Tree";
            if (tileName.StartsWith("PineTrees_") || tileName.StartsWith("WinterTrees_")) return "Pine Tree";
            if (tileName.StartsWith("CoconutTrees_")) return "Palm Tree";
            if (tileName.StartsWith("DeadTrees_") || tileName.StartsWith("WinterDeadTrees_")) return "Dead Tree";
            if (tileName.StartsWith("Wheatfield_")) return "Wheat Field";
            if (tileName.StartsWith("Rocks_")) return "Stone Deposit";
            if (tileName.StartsWith("GoldOre_")) return "Gold Deposit";
            if (tileName.StartsWith("IronOre_")) return "Iron Deposit";
            if (tileName.StartsWith("CrystalOre_")) return "Crystal Deposit";
            if (tileName.StartsWith("Cactus_")) return "Cactus";
            if (tileName.StartsWith("Tumbleweed_")) return "Tumbleweed";
            int us = tileName.IndexOf('_');
            return us > 0 ? tileName[..us] : tileName;
        }

        private static string DecorationDesc(string tileName)
        {
            if (Goblin.IsTreeTile(tileName)) return "Chop for Wood";
            if (tileName.StartsWith("Wheatfield_")) return "Harvest for Food";
            if (tileName.StartsWith("Rocks_")) return "Mine for Stone";
            if (tileName.StartsWith("GoldOre_")) return "Mine for Gold";
            if (tileName.StartsWith("IronOre_")) return "Mine for Iron";
            if (tileName.StartsWith("CrystalOre_")) return "Mine for Crystal";
            return "Desert flora";
        }
```

- [ ] **Step 5: Poll the amount live in Update**

Find (in `Update`, after the building/goblin polling lines):
```csharp
            if (_selKind == SelKind.Building) UpdateProgressUI();
            if (_selKind == SelKind.Goblins) UpdateCarryUI();
```
Right after, add:
```csharp
            if (_selKind == SelKind.Decoration) UpdateResourceAmount();
```

- [ ] **Step 6: Add UpdateResourceAmount**

After `UpdateCarryUI`, add:
```csharp
        private void UpdateResourceAmount()
        {
            if (!_selDecoHarvestable || _decorationMap == null) return;
            var tile = _decorationMap.GetTile(_selDecoCell);
            if (tile == null) { Hide(); return; } // node depleted/removed
            string newAmount = AmountLine(_selDecoCell, tile.name);
            if (newAmount == _lastAmountLine) return;
            _lastAmountLine = newAmount;
            if (_descriptionLabel != null)
                _descriptionLabel.text = _selDecoBaseDesc + "\n" + newAmount;
        }
```

- [ ] **Step 7: Refresh + compile check**

`Unity_ReadConsole` Types=["Error"] → 0. (`TreeHP`, `Goblin.IsHarvestable`/`IsTreeTile`/`KindOf`/`MaxHpFor` are all in RTSCL.World.Unity — same namespace.)

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/World/Unity/Goblin.cs Assets/Scripts/World/Unity/ObjectInspector.cs
git commit -m "feat(info): resource-node names/descriptions + live remaining/max amount"
```

---

## Task 5: Starting resources (100 Wood + 100 Food)

**Files:**
- Modify: `Assets/Scripts/World/Unity/MainBaseSetup.cs`

- [ ] **Step 1: Grant baseline after Reset**

In `OnNewWorld`, find:
```csharp
            WorldGrid.Width = world.Width;
            WorldGrid.Height = world.Height;
```
Right after, add:
```csharp
            // Starting baseline resources for the local player.
            ResourceBank.Add(ResourceKind.Wood, 100);
            ResourceBank.Add(ResourceKind.Food, 100);
```

(`ResourceBank.Reset()` earlier in `OnNewWorld` zeroed everything, so this sets exactly 100/100.)

- [ ] **Step 2: Refresh + compile check**

`Unity_ReadConsole` Types=["Error"] → 0.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Unity/MainBaseSetup.cs
git commit -m "feat(econ): start with 100 Wood + 100 Food"
```

---

## Task 6: Apply balance numbers to assets (Unity MCP)

**Files:**
- Modify: `Assets/Generated/Units/ClubGoblin.asset`, `Assets/Generated/Buildings/Huts_0.asset`, `Barracks_0.asset`, `Workshop.asset`, `Assets/Generated/Upgrades/*.asset`

- [ ] **Step 1: Set all asset costs via Unity MCP**

```csharp
using UnityEditor;
using UnityEngine;
using RTSCL.World.Unity;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        // Units — Food only (Farmer already 0 wood / 50 food).
        var club = AssetDatabase.LoadAssetAtPath<GoblinUnitDefinition>("Assets/Generated/Units/ClubGoblin.asset");
        if (club != null) { club.WoodCost = 0; club.FoodCost = 100; EditorUtility.SetDirty(club); }

        // Buildings — Wood + Stone.
        void Bld(string path, int wood, int stone)
        {
            var b = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
            if (b == null) { result.LogError($"missing {path}"); return; }
            b.WoodCost = wood; b.StoneCost = stone; EditorUtility.SetDirty(b);
        }
        Bld("Assets/Generated/Buildings/Huts_0.asset", 60, 20);
        Bld("Assets/Generated/Buildings/Barracks_0.asset", 120, 60);
        Bld("Assets/Generated/Buildings/Workshop.asset", 150, 100);

        // Upgrades — Iron/Gold/Crystal (wood 0).
        void Upg(string path, int iron, int gold, int crystal)
        {
            var u = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(path);
            if (u == null) { result.LogError($"missing {path}"); return; }
            u.WoodCost = 0; u.IronCost = iron; u.GoldCost = gold; u.CrystalCost = crystal;
            EditorUtility.SetDirty(u);
        }
        Upg("Assets/Generated/Upgrades/Upgrade_FarmerHarvest.asset", 200, 0, 0);
        Upg("Assets/Generated/Upgrades/Upgrade_ClubDamage.asset", 200, 150, 0);
        Upg("Assets/Generated/Upgrades/Upgrade_ClubHp.asset", 0, 200, 150);

        AssetDatabase.SaveAssets();
        result.Log("Balance numbers applied: Club 100food; Hut 60/20, Barracks 120/60, Workshop 150/100; upgrades 200I / 200I+150G / 200G+150C");
    }
}
```

- [ ] **Step 2: Verify console — expect the summary log, 0 errors**

`Unity_ReadConsole` Types=["Error"] → 0.

- [ ] **Step 3: Commit**

```bash
git add Assets/Generated/Units/ClubGoblin.asset Assets/Generated/Buildings/Huts_0.asset Assets/Generated/Buildings/Barracks_0.asset Assets/Generated/Buildings/Workshop.asset Assets/Generated/Upgrades/Upgrade_FarmerHarvest.asset Assets/Generated/Upgrades/Upgrade_ClubDamage.asset Assets/Generated/Upgrades/Upgrade_ClubHp.asset
git commit -m "feat(econ): balance numbers — units food, buildings wood+stone, upgrades ore"
```

---

## Task 7: Final compile + smoke verification

**Files:** none changed.

- [ ] **Step 1: Full refresh + console check via Unity MCP**

`Unity_ReadConsole` Types=["Error"] + FilterText="CS". Expect 0.

- [ ] **Step 2: Run RTSCL.World.Tests**

TestRunnerApi + Temp file callback. Expect 42/42 pass.

- [ ] **Step 3: Solo smoke (user manually)**

- Start: Food 100, Wood 100; Stone/Iron/Gold/Crystal 0.
- Train Farmer → −50 Food; Club → −100 Food (no wood). Cards grey when food short.
- Build Hut → needs 60 Wood + 20 Stone; greyed until both; deducts both. Barracks 120/60, Workshop 150/100.
- Workshop upgrades show ore costs; Tough Hide (200 Gold + 150 Crystal) needs heavy mining; purchase deducts the ores.
- Click a tree → "Oak/Pine/Palm/Dead Tree" + "Chop for Wood" + "Wood: X / 50". Rock → "Stone Deposit" + "Mine for Stone" + "Stone: X / 100". Ore → matching name + "/ 200". Wheat → "Wheat Field" + "Food: X / 500".
- Amount ticks down live while a farmer harvests the selected node; panel hides when depleted.
- Cactus/tumbleweed → name + "Desert flora", no amount.

- [ ] **Step 4: No commit unless a fix was needed**

---

## Plan Self-Review Notes

**Spec coverage:**
- StoneCost / ore-cost fields (Task 1)
- Building wood+stone affordability+deduct (Task 2)
- Inspector cost strings + per-type affordability + click handlers (Task 3)
- Resource-node names/desc + live amount + MaxHpFor public (Task 4)
- Starting 100/100 (Task 5)
- Balance numbers on assets (Task 6)

**Placeholder scan:** none.

**Type consistency:**
- `BuildingDefinition.StoneCost`, `UpgradeDefinition.IronCost/GoldCost/CrystalCost` (Task 1) → read in Task 2 (placer), Task 3 (cards/affordability/clicks), Task 6 (assets).
- `ResourceBank.Get(ResourceKind)` / `Add(ResourceKind,int)` (existing) used for Stone/Iron/Gold/Crystal everywhere.
- `CardRefs.{StoneCost,IronCost,GoldCost,CrystalCost}` (Task 3) consumed in Refresh (Task 3).
- `Goblin.MaxHpFor` public (Task 4) + existing public `IsHarvestable`/`IsTreeTile`/`KindOf` + `TreeHP.GetHP` used by Task 4 info display.
- `ResourceKind` enum values (Wood/Food/Stone/Iron/Gold/Crystal) — existing.

**Compile-clean per commit:** each task compiles independently (fields first, then consumers). Task 4 needs `MaxHpFor` public (same task). Task 6 needs Task 1 fields (ordered after). No interim broken state.

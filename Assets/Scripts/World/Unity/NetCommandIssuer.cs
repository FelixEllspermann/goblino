// NetCommandIssuer.cs — Public API for issuing player commands in both solo and multiplayer.
//
// PATTERN: every IssueX method:
//   1. Validates inputs (null checks, affordability guards already done by callers).
//   2. Applies the command locally immediately (local-immediate authority).
//   3. Serializes it via NetWireFormat and sends via NetCommandBridge.Send().
//      In solo, Send() is a no-op (OutgoingSender is null).
//
// Callers: GoblinSelectionController, BuildingPlacer, BuildingPaletteUI, Goblin (combat).
// Do NOT call Apply methods in NetCommandApplier from here — Apply is only for incoming remote messages.
//
// Formation note (IssueMove/ApplyMove): the grid offset formula must be identical here and in
// NetCommandApplier.ApplyMove so remotes see the same unit positions without sending per-unit coords.
using System.Collections.Generic;
using RTSCL.World;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-command Issue helpers. Issuer applies locally + serializes + sends via Bridge.
    /// Solo (NetCommandBridge.OutgoingSender == null): no wire send, just local application.</summary>
    public static class NetCommandIssuer
    {
        /// <summary>Moves a group of units to a world position, spreading them in a deterministic grid formation.
        /// Formation offsets are computed identically on all clients using the same formula — only the center
        /// target is sent over the wire.</summary>
        /// <param name="units">Units to move (only units owned by the local player should be passed).</param>
        /// <param name="worldTarget">Center of the desired formation in world-space.</param>
        public static void IssueMove(IReadOnlyList<Goblin> units, Vector3 worldTarget)
        {
            if (units == null || units.Count == 0) return;

            int n = units.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
            int rows = Mathf.CeilToInt((float)n / cols);
            const float spacing = 1.0f;

            // Build the wire NetId list and apply per-unit move locally with the
            // same deterministic formation offset that receivers will recompute.
            var wire = new List<NetWireFormat.WireNetIdLocal>(n);
            for (int i = 0; i < n; i++)
            {
                var g = units[i];
                if (g == null) continue;
                wire.Add(new NetWireFormat.WireNetIdLocal(g.NetId.Owner, g.NetId.LocalIndex));

                int col = i % cols;
                int row = i / cols;
                // Grid offset centered at worldTarget. Receivers apply the same formula in ApplyMove.
                Vector3 offset = new(
                    (col - (cols - 1) * 0.5f) * spacing,
                    (row - (rows - 1) * 0.5f) * spacing, 0f);
                g.SetMoveCommand(worldTarget + offset);
            }

            // Send center target — remotes apply the same formation formula deterministically.
            NetCommandBridge.Send(NetWireFormat.PackCmdMove(wire, worldTarget.x, worldTarget.y));
        }

        /// <summary>Commands a group of workers to harvest the tree at treeCell.
        /// Applies locally then broadcasts worker NetIds + tile coords.</summary>
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

        /// <summary>Commands a group of workers to assist construction at buildOrigin.
        /// Workers must already be assigned via Goblin.SetBuildCommand.</summary>
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

        /// <summary>Places a building for the local player: applies locally with charge=true (deducts wood),
        /// then broadcasts the placement. Remote clients apply with charge=false (no deduction).
        /// def must be registered in NetworkCatalog or the call is dropped with a warning.</summary>
        public static void IssuePlaceBuilding(BuildingDefinition def, Vector2Int origin, ulong owner)
        {
            if (def == null) return;
            if (!NetworkCatalog.TryGetBuildingIndex(def, out byte idx))
            {
                Debug.LogWarning($"[Net] Cannot issue place: {def.name} missing from NetworkCatalog");
                return;
            }
            // Issuer applies locally with charge=true so wood is deducted on the issuing client.
            if (NetCommandApplier.Placer != null)
                NetCommandApplier.Placer.PlaceForce(def, origin, charge: true, requireConstruction: true, owner: owner);
            NetCommandBridge.Send(NetWireFormat.PackCmdPlaceBuilding(idx, origin.x, origin.y, owner));
        }

        /// <summary>Starts training a unit at buildingOrigin.
        /// Pre-reserves a NetId index via GoblinNetRegistry.NextLocalIndex so both the local client and
        /// all remote clients spawn the new goblin with the same NetId (deterministic assignment).</summary>
        public static void IssueTrainUnit(Vector2Int buildingOrigin, GoblinUnitDefinition def, ulong owner)
        {
            if (def == null) return;
            if (!NetworkCatalog.TryGetUnitIndex(def, out byte idx))
            {
                Debug.LogWarning($"[Net] Cannot issue train: {def.name} missing from NetworkCatalog");
                return;
            }
            // Reserve the index BEFORE the local TryStart so the counter is bumped identically on all clients.
            ushort reserved = GoblinNetRegistry.NextLocalIndex(owner);
            // Local-immediate: start production with the reserved index.
            GoblinProduction.TryStart(buildingOrigin, def, reserved);
            NetCommandBridge.Send(NetWireFormat.PackCmdTrainUnit(buildingOrigin.x, buildingOrigin.y, idx, owner, reserved));
        }

        /// <summary>Orders attacker to attack target: applies SetAttackCommand locally, then broadcasts.
        /// Guards: attacker must have AttackDamage &gt; 0 and target must be alive.</summary>
        public static void IssueAttack(Goblin attacker, Goblin target)
        {
            if (attacker == null || target == null || attacker == target) return;
            if (attacker.AttackDamage <= 0) return;
            if (target.CurrentHp <= 0) return;

            attacker.SetAttackCommand(target);

            var wAtk = new NetWireFormat.WireNetIdLocal(attacker.NetId.Owner, attacker.NetId.LocalIndex);
            var wTgt = new NetWireFormat.WireNetIdLocal(target.NetId.Owner, target.NetId.LocalIndex);
            NetCommandBridge.Send(NetWireFormat.PackCmdAttack(wAtk, wTgt));
        }

        /// <summary>Applies damage locally (TakeDamage) and broadcasts EvDamage to all clients.
        /// Only called by Goblin.Update when IsLocalOwner is true — attacker-owner authoritative.
        /// Remote clients receive EvDamage and apply via NetCommandApplier.ApplyDamage.</summary>
        public static void IssueDamage(Goblin target, int damage, Goblin attacker)
        {
            if (target == null || attacker == null) return;
            if (damage <= 0) return;

            target.TakeDamage(damage, attacker);

            var wTgt = new NetWireFormat.WireNetIdLocal(target.NetId.Owner, target.NetId.LocalIndex);
            var wAtk = new NetWireFormat.WireNetIdLocal(attacker.NetId.Owner, attacker.NetId.LocalIndex);
            NetCommandBridge.Send(NetWireFormat.PackEvDamage(wTgt, damage, wAtk));
        }

        /// <summary>Purchases an upgrade for owner: marks it purchased + applies effects to all owned units,
        /// then broadcasts CmdPurchaseUpgrade. Guards against double-purchase.</summary>
        public static void IssuePurchaseUpgrade(UpgradeKind kind, ulong owner)
        {
            if (PlayerUpgrades.IsPurchased(owner, kind)) return;

            // Local-immediate: mark + apply to all owned units.
            PlayerUpgrades.MarkPurchased(owner, kind);
            UpgradeEffects.ApplyToOwnedUnits(owner, kind);

            NetCommandBridge.Send(NetWireFormat.PackCmdPurchaseUpgrade((byte)kind, owner));
        }
    }
}

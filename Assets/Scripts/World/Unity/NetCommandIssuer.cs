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
                Vector3 offset = new(
                    (col - (cols - 1) * 0.5f) * spacing,
                    (row - (rows - 1) * 0.5f) * spacing, 0f);
                g.SetMoveCommand(worldTarget + offset);
            }

            // Send center target — remotes apply the same formation formula deterministically.
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
            // Issuer applies locally with charge=true so wood is deducted on the issuing client.
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

        public static void IssueDamage(Goblin target, int damage, Goblin attacker)
        {
            if (target == null || attacker == null) return;
            if (damage <= 0) return;

            target.TakeDamage(damage, attacker);

            var wTgt = new NetWireFormat.WireNetIdLocal(target.NetId.Owner, target.NetId.LocalIndex);
            var wAtk = new NetWireFormat.WireNetIdLocal(attacker.NetId.Owner, attacker.NetId.LocalIndex);
            NetCommandBridge.Send(NetWireFormat.PackEvDamage(wTgt, damage, wAtk));
        }
    }
}

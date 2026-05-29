// NetCommandApplier.cs — Applies incoming network commands to local sim state.
// Called by NetworkManager (Assembly-CSharp) when a P2P message arrives, via Apply(byte[], ulong sender).
//
// OWNERSHIP VALIDATION: every Apply method checks that the sender SteamID matches the unit/building owner.
// Mismatches are logged and silently dropped (light anti-cheat). senderOwnerCheck == 0UL skips the check
// (used in solo play where sender is not set).
//
// REMOTE vs LOCAL: Apply* methods are pure "local simulation mutations" — they never call NetCommandBridge.Send.
// The issuer (NetCommandIssuer) handles both local-apply + wire-send for commands originating locally.
// Apply() is only called for REMOTE messages echoed back by the host via NetworkManager.RouteMessage.
//
// Placer and Spawner are set by MainBaseSetup.OnNewWorld at scene start; apply calls are no-ops if null.
//
// To add a new command:
//   1. Add a const byte in NetWireFormat + Pack method.
//   2. Add ApplyX here.
//   3. Add a dispatcher case in Apply().
//   4. Add IssueX in NetCommandIssuer.
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
        // Set by MainBaseSetup at scene start — required for PlaceForce and TryStart.
        public static BuildingPlacer Placer;
        public static GoblinSpawner Spawner;

        // ------------- Per-command local mutations -------------

        /// <summary>Applies a move command to a list of goblins using the same deterministic grid-formation
        /// formula used by IssueMove. senderOwnerCheck = 0 skips ownership validation (solo).</summary>
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
                // Anti-cheat: reject commands targeting units the sender doesn't own.
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

        /// <summary>Applies a harvest command: routes each goblin to harvest treeCell.
        /// Ownership validated per-unit against senderOwnerCheck.</summary>
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

        /// <summary>Applies a build-assist command: routes each worker to help construct at origin.
        /// Ownership validated per-unit against senderOwnerCheck.</summary>
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

        /// <summary>Applies a remote place-building: calls PlaceForce with charge=false (no wood deduction —
        /// the issuing client already charged itself). owner is the placing player's SteamID.</summary>
        public static void ApplyPlaceBuilding(BuildingDefinition def, Vector2Int origin, ulong owner)
        {
            if (def == null || Placer == null) return;
            // charge=false: remote clients do not deduct resources (issuer already did).
            Placer.PlaceForce(def, origin, charge: false, requireConstruction: true, owner: owner);
        }

        /// <summary>Starts training a unit on a remote client using the pre-reserved NetId index
        /// that was assigned by the issuer via GoblinNetRegistry.NextLocalIndex. This ensures all
        /// clients spawn the new goblin with the same NetId (deterministic identity).</summary>
        public static void ApplyTrainUnit(Vector2Int buildingOrigin, GoblinUnitDefinition def, ulong owner, ushort reservedLocalIndex)
        {
            if (def == null) return;
            GoblinProduction.TryStart(buildingOrigin, def, reservedLocalIndex);
        }

        /// <summary>Applies an attack command on a remote client: routes attacker to target.
        /// Validates that sender owns the attacker (anti-cheat).</summary>
        public static void ApplyAttack(GoblinNetId attackerId, GoblinNetId targetId, ulong sender)
        {
            if (!GoblinNetRegistry.TryGet(attackerId, out var attacker) || attacker == null)
            {
                Debug.LogWarning($"[Net] ApplyAttack dropped: attacker {attackerId} not found");
                return;
            }
            // Anti-cheat: only the attacker's owner may issue attack commands for it.
            if (sender != 0UL && attacker.NetId.Owner != sender)
            {
                Debug.LogWarning($"[Net] ApplyAttack dropped: sender {sender} != attacker.Owner {attacker.NetId.Owner}");
                return;
            }
            if (!GoblinNetRegistry.TryGet(targetId, out var target) || target == null) return;
            attacker.SetAttackCommand(target);
        }

        /// <summary>Applies received EvDamage to the target goblin. Only the attacker's owner sends EvDamage
        /// (Goblin.Update IsLocalOwner guard). Validates sender == attacker owner before applying.</summary>
        public static void ApplyDamage(GoblinNetId targetId, int damage, GoblinNetId attackerId, ulong sender)
        {
            // Anti-cheat: damage must come from the attacker's owner.
            if (sender != 0UL && attackerId.Owner != sender)
            {
                Debug.LogWarning($"[Net] ApplyDamage dropped: sender {sender} != attacker.Owner {attackerId.Owner}");
                return;
            }
            if (!GoblinNetRegistry.TryGet(targetId, out var target) || target == null) return;
            // Attacker may have died since the packet was sent — null is accepted by TakeDamage.
            GoblinNetRegistry.TryGet(attackerId, out var attacker);
            target.TakeDamage(damage, attacker);
        }

        /// <summary>Applies a received upgrade purchase for owner. Validates sender == owner.
        /// Guards against double-purchase (idempotent).</summary>
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

        // ------------- Top-level dispatcher -------------

        /// <summary>Deserializes and dispatches an incoming network message to the appropriate Apply method.
        /// Called by NetworkManager (Assembly-CSharp) for each received P2P packet.
        /// payload[0] is the message type byte (see NetWireFormat constants).
        /// sender is the SteamID of the connection that sent the packet (used for ownership validation).</summary>
        public static void Apply(byte[] payload, ulong sender)
        {
            if (payload == null || payload.Length == 0) return;
            switch (payload[0])
            {
                // CmdMove: [type:1][count:2][units:10*n][x:4f][y:4f]
                case NetWireFormat.CmdMove:
                    if (NetWireFormat.TryUnpackUnitsCmd(payload, NetWireFormat.CmdMove, 8, out var moveWire, out var mr))
                    {
                        float mx = mr.ReadSingle(); float my = mr.ReadSingle();
                        mr.Dispose();
                        ApplyMove(WireToNetIds(moveWire), new Vector3(mx, my, 0f), sender);
                    }
                    break;
                // CmdHarvest: [type:1][count:2][units:10*n][tileX:4][tileY:4]
                case NetWireFormat.CmdHarvest:
                    if (NetWireFormat.TryUnpackUnitsCmd(payload, NetWireFormat.CmdHarvest, 8, out var harvWire, out var hr))
                    {
                        int tx = hr.ReadInt32(); int ty = hr.ReadInt32();
                        hr.Dispose();
                        ApplyHarvest(WireToNetIds(harvWire), new Vector3Int(tx, ty, 0), sender);
                    }
                    break;
                // CmdBuildAssist: [type:1][count:2][units:10*n][originX:4][originY:4]
                case NetWireFormat.CmdBuildAssist:
                    if (NetWireFormat.TryUnpackUnitsCmd(payload, NetWireFormat.CmdBuildAssist, 8, out var bldWire, out var br))
                    {
                        int ox = br.ReadInt32(); int oy = br.ReadInt32();
                        br.Dispose();
                        ApplyBuildAssist(WireToNetIds(bldWire), new Vector2Int(ox, oy), sender);
                    }
                    break;
                // CmdPlaceBuilding: [type:1][defIdx:1][ox:4][oy:4][owner:8] = 18 bytes
                case NetWireFormat.CmdPlaceBuilding:
                    if (payload.Length >= 18)
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
                // CmdTrainUnit: [type:1][ox:4][oy:4][unitIdx:1][owner:8][reservedIdx:2] = 20 bytes
                case NetWireFormat.CmdTrainUnit:
                    if (payload.Length >= 20)
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
                // CmdAttack: [type:1][atkOwner:8][atkIdx:2][tgtOwner:8][tgtIdx:2] = 21 bytes
                case NetWireFormat.CmdAttack:
                    if (payload.Length >= 21)
                    {
                        using var ams = new System.IO.MemoryStream(payload, 1, payload.Length - 1);
                        using var ar = new System.IO.BinaryReader(ams);
                        ulong aOwn = ar.ReadUInt64(); ushort aIdx = ar.ReadUInt16();
                        ulong tOwn = ar.ReadUInt64(); ushort tIdx = ar.ReadUInt16();
                        ApplyAttack(new GoblinNetId(aOwn, aIdx), new GoblinNetId(tOwn, tIdx), sender);
                    }
                    break;
                // EvDamage: [type:1][tgtOwner:8][tgtIdx:2][damage:4][atkOwner:8][atkIdx:2] = 25 bytes
                case NetWireFormat.EvDamage:
                    if (payload.Length >= 25)
                    {
                        using var dms = new System.IO.MemoryStream(payload, 1, payload.Length - 1);
                        using var dr = new System.IO.BinaryReader(dms);
                        ulong tOwn = dr.ReadUInt64(); ushort tIdx = dr.ReadUInt16();
                        int dmg = dr.ReadInt32();
                        ulong aOwn = dr.ReadUInt64(); ushort aIdx = dr.ReadUInt16();
                        ApplyDamage(new GoblinNetId(tOwn, tIdx), dmg, new GoblinNetId(aOwn, aIdx), sender);
                    }
                    break;
                // CmdPurchaseUpgrade: [type:1][upgradeKind:1][owner:8] = 10 bytes
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
            }
        }

        /// <summary>Converts wire-format WireNetIdLocal list to GoblinNetId list for registry lookup.</summary>
        private static List<GoblinNetId> WireToNetIds(List<NetWireFormat.WireNetIdLocal> wire)
        {
            var ids = new List<GoblinNetId>(wire.Count);
            foreach (var w in wire) ids.Add(new GoblinNetId(w.Owner, w.LocalIndex));
            return ids;
        }
    }
}

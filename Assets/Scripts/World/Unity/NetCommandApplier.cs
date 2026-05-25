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

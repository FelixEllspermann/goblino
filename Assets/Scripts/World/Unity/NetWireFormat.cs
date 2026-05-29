// NetWireFormat.cs — Low-level byte-layout pack/unpack for all network command and event types.
// Lives in RTSCL.World.Unity (not Assembly-CSharp) so the asmdef boundary is preserved:
// no Steamworks dependency, just System.IO.BinaryWriter/Reader.
//
// WIRE PROTOCOL — all messages start with a 1-byte message type:
//   CmdMove          (2): [type:1][unitCount:2][units:10*n][x:4f][y:4f]           total = 3+8+10n
//   CmdHarvest       (3): [type:1][unitCount:2][units:10*n][tileX:4][tileY:4]     total = 3+8+10n
//   CmdBuildAssist   (4): [type:1][unitCount:2][units:10*n][originX:4][originY:4] total = 3+8+10n
//   CmdPlaceBuilding (5): [type:1][defIdx:1][originX:4][originY:4][owner:8]        total = 18
//   CmdTrainUnit     (6): [type:1][originX:4][originY:4][unitIdx:1][owner:8][reservedIdx:2] total = 20
//   CmdAttack        (7): [type:1][atkOwner:8][atkIdx:2][tgtOwner:8][tgtIdx:2]    total = 21
//   EvDamage         (8): [type:1][tgtOwner:8][tgtIdx:2][damage:4][atkOwner:8][atkIdx:2] total = 25
//   CmdPurchaseUpgrade(9):[type:1][upgradeKind:1][owner:8]                         total = 10
//
// Each "unit" in the units-list is a WireNetIdLocal: [owner:8 ulong][localIndex:2 ushort] = 10 bytes.
//
// Byte constants here MUST stay in sync with NetMessageType in Assets/Scripts/Lobby/NetMessages.cs.
// If you add a new command, add a constant, a Pack method, a dispatcher case in NetCommandApplier.Apply,
// an Apply method in NetCommandApplier, and an Issue method in NetCommandIssuer.
using System.Collections.Generic;
using System.IO;

namespace RTSCL.World.Unity
{
    /// <summary>Pack/unpack helpers for all 8 command/event types. Lives in World.Unity so
    /// the asmdef boundary is preserved (no Steamworks dependency). The byte constants
    /// here mirror RTSCL.Lobby.NetMessageType values.</summary>
    public static class NetWireFormat
    {
        // Message type byte constants — must match NetMessageType enum in Lobby/NetMessages.cs.
        public const byte CmdMove = 2;
        public const byte CmdHarvest = 3;
        public const byte CmdBuildAssist = 4;
        public const byte CmdPlaceBuilding = 5;
        public const byte CmdTrainUnit = 6;
        public const byte CmdAttack = 7;
        public const byte EvDamage = 8;
        public const byte CmdPurchaseUpgrade = 9;

        /// <summary>Wire-format unit identity: owner SteamID (8 bytes) + per-owner local index (2 bytes) = 10 bytes total.
        /// Maps 1:1 to GoblinNetId but avoids referencing that struct from pack/unpack helpers.</summary>
        public readonly struct WireNetIdLocal
        {
            public readonly ulong Owner;
            public readonly ushort LocalIndex;
            public WireNetIdLocal(ulong o, ushort i) { Owner = o; LocalIndex = i; }
        }

        // -------- Pack helpers --------
        // All return a freshly allocated byte[] ready to pass to NetCommandBridge.Send().

        /// <summary>Packs CmdMove: units list + float center-target (x, y).
        /// Layout: [2][count:2][units:10*n][x:4f][y:4f]</summary>
        public static byte[] PackCmdMove(IList<WireNetIdLocal> ids, float x, float y) =>
            PackUnitsCmd(CmdMove, ids, w => { w.Write(x); w.Write(y); });

        /// <summary>Packs CmdHarvest: units list + int tile cell (tx, ty).
        /// Layout: [3][count:2][units:10*n][tx:4][ty:4]</summary>
        public static byte[] PackCmdHarvest(IList<WireNetIdLocal> ids, int tx, int ty) =>
            PackUnitsCmd(CmdHarvest, ids, w => { w.Write(tx); w.Write(ty); });

        /// <summary>Packs CmdBuildAssist: units list + int building origin (ox, oy).
        /// Layout: [4][count:2][units:10*n][ox:4][oy:4]</summary>
        public static byte[] PackCmdBuildAssist(IList<WireNetIdLocal> ids, int ox, int oy) =>
            PackUnitsCmd(CmdBuildAssist, ids, w => { w.Write(ox); w.Write(oy); });

        /// <summary>Packs CmdPlaceBuilding: building catalog byte index + origin + owner SteamID.
        /// Layout: [5][defIdx:1][ox:4][oy:4][owner:8] = 18 bytes total.</summary>
        public static byte[] PackCmdPlaceBuilding(byte defIndex, int ox, int oy, ulong owner)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(CmdPlaceBuilding); w.Write(defIndex); w.Write(ox); w.Write(oy); w.Write(owner);
            return ms.ToArray();
        }

        /// <summary>Packs CmdTrainUnit: building origin + unit catalog byte index + owner + pre-reserved NetId index.
        /// Layout: [6][ox:4][oy:4][unitIdx:1][owner:8][reservedIdx:2] = 20 bytes total.
        /// reservedIndex is pre-reserved via GoblinNetRegistry.NextLocalIndex so all clients spawn with the same NetId.</summary>
        public static byte[] PackCmdTrainUnit(int ox, int oy, byte unitDefIndex, ulong owner, ushort reservedIndex)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(CmdTrainUnit); w.Write(ox); w.Write(oy); w.Write(unitDefIndex); w.Write(owner); w.Write(reservedIndex);
            return ms.ToArray();
        }

        /// <summary>Packs CmdAttack: attacker NetId + target NetId.
        /// Layout: [7][atkOwner:8][atkIdx:2][tgtOwner:8][tgtIdx:2] = 21 bytes total.</summary>
        public static byte[] PackCmdAttack(WireNetIdLocal attacker, WireNetIdLocal target)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(CmdAttack);
            w.Write(attacker.Owner); w.Write(attacker.LocalIndex);
            w.Write(target.Owner);   w.Write(target.LocalIndex);
            return ms.ToArray();
        }

        /// <summary>Packs EvDamage: target NetId + int damage + attacker NetId.
        /// Layout: [8][tgtOwner:8][tgtIdx:2][damage:4][atkOwner:8][atkIdx:2] = 25 bytes total.
        /// Only issued by the attacker-owner client (IsLocalOwner guard in Goblin.Update).</summary>
        public static byte[] PackEvDamage(WireNetIdLocal target, int damage, WireNetIdLocal attacker)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(EvDamage);
            w.Write(target.Owner);   w.Write(target.LocalIndex);
            w.Write(damage);
            w.Write(attacker.Owner); w.Write(attacker.LocalIndex);
            return ms.ToArray();
        }

        /// <summary>Packs CmdPurchaseUpgrade: UpgradeKind byte + owner SteamID.
        /// Layout: [9][upgradeKind:1][owner:8] = 10 bytes total.</summary>
        public static byte[] PackCmdPurchaseUpgrade(byte upgradeKind, ulong owner)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(CmdPurchaseUpgrade);
            w.Write(upgradeKind);
            w.Write(owner);
            return ms.ToArray();
        }

        /// <summary>Shared packer for the three unit-list commands (Move/Harvest/BuildAssist).
        /// Layout: [type:1][count:2 ushort][owner:8 ulong + localIdx:2 ushort per unit][tail written by writeTail].</summary>
        private static byte[] PackUnitsCmd(byte type, IList<WireNetIdLocal> ids, System.Action<BinaryWriter> writeTail)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(type);
            w.Write((ushort)(ids?.Count ?? 0));
            // Each unit entry: 8-byte owner SteamID + 2-byte local index = 10 bytes.
            if (ids != null) foreach (var id in ids) { w.Write(id.Owner); w.Write(id.LocalIndex); }
            writeTail(w);
            return ms.ToArray();
        }

        /// <summary>Begin unpacking a units-list command. Caller reads the trailing payload
        /// from the returned BinaryReader (must Dispose it). Returns false on a malformed/short payload.
        /// tailBytes = expected byte count after the unit list (e.g. 8 for two floats or two ints).</summary>
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
                // Each entry is 10 bytes: 8-byte owner ulong + 2-byte localIndex ushort.
                if (ms.Position + 10 > ms.Length) { r.Dispose(); ms.Dispose(); return false; }
                ids.Add(new WireNetIdLocal(r.ReadUInt64(), r.ReadUInt16()));
            }
            if (ms.Position + tailBytes > ms.Length) { r.Dispose(); ms.Dispose(); return false; }
            reader = r;
            return true;
        }
    }
}

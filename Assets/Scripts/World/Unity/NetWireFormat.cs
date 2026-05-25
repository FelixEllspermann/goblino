using System.Collections.Generic;
using System.IO;

namespace RTSCL.World.Unity
{
    /// <summary>Pack/unpack helpers for the 5 command types. Lives in World.Unity so
    /// the asmdef boundary is preserved (no Steamworks dependency). The byte constants
    /// here mirror RTSCL.Lobby.NetMessageType values.</summary>
    public static class NetWireFormat
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

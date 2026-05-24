using System.IO;

namespace RTSCL.Lobby
{
    public enum NetMessageType : byte
    {
        GameStart = 1,
    }

    /// <summary>Binary pack/unpack helpers for the wire protocol. Frame = [byte type | payload].</summary>
    public static class NetMessages
    {
        public static byte[] PackGameStart(int seed)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)NetMessageType.GameStart);
            w.Write(seed);
            return ms.ToArray();
        }

        public static bool TryUnpackGameStart(byte[] payload, out int seed)
        {
            seed = 0;
            if (payload == null || payload.Length < 5) return false;
            if (payload[0] != (byte)NetMessageType.GameStart) return false;
            using var ms = new MemoryStream(payload, 1, payload.Length - 1);
            using var r = new BinaryReader(ms);
            seed = r.ReadInt32();
            return true;
        }
    }
}

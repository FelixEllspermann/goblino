using System.Collections.Generic;
using System.IO;
using Steamworks;

namespace RTSCL.Lobby
{
    public enum NetMessageType : byte
    {
        GameStart = 1,
    }

    public sealed class PlayerSlot
    {
        public CSteamID SteamId;
        public byte SpawnIndex;
    }

    /// <summary>Binary pack/unpack helpers for the wire protocol. Frame = [byte type | payload].</summary>
    public static class NetMessages
    {
        public static byte[] PackGameStart(int seed, IList<PlayerSlot> slots)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)NetMessageType.GameStart);
            w.Write(seed);
            w.Write((byte)(slots?.Count ?? 0));
            if (slots != null)
            {
                foreach (var s in slots)
                {
                    w.Write(s.SteamId.m_SteamID);
                    w.Write(s.SpawnIndex);
                }
            }
            return ms.ToArray();
        }

        public static bool TryUnpackGameStart(byte[] payload, out int seed, out List<PlayerSlot> slots)
        {
            seed = 0; slots = null;
            if (payload == null || payload.Length < 6) return false;
            if (payload[0] != (byte)NetMessageType.GameStart) return false;
            using var ms = new MemoryStream(payload, 1, payload.Length - 1);
            using var r = new BinaryReader(ms);
            seed = r.ReadInt32();
            int n = r.ReadByte();
            slots = new List<PlayerSlot>(n);
            for (int i = 0; i < n; i++)
            {
                if (ms.Position + 9 > ms.Length) return false;
                slots.Add(new PlayerSlot { SteamId = new CSteamID(r.ReadUInt64()), SpawnIndex = r.ReadByte() });
            }
            return true;
        }
    }
}

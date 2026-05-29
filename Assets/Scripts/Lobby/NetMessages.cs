// NetMessages.cs
// Role: Wire-protocol definitions for the Assembly-CSharp (lobby/network) layer.
//       Defines NetMessageType (the leading byte of every frame), the PlayerSlot data
//       class used in the GameStart payload, and binary pack/unpack helpers for GameStart.
//
// Wire frame format: [byte messageType | ...payload bytes...]
//
// Message ownership by layer:
//   Assembly-CSharp (this file): GameStart (type 1) — packed/unpacked here.
//   RTSCL.World.Unity (NetWireFormat.cs): types 2-9 (CmdMove…CmdPurchaseUpgrade) —
//       packed/unpacked there so the world layer has no Steamworks dependency.
//   Assembly-CSharp passes those payloads through opaque (byte[]) without inspecting them.
//
// GameStart payload layout (after the leading type byte):
//   int32   seed           (4 bytes, little-endian)
//   byte    slotCount      (1 byte)
//   per slot × slotCount:
//     uint64 steamId       (8 bytes)
//     byte   spawnIndex    (1 byte)
//
// Where to adjust:
//   • Adding a new game-start parameter (e.g. map size): extend both PackGameStart and
//     TryUnpackGameStart — add writes after the slot loop on pack, reads after on unpack.
//   • Adding a new command/event message type: add an entry to NetMessageType, then
//     implement pack/unpack in NetWireFormat.cs (world side) or here if Assembly-CSharp owns it.
//   • Max slot count is implicitly 255 (byte field). Practical limit is 4 (lobby max-members).

using System.Collections.Generic;
using System.IO;
using Steamworks;

namespace RTSCL.Lobby
{
    /// <summary>Leading byte of every network frame. Drives routing in NetworkManager.RouteMessage
    /// and NetCommandApplier.Apply. Values must remain stable across builds — changing an
    /// assigned value is a wire-breaking change requiring a GameVersion bump.</summary>
    public enum NetMessageType : byte
    {
        GameStart = 1,
        CmdMove = 2,
        CmdHarvest = 3,
        CmdBuildAssist = 4,
        CmdPlaceBuilding = 5,
        CmdTrainUnit = 6,
        CmdAttack = 7,
        EvDamage = 8,
        CmdPurchaseUpgrade = 9,
    }

    /// <summary>Per-player spawn assignment broadcast in the GameStart payload.
    /// Host is always SpawnIndex 0; others are sorted by SteamID (see LobbyPanel.OnStart).</summary>
    public sealed class PlayerSlot
    {
        public CSteamID SteamId;
        public byte SpawnIndex;
    }

    /// <summary>Binary pack/unpack helpers for the wire protocol. Frame = [byte type | payload].</summary>
    public static class NetMessages
    {
        /// <summary>Serialises a GameStart message including the world seed and all player slot assignments.
        /// The host sends this to all clients via NetworkManager.SendToAll.</summary>
        public static byte[] PackGameStart(int seed, IList<PlayerSlot> slots)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)NetMessageType.GameStart);
            w.Write(seed);                          // 4 bytes, little-endian
            w.Write((byte)(slots?.Count ?? 0));     // slot count (1 byte, max 255)
            if (slots != null)
            {
                foreach (var s in slots)
                {
                    w.Write(s.SteamId.m_SteamID);  // 8 bytes
                    w.Write(s.SpawnIndex);           // 1 byte
                }
            }
            return ms.ToArray();
        }

        /// <summary>Deserialises a GameStart payload. Returns false if the buffer is too short,
        /// malformed, or has the wrong message type byte.</summary>
        public static bool TryUnpackGameStart(byte[] payload, out int seed, out List<PlayerSlot> slots)
        {
            seed = 0; slots = null;
            // Minimum valid size: type(1) + seed(4) + slotCount(1) = 6 bytes.
            if (payload == null || payload.Length < 6) return false;
            if (payload[0] != (byte)NetMessageType.GameStart) return false;
            // Skip the type byte; BinaryReader reads the rest.
            using var ms = new MemoryStream(payload, 1, payload.Length - 1);
            using var r = new BinaryReader(ms);
            seed = r.ReadInt32();
            int n = r.ReadByte();
            slots = new List<PlayerSlot>(n);
            for (int i = 0; i < n; i++)
            {
                // Each slot is 9 bytes (uint64 + byte). Guard against truncated payloads.
                if (ms.Position + 9 > ms.Length) return false;
                slots.Add(new PlayerSlot { SteamId = new CSteamID(r.ReadUInt64()), SpawnIndex = r.ReadByte() });
            }
            return true;
        }
    }
}

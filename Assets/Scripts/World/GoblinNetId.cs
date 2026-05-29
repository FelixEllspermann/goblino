// GoblinNetId.cs — Lightweight network identity for a single goblin unit.
// Lives in RTSCL.World (pure assembly) so it can be used in unit tests and referenced from
// RTSCL.World.Unity without pulling in Assembly-CSharp or Steamworks.
// The Owner field is a CSteamID value cast to ulong. LocalIndex is a per-owner counter
// incremented by GoblinNetRegistry.NextLocalIndex at spawn time, ensuring each unit
// gets a unique ID that matches across all clients.

using System;

namespace RTSCL.World
{
    /// <summary>Owner-scoped unit identifier. Same value on all clients for a given goblin.
    /// 10 bytes on the wire (8 byte Owner ulong + 2 byte LocalIndex ushort).
    /// Suitable as a Dictionary key; equality and hash are value-based.</summary>
    public readonly struct GoblinNetId : IEquatable<GoblinNetId>
    {
        /// <summary>Steam ID of the player who owns this unit (cast from CSteamID.m_SteamID).</summary>
        public readonly ulong Owner;
        /// <summary>Per-owner sequential counter assigned at spawn time; starts at 0 for each player.</summary>
        public readonly ushort LocalIndex;

        public GoblinNetId(ulong owner, ushort localIndex)
        {
            Owner = owner;
            LocalIndex = localIndex;
        }

        public bool Equals(GoblinNetId other) => Owner == other.Owner && LocalIndex == other.LocalIndex;
        public override bool Equals(object obj) => obj is GoblinNetId other && Equals(other);
        /// <summary>Hash combines Owner and LocalIndex with prime multiplication to reduce collisions
        /// in Dictionary buckets when many units share the same owner.</summary>
        public override int GetHashCode() => unchecked((Owner.GetHashCode() * 397) ^ LocalIndex);
        public override string ToString() => $"({Owner}:{LocalIndex})";

        public static bool operator ==(GoblinNetId a, GoblinNetId b) => a.Equals(b);
        public static bool operator !=(GoblinNetId a, GoblinNetId b) => !a.Equals(b);
    }
}

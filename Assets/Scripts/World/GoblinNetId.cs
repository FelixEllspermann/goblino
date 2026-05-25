using System;

namespace RTSCL.World
{
    /// <summary>Owner-scoped unit identifier. Same value on all clients for a given goblin.</summary>
    public readonly struct GoblinNetId : IEquatable<GoblinNetId>
    {
        public readonly ulong Owner;
        public readonly ushort LocalIndex;

        public GoblinNetId(ulong owner, ushort localIndex)
        {
            Owner = owner;
            LocalIndex = localIndex;
        }

        public bool Equals(GoblinNetId other) => Owner == other.Owner && LocalIndex == other.LocalIndex;
        public override bool Equals(object obj) => obj is GoblinNetId other && Equals(other);
        public override int GetHashCode() => unchecked((Owner.GetHashCode() * 397) ^ LocalIndex);
        public override string ToString() => $"({Owner}:{LocalIndex})";

        public static bool operator ==(GoblinNetId a, GoblinNetId b) => a.Equals(b);
        public static bool operator !=(GoblinNetId a, GoblinNetId b) => !a.Equals(b);
    }
}

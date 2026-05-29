// Tests for GoblinNetId (RTSCL.World assembly).
// GoblinNetId is a value-struct that uniquely identifies a unit on the wire as (Owner SteamID64, LocalIndex).
// These tests confirm correct value-equality semantics: == / != operators, GetHashCode consistency,
// and that each component (Owner, LocalIndex) is independently part of the identity.
using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class GoblinNetIdTests
    {
        // Two GoblinNetIds with identical Owner and LocalIndex must be equal by value, operator, and hash.
        [Test]
        public void EqualForSameValues()
        {
            var a = new GoblinNetId(1234UL, 5);
            var b = new GoblinNetId(1234UL, 5);
            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        // Same LocalIndex but different Owner SteamID64 must produce a distinct identity
        // (different players' units must not collide in GoblinNetRegistry).
        [Test]
        public void DifferentOwnerNotEqual()
        {
            var a = new GoblinNetId(1234UL, 5);
            var b = new GoblinNetId(9999UL, 5);
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a != b);
        }

        // Same Owner but different LocalIndex must also produce a distinct identity
        // (the per-owner counter must be part of the struct comparison).
        [Test]
        public void DifferentIndexNotEqual()
        {
            var a = new GoblinNetId(1234UL, 5);
            var b = new GoblinNetId(1234UL, 6);
            Assert.AreNotEqual(a, b);
        }

        // A zero-value GoblinNetId (Owner=0, Index=0) must be a valid, self-equal identity
        // so default-initialised structs don't cause unexpected collisions or exceptions.
        [Test]
        public void ZeroOwnerIsValid()
        {
            var a = new GoblinNetId(0UL, 0);
            var b = new GoblinNetId(0UL, 0);
            Assert.AreEqual(a, b);
        }
    }
}

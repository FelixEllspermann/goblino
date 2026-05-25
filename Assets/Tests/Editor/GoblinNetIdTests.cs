using NUnit.Framework;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class GoblinNetIdTests
    {
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

        [Test]
        public void DifferentOwnerNotEqual()
        {
            var a = new GoblinNetId(1234UL, 5);
            var b = new GoblinNetId(9999UL, 5);
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void DifferentIndexNotEqual()
        {
            var a = new GoblinNetId(1234UL, 5);
            var b = new GoblinNetId(1234UL, 6);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void ZeroOwnerIsValid()
        {
            var a = new GoblinNetId(0UL, 0);
            var b = new GoblinNetId(0UL, 0);
            Assert.AreEqual(a, b);
        }
    }
}

using NUnit.Framework;
using Unity.Mathematics;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class PathfinderTests
    {
        private static System.Func<int, int, bool> Open() => (x, y) => true;

        [Test]
        public void StraightHorizontal()
        {
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(4, 0), 5, 5, Open());
            Assert.IsNotNull(path);
            Assert.AreEqual(4, path.Count);
            Assert.AreEqual(new int2(4, 0), path[path.Count - 1]);
        }

        [Test]
        public void DiagonalShortcut()
        {
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(2, 2), 5, 5, Open());
            Assert.IsNotNull(path);
            Assert.AreEqual(2, path.Count); // two diagonal steps
            Assert.AreEqual(new int2(2, 2), path[1]);
        }

        [Test]
        public void GoalEqualsStart()
        {
            var path = Pathfinder.FindPath(new int2(2, 2), new int2(2, 2), 5, 5, Open());
            Assert.IsNotNull(path);
            Assert.AreEqual(0, path.Count);
        }

        [Test]
        public void RoutesAroundWall()
        {
            // Wall on column x=2 for y=0..3, gap at y=4.
            System.Func<int, int, bool> passable = (x, y) => !(x == 2 && y <= 3);
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(4, 0), 5, 5, passable);
            Assert.IsNotNull(path);
            Assert.AreEqual(new int2(4, 0), path[path.Count - 1]);
            foreach (var c in path) Assert.IsFalse(c.x == 2 && c.y <= 3, "path crossed the wall");
        }

        [Test]
        public void UnreachableReturnsNull()
        {
            // Full wall column x=2 isolates the right half.
            System.Func<int, int, bool> passable = (x, y) => x != 2;
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(4, 0), 5, 5, passable);
            Assert.IsNull(path);
        }

        [Test]
        public void NoDiagonalCornerCut()
        {
            // (1,0) and (0,1) blocked → (0,0) cannot reach (1,1) diagonally (corner cut forbidden),
            // and no orthogonal route exists → null.
            System.Func<int, int, bool> passable = (x, y) => !((x == 1 && y == 0) || (x == 0 && y == 1));
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(1, 1), 3, 3, passable);
            Assert.IsNull(path);
        }
    }
}

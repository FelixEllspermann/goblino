// Tests for Pathfinder (RTSCL.World assembly).
// Pathfinder implements A* with 8-directional (octile) movement, an injected passability predicate,
// and a no-corner-cut rule. Tests cover: straight movement step count, diagonal optimisation,
// zero-length paths, obstacle avoidance, hard impassability, and the corner-cut prohibition.
using NUnit.Framework;
using Unity.Mathematics;
using RTSCL.World;

namespace RTSCL.World.Tests
{
    public class PathfinderTests
    {
        // Helper: passability predicate that treats every cell as open (no obstacles).
        private static System.Func<int, int, bool> Open() => (x, y) => true;

        // A straight 4-step horizontal walk must produce exactly 4 path nodes ending at the goal.
        [Test]
        public void StraightHorizontal()
        {
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(4, 0), 5, 5, Open());
            Assert.IsNotNull(path);
            Assert.AreEqual(4, path.Count);
            Assert.AreEqual(new int2(4, 0), path[path.Count - 1]);
        }

        // A 2-step diagonal move (0,0)→(2,2) must be taken as two diagonal steps, not four cardinal —
        // confirms octile heuristic enables diagonal shortcuts on open terrain.
        [Test]
        public void DiagonalShortcut()
        {
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(2, 2), 5, 5, Open());
            Assert.IsNotNull(path);
            Assert.AreEqual(2, path.Count); // two diagonal steps
            Assert.AreEqual(new int2(2, 2), path[1]);
        }

        // When start equals goal the returned path must be non-null and empty (zero steps).
        [Test]
        public void GoalEqualsStart()
        {
            var path = Pathfinder.FindPath(new int2(2, 2), new int2(2, 2), 5, 5, Open());
            Assert.IsNotNull(path);
            Assert.AreEqual(0, path.Count);
        }

        // A partial wall with a single-cell gap must be routed around; no node in the path may
        // intersect the blocked cells.
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

        // A full-column wall with no gap must cause FindPath to return null (no route exists).
        [Test]
        public void UnreachableReturnsNull()
        {
            // Full wall column x=2 isolates the right half.
            System.Func<int, int, bool> passable = (x, y) => x != 2;
            var path = Pathfinder.FindPath(new int2(0, 0), new int2(4, 0), 5, 5, passable);
            Assert.IsNull(path);
        }

        // When both orthogonal neighbours adjacent to a diagonal step are blocked, the diagonal must
        // not be taken (corner-cut prohibition). With no orthogonal route available, result is null.
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

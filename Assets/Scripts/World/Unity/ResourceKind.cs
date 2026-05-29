// ResourceKind.cs — enum of all resource types used as an index into ResourceBank._amounts[].
// Values are fixed ints; never reorder or the save/network format breaks.
// To add a resource: append a new value, widen ResourceBank array to match, and update ResourceUI.
namespace RTSCL.World.Unity
{
    /// <summary>All harvestable/tradeable resource types. Integer value = array index in ResourceBank.</summary>
    public enum ResourceKind { Wood = 0, Food = 1, Stone = 2, Gold = 3, Iron = 4, Crystal = 5 }
}

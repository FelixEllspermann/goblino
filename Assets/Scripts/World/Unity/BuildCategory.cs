// BuildCategory.cs — categories for the build menu sidebar. Declaration order = top-to-bottom tab order.
// Assigned per building on BuildingDefinition.Category; BuildMenu groups buildings by this.
namespace RTSCL.World.Unity
{
    /// <summary>Build-menu category. Order here is the tab order in the sidebar.</summary>
    public enum BuildCategory
    {
        Economy = 0,
        Military = 1,
        Defense = 2,
        Naval = 3,
        Advanced = 4,
    }
}

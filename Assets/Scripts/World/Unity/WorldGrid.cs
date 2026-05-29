// WorldGrid.cs — static holder for the current map dimensions in tiles.
// Set once by WorldGeneratorBootstrap (or equivalent) at world-generation time.
// Read by the Goblin pathfinder to clamp/validate cell coordinates.
// No reset needed: new-world generation always overwrites Width and Height before any unit spawns.
namespace RTSCL.World.Unity
{
    /// <summary>Current world grid dimensions in tiles, set at world-generation time and consumed by Goblin pathfinding.</summary>
    public static class WorldGrid
    {
        /// <summary>Tile columns in the current map. Set by the world generator before any units spawn.</summary>
        public static int Width;
        /// <summary>Tile rows in the current map. Set by the world generator before any units spawn.</summary>
        public static int Height;
    }
}

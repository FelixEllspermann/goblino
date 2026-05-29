// Biome.cs — Enum of all possible tile biomes produced by the world generator.
// BiomeClassifier maps noise values → these values. ContinentShaper post-processes them.
// Add new biome cases here first, then handle them in BiomeClassifier.Classify and the
// Unity-side tilemap painter (WorldPainter / MainBaseSetup).

namespace RTSCL.World
{
    /// <summary>All distinct biome types a map tile can have.
    /// Ordered so that 0 (DeepWater) is always impassable by land units.</summary>
    public enum Biome
    {
        DeepWater = 0,    // Impassable water — forms the ocean and enclosed water bodies
        Shore = 1,        // Shallow coastal fringe — impassable for buildings/units
        Cliff = 2,        // High-elevation rocky terrain — impassable
        Snow = 3,         // Cold high-altitude land — passable but no resources
        Desert = 4,       // Hot, dry land — passable but no food resources
        TropicalCoast = 5,// Hot, moist land adjacent to water — passable
        Forest = 6,       // High-moisture land — valid spawn & food cluster biome; trees spawn here
        Grassland = 7,    // Mid-moisture land — valid spawn & food cluster biome
        DryGrass = 8,     // Low-moisture land — passable, but no food clusters
    }
}

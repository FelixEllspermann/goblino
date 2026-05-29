// IBiomeTileResolver.cs
// Strategy interface consumed by TilePainter.
// Implement this (or use SingleTileBiomeResolver) to control which tile asset
// is placed for each biome cell. Swap implementations on the TilePainter ctor
// to support animated tiles, transitional edge tiles, etc.

using RTSCL.World;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    /// <summary>Maps a world cell (x, y) to the Unity TileBase that should be painted
    /// at that position. Receives the full WorldData so resolvers can inspect neighbours.</summary>
    public interface IBiomeTileResolver
    {
        TileBase GetTile(WorldData world, int x, int y);
    }
}

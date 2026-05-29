// TilePainter.cs  (pure C# helper — RTSCL.World.Unity)
// Converts a WorldData grid into Tilemap cells using an IBiomeTileResolver.
// Called once per world generation by WorldGeneratorBootstrap (or a similar driver).
// To repaint after world changes: call Paint() again — it clears and rewrites all tiles.
// To change which tiles are used per biome: swap the IBiomeTileResolver passed to the ctor.

using RTSCL.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    /// <summary>Converts a WorldData grid into Tilemap cells via an IBiomeTileResolver.
    /// Uses SetTiles (batch API) for a single draw-call instead of per-cell SetTile calls.</summary>
    public sealed class TilePainter
    {
        private readonly Tilemap _tilemap;
        private readonly IBiomeTileResolver _resolver;

        public TilePainter(Tilemap tilemap, IBiomeTileResolver resolver)
        {
            _tilemap  = tilemap;
            _resolver = resolver;
        }

        /// <summary>Clears the tilemap and paints every cell of <paramref name="world"/>
        /// by querying the resolver for each (x, y) position.
        /// Batches all writes via SetTiles for performance on large maps.</summary>
        public void Paint(WorldData world)
        {
            _tilemap.ClearAllTiles();
            // Pre-allocate flat arrays for the batch SetTiles call.
            var positions = new Vector3Int[world.Width * world.Height];
            var tiles     = new TileBase[world.Width * world.Height];
            int i = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                positions[i] = new Vector3Int(x, y, 0);
                tiles[i]     = _resolver.GetTile(world, x, y);
                i++;
            }
            _tilemap.SetTiles(positions, tiles);
            _tilemap.RefreshAllTiles();
        }
    }
}

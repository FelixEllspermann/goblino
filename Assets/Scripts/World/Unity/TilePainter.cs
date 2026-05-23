using RTSCL.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    public sealed class TilePainter
    {
        private readonly Tilemap _tilemap;
        private readonly IBiomeTileResolver _resolver;

        public TilePainter(Tilemap tilemap, IBiomeTileResolver resolver)
        {
            _tilemap  = tilemap;
            _resolver = resolver;
        }

        public void Paint(WorldData world)
        {
            _tilemap.ClearAllTiles();
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

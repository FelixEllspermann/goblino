using RTSCL.World;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    public interface IBiomeTileResolver
    {
        TileBase GetTile(WorldData world, int x, int y);
    }
}

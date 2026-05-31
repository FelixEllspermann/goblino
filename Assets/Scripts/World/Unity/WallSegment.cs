// WallSegment.cs (MonoBehaviour — RTSCL.World.Unity)
// Attached by BuildingPlacer to wall buildings (BuildingDefinition with WallCornerSprite set). Registers
// itself in WallRegistry (so it blocks movement) and auto-picks its sprite from its 4 wall neighbours:
//   both a vertical AND horizontal neighbour → corner/junction sprite;
//   only vertical (N/S) neighbours → vertical sprite; otherwise → horizontal sprite (E/W run or isolated).
// Placing/removing any wall refreshes its neighbours so a run re-tiles correctly. Construction tint
// (a colour lerp on the same renderer) is independent of the sprite we set here.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>One placed wall tile: blocks movement + auto-tiles its sprite from neighbouring walls.</summary>
    public sealed class WallSegment : MonoBehaviour
    {
        private Vector2Int _cell;
        private Sprite _corner, _horizontal, _vertical;
        private SpriteRenderer _sr;

        /// <summary>Wire up a wall tile from its definition + origin cell, then refresh the local run.</summary>
        public static void AttachTo(GameObject buildingGo, Vector2Int origin, BuildingDefinition def)
        {
            var w = buildingGo.AddComponent<WallSegment>();
            w._cell = origin;
            w._corner = def.WallCornerSprite;
            w._horizontal = def.WallHorizontalSprite;
            w._vertical = def.WallVerticalSprite;
            w._sr = buildingGo.GetComponent<SpriteRenderer>();
            WallRegistry.Register(origin, w);
            WallRegistry.RefreshAround(origin);   // recompute self + neighbours
        }

        /// <summary>Pick the sprite that matches this tile's current wall neighbours.</summary>
        public void RecomputeSprite()
        {
            if (_sr == null) return;
            bool vertical   = WallRegistry.IsWall(_cell.x, _cell.y + 1) || WallRegistry.IsWall(_cell.x, _cell.y - 1);
            bool horizontal = WallRegistry.IsWall(_cell.x + 1, _cell.y) || WallRegistry.IsWall(_cell.x - 1, _cell.y);
            Sprite chosen = (vertical && horizontal) ? _corner
                          : vertical                 ? _vertical
                          :                            _horizontal;   // E/W run or isolated
            if (chosen != null) _sr.sprite = chosen;
        }

        // Destroyed (combat / world reset) → drop from the registry and re-tile the neighbours.
        private void OnDestroy()
        {
            WallRegistry.Unregister(_cell);
            WallRegistry.RefreshAround(_cell);
        }
    }
}

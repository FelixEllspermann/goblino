using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Random = UnityEngine.Random;

namespace RTSCL.World.Unity
{
    public sealed class GoblinSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Tilemap _terrainMap;
        [SerializeField] private Tilemap _decorationMap;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _goblinsRoot;

        [Header("Goblin Kinds")]
        [SerializeField] private List<GoblinKind> _kinds = new();

        [System.Serializable]
        public class GoblinKind
        {
            public string Name = "Goblin";
            public Sprite[] WalkFrames;
        }

        /// <summary>Spawn N goblins in passable cells around a center world position.</summary>
        public void SpawnGroupAt(Vector3 centerWorld, int count)
        {
            if (_terrainMap == null) return;
            var center = _terrainMap.WorldToCell(centerWorld);
            var spawned = 0;
            // Spiral outward from center until we've placed N
            for (int ring = 1; ring < 12 && spawned < count; ring++)
            {
                for (int dx = -ring; dx <= ring && spawned < count; dx++)
                for (int dy = -ring; dy <= ring && spawned < count; dy++)
                {
                    if (Mathf.Abs(dx) != ring && Mathf.Abs(dy) != ring) continue; // edge of ring only
                    var c = new Vector3Int(center.x + dx, center.y + dy, 0);
                    if (!IsPassable(c)) continue;
                    SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f));
                    spawned++;
                }
            }
        }

        /// <summary>Spawn N goblins distributed evenly in rings around a building footprint (skipping the footprint cells).</summary>
        public void SpawnAroundFootprint(Vector2Int origin, Vector2Int footprint, int count)
        {
            if (_terrainMap == null || count <= 0) return;

            var occupied = new HashSet<Vector2Int>();
            for (int dy = 0; dy < footprint.y; dy++)
            for (int dx = 0; dx < footprint.x; dx++)
                occupied.Add(new Vector2Int(origin.x + dx, origin.y + dy));

            int spawned = 0;
            for (int ring = 1; ring < 12 && spawned < count; ring++)
            {
                var ringCells = CollectFootprintRing(origin, footprint, ring);
                var available = new List<Vector2Int>(ringCells.Count);
                foreach (var c in ringCells)
                {
                    if (occupied.Contains(c)) continue;
                    if (!IsPassable(new Vector3Int(c.x, c.y, 0))) continue;
                    available.Add(c);
                }
                if (available.Count == 0) continue;

                int remaining = count - spawned;
                if (remaining >= available.Count)
                {
                    foreach (var c in available)
                    {
                        SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f));
                        spawned++;
                        if (spawned >= count) break;
                    }
                }
                else
                {
                    // Pick `remaining` cells evenly spaced around the ring
                    for (int i = 0; i < remaining; i++)
                    {
                        int idx = (i * available.Count) / remaining;
                        var c = available[idx];
                        SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f));
                        spawned++;
                    }
                }
            }
        }

        /// <summary>Cells exactly at Chebyshev distance `ring` outside a rectangular footprint, in clockwise order.</summary>
        private static List<Vector2Int> CollectFootprintRing(Vector2Int origin, Vector2Int footprint, int ring)
        {
            int minX = origin.x - ring;
            int maxX = origin.x + footprint.x - 1 + ring;
            int minY = origin.y - ring;
            int maxY = origin.y + footprint.y - 1 + ring;

            var list = new List<Vector2Int>();
            for (int x = minX; x <= maxX; x++)         list.Add(new Vector2Int(x, maxY));      // top row L→R
            for (int y = maxY - 1; y > minY; y--)      list.Add(new Vector2Int(maxX, y));      // right col T→B
            for (int x = maxX; x >= minX; x--)         list.Add(new Vector2Int(x, minY));      // bottom row R→L
            for (int y = minY + 1; y < maxY; y++)      list.Add(new Vector2Int(minX, y));      // left col B→T
            return list;
        }

        private bool IsPassable(Vector3Int cell)
        {
            var t = _terrainMap.GetTile(cell);
            if (t == null) return false;
            var n = t.name;
            return n != "DeepWater" && n != "Cliff" && n != "Shore";
        }

        public void ClearAllGoblins()
        {
            var copy = new List<Goblin>(Goblin.All);
            foreach (var g in copy)
                if (g != null) DestroyImmediate(g.gameObject);
        }

        public Goblin SpawnAt(Vector3 worldPos, GoblinKind kind = null)
        {
            if (_kinds.Count == 0) { Debug.LogWarning("No goblin kinds configured"); return null; }
            kind ??= _kinds[Random.Range(0, _kinds.Count)];
            if (kind.WalkFrames == null || kind.WalkFrames.Length == 0)
            { Debug.LogWarning($"Goblin kind '{kind.Name}' has no frames"); return null; }

            var go = new GameObject($"Goblin_{kind.Name}");
            if (_goblinsRoot != null) go.transform.SetParent(_goblinsRoot, false);
            go.transform.position = SnapToCellCenter(worldPos);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 25;  // above buildings

            var goblin = go.AddComponent<Goblin>();
            goblin.Init(kind.Name, kind.WalkFrames, _terrainMap, _decorationMap);
            return goblin;
        }

        private Vector3 SnapToCellCenter(Vector3 worldPos)
        {
            if (_terrainMap == null) return worldPos;
            var cell = _terrainMap.WorldToCell(worldPos);
            var c = _terrainMap.CellToWorld(cell);
            return c + new Vector3(0.5f, 0.5f, 0f);  // center of cell
        }

    }
}

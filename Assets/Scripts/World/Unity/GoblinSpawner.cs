// =============================================================================
// GoblinSpawner.cs  —  RTSCL.World.Unity
//
// Factory for all goblin GameObjects. Three public entry points:
//   SpawnGroupAt         — N goblins spiralling out from a center (fallback path).
//   SpawnAroundFootprint — N goblins evenly distributed in ring-1 of a building
//                          footprint; used by MainBaseSetup for starting teams.
//   SpawnByKindAroundFootprint — single goblin of a specific kind at the first
//                          free cell ringing a footprint; used by GoblinProduction
//                          when a training completes.
//
// SpawnAt is the shared low-level instantiator. It creates the GameObject,
// assigns a GoblinNetId (using the pre-reserved index if provided, or the next
// from GoblinNetRegistry.NextLocalIndex), calls Goblin.Init, sets owner, and
// applies any already-purchased upgrades via UpgradeEffects.ApplyExistingTo.
//
// To add a new goblin kind: add an entry to the _kinds list in the Inspector
// (Name + WalkFrames + Definition). No code changes needed.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using RTSCL.World;
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

        /// <summary>Per-kind asset bundle: sprite frames + stats definition.
        /// Populated in the Inspector; looked up by name string at spawn time.</summary>
        [System.Serializable]
        public class GoblinKind
        {
            public string Name = "Goblin";
            public Sprite[] WalkFrames;
            public GoblinUnitDefinition Definition;
        }

        /// <summary>Spawn N goblins in passable cells spiralling out from a center world position.
        /// Used as a fallback when no keep definition is found (no owner assignment).</summary>
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

        /// <summary>Spawn N goblins distributed evenly in rings around a building footprint,
        /// skipping the footprint cells themselves. Rings expand until N units are placed.
        /// If kindName is set all spawned goblins are of that kind; otherwise a random kind
        /// per goblin. Used by MainBaseSetup to place starting teams around the Keep.</summary>
        public void SpawnAroundFootprint(Vector2Int origin, Vector2Int footprint, int count, string kindName = null, ulong owner = 0UL)
        {
            if (_terrainMap == null || count <= 0) return;
            var forcedKind = string.IsNullOrEmpty(kindName) ? null : FindKind(kindName);

            // Build a fast-lookup set of the footprint cells so we skip them in ring checks.
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
                    // Fill the whole ring.
                    foreach (var c in available)
                    {
                        SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f), forcedKind, owner);
                        spawned++;
                        if (spawned >= count) break;
                    }
                }
                else
                {
                    // Pick `remaining` cells evenly spaced around the ring to avoid clumping.
                    for (int i = 0; i < remaining; i++)
                    {
                        int idx = (i * available.Count) / remaining;
                        var c = available[idx];
                        SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f), forcedKind, owner);
                        spawned++;
                    }
                }
            }
        }

        /// <summary>Spawn one goblin of the given kind on the first free cell ringing the footprint.</summary>
        public Goblin SpawnByKindAroundFootprint(string kindName, Vector2Int origin, Vector2Int footprint, ulong owner = 0UL, ushort? reservedIndex = null)
        {
            if (_terrainMap == null) return null;
            var kind = FindKind(kindName);
            if (kind == null)
            {
                Debug.LogWarning($"[GoblinSpawner] Kind '{kindName}' not configured");
                return null;
            }

            var occupied = new HashSet<Vector2Int>();
            for (int dy = 0; dy < footprint.y; dy++)
            for (int dx = 0; dx < footprint.x; dx++)
                occupied.Add(new Vector2Int(origin.x + dx, origin.y + dy));

            for (int ring = 1; ring < 12; ring++)
            {
                var cells = CollectFootprintRing(origin, footprint, ring);
                foreach (var c in cells)
                {
                    if (occupied.Contains(c)) continue;
                    if (!IsPassable(new Vector3Int(c.x, c.y, 0))) continue;
                    return SpawnAt(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f), kind, owner, reservedIndex);
                }
            }
            return null;
        }

        /// <summary>Spawn a single goblin of the named kind at a world position (used by MonsterSpawner,
        /// boat spawns, and centered keep/barracks production). <paramref name="reservedIndex"/> pre-reserves
        /// the NetId so trained units match across clients. Returns null if the kind isn't configured.</summary>
        public Goblin SpawnKindAt(string kindName, Vector3 worldPos, ulong owner, ushort? reservedIndex = null)
        {
            var kind = FindKind(kindName);
            if (kind == null) { Debug.LogWarning($"[GoblinSpawner] Kind '{kindName}' not configured"); return null; }
            return SpawnAt(worldPos, kind, owner, reservedIndex);
        }

        // Linear search by Name string. Called at spawn time (infrequent), not per-frame.
        private GoblinKind FindKind(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var k in _kinds)
                if (k != null && k.Name == name) return k;
            return null;
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

        // Simplified passability check for spawn placement: no decoration-map check (spawner
        // places units regardless of trees). Matching the terrain rules from Goblin.IsCellPassable
        // (water/cliff/shore blocked) ensures units don't spawn in impassable terrain.
        private bool IsPassable(Vector3Int cell)
        {
            var t = _terrainMap.GetTile(cell);
            if (t == null) return false;
            var n = t.name;
            return n != "DeepWater" && n != "Cliff" && n != "Shore";
        }

        /// <summary>Destroy all live goblins via DestroyImmediate (safe in edit mode and during
        /// world reset). Iterates a copy of Goblin.All because Destroy modifies it mid-loop.</summary>
        public void ClearAllGoblins()
        {
            var copy = new List<Goblin>(Goblin.All);
            foreach (var g in copy)
                if (g != null) DestroyImmediate(g.gameObject);
        }

        /// <summary>Low-level goblin factory. Creates the GameObject, assigns a NetId (using
        /// <paramref name="reservedIndex"/> if provided so the ID matches across clients for
        /// trained units), calls Goblin.Init, sets owner, and applies existing upgrades.
        /// Returns null if the kind has no configured walk frames.</summary>
        public Goblin SpawnAt(Vector3 worldPos, GoblinKind kind = null, ulong owner = 0UL, ushort? reservedIndex = null)
        {
            if (_kinds.Count == 0) { Debug.LogWarning("No goblin kinds configured"); return null; }
            kind ??= _kinds[Random.Range(0, _kinds.Count)];
            if (kind.WalkFrames == null || kind.WalkFrames.Length == 0)
            { Debug.LogWarning($"Goblin kind '{kind.Name}' has no frames"); return null; }

            var go = new GameObject($"Goblin_{kind.Name}");
            if (_goblinsRoot != null) go.transform.SetParent(_goblinsRoot, false);
            go.transform.position = SnapToCellCenter(worldPos);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 25;  // above buildings (15) and terrain

            // Use the pre-reserved index if given (trained units pre-reserve in GoblinNetRegistry
            // so all clients assign the same NetId without a round-trip).
            ushort idx = reservedIndex ?? GoblinNetRegistry.NextLocalIndex(owner);
            var netId = new GoblinNetId(owner, idx);

            var goblin = go.AddComponent<Goblin>();
            goblin.Init(netId, kind.Name, kind.WalkFrames, _terrainMap, _decorationMap, kind.Definition);
            goblin.SetOwner(owner);
            // Apply any already-purchased upgrades (harvest speed, damage, etc.) to the new unit.
            UpgradeEffects.ApplyExistingTo(goblin);
            // Hide enemy units under fog: any unit NOT owned by the local player only shows while in active
            // vision (Visible), not in the explored half-fog. (Monsters already get FogHide from MonsterSpawner.)
            if (owner != WorldStartContext.LocalPlayer && go.GetComponent<FogHide>() == null)
                go.AddComponent<FogHide>();
            return goblin;
        }

        // Snap an arbitrary world position to the center of the tilemap cell it falls in.
        // Prevents goblins from spawning on tile-boundary seams.
        private Vector3 SnapToCellCenter(Vector3 worldPos)
        {
            if (_terrainMap == null) return worldPos;
            var cell = _terrainMap.WorldToCell(worldPos);
            var c = _terrainMap.CellToWorld(cell);
            return c + new Vector3(0.5f, 0.5f, 0f);  // center of cell
        }

    }
}

// MonsterSpawner.cs — Seed-deterministic, biome-aware placement of neutral monsters. Run from
// MainBaseSetup.OnNewWorld AFTER player teams so spawn order (and thus NetIds) is identical on every
// client. Monsters are owned by the host (solo = 0). Each gets a MonsterAI with Home set.
using System.Collections.Generic;
using RTSCL.World;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace RTSCL.World.Unity
{
    /// <summary>Places neutral monsters into matching biomes, away from player spawns, deterministically.</summary>
    public sealed class MonsterSpawner : MonoBehaviour
    {
        /// <summary>Per-type spawn rule, configured in the Inspector.</summary>
        [System.Serializable]
        public sealed class MonsterType
        {
            public string KindName;     // matches a GoblinSpawner kind + GoblinUnitDefinition.SpawnerKindName
            public Biome[] Biomes;      // acceptable spawn biomes
            public int Count = 3;
            [Tooltip("If true, only spawn on land cells adjacent to water (coastal) — e.g. Giant Crab.")]
            public bool RequireAdjacentWater;
        }

        [SerializeField] private GoblinSpawner _spawner;
        [SerializeField] private List<MonsterType> _types = new();
        [Tooltip("Minimum distance (cells) a monster must keep from every player spawn point.")]
        [SerializeField] private int _minDistanceFromSpawns = 22;

        /// <summary>Spawn all configured monster types for the given world + seed.</summary>
        public void SpawnAll(WorldData world, uint seed)
        {
            if (_spawner == null || world == null) return;
            ulong owner = WorldStartContext.PendingSlots != null ? WorldStartContext.HostPlayer : 0UL;
            var rng = new Random(seed == 0u ? 99u : seed * 2654435761u + 1u);

            foreach (var t in _types)
            {
                if (t == null || string.IsNullOrEmpty(t.KindName) || t.Biomes == null || t.Biomes.Length == 0) continue;
                int placed = 0, attempts = 0, maxAttempts = Mathf.Max(1, t.Count) * 200;
                while (placed < t.Count && attempts < maxAttempts)
                {
                    attempts++;
                    int x = rng.NextInt(0, world.Width);
                    int y = rng.NextInt(0, world.Height);
                    if (!IsBiomeMatch(world, x, y, t.Biomes)) continue;
                    if (t.RequireAdjacentWater && !HasWaterNeighbor(world, x, y)) continue;
                    if (TooCloseToSpawn(world, x, y)) continue;
                    var pos = new Vector3(x + 0.5f, y + 0.5f, 0f);
                    var g = _spawner.SpawnKindAt(t.KindName, pos, owner);
                    if (g == null) continue;
                    g.MarkNeutral();
                    g.gameObject.AddComponent<MonsterAI>().Home = pos;
                    g.gameObject.AddComponent<FogHide>();   // hidden under fog until a unit sees it
                    placed++;
                }
            }
        }

        private static bool IsBiomeMatch(WorldData w, int x, int y, Biome[] biomes)
        {
            var b = w.BiomeAt(x, y);
            if (b == Biome.DeepWater || b == Biome.Shore || b == Biome.Cliff) return false;
            foreach (var wanted in biomes) if (b == wanted) return true;
            return false;
        }

        // True if any 8-neighbour cell is water (DeepWater/Shore) — i.e. this land cell is coastal.
        private static bool HasWaterNeighbor(WorldData w, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= w.Width || ny < 0 || ny >= w.Height) continue;
                var b = w.BiomeAt(nx, ny);
                if (b == Biome.DeepWater || b == Biome.Shore) return true;
            }
            return false;
        }

        private bool TooCloseToSpawn(WorldData w, int x, int y)
        {
            if (w.Spawns == null) return false;
            int minSq = _minDistanceFromSpawns * _minDistanceFromSpawns;
            foreach (var s in w.Spawns)
            {
                int dx = s.x - x, dy = s.y - y;
                if (dx * dx + dy * dy < minSq) return true;
            }
            return false;
        }
    }
}

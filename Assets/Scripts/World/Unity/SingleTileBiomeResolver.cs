using System;
using System.Collections.Generic;
using RTSCL.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    [CreateAssetMenu(menuName = "RTSCL/Single-Tile Biome Resolver",
                     fileName = "SingleTileBiomeResolver")]
    public sealed class SingleTileBiomeResolver : ScriptableObject, IBiomeTileResolver
    {
        [Serializable]
        public struct Entry
        {
            public Biome Biome;
            public TileBase Tile;
        }

        [SerializeField] private List<Entry> _entries = new();

        private Dictionary<Biome, TileBase> _lookup;

        public TileBase GetTile(WorldData world, int x, int y)
        {
            EnsureLookup();
            return _lookup.TryGetValue(world.BiomeAt(x, y), out var tile) ? tile : null;
        }

        private void EnsureLookup()
        {
            if (_lookup != null && _lookup.Count == _entries.Count) return;
            _lookup = new Dictionary<Biome, TileBase>(_entries.Count);
            foreach (var e in _entries)
                if (e.Tile != null) _lookup[e.Biome] = e.Tile;
        }

        private void OnValidate() => _lookup = null;
    }
}

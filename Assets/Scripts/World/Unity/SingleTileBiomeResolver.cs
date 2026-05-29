// SingleTileBiomeResolver.cs  (ScriptableObject — RTSCL.World.Unity)
// Concrete IBiomeTileResolver that assigns one flat TileBase per Biome enum value.
// Configure the Biome→Tile mapping in the Inspector under Assets/Settings or wherever
// the asset lives (Create → RTSCL → Single-Tile Biome Resolver).
// To add a new biome tile: add a row to the _entries list in the asset.
// To support animated/rule tiles per biome, implement a different IBiomeTileResolver.

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
        /// <summary>One Inspector row: a Biome enum value paired with its tile asset.</summary>
        [Serializable]
        public struct Entry
        {
            public Biome Biome;
            public TileBase Tile;
        }

        [SerializeField] private List<Entry> _entries = new();

        // Runtime lookup built lazily from _entries. Invalidated by OnValidate
        // so Inspector edits are reflected immediately in Play mode.
        private Dictionary<Biome, TileBase> _lookup;

        /// <summary>Returns the tile for the biome at (x, y), or null if unmapped.</summary>
        public TileBase GetTile(WorldData world, int x, int y)
        {
            EnsureLookup();
            return _lookup.TryGetValue(world.BiomeAt(x, y), out var tile) ? tile : null;
        }

        /// <summary>Builds (or rebuilds) the biome→tile dictionary from _entries if stale.</summary>
        private void EnsureLookup()
        {
            if (_lookup != null && _lookup.Count == _entries.Count) return;
            _lookup = new Dictionary<Biome, TileBase>(_entries.Count);
            foreach (var e in _entries)
                if (e.Tile != null) _lookup[e.Biome] = e.Tile;
        }

        // Clears the lookup so the next GetTile call rebuilds from the updated list.
        private void OnValidate() => _lookup = null;
    }
}

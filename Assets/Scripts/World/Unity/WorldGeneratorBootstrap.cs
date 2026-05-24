using RTSCL.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    public sealed class WorldGeneratorBootstrap : MonoBehaviour
    {
        [Header("Tilemaps")]
        [SerializeField] private Tilemap _terrainMap;
        [SerializeField] private Tilemap _decorationMap;

        [Header("Resolver")]
        [SerializeField] private SingleTileBiomeResolver _resolver;

        [Header("Decorations")]
        [SerializeField] private DecorationPlacer.DecorationConfig _decorationConfig;

        [Header("Generation")]
        [Tooltip("-1 = pick a fresh random seed each run")]
        [SerializeField] private int _seed = -1;
        [SerializeField] private WorldGenConfig _config = new WorldGenConfig();

        [Header("Camera")]
        [SerializeField] private Camera _cameraToFit;
        [SerializeField] private bool _autoFitCamera = true;

        private WorldData _currentWorld;

        public WorldData CurrentWorld => _currentWorld;

        private void Start() => Regenerate();

        [ContextMenu("Regenerate")]
        public void Regenerate()
        {
            int effectiveSeed = _seed < 0 ? Random.Range(1, int.MaxValue) : _seed;
            RegenerateWithSeed(effectiveSeed);
        }

        public void RegenerateWithSeed(int seed)
        {
            if (_terrainMap == null || _decorationMap == null || _resolver == null)
            {
                Debug.LogError("[WorldGen] Wire up Terrain Tilemap, Decoration Tilemap, " +
                               "and Resolver in the Inspector.", this);
                return;
            }

            Debug.Log($"[WorldGen] Generating with seed {seed}…");

            WorldData world;
            try { world = new WorldGenerator().Generate(seed, _config); }
            catch (System.Exception e)
            {
                Debug.LogError($"[WorldGen] Generation failed: {e.Message}", this);
                return;
            }

            new TilePainter(_terrainMap, _resolver).Paint(world);
            new DecorationPlacer(_decorationMap, _decorationConfig).Place(world, world.Seed);

            if (_autoFitCamera && _cameraToFit != null)
                CameraFitter.Fit(_cameraToFit, world.Width, world.Height);

            if (_cameraToFit != null)
            {
                var rts = _cameraToFit.GetComponent<RTSCamera2D>();
                if (rts != null) rts.SetWorldBounds(world.Width, world.Height);
            }

            _currentWorld = world;
            Debug.Log($"[WorldGen] Done. Spawns: {world.Spawns.Length}, " +
                      $"Clusters: {world.Resources.Count}.");
        }
    }
}

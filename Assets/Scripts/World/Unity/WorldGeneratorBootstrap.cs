// WorldGeneratorBootstrap.cs — MonoBehaviour that drives world generation at scene load.
// Placed in SampleScene. On Start() it checks PendingSeed (set by GameStartLoader for multiplayer),
// otherwise falls back to the Inspector seed (_seed) or a random one (_seed < 0).
//
// Dependency inversion for multiplayer:
//   GameStartLoader (Assembly-CSharp) writes WorldGeneratorBootstrap.PendingSeed before loading SampleScene.
//   This script reads + clears PendingSeed in Start() so all clients generate the same map from the same seed.
//
// To adjust map generation parameters: edit WorldGenConfig in the Inspector (_config field).
// To adjust decoration density: edit DecorationConfig in the Inspector (_decorationConfig field).
// To change the camera start zoom: adjust _startOrthoSize in the Inspector.
using RTSCL.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    /// <summary>Scene-level entry point for world generation. Wires WorldGenerator → TilePainter → DecorationPlacer
    /// in sequence and positions the camera. Also exposes PendingSeed for the multiplayer handshake.</summary>
    public sealed class WorldGeneratorBootstrap : MonoBehaviour
    {
        [Header("Tilemaps")]
        [SerializeField] private Tilemap _terrainMap;
        [SerializeField] private Tilemap _decorationMap;

        [Header("Resolver")]
        [SerializeField] private SingleTileBiomeResolver _resolver;

        [Header("Decorations")]
        [SerializeField] private DecorationPlacer.DecorationConfig _decorationConfig;
        [Tooltip("Wild berry-bush tile (harvestable food). Injected into the decoration config at generation " +
                 "time — set here as a top-level ref because nested-config object refs don't persist reliably.")]
        [SerializeField] private UnityEngine.Tilemaps.TileBase _berryTile;

        [Header("Generation")]
        [Tooltip("-1 = pick a fresh random seed each run")]
        [SerializeField] private int _seed = -1;
        [SerializeField] private WorldGenConfig _config = new WorldGenConfig();

        [Header("Camera")]
        [SerializeField] private Camera _cameraToFit;
        [Tooltip("Center camera on the first spawn point (Keep) at startup")]
        [SerializeField] private bool _autoFitCamera = true;
        [Tooltip("Orthographic size at game start — smaller = more zoomed in. 8 ≈ keep + a few rings of vision")]
        [SerializeField] private float _startOrthoSize = 8f;

        private WorldData _currentWorld;

        /// <summary>The last successfully generated WorldData. Read by MainBaseSetup to place keeps and units.</summary>
        public WorldData CurrentWorld => _currentWorld;

        /// <summary>External one-shot seed override. Set by the networking layer before
        /// scene load to make all clients use the same map seed; consumed on first read so
        /// a subsequent Solo restart picks a fresh random seed.
        /// Set by: GameStartLoader (Assembly-CSharp) via WorldGeneratorBootstrap.PendingSeed = seed.</summary>
        public static int? PendingSeed;

        /// <summary>On scene load: use PendingSeed if provided (multiplayer), otherwise generate with the
        /// Inspector seed or a fresh random seed (solo / editor regeneration).</summary>
        private void Start()
        {
            // Apply a map size chosen in the Solo Setup menu (consumed once), overriding the config dims.
            if (WorldStartContext.PendingMapSize.HasValue)
            {
                int dim = WorldStartContext.SizeToDimension(WorldStartContext.PendingMapSize.Value);
                _config.Width = dim;
                _config.Height = dim;
                WorldStartContext.PendingMapSize = null;
            }

            if (PendingSeed.HasValue)
            {
                var seed = PendingSeed.Value;
                // Consume immediately so next solo session doesn't reuse this seed.
                PendingSeed = null;
                RegenerateWithSeed(seed);
            }
            else
            {
                Regenerate();
            }
        }

        /// <summary>Editor context-menu shortcut. Picks the Inspector seed or a random one if _seed &lt; 0.</summary>
        [ContextMenu("Regenerate")]
        public void Regenerate()
        {
            int effectiveSeed = _seed < 0 ? Random.Range(1, int.MaxValue) : _seed;
            RegenerateWithSeed(effectiveSeed);
        }

        /// <summary>Core generation pipeline: WorldGenerator → TilePainter → DecorationPlacer → camera fit.
        /// Called both from Start() and from editor tooling.</summary>
        /// <param name="seed">Deterministic seed shared across all clients in multiplayer.</param>
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

            // Paint terrain biome tiles, then overlay decorations (trees, stone, ore, etc.).
            new TilePainter(_terrainMap, _resolver).Paint(world);
            // Inject the berry tile into the config (top-level ref → reliable persistence, unlike nested).
            if (_berryTile != null && _decorationConfig != null) _decorationConfig.BerryTile = _berryTile;
            new DecorationPlacer(_decorationMap, _decorationConfig).Place(world, world.Seed);

            // Fit camera to world bounds and focus on spawn[0] (local player's starting keep).
            if (_cameraToFit != null)
            {
                var rts = _cameraToFit.GetComponent<RTSCamera2D>();
                if (rts != null) rts.SetWorldBounds(world.Width, world.Height);

                if (_autoFitCamera && world.Spawns != null && world.Spawns.Length > 0)
                {
                    var s = world.Spawns[0];
                    var focusPos = new Vector3(s.x + 0.5f, s.y + 0.5f, 0f);
                    if (rts != null)
                        rts.FocusOn(focusPos, _startOrthoSize);
                    else
                    {
                        // Fallback for cameras without RTSCamera2D.
                        _cameraToFit.orthographic = true;
                        _cameraToFit.orthographicSize = _startOrthoSize;
                        _cameraToFit.transform.position =
                            new Vector3(focusPos.x, focusPos.y, -10f);
                    }
                }
            }

            _currentWorld = world;
            Debug.Log($"[WorldGen] Done. Spawns: {world.Spawns.Length}, " +
                      $"Clusters: {world.Resources.Count}.");
        }
    }
}

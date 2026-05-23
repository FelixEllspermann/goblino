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

        [Header("Runtime UI")]
        [SerializeField] private bool _showRuntimePanel = true;

        private string _seedInput = "";
        private int _lastSeed;

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

            _lastSeed = seed;
            _seedInput = seed.ToString();
            Debug.Log($"[WorldGen] Done. Spawns: {world.Spawns.Length}, " +
                      $"Clusters: {world.Resources.Count}.");
        }

        private void OnGUI()
        {
            if (!_showRuntimePanel) return;
            if (!Application.isPlaying) return;

            const int width = 240;
            const int height = 110;
            GUILayout.BeginArea(new Rect(10, 10, width, height), GUI.skin.box);
            GUILayout.Label($"<b>World Generator</b>  (last: {_lastSeed})", RichLabel());

            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed:", GUILayout.Width(40));
            _seedInput = GUILayout.TextField(_seedInput, GUILayout.Width(160));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate"))
            {
                if (int.TryParse(_seedInput, out int parsed)) RegenerateWithSeed(parsed);
                else Debug.LogWarning($"[WorldGen] '{_seedInput}' is not a valid int seed.");
            }
            if (GUILayout.Button("Random"))
            {
                RegenerateWithSeed(Random.Range(1, int.MaxValue));
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private static GUIStyle s_richLabel;
        private static GUIStyle RichLabel()
        {
            if (s_richLabel == null)
                s_richLabel = new GUIStyle(GUI.skin.label) { richText = true };
            return s_richLabel;
        }
    }
}

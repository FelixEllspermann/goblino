using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Random = UnityEngine.Random;

namespace RTSCL.World.Unity
{
    public sealed class GoblinSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Tilemap _terrainMap;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _goblinsRoot;

        [Header("Goblin Kinds")]
        [SerializeField] private List<GoblinKind> _kinds = new();

        [Header("Auto-Spawn")]
        [SerializeField] private int _autoSpawnCount = 10;
        [SerializeField] private bool _spawnOnStart = true;

        [Header("Runtime UI")]
        [SerializeField] private bool _showSpawnButton = true;

        [System.Serializable]
        public class GoblinKind
        {
            public string Name = "Goblin";
            public Sprite[] WalkFrames;
        }

        private bool _spawnAtCursorArmed;

        private void Start()
        {
            if (_spawnOnStart) AutoSpawn();
        }

        public void AutoSpawn()
        {
            for (int i = 0; i < _autoSpawnCount; i++)
                SpawnAtRandomLand();
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
            goblin.Init(kind.Name, kind.WalkFrames, _terrainMap);
            return goblin;
        }

        private Vector3 SnapToCellCenter(Vector3 worldPos)
        {
            if (_terrainMap == null) return worldPos;
            var cell = _terrainMap.WorldToCell(worldPos);
            var c = _terrainMap.CellToWorld(cell);
            return c + new Vector3(0.5f, 0.5f, 0f);  // center of cell
        }

        private void SpawnAtRandomLand()
        {
            if (_terrainMap == null) return;
            var bounds = _terrainMap.cellBounds;
            for (int tries = 0; tries < 100; tries++)
            {
                int x = Random.Range(bounds.xMin, bounds.xMax);
                int y = Random.Range(bounds.yMin, bounds.yMax);
                var tile = _terrainMap.GetTile(new Vector3Int(x, y, 0));
                if (tile == null) continue;
                var n = tile.name;
                if (n == "DeepWater" || n == "Cliff" || n == "Shore") continue;
                SpawnAt(new Vector3(x + 0.5f, y + 0.5f, 0));
                return;
            }
        }

        private void OnGUI()
        {
            if (!_showSpawnButton || !Application.isPlaying) return;

            const int width = 220;
            GUILayout.BeginArea(new Rect((Screen.width - width) * 0.5f, 10, width, 60), GUI.skin.box);
            GUILayout.Label($"<b>Goblins:</b> {Goblin.All.Count}", RichLabel());

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_spawnAtCursorArmed ? "Click on map…" : "Spawn at cursor"))
                _spawnAtCursorArmed = !_spawnAtCursorArmed;
            if (GUILayout.Button("+10 random"))
                for (int i = 0; i < 10; i++) SpawnAtRandomLand();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            if (_spawnAtCursorArmed && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                {
                    var mp = Mouse.current.position.ReadValue();
                    var wp = _camera.ScreenToWorldPoint(new Vector3(mp.x, mp.y, -_camera.transform.position.z));
                    SpawnAt(wp);
                    Event.current.Use();
                }
            }
        }

        private static GUIStyle s_rich;
        private static GUIStyle RichLabel()
        {
            if (s_rich == null) s_rich = new GUIStyle(GUI.skin.label) { richText = true };
            return s_rich;
        }
    }
}

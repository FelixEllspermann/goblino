// RallyPoints.cs — Owner-local store of each building's rally point (world position), keyed by
// footprint origin cell. NOT networked: only the unit MOVE that results from a rally is synced
// (via NetCommandIssuer.IssueMove). Cleared on a new world via MainBaseSetup.OnNewWorld.
//
// Set by: BuildingPlacer.PlaceForce (default), ObjectInspector right-click (player override).
// Read by: ObjectInspector (visual), GoblinProductionRunner (where trained units walk).
using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Per-building rally points. Owner-local; keyed by building footprint origin.</summary>
    public static class RallyPoints
    {
        private static readonly Dictionary<Vector2Int, Vector3> _points = new();

        public static void Set(Vector2Int origin, Vector3 worldPos) => _points[origin] = worldPos;
        public static bool TryGet(Vector2Int origin, out Vector3 worldPos) => _points.TryGetValue(origin, out worldPos);
        public static void Remove(Vector2Int origin) => _points.Remove(origin);
        public static void Clear() => _points.Clear();

        /// <summary>Default rally: a couple of cells below the footprint, centered on its width.</summary>
        public static Vector3 Default(Vector2Int origin, Vector2Int footprint) =>
            new Vector3(origin.x + footprint.x * 0.5f, origin.y - 1.5f, 0f);
    }

    // NOTE: RallyVisual lives here (alongside RallyPoints) rather than in its own file purely because
    // the headless editor's asset import would not register a freshly-created standalone .cs into the
    // RTSCL.World.Unity assembly. Functionally identical; can be split into RallyVisual.cs later.

    /// <summary>Single reusable visual for the currently-selected building's rally point: a flag icon
    /// (GUI_33) at the rally, plus a dashed LineRenderer from the building to it whose dashes flow
    /// ("marching ants") toward the rally. Created lazily (RallyVisual.Instance) and driven by
    /// ObjectInspector (Show on building select, Hide on deselect / other selection).
    /// To tune: LineWidth, ScrollSpeed, IconScale, DashesPerUnit, the line tint.</summary>
    public sealed class RallyVisual : MonoBehaviour
    {
        private const float LineWidth = 0.09f;
        private const float ScrollSpeed = 2.0f;    // marching-ants speed (texture units / sec)
        private const float IconScale = 0.7f;
        private const float DashesPerUnit = 1.5f;  // dash repeats per world unit of line length

        private static RallyVisual _instance;
        public static RallyVisual Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("RallyVisual");
                    _instance = go.AddComponent<RallyVisual>();
                    _instance.Build();
                    _instance.HideNow();
                }
                return _instance;
            }
        }

        private LineRenderer _line;
        private SpriteRenderer _icon;
        private Material _lineMat;
        private bool _active;
        private float _offset;

        private void Build()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.widthMultiplier = LineWidth;
            _line.numCapVertices = 0;
            _line.numCornerVertices = 0;
            _line.useWorldSpace = true;
            _line.textureMode = LineTextureMode.Tile;
            _line.sortingOrder = 22;                 // above buildings (15), below units (25)
            _line.alignment = LineAlignment.TransformZ;
            _lineMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = DashTex() };
            _lineMat.color = new Color(1f, 1f, 0.6f, 0.9f);
            _line.material = _lineMat;

            var iconGo = new GameObject("RallyIcon");
            iconGo.transform.SetParent(transform, false);
            _icon = iconGo.AddComponent<SpriteRenderer>();
            _icon.sortingOrder = 24;
            iconGo.transform.localScale = Vector3.one * IconScale;
        }

        /// <summary>Position the flag at <paramref name="rally"/> and draw the dashed line from
        /// <paramref name="buildingCenter"/> to it. Enables both renderers.</summary>
        public void Show(Vector3 buildingCenter, Vector3 rally, Sprite icon)
        {
            _active = true;
            if (icon != null && _icon.sprite != icon) _icon.sprite = icon;
            _icon.gameObject.SetActive(true);
            _icon.transform.position = new Vector3(rally.x, rally.y, 0f);
            _line.enabled = true;
            _line.SetPosition(0, new Vector3(buildingCenter.x, buildingCenter.y, 0f));
            _line.SetPosition(1, new Vector3(rally.x, rally.y, 0f));
            float len = Vector2.Distance(buildingCenter, rally);
            _lineMat.mainTextureScale = new Vector2(Mathf.Max(1f, len * DashesPerUnit), 1f);
        }

        public void Hide()
        {
            if (_instance == null) return;
            HideNow();
        }

        private void HideNow()
        {
            _active = false;
            if (_line != null) _line.enabled = false;
            if (_icon != null) _icon.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_active || _lineMat == null) return;
            _offset -= ScrollSpeed * Time.deltaTime;
            _lineMat.mainTextureOffset = new Vector2(_offset, 0f);
        }

        private static Texture2D s_dash;
        private static Texture2D DashTex()
        {
            if (s_dash != null) return s_dash;
            const int W = 8;
            s_dash = new Texture2D(W, 1, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[W];
            for (int x = 0; x < W; x++)
                px[x] = x < 4 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            s_dash.SetPixels32(px);
            s_dash.Apply();
            return s_dash;
        }
    }
}

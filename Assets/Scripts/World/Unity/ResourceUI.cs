// ResourceUI.cs  (MonoBehaviour — RTSCL.World.Unity)
// Builds the top resource-counter HUD bar entirely at runtime (no prefab/UGUI asset).
// Each ResourceKind gets a cell: [icon | count text]. Population gets a matching cell
// anchored to the right edge. A shared tooltip (UIHoverLabel) appears below each cell
// on hover.
//
// Built-once on OnEnable via BuildBar() (idempotent via _built flag).
// Updates driven by static events: ResourceBank.OnChanged + PopulationManager.OnChanged.
//
// Where to adjust:
//   - Add/remove tracked resources: edit the _kinds list in the Inspector.
//   - Cell spacing: _cellWidth. Left/right padding: _pad.
//   - Icon & text sizes: _iconSize / _fontSize.
//   - Population icon: assign _populationIcon in the Inspector.
//   - Tooltip look: BuildTooltip() — background Image.color, font size 16.
//   Note: Runtime UI uses sprite = null (flat colored rects) for backgrounds;
//   builtin UISprite returns null in this project. Cell backgrounds are transparent (alpha 0).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    /// <summary>Top-bar HUD showing one cell per ResourceKind plus a population cell.
    /// Entire hierarchy is built in code; no UI prefab required.</summary>
    public sealed class ResourceUI : MonoBehaviour
    {
        /// <summary>Inspector binding: maps a ResourceKind to its icon sprite.</summary>
        [System.Serializable]
        public sealed class KindIcon
        {
            public ResourceKind Kind;
            public Sprite Icon;
        }

        [SerializeField] private RectTransform _countersRoot; // full-width top bar
        [SerializeField] private Font _font;
        [SerializeField] private List<KindIcon> _kinds = new();
        [SerializeField] private Sprite _populationIcon;

        [Header("Layout")]
        [SerializeField] private float _iconSize = 24f;
        [SerializeField] private int _fontSize = 18;
        [SerializeField] private float _pad = 12f;       // left/right edge padding
        [SerializeField] private float _cellWidth = 92f; // horizontal step per resource cell
        [SerializeField] private float _countWidth = 56f;
        [SerializeField] private float _popWidth = 70f;

        private readonly Dictionary<ResourceKind, Text> _texts = new();
        private Text _popText;
        private bool _built;  // prevents rebuilding the hierarchy on re-enable

        private RectTransform _tipRt;
        private Text _tipText;

        private void OnEnable()
        {
            BuildBar();
            // Subscribe to static events so the UI updates reactively without polling.
            ResourceBank.OnChanged += OnResourceChanged;
            PopulationManager.OnChanged += UpdatePopulation;
            // Sync current values immediately in case events fired while disabled.
            foreach (var ki in _kinds)
                if (_texts.TryGetValue(ki.Kind, out var t)) t.text = ResourceBank.Get(ki.Kind).ToString();
            UpdatePopulation();
        }

        private void OnDisable()
        {
            ResourceBank.OnChanged -= OnResourceChanged;
            PopulationManager.OnChanged -= UpdatePopulation;
        }

        /// <summary>Creates the entire HUD hierarchy once. Safe to call repeatedly — guarded by _built.</summary>
        private void BuildBar()
        {
            if (_countersRoot == null || _built) return;
            _built = true;

            BuildTooltip();

            // Resource cells, left to right, anchored to left edge of the bar.
            for (int i = 0; i < _kinds.Count; i++)
            {
                var ki = _kinds[i];
                float x = _pad + i * _cellWidth;
                var cell = MakeCell($"{ki.Kind}Cell", anchorRight: false, x, _iconSize + 4f + _countWidth, ki.Kind.ToString());
                AddIcon(cell, ki.Icon, 0f);
                var txt = AddText(cell, _iconSize + 4f, _countWidth, TextAnchor.MiddleLeft);
                txt.text = "0";
                _texts[ki.Kind] = txt;
            }

            // Population cell, anchored to the right edge: [farmer icon] [used / cap].
            // Pushed further left from the edge so it isn't crammed against the corner.
            const float popRightMargin = 80f;
            float popCellW = _iconSize + 4f + _popWidth;
            var popCell = MakeCell("PopCell", anchorRight: true, -(_pad + popRightMargin), popCellW, "Population");
            AddIcon(popCell, _populationIcon, 0f);
            _popText = AddText(popCell, _iconSize + 4f, _popWidth, TextAnchor.MiddleLeft);
        }

        /// <summary>Creates a transparent Image container cell with a UIHoverLabel attached.
        /// anchorRight=true anchors to the right edge of _countersRoot (for the pop cell).</summary>
        private RectTransform MakeCell(string name, bool anchorRight, float x, float width, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_countersRoot, false);
            var rt = (RectTransform)go.transform;
            float ax = anchorRight ? 1f : 0f;
            // Anchor and pivot on the same side so anchoredPosition offsets are intuitive.
            rt.anchorMin = new Vector2(ax, 0.5f);
            rt.anchorMax = new Vector2(ax, 0.5f);
            rt.pivot = new Vector2(anchorRight ? 1f : 0f, 0.5f);
            rt.sizeDelta = new Vector2(width, _iconSize + 8f);
            rt.anchoredPosition = new Vector2(x, 0f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // transparent hitbox — still receives pointer events
            img.raycastTarget = true;
            go.AddComponent<UIHoverLabel>().Init(this, label);
            return rt;
        }

        /// <summary>Adds a non-raycasting icon Image as a child of <paramref name="cell"/>,
        /// left-anchored at <paramref name="localX"/> with preserveAspect enabled.</summary>
        private void AddIcon(RectTransform cell, Sprite sprite, float localX)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(cell, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(_iconSize, _iconSize);
            rt.anchoredPosition = new Vector2(localX, 0f);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;  // clicks pass through to the cell's hitbox
        }

        /// <summary>Adds a Text counter child to <paramref name="cell"/>,
        /// offset by <paramref name="localX"/> from the cell's left edge.</summary>
        private Text AddText(RectTransform cell, float localX, float width, TextAnchor align)
        {
            var go = new GameObject("Count", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(cell, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, _iconSize + 4f);
            rt.anchoredPosition = new Vector2(localX, 0f);
            var txt = go.GetComponent<Text>();
            txt.font = _font;
            txt.fontSize = _fontSize;
            txt.color = Color.white;
            txt.alignment = align;
            // Allow overflow so long numbers don't clip.
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
            txt.text = "";
            return txt;
        }

        /// <summary>Creates the shared tooltip panel at Canvas root level (above all cells).
        /// Pivot (0.5, 1) so it drops below the hovered cell's bottom edge.
        /// Background is a flat dark rect — builtin UISprite returns null here.</summary>
        private void BuildTooltip()
        {
            // Parent to the Canvas root so the tooltip renders above all HUD elements.
            var canvas = _countersRoot.GetComponentInParent<Canvas>();
            var parent = canvas != null ? canvas.transform : _countersRoot.parent;

            var go = new GameObject("ResourceTooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            _tipRt = (RectTransform)go.transform;
            // pivot.y=1 means the RectTransform.position aligns to the tooltip's top edge,
            // so setting position to the cell's bottom places it just below.
            _tipRt.pivot = new Vector2(0.5f, 1f);
            _tipRt.sizeDelta = new Vector2(90f, 24f);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.85f);
            bg.raycastTarget = false;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtGo.transform.SetParent(_tipRt, false);
            var trt = (RectTransform)txtGo.transform;
            // Stretch to fill the background with 8px horizontal and 2px vertical padding.
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 2f); trt.offsetMax = new Vector2(-8f, -2f);
            _tipText = txtGo.GetComponent<Text>();
            _tipText.font = _font;
            _tipText.fontSize = 16;
            _tipText.color = Color.white;
            _tipText.alignment = TextAnchor.MiddleCenter;
            _tipText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _tipText.verticalOverflow = VerticalWrapMode.Overflow;
            _tipText.raycastTarget = false;

            go.transform.SetAsLastSibling();  // render on top of all siblings
            go.SetActive(false);
        }

        /// <summary>Sizes the tooltip to fit its text and positions it below the hovered cell.
        /// Called by UIHoverLabel.OnPointerEnter.</summary>
        public void ShowTooltip(string label, RectTransform cell)
        {
            if (_tipRt == null) return;
            _tipText.text = label;
            _tipRt.sizeDelta = new Vector2(Mathf.Max(60f, _tipText.preferredWidth + 16f), 24f);

            var corners = new Vector3[4];
            cell.GetWorldCorners(corners); // 0=BL, 1=TL, 2=TR, 3=BR (world/screen space)
            float centerX = (corners[0].x + corners[2].x) * 0.5f;
            float bottomY = corners[0].y;
            // Place 4px below the cell's bottom edge; pivot.y=1 aligns the top of the tooltip here.
            _tipRt.position = new Vector3(centerX, bottomY - 4f, 0f);
            _tipRt.gameObject.SetActive(true);
        }

        /// <summary>Hides the shared tooltip. Called by UIHoverLabel.OnPointerExit.</summary>
        public void HideTooltip()
        {
            if (_tipRt != null) _tipRt.gameObject.SetActive(false);
        }

        private void OnResourceChanged(ResourceKind kind, int value)
        {
            if (_texts.TryGetValue(kind, out var t)) t.text = value.ToString();
        }

        private void UpdatePopulation()
        {
            if (_popText != null)
                _popText.text = $"{PopulationManager.Used} / {PopulationManager.Cap}";
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    public sealed class ResourceUI : MonoBehaviour
    {
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
        private bool _built;

        private RectTransform _tipRt;
        private Text _tipText;

        private void OnEnable()
        {
            BuildBar();
            ResourceBank.OnChanged += OnResourceChanged;
            PopulationManager.OnChanged += UpdatePopulation;
            foreach (var ki in _kinds)
                if (_texts.TryGetValue(ki.Kind, out var t)) t.text = ResourceBank.Get(ki.Kind).ToString();
            UpdatePopulation();
        }

        private void OnDisable()
        {
            ResourceBank.OnChanged -= OnResourceChanged;
            PopulationManager.OnChanged -= UpdatePopulation;
        }

        private void BuildBar()
        {
            if (_countersRoot == null || _built) return;
            _built = true;

            BuildTooltip();

            // Resource cells, left to right.
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
            float popCellW = _iconSize + 4f + _popWidth;
            var popCell = MakeCell("PopCell", anchorRight: true, -_pad, popCellW, "Population");
            AddIcon(popCell, _populationIcon, 0f);
            _popText = AddText(popCell, _iconSize + 4f, _popWidth, TextAnchor.MiddleLeft);
        }

        private RectTransform MakeCell(string name, bool anchorRight, float x, float width, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_countersRoot, false);
            var rt = (RectTransform)go.transform;
            float ax = anchorRight ? 1f : 0f;
            rt.anchorMin = new Vector2(ax, 0.5f);
            rt.anchorMax = new Vector2(ax, 0.5f);
            rt.pivot = new Vector2(anchorRight ? 1f : 0f, 0.5f);
            rt.sizeDelta = new Vector2(width, _iconSize + 8f);
            rt.anchoredPosition = new Vector2(x, 0f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // transparent hitbox
            img.raycastTarget = true;
            go.AddComponent<UIHoverLabel>().Init(this, label);
            return rt;
        }

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
            img.raycastTarget = false;
        }

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
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
            txt.text = "";
            return txt;
        }

        private void BuildTooltip()
        {
            var canvas = _countersRoot.GetComponentInParent<Canvas>();
            var parent = canvas != null ? canvas.transform : _countersRoot.parent;

            var go = new GameObject("ResourceTooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            _tipRt = (RectTransform)go.transform;
            _tipRt.pivot = new Vector2(0.5f, 1f);
            _tipRt.sizeDelta = new Vector2(90f, 24f);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.85f);
            bg.raycastTarget = false;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtGo.transform.SetParent(_tipRt, false);
            var trt = (RectTransform)txtGo.transform;
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

            go.transform.SetAsLastSibling();
            go.SetActive(false);
        }

        public void ShowTooltip(string label, RectTransform cell)
        {
            if (_tipRt == null) return;
            _tipText.text = label;
            _tipRt.sizeDelta = new Vector2(Mathf.Max(60f, _tipText.preferredWidth + 16f), 24f);

            var corners = new Vector3[4];
            cell.GetWorldCorners(corners); // 0=BL,1=TL,2=TR,3=BR
            float centerX = (corners[0].x + corners[2].x) * 0.5f;
            float bottomY = corners[0].y;
            _tipRt.position = new Vector3(centerX, bottomY - 4f, 0f);
            _tipRt.gameObject.SetActive(true);
        }

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

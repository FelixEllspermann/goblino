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

            // Resource cells, left to right.
            for (int i = 0; i < _kinds.Count; i++)
            {
                var ki = _kinds[i];
                float x = _pad + i * _cellWidth;
                MakeIcon($"{ki.Kind}Icon", ki.Icon, anchorRight: false, x);
                var txt = MakeText($"{ki.Kind}Count", anchorRight: false, x + _iconSize + 4f, _countWidth, TextAnchor.MiddleLeft);
                txt.text = "0";
                _texts[ki.Kind] = txt;
            }

            // Population cell, anchored to the right edge: [farmer icon] [used / cap].
            _popText = MakeText("PopCount", anchorRight: true, -_pad, _popWidth, TextAnchor.MiddleRight);
            MakeIcon("PopIcon", _populationIcon, anchorRight: true, -(_pad + _popWidth + 4f));
        }

        private void MakeIcon(string name, Sprite sprite, bool anchorRight, float x)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_countersRoot, false);
            var rt = (RectTransform)go.transform;
            float ax = anchorRight ? 1f : 0f;
            rt.anchorMin = new Vector2(ax, 0.5f);
            rt.anchorMax = new Vector2(ax, 0.5f);
            rt.pivot = new Vector2(anchorRight ? 1f : 0f, 0.5f);
            rt.sizeDelta = new Vector2(_iconSize, _iconSize);
            rt.anchoredPosition = new Vector2(x, 0f);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        private Text MakeText(string name, bool anchorRight, float x, float width, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(_countersRoot, false);
            var rt = (RectTransform)go.transform;
            float ax = anchorRight ? 1f : 0f;
            rt.anchorMin = new Vector2(ax, 0.5f);
            rt.anchorMax = new Vector2(ax, 0.5f);
            rt.pivot = new Vector2(anchorRight ? 1f : 0f, 0.5f);
            rt.sizeDelta = new Vector2(width, _iconSize + 4f);
            rt.anchoredPosition = new Vector2(x, 0f);
            var txt = go.GetComponent<Text>();
            txt.font = _font;
            txt.fontSize = _fontSize;
            txt.color = Color.white;
            txt.alignment = align;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.text = "";
            return txt;
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

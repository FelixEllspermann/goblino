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

        [SerializeField] private RectTransform _countersRoot;
        [SerializeField] private Text _populationLabel;
        [SerializeField] private Font _font;
        [SerializeField] private List<KindIcon> _kinds = new();

        [Header("Layout")]
        [SerializeField] private float _rowHeight = 28f;
        [SerializeField] private float _iconSize = 20f;
        [SerializeField] private int _fontSize = 20;

        private readonly Dictionary<ResourceKind, Text> _texts = new();

        private void OnEnable()
        {
            BuildCounters();
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

        private void BuildCounters()
        {
            if (_countersRoot == null || _texts.Count > 0) return;
            for (int i = 0; i < _kinds.Count; i++)
            {
                var ki = _kinds[i];
                float y = -i * _rowHeight;

                var iconGo = new GameObject($"{ki.Kind}Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(_countersRoot, false);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = new Vector2(0f, 1f); irt.anchorMax = new Vector2(0f, 1f); irt.pivot = new Vector2(0f, 1f);
                irt.sizeDelta = new Vector2(_iconSize, _iconSize);
                irt.anchoredPosition = new Vector2(0f, y);
                var img = iconGo.GetComponent<Image>();
                img.sprite = ki.Icon; img.preserveAspect = true; img.raycastTarget = false;

                var txtGo = new GameObject($"{ki.Kind}Count", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                txtGo.transform.SetParent(_countersRoot, false);
                var trt = (RectTransform)txtGo.transform;
                trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 1f);
                trt.sizeDelta = new Vector2(90f, _iconSize);
                trt.anchoredPosition = new Vector2(_iconSize + 6f, y);
                var txt = txtGo.GetComponent<Text>();
                txt.font = _font; txt.fontSize = _fontSize; txt.color = Color.white;
                txt.alignment = TextAnchor.MiddleLeft; txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.text = "0";
                _texts[ki.Kind] = txt;
            }
        }

        private void OnResourceChanged(ResourceKind kind, int value)
        {
            if (_texts.TryGetValue(kind, out var t)) t.text = value.ToString();
        }

        private void UpdatePopulation()
        {
            if (_populationLabel != null)
                _populationLabel.text = $"{PopulationManager.Used} / {PopulationManager.Cap}";
        }
    }
}

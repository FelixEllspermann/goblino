// UITooltip.cs — one shared hover tooltip for the whole UI (build menu, inspector cards, etc).
// Lazily builds a dark panel on the first Show(), parents it to the top Canvas, sizes it to fit the text
// (preferred size + padding, clamped to a max width with wrapping), and follows the mouse. raycastTarget
// is off so it never eats clicks/hover. Call UITooltip.Show(text, font) on pointer-enter, UITooltip.Hide() on exit.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    /// <summary>Process-wide hover tooltip. Auto-creates itself; size adapts to the text.</summary>
    public sealed class UITooltip : MonoBehaviour
    {
        private const float MaxWidth = 320f;     // wrap beyond this
        private const float PadX = 12f, PadY = 8f;
        private const float CursorOffset = 16f;

        private static UITooltip _instance;
        private RectTransform _rect;
        private Text _text;
        private Font _font;
        private bool _visible;

        /// <summary>Show the tooltip with <paramref name="text"/>. <paramref name="font"/> is used the first
        /// time the panel is built (callers pass their card font). No-op for empty text.</summary>
        public static void Show(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) { Hide(); return; }
            EnsureInstance(font);
            if (_instance == null) return;
            _instance.ShowImpl(text);
        }

        public static void Hide()
        {
            if (_instance != null) _instance.HideImpl();
        }

        private static void EnsureInstance(Font font)
        {
            if (_instance != null) return;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            var go = new GameObject("UITooltip", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _instance = go.AddComponent<UITooltip>();
            _instance._font = font;
            _instance.Build();
        }

        private void Build()
        {
            _rect = (RectTransform)transform;
            _rect.pivot = new Vector2(0f, 0f);

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);
            bg.raycastTarget = false;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(PadX, PadY); tr.offsetMax = new Vector2(-PadX, -PadY);
            _text = txtGo.AddComponent<Text>();
            _text.font = _font;
            _text.fontSize = 14;
            _text.color = Color.white;
            _text.alignment = TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.raycastTarget = false;

            gameObject.SetActive(false);
        }

        private void ShowImpl(string text)
        {
            _text.text = text;
            // Size to content: measure unconstrained preferred width, clamp to MaxWidth, then measure
            // wrapped preferred height at that width.
            float prefW = _text.preferredWidth;
            float w = Mathf.Min(prefW, MaxWidth - 2f * PadX);
            _text.rectTransform.sizeDelta = new Vector2(w, _text.rectTransform.sizeDelta.y);
            float h = _text.preferredHeight;
            _rect.sizeDelta = new Vector2(w + 2f * PadX, h + 2f * PadY);
            gameObject.SetActive(true);
            _visible = true;
            transform.SetAsLastSibling();
            Position();
        }

        private void HideImpl()
        {
            _visible = false;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_visible) Position();
        }

        private void Position()
        {
            if (Mouse.current == null) return;
            Vector2 m = Mouse.current.position.ReadValue();
            float w = _rect.sizeDelta.x, h = _rect.sizeDelta.y;
            float x = Mathf.Min(m.x + CursorOffset, Screen.width - w - 4f);
            float y = Mathf.Min(m.y + CursorOffset, Screen.height - h - 4f);
            _rect.position = new Vector3(Mathf.Max(4f, x), Mathf.Max(4f, y), 0f);
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }
    }
}

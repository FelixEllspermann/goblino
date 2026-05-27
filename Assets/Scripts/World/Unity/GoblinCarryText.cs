using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Floating "X" text above a Farmer showing carried wood count.
    /// Visible only when CarriedWood > 0 AND the Goblin is owned locally (or solo).</summary>
    public sealed class GoblinCarryText : MonoBehaviour
    {
        private const float YOffset = 1.05f;
        private const int FontSize = 28;

        private Goblin _goblin;
        private TextMesh _label;
        private MeshRenderer _renderer;

        public static GoblinCarryText AttachTo(Goblin owner)
        {
            var go = new GameObject("CarryText");
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = new Vector3(0f, YOffset, 0f);
            var ct = go.AddComponent<GoblinCarryText>();
            ct._goblin = owner;
            return ct;
        }

        private void Awake()
        {
            _label = gameObject.AddComponent<TextMesh>();
            _label.text = "";
            _label.fontSize = FontSize;
            _label.characterSize = 0.04f;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = new Color(0.95f, 0.85f, 0.45f);
            _renderer = GetComponent<MeshRenderer>();
            _renderer.sortingOrder = 31;
            _renderer.enabled = false;
        }

        private void LateUpdate()
        {
            if (_goblin == null) { _renderer.enabled = false; return; }
            bool isLocal = _goblin.Owner == WorldStartContext.LocalPlayer || _goblin.Owner == 0UL;
            bool show = isLocal && _goblin.CarriedWood > 0;
            if (_renderer.enabled != show) _renderer.enabled = show;
            if (show) _label.text = _goblin.CarriedWood.ToString();
        }
    }
}

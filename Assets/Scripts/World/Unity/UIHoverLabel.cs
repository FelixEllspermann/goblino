using UnityEngine;
using UnityEngine.EventSystems;

namespace RTSCL.World.Unity
{
    /// <summary>Attached to a HUD cell. On pointer enter/exit, shows/hides the shared
    /// ResourceUI tooltip with this cell's label.</summary>
    public sealed class UIHoverLabel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private ResourceUI _owner;
        private string _label;

        public void Init(ResourceUI owner, string label)
        {
            _owner = owner;
            _label = label;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (_owner != null) _owner.ShowTooltip(_label, (RectTransform)transform);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (_owner != null) _owner.HideTooltip();
        }
    }
}

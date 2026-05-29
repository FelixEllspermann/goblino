// UIHoverLabel.cs  (MonoBehaviour — RTSCL.World.Unity)
// Thin tooltip trigger added to every resource/population cell by ResourceUI.BuildBar().
// Delegates to ResourceUI.ShowTooltip / HideTooltip — no tooltip state of its own.
// Requires an EventSystem in the scene with InputSystemUIInputModule (New Input System).

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

        /// <summary>Called by ResourceUI after attaching this component to a cell GameObject.</summary>
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

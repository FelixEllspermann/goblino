using UnityEngine;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    public sealed class ResourceUI : MonoBehaviour
    {
        [SerializeField] private Text _woodLabel;

        private void OnEnable()
        {
            ResourceBank.OnWoodChanged += UpdateLabel;
            UpdateLabel(ResourceBank.Wood);
        }

        private void OnDisable()
        {
            ResourceBank.OnWoodChanged -= UpdateLabel;
        }

        private void UpdateLabel(int wood)
        {
            if (_woodLabel != null) _woodLabel.text = wood.ToString();
        }
    }
}

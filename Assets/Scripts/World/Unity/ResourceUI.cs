using UnityEngine;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    public sealed class ResourceUI : MonoBehaviour
    {
        [SerializeField] private Text _woodLabel;
        [SerializeField] private Text _populationLabel;

        private void OnEnable()
        {
            ResourceBank.OnWoodChanged += UpdateWood;
            PopulationManager.OnChanged += UpdatePopulation;
            UpdateWood(ResourceBank.Wood);
            UpdatePopulation();
        }

        private void OnDisable()
        {
            ResourceBank.OnWoodChanged -= UpdateWood;
            PopulationManager.OnChanged -= UpdatePopulation;
        }

        private void UpdateWood(int wood)
        {
            if (_woodLabel != null) _woodLabel.text = wood.ToString();
        }

        private void UpdatePopulation()
        {
            if (_populationLabel != null)
                _populationLabel.text = $"{PopulationManager.Used} / {PopulationManager.Cap}";
        }
    }
}

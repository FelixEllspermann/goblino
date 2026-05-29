using UnityEngine;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    public sealed class ResourceUI : MonoBehaviour
    {
        [SerializeField] private Image _woodIcon;
        [SerializeField] private Image _foodIcon;
        [SerializeField] private Text _woodLabel;
        [SerializeField] private Text _foodLabel;
        [SerializeField] private Text _populationLabel;

        private void OnEnable()
        {
            ResourceBank.OnWoodChanged += UpdateWood;
            ResourceBank.OnFoodChanged += UpdateFood;
            PopulationManager.OnChanged += UpdatePopulation;
            UpdateWood(ResourceBank.Wood);
            UpdateFood(ResourceBank.Food);
            UpdatePopulation();
        }

        private void OnDisable()
        {
            ResourceBank.OnWoodChanged -= UpdateWood;
            ResourceBank.OnFoodChanged -= UpdateFood;
            PopulationManager.OnChanged -= UpdatePopulation;
        }

        private void UpdateWood(int wood)
        {
            if (_woodLabel != null) _woodLabel.text = wood.ToString();
        }

        private void UpdateFood(int food)
        {
            if (_foodLabel != null) _foodLabel.text = food.ToString();
        }

        private void UpdatePopulation()
        {
            if (_populationLabel != null)
                _populationLabel.text = $"{PopulationManager.Used} / {PopulationManager.Cap}";
        }
    }
}

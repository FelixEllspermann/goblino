using UnityEngine;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    public sealed class BuildingPaletteUI : MonoBehaviour
    {
        [SerializeField] private BuildingCatalog _catalog;
        [SerializeField] private BuildingPlacer _placer;
        [SerializeField] private RectTransform _buttonContainer;
        [SerializeField] private GameObject _buttonPrefab;   // optional: a Button prefab with an Image child

        private void Start()
        {
            if (_catalog == null || _placer == null || _buttonContainer == null) return;

            foreach (var def in _catalog.Buildings)
            {
                if (def == null) continue;
                var btn = CreateButton(def);
                btn.onClick.AddListener(() => _placer.Select(def));
            }
        }

        private Button CreateButton(BuildingDefinition def)
        {
            GameObject go;
            if (_buttonPrefab != null)
            {
                go = Instantiate(_buttonPrefab, _buttonContainer, false);
            }
            else
            {
                go = new GameObject($"Btn_{def.name}",
                    typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_buttonContainer, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(56, 56);
            }

            var img = go.GetComponentInChildren<Image>();
            if (img != null && def.Sprite != null)
            {
                img.sprite = def.Sprite;
                img.preserveAspect = true;
            }

            var btn = go.GetComponent<Button>();
            return btn;
        }
    }
}

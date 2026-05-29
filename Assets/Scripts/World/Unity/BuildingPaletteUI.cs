// BuildingPaletteUI.cs  (MonoBehaviour — RTSCL.World.Unity)
// Spawns one Button per BuildingDefinition listed in BuildingCatalog.
// Clicking a button calls BuildingPlacer.Select(def), entering placement mode.
// Buttons are created at Start() — the catalog list drives the UI order.
//
// Where to adjust:
//   - Add/remove buildable structures: edit BuildingCatalog.asset.
//   - Button appearance: assign _buttonPrefab (optional). Without a prefab, a 56×56
//     flat Image+Button is created. The first Image child receives the building sprite.
//   - Button size (fallback): set rt.sizeDelta in CreateButton().
//   - Layout: configure HorizontalLayoutGroup or similar on _buttonContainer in the scene.

using UnityEngine;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    /// <summary>Populates the building palette strip with one button per BuildingDefinition
    /// in the catalog. Each button triggers BuildingPlacer.Select() on click.</summary>
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
                // Capture def for the lambda to avoid closure-over-loop-variable issues.
                btn.onClick.AddListener(() => _placer.Select(def));
            }
        }

        /// <summary>Instantiates or constructs a Button for <paramref name="def"/> and
        /// sets its Image sprite to the building's preview sprite.</summary>
        private Button CreateButton(BuildingDefinition def)
        {
            GameObject go;
            if (_buttonPrefab != null)
            {
                // Use the designer-authored prefab if provided.
                go = Instantiate(_buttonPrefab, _buttonContainer, false);
            }
            else
            {
                // Fallback: minimal 56×56 Image+Button with no visual background.
                go = new GameObject($"Btn_{def.name}",
                    typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_buttonContainer, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(56, 56);
            }

            // Assign the building sprite to the first Image found in the hierarchy.
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

// MatchManager.cs — Win/lose for solo matches. A faction is eliminated when it owns zero buildings.
// The player (LocalPlayer / owner 0 in solo) loses when it has no buildings; it wins when every other
// participating faction has none. On game over the game pauses (Time.timeScale = 0) and a full-screen
// VICTORY/DEFEAT overlay with a "Back to Menu" button appears.
//
// MainBaseSetup.OnNewWorld calls Begin(owners) after spawning, and resets Time.timeScale on a new world.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    /// <summary>Tracks faction survival (by building ownership) and shows the game-over overlay.</summary>
    public sealed class MatchManager : MonoBehaviour
    {
        [SerializeField] private BuildingPlacer _placer;
        [SerializeField] private Font _font;
        [SerializeField] private float _checkInterval = 0.5f;

        private readonly List<ulong> _factions = new();
        private bool _over;
        private float _t;

        /// <summary>Start tracking the given participating factions (player + bots). Resets game-over state.</summary>
        public void Begin(IEnumerable<ulong> owners)
        {
            _factions.Clear();
            _factions.AddRange(owners);
            _over = false;
            Time.timeScale = 1f;
        }

        private void Update()
        {
            if (_over || _placer == null || _factions.Count == 0) return;
            _t += Time.unscaledDeltaTime;
            if (_t < _checkInterval) return;
            _t = 0f;

            ulong me = WorldStartContext.LocalPlayer;   // 0 in solo
            bool playerAlive = _placer.HasAnyBuilding(me);
            bool anyEnemyAlive = false;
            foreach (var o in _factions)
            {
                if (o == me) continue;
                if (_placer.HasAnyBuilding(o)) { anyEnemyAlive = true; break; }
            }

            if (!playerAlive) GameOver(false);
            else if (!anyEnemyAlive) GameOver(true);
        }

        private void GameOver(bool victory)
        {
            _over = true;
            Time.timeScale = 0f;
            BuildOverlay(victory);
        }

        private void BuildOverlay(bool victory)
        {
            var canvasGo = new GameObject("GameOverCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Dim full-screen backdrop.
            var dim = new GameObject("Dim", typeof(RectTransform));
            dim.transform.SetParent(canvasGo.transform, false);
            var drt = dim.GetComponent<RectTransform>();
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;
            var dimg = dim.AddComponent<Image>();
            dimg.color = new Color(0f, 0f, 0f, 0.72f);

            // Title.
            MakeText(canvasGo.transform, victory ? "VICTORY" : "DEFEAT", 64, new Vector2(0, 60), new Vector2(600, 100),
                     victory ? new Color(0.5f, 0.9f, 0.4f) : new Color(0.9f, 0.4f, 0.4f));

            // Back-to-menu button.
            var btnGo = new GameObject("BackButton", typeof(RectTransform));
            btnGo.transform.SetParent(canvasGo.transform, false);
            var brt = btnGo.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(320, 60); brt.anchoredPosition = new Vector2(0, -60);
            var bimg = btnGo.AddComponent<Image>();
            bimg.color = new Color(0.25f, 0.45f, 0.70f, 1f);
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = bimg;
            btn.onClick.AddListener(BackToMenu);
            MakeText(btnGo.transform, "Back to Menu", 24, Vector2.zero, new Vector2(320, 60), Color.white);
        }

        private void MakeText(Transform parent, string content, int size, Vector2 pos, Vector2 sz, Color col)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = sz; rt.anchoredPosition = pos;
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size; t.color = col; t.alignment = TextAnchor.MiddleCenter; t.text = content;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void BackToMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("MainMenu");
        }
    }
}

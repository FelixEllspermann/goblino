// SoloSetupPanel.cs
// Role: The "Play Solo" setup screen — choose a seed (blank = random) and the number of AI bots,
//       then Start loads SampleScene with those settings.
//
// How it fits:
//   MainPanel.OnPlaySolo navigates here via MenuRoot.ShowSoloSetup(). Start writes
//   WorldGeneratorBootstrap.PendingSeed (consumed at scene load) and WorldStartContext.SoloBotCount,
//   then loads the game scene directly (no networking, like the old solo path).
//
// Where to adjust:
//   • Max bots: MaxBots constant (clamped against available spawn points at spawn time too).
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RTSCL.Lobby
{
    /// <summary>Solo game setup: seed + bot count, then load SampleScene.</summary>
    public sealed class SoloSetupPanel : MonoBehaviour
    {
        private const int MaxBots = 3;

        [SerializeField] private MenuRoot _root;
        [SerializeField] private InputField _seedInput;
        [SerializeField] private Text _botCountLabel;
        [SerializeField] private Button _botMinusButton;
        [SerializeField] private Button _botPlusButton;
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _backButton;

        private int _bots = 1;

        private void OnEnable()
        {
            if (_botMinusButton != null) _botMinusButton.onClick.AddListener(BotsDown);
            if (_botPlusButton != null)  _botPlusButton.onClick.AddListener(BotsUp);
            if (_startButton != null)    _startButton.onClick.AddListener(OnStart);
            if (_backButton != null)     _backButton.onClick.AddListener(OnBack);
            RefreshBots();
        }

        private void OnDisable()
        {
            if (_botMinusButton != null) _botMinusButton.onClick.RemoveListener(BotsDown);
            if (_botPlusButton != null)  _botPlusButton.onClick.RemoveListener(BotsUp);
            if (_startButton != null)    _startButton.onClick.RemoveListener(OnStart);
            if (_backButton != null)     _backButton.onClick.RemoveListener(OnBack);
        }

        private void BotsUp()   { _bots = Mathf.Min(MaxBots, _bots + 1); RefreshBots(); }
        private void BotsDown() { _bots = Mathf.Max(0, _bots - 1); RefreshBots(); }
        private void RefreshBots() { if (_botCountLabel != null) _botCountLabel.text = $"Bots: {_bots}"; }

        private void OnStart()
        {
            // Blank/invalid seed → random (PendingSeed null → WorldGeneratorBootstrap picks one).
            int? seed = null;
            if (_seedInput != null && !string.IsNullOrWhiteSpace(_seedInput.text)
                && int.TryParse(_seedInput.text.Trim(), out int s))
                seed = Mathf.Abs(s);
            RTSCL.World.Unity.WorldGeneratorBootstrap.PendingSeed = seed;
            RTSCL.World.Unity.WorldStartContext.SoloBotCount = _bots;
            SceneManager.LoadScene("SampleScene");
        }

        private void OnBack() => _root?.ShowMain();
    }
}

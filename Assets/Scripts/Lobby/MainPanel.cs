// MainPanel.cs
// Role: The main-menu home screen (Play Solo / Multiplayer / Quit buttons).
//       Wires UI → game flow and disables the Multiplayer button when Steam is unavailable.
//
// How it fits:
//   MenuRoot activates/deactivates this panel's GameObject. OnEnable/OnDisable subscribe
//   and unsubscribe button listeners so no dangling listeners accumulate across panel swaps.
//
// Where to adjust:
//   • Adding a new top-level button (e.g. Settings): add a [SerializeField] Button field,
//     wire it in OnEnable/OnDisable, and add a handler like the existing ones.
//   • Solo scene name: change the string in OnPlaySolo if the scene is renamed.
//   • Steam availability message: edit the text literal in OnEnable.

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RTSCL.Lobby
{
    /// <summary>Main-menu home panel. Shown by default; navigates to MultiplayerPanel or
    /// directly loads SampleScene for solo play.</summary>
    public sealed class MainPanel : MonoBehaviour
    {
        [SerializeField] private MenuRoot _root;
        [SerializeField] private Button _playSoloButton;
        [SerializeField] private Button _multiplayerButton;
        [SerializeField] private Button _quitButton;
        // Shown below the Multiplayer button when Steam is not running.
        [SerializeField] private Text _multiplayerStatusLabel;

        private void OnEnable()
        {
            if (_playSoloButton != null)    _playSoloButton.onClick.AddListener(OnPlaySolo);
            if (_multiplayerButton != null) _multiplayerButton.onClick.AddListener(OnMultiplayer);
            if (_quitButton != null)        _quitButton.onClick.AddListener(OnQuit);

            // Gate multiplayer on Steam being ready — avoids confusing errors if the
            // player launches without Steam or if SteamAPI.InitEx failed.
            bool steamOk = SteamManager.Initialized;
            if (_multiplayerButton != null) _multiplayerButton.interactable = steamOk;
            if (_multiplayerStatusLabel != null)
                _multiplayerStatusLabel.text = steamOk ? string.Empty : "Steam not running";
        }

        private void OnDisable()
        {
            // Remove listeners to prevent double-registration when the panel is shown again.
            if (_playSoloButton != null)    _playSoloButton.onClick.RemoveListener(OnPlaySolo);
            if (_multiplayerButton != null) _multiplayerButton.onClick.RemoveListener(OnMultiplayer);
            if (_quitButton != null)        _quitButton.onClick.RemoveListener(OnQuit);
        }

        // Opens the solo setup panel (seed + bot count). Start there loads SampleScene.
        private void OnPlaySolo()    => _root?.ShowSoloSetup();
        private void OnMultiplayer() => _root?.ShowMultiplayer();
        private void OnQuit()
        {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
        }
    }
}

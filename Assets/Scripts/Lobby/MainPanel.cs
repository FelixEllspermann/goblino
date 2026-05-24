using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RTSCL.Lobby
{
    public sealed class MainPanel : MonoBehaviour
    {
        [SerializeField] private MenuRoot _root;
        [SerializeField] private Button _playSoloButton;
        [SerializeField] private Button _multiplayerButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private Text _multiplayerStatusLabel;

        private void OnEnable()
        {
            if (_playSoloButton != null)    _playSoloButton.onClick.AddListener(OnPlaySolo);
            if (_multiplayerButton != null) _multiplayerButton.onClick.AddListener(OnMultiplayer);
            if (_quitButton != null)        _quitButton.onClick.AddListener(OnQuit);

            bool steamOk = SteamManager.Initialized;
            if (_multiplayerButton != null) _multiplayerButton.interactable = steamOk;
            if (_multiplayerStatusLabel != null)
                _multiplayerStatusLabel.text = steamOk ? string.Empty : "Steam not running";
        }

        private void OnDisable()
        {
            if (_playSoloButton != null)    _playSoloButton.onClick.RemoveListener(OnPlaySolo);
            if (_multiplayerButton != null) _multiplayerButton.onClick.RemoveListener(OnMultiplayer);
            if (_quitButton != null)        _quitButton.onClick.RemoveListener(OnQuit);
        }

        private void OnPlaySolo()    => SceneManager.LoadScene("SampleScene");
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

using UnityEngine;

namespace RTSCL.Lobby
{
    /// <summary>Owns the three main-menu panels and switches between them in response
    /// to button clicks and LobbyManager events.</summary>
    public sealed class MenuRoot : MonoBehaviour
    {
        [SerializeField] private GameObject _mainPanel;
        [SerializeField] private GameObject _multiplayerPanel;
        [SerializeField] private GameObject _lobbyPanel;

        public void ShowMain()         => Show(_mainPanel);
        public void ShowMultiplayer()  => Show(_multiplayerPanel);
        public void ShowLobby()        => Show(_lobbyPanel);

        private void OnEnable()
        {
            LobbyManager.OnLobbyEntered += OnLobbyEntered;
            LobbyManager.OnLobbyLeft    += OnLobbyLeft;
        }

        private void OnDisable()
        {
            LobbyManager.OnLobbyEntered -= OnLobbyEntered;
            LobbyManager.OnLobbyLeft    -= OnLobbyLeft;
        }

        private void OnLobbyEntered(Steamworks.CSteamID _) => ShowLobby();
        private void OnLobbyLeft() => ShowMain();

        private void Show(GameObject panel)
        {
            if (_mainPanel != null)         _mainPanel.SetActive(panel == _mainPanel);
            if (_multiplayerPanel != null)  _multiplayerPanel.SetActive(panel == _multiplayerPanel);
            if (_lobbyPanel != null)        _lobbyPanel.SetActive(panel == _lobbyPanel);
        }
    }
}

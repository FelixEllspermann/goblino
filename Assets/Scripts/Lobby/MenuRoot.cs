// MenuRoot.cs
// Role: Top-level navigator for the three main-menu panels (Main / Multiplayer / Lobby).
//       Listens to LobbyManager events so the UI automatically transitions when the player
//       enters or leaves a lobby, without panels needing to reference each other directly.
//
// How it fits:
//   MainPanel calls _root.ShowMultiplayer().
//   MultiplayerPanel calls _root.ShowMain() (back button) or relies on OnLobbyEntered here.
//   LobbyPanel calls LobbyManager.LeaveLobby() → triggers OnLobbyLeft → ShowMain() here.
//
// Where to adjust:
//   • Adding a fourth panel (e.g. Settings): add a [SerializeField] field, a Show* method,
//     and a SetActive call in Show(). Subscribe to any relevant event in OnEnable.
//   • Lobby-auto-navigate: the navigation on OnLobbyEntered / OnLobbyLeft is intentional
//     so the host and joining client both land on LobbyPanel without extra cross-references.

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

        /// <summary>Activate the main home panel, hide the others.</summary>
        public void ShowMain()         => Show(_mainPanel);
        /// <summary>Activate the lobby-browser panel, hide the others.</summary>
        public void ShowMultiplayer()  => Show(_multiplayerPanel);
        /// <summary>Activate the in-lobby panel, hide the others.</summary>
        public void ShowLobby()        => Show(_lobbyPanel);

        private void OnEnable()
        {
            // Auto-navigate when Steam lobby state changes — e.g. OnLobbyEntered fires
            // both when creating and when joining an existing lobby.
            LobbyManager.OnLobbyEntered += OnLobbyEntered;
            LobbyManager.OnLobbyLeft    += OnLobbyLeft;
        }

        private void OnDisable()
        {
            LobbyManager.OnLobbyEntered -= OnLobbyEntered;
            LobbyManager.OnLobbyLeft    -= OnLobbyLeft;
        }

        // Lobby ID arg is unused here; the panel just needs to become visible.
        private void OnLobbyEntered(Steamworks.CSteamID _) => ShowLobby();
        private void OnLobbyLeft() => ShowMain();

        // Exactly one panel is active at a time. SetActive(false) on inactive panels is a
        // safe no-op and avoids null-checks on each transition.
        private void Show(GameObject panel)
        {
            if (_mainPanel != null)         _mainPanel.SetActive(panel == _mainPanel);
            if (_multiplayerPanel != null)  _multiplayerPanel.SetActive(panel == _multiplayerPanel);
            if (_lobbyPanel != null)        _lobbyPanel.SetActive(panel == _lobbyPanel);
        }
    }
}

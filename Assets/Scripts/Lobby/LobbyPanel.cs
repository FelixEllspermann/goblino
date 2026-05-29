// LobbyPanel.cs
// Role: In-lobby UI shown after a player creates or joins a lobby.
//       Displays the member list, enables Start Game for the host when all P2P
//       connections are established, and handles Leave.
//
// How it fits:
//   MenuRoot activates this panel via OnLobbyEntered → ShowLobby().
//   OnStart() is the authoritative host action: assigns spawn slots deterministically,
//   broadcasts GameStart over P2P, marks the lobby non-joinable, then fires
//   NetworkSession.RaiseGameStartReceived() so both host and clients load SampleScene.
//
// Start-button readiness logic:
//   The button becomes interactable only when NetworkManager.ConnectedCount >= memberCount-1,
//   meaning every non-host member has an open P2P connection. This prevents a race where the
//   host sends GameStart before all clients are reachable.
//
// Slot assignment (OnStart):
//   Host is always SpawnIndex 0. Remaining members are sorted ascending by SteamID for
//   a deterministic and fair ordering. The sorted list becomes NetworkSession.PlayerSlots.
//
// Where to adjust:
//   • Allow non-host to start:  remove the `if (!NetworkSession.IsHost) return;` guard
//     (not recommended — host has the P2P listen socket).
//   • Change spawn ordering:    modify the Sort lambda in OnStart.
//   • Lobby-joinable after game start: remove the SetLobbyJoinable(false) call if you
//     want late-joiners (requires additional reconnect logic).

using UnityEngine;
using UnityEngine.UI;
using Steamworks;

namespace RTSCL.Lobby
{
    /// <summary>In-lobby panel showing the member list and host Start Game control.</summary>
    public sealed class LobbyPanel : MonoBehaviour
    {
        [SerializeField] private MenuRoot _root;
        [SerializeField] private Text _headerLabel;
        // ScrollRect content RectTransform — member rows are parented here.
        [SerializeField] private RectTransform _memberListContent;
        [SerializeField] private Button _leaveButton;
        // Only interactable for the host once all P2P connections are established.
        [SerializeField] private Button _startButton;
        [SerializeField] private Font _rowFont;
        [SerializeField] private Sprite _rowSprite;

        private void OnEnable()
        {
            if (_leaveButton != null) _leaveButton.onClick.AddListener(OnLeave);
            if (_startButton != null) _startButton.onClick.AddListener(OnStart);
            LobbyManager.OnLobbyMembersChanged += Refresh;
            // Refresh the start-button state whenever a P2P connection opens or drops.
            NetworkManager.OnConnected         += OnConnectionsChanged;
            NetworkManager.OnDisconnected      += OnConnectionsChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (_leaveButton != null) _leaveButton.onClick.RemoveListener(OnLeave);
            if (_startButton != null) _startButton.onClick.RemoveListener(OnStart);
            LobbyManager.OnLobbyMembersChanged -= Refresh;
            NetworkManager.OnConnected         -= OnConnectionsChanged;
            NetworkManager.OnDisconnected      -= OnConnectionsChanged;
        }

        private void OnLeave() => LobbyManager.LeaveLobby();

        private void OnStart()
        {
            // Only the host can start; clients see "Waiting for host…" label.
            if (!NetworkSession.IsHost) return;

            // Build slot list: host first (SpawnIndex 0), then others sorted ascending by SteamID.
            // SteamID sort is deterministic across all clients — everyone will compute the same order.
            var members = LobbyManager.GetCurrentMembers();
            var ordered = new System.Collections.Generic.List<(Steamworks.CSteamID id, string name, bool isHost)>(members);
            ordered.Sort((a, b) =>
            {
                if (a.isHost != b.isHost) return a.isHost ? -1 : 1;
                return a.id.m_SteamID.CompareTo(b.id.m_SteamID);
            });
            var slots = new System.Collections.Generic.List<PlayerSlot>(ordered.Count);
            for (byte i = 0; i < ordered.Count; i++)
                slots.Add(new PlayerSlot { SteamId = ordered[i].id, SpawnIndex = i });

            // Generate and broadcast the game seed so all clients build an identical world.
            var seed = Random.Range(1, int.MaxValue);
            NetworkSession.GameSeed = seed;
            NetworkSession.PlayerSlots = slots;
            // SendToAll sends to all connected clients. The host applies locally via RaiseGameStartReceived.
            NetworkManager.SendToAll(NetMessages.PackGameStart(seed, slots));
            // Prevent new players from joining mid-game.
            Steamworks.SteamMatchmaking.SetLobbyJoinable(LobbyManager.CurrentLobby, false);
            // Host triggers its own scene load through the same event path as clients.
            NetworkSession.RaiseGameStartReceived();
        }

        // CSteamID arg is the newly connected/disconnected peer — unused here; we only need to re-check count.
        private void OnConnectionsChanged(CSteamID _) => RefreshStartButton();

        /// <summary>Rebuilds the header and member list. Called on panel enable and on lobby membership changes.</summary>
        private void Refresh()
        {
            if (!LobbyManager.InLobby) return;

            string host = SteamMatchmaking.GetLobbyData(LobbyManager.CurrentLobby, LobbyManager.LobbyDataHostName);
            if (string.IsNullOrEmpty(host)) host = "Unknown";
            if (_headerLabel != null) _headerLabel.text = $"{host}'s Lobby";

            if (_memberListContent != null)
            {
                for (int i = _memberListContent.childCount - 1; i >= 0; i--)
                    Destroy(_memberListContent.GetChild(i).gameObject);
                foreach (var (id, name, isHost) in LobbyManager.GetCurrentMembers())
                    BuildRow(name, isHost);
            }

            RefreshStartButton();
        }

        /// <summary>Updates Start button interactability and label text.
        /// Host is ready when ConnectedCount >= memberCount-1 (every non-host has a P2P link).</summary>
        private void RefreshStartButton()
        {
            if (_startButton == null) return;
            bool isHost = NetworkSession.IsHost;
            int memberCount = LobbyManager.InLobby
                ? SteamMatchmaking.GetNumLobbyMembers(LobbyManager.CurrentLobby)
                : 0;
            int connected = NetworkManager.ConnectedCount;
            // ready = host AND all other members connected via P2P
            bool ready = isHost && connected >= memberCount - 1;
            _startButton.interactable = ready;
            var label = _startButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                if (!isHost)
                    label.text = "Waiting for host…";
                else if (ready)
                    label.text = "Start Game";
                else
                    label.text = $"Waiting for {memberCount - 1 - connected} player(s)…";
            }
        }

        /// <summary>Procedurally builds a single member row: dark rounded card + name label.</summary>
        private void BuildRow(string name, bool isHost)
        {
            var row = new GameObject($"Member_{name}", typeof(RectTransform));
            row.transform.SetParent(_memberListContent, false);
            var bg = row.AddComponent<Image>();
            bg.sprite = _rowSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.18f, 0.18f, 0.22f, 0.95f);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 40;
            le.flexibleWidth = 1;

            var lbl = new GameObject("Label", typeof(RectTransform));
            lbl.transform.SetParent(row.transform, false);
            var lrt = lbl.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(12, 0); lrt.offsetMax = new Vector2(-12, 0);
            var txt = lbl.AddComponent<Text>();
            txt.text = isHost ? $"{name}  (host)" : name;
            txt.font = _rowFont; txt.fontSize = 16; txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }
}

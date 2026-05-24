using UnityEngine;
using UnityEngine.UI;
using Steamworks;

namespace RTSCL.Lobby
{
    public sealed class LobbyPanel : MonoBehaviour
    {
        [SerializeField] private MenuRoot _root;
        [SerializeField] private Text _headerLabel;
        [SerializeField] private RectTransform _memberListContent;
        [SerializeField] private Button _leaveButton;
        [SerializeField] private Button _startButton;
        [SerializeField] private Font _rowFont;
        [SerializeField] private Sprite _rowSprite;

        private void OnEnable()
        {
            if (_leaveButton != null) _leaveButton.onClick.AddListener(OnLeave);
            if (_startButton != null) _startButton.onClick.AddListener(OnStart);
            LobbyManager.OnLobbyMembersChanged += Refresh;
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
            if (!NetworkSession.IsHost) return;
            var seed = Random.Range(1, int.MaxValue);
            NetworkSession.GameSeed = seed;
            NetworkManager.SendToAll(NetMessages.PackGameStart(seed));
            SteamMatchmaking.SetLobbyJoinable(LobbyManager.CurrentLobby, false);
            NetworkSession.RaiseGameStartReceived();
        }

        private void OnConnectionsChanged(CSteamID _) => RefreshStartButton();

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

        private void RefreshStartButton()
        {
            if (_startButton == null) return;
            bool isHost = NetworkSession.IsHost;
            int memberCount = LobbyManager.InLobby
                ? SteamMatchmaking.GetNumLobbyMembers(LobbyManager.CurrentLobby)
                : 0;
            int connected = NetworkManager.ConnectedCount;
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

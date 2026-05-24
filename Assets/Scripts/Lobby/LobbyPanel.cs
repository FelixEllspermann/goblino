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
            // Start is intentionally disabled in this sub-project.
            if (_startButton != null) _startButton.interactable = false;
            LobbyManager.OnLobbyMembersChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (_leaveButton != null) _leaveButton.onClick.RemoveListener(OnLeave);
            LobbyManager.OnLobbyMembersChanged -= Refresh;
        }

        private void OnLeave() => LobbyManager.LeaveLobby();

        private void Refresh()
        {
            if (!LobbyManager.InLobby) return;

            // Header: "{HostName}'s Lobby"
            string host = SteamMatchmaking.GetLobbyData(LobbyManager.CurrentLobby, LobbyManager.LobbyDataHostName);
            if (string.IsNullOrEmpty(host)) host = "Unknown";
            if (_headerLabel != null) _headerLabel.text = $"{host}'s Lobby";

            // Member list
            if (_memberListContent == null) return;
            for (int i = _memberListContent.childCount - 1; i >= 0; i--)
                Destroy(_memberListContent.GetChild(i).gameObject);

            foreach (var (id, name, isHost) in LobbyManager.GetCurrentMembers())
                BuildRow(name, isHost);
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

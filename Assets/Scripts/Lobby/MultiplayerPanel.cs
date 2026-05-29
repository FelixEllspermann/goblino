// MultiplayerPanel.cs
// Role: Lobby-browser UI. Shows a scrollable list of public lobbies (filtered by game version),
//       lets the player create a new lobby or join an existing one.
//
// How it fits:
//   OnEnable triggers a lobby-list refresh immediately so the panel is never stale.
//   LobbyManager.OnLobbyListReceived → OnListReceived rebuilds the row list.
//   LobbyManager.OnError → OnError surfaces matchmaking failures without crashing.
//   Clicking "Join" in a row calls LobbyManager.JoinLobby — the LobbyEnter_t Steam
//   callback eventually fires, which MenuRoot catches to navigate to LobbyPanel.
//
// Where to adjust:
//   • Row height / colors: constants are inline in BuildRow (rle.preferredHeight = 48,
//     bg.color, joinImg.color) — extract to SerializeField if designer tuning is needed.
//   • Max-players filter: add SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable
//     inside LobbyManager.RequestLobbyList (not here) before RequestLobbyList() is called.
//   • Sort order: reorder `list` before the BuildRow loop in OnListReceived.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Steamworks;

namespace RTSCL.Lobby
{
    /// <summary>Panel that displays the public lobby browser and handles Create / Refresh / Join.</summary>
    public sealed class MultiplayerPanel : MonoBehaviour
    {
        [SerializeField] private MenuRoot _root;
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _createButton;
        [SerializeField] private Button _refreshButton;
        // ScrollRect's content RectTransform — rows are parented here.
        [SerializeField] private RectTransform _listContent;
        [SerializeField] private Text _emptyLabel;
        [SerializeField] private Text _errorLabel;
        // Assigned in the Inspector; used by every dynamically-created Text child in BuildRow.
        [SerializeField] private Font _rowFont;
        // Sliced sprite used for row backgrounds and the Join button background.
        [SerializeField] private Sprite _rowSprite;

        private void OnEnable()
        {
            if (_backButton != null)    _backButton.onClick.AddListener(OnBack);
            if (_createButton != null)  _createButton.onClick.AddListener(OnCreate);
            if (_refreshButton != null) _refreshButton.onClick.AddListener(OnRefresh);
            LobbyManager.OnLobbyListReceived += OnListReceived;
            LobbyManager.OnError += OnError;
            ClearError();
            // Kick off a refresh immediately so the list is populated when the panel appears.
            OnRefresh();
        }

        private void OnDisable()
        {
            if (_backButton != null)    _backButton.onClick.RemoveListener(OnBack);
            if (_createButton != null)  _createButton.onClick.RemoveListener(OnCreate);
            if (_refreshButton != null) _refreshButton.onClick.RemoveListener(OnRefresh);
            LobbyManager.OnLobbyListReceived -= OnListReceived;
            LobbyManager.OnError -= OnError;
        }

        private void OnBack()    => _root?.ShowMain();
        // CreateLobby is async — the result arrives via LobbyCreated_t then LobbyEnter_t.
        private void OnCreate()  { ClearError(); LobbyManager.CreateLobby(); }
        // RequestLobbyList is async — result arrives via LobbyMatchList_t → OnLobbyListReceived.
        private void OnRefresh() { ClearError(); LobbyManager.RequestLobbyList(); }

        private void OnError(string msg)
        {
            if (_errorLabel == null) return;
            _errorLabel.text = msg;
            _errorLabel.gameObject.SetActive(true);
        }

        private void ClearError()
        {
            if (_errorLabel == null) return;
            _errorLabel.text = string.Empty;
            _errorLabel.gameObject.SetActive(false);
        }

        /// <summary>Rebuilds the lobby row list. Called whenever LobbyManager receives a
        /// fresh LobbyMatchList_t result from Steam.</summary>
        private void OnListReceived(List<LobbyInfo> list)
        {
            if (_listContent == null) return;
            // Destroy existing rows before building new ones.
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            if (_emptyLabel != null) _emptyLabel.gameObject.SetActive(list.Count == 0);

            foreach (var info in list) BuildRow(info);
        }

        /// <summary>Procedurally builds a UI row for one lobby entry.
        /// Layout: [HorizontalLayoutGroup] → Name (flexible) + Join Button (fixed 96px wide).</summary>
        private void BuildRow(LobbyInfo info)
        {
            var row = new GameObject($"Lobby_{info.Id.m_SteamID}", typeof(RectTransform));
            row.transform.SetParent(_listContent, false);
            var bg = row.AddComponent<Image>();
            bg.sprite = _rowSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.18f, 0.18f, 0.22f, 0.95f);
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(12, 12, 8, 8);
            hl.spacing = 10;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childAlignment = TextAnchor.MiddleLeft;
            var rle = row.AddComponent<LayoutElement>();
            rle.preferredHeight = 48;
            rle.flexibleWidth = 1;

            var nameGo = new GameObject("Name", typeof(RectTransform));
            nameGo.transform.SetParent(row.transform, false);
            var nameText = nameGo.AddComponent<Text>();
            nameText.text = $"{info.HostName} — {info.CurrentMembers}/{info.MaxMembers}";
            nameText.font = _rowFont;
            nameText.fontSize = 16;
            nameText.color = Color.white;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var nameLE = nameGo.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1;

            var joinGo = new GameObject("Join", typeof(RectTransform));
            joinGo.transform.SetParent(row.transform, false);
            var joinImg = joinGo.AddComponent<Image>();
            joinImg.sprite = _rowSprite;
            joinImg.type = Image.Type.Sliced;
            joinImg.color = new Color(0.25f, 0.55f, 0.3f, 1f);
            var joinBtn = joinGo.AddComponent<Button>();
            joinBtn.targetGraphic = joinImg;
            var joinLE = joinGo.AddComponent<LayoutElement>();
            joinLE.preferredWidth = 96;
            joinLE.preferredHeight = 32;
            var lbl = new GameObject("Label", typeof(RectTransform));
            lbl.transform.SetParent(joinGo.transform, false);
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
            var lblTxt = lbl.AddComponent<Text>();
            lblTxt.text = "Join"; lblTxt.font = _rowFont; lblTxt.fontSize = 14;
            lblTxt.color = Color.white;
            lblTxt.alignment = TextAnchor.MiddleCenter;

            // Capture lobby ID in a local variable — the lambda must not close over `info`
            // directly because `info` changes across loop iterations.
            var capturedId = info.Id;
            joinBtn.onClick.AddListener(() => LobbyManager.JoinLobby(capturedId));
        }
    }
}

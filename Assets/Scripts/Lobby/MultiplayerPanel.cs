using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Steamworks;

namespace RTSCL.Lobby
{
    public sealed class MultiplayerPanel : MonoBehaviour
    {
        [SerializeField] private MenuRoot _root;
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _createButton;
        [SerializeField] private Button _refreshButton;
        [SerializeField] private RectTransform _listContent;
        [SerializeField] private Text _emptyLabel;
        [SerializeField] private Text _errorLabel;
        [SerializeField] private Font _rowFont;
        [SerializeField] private Sprite _rowSprite;

        private void OnEnable()
        {
            if (_backButton != null)    _backButton.onClick.AddListener(OnBack);
            if (_createButton != null)  _createButton.onClick.AddListener(OnCreate);
            if (_refreshButton != null) _refreshButton.onClick.AddListener(OnRefresh);
            LobbyManager.OnLobbyListReceived += OnListReceived;
            LobbyManager.OnError += OnError;
            ClearError();
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
        private void OnCreate()  { ClearError(); LobbyManager.CreateLobby(); }
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

        private void OnListReceived(List<LobbyInfo> list)
        {
            if (_listContent == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            if (_emptyLabel != null) _emptyLabel.gameObject.SetActive(list.Count == 0);

            foreach (var info in list) BuildRow(info);
        }

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

            var capturedId = info.Id;
            joinBtn.onClick.AddListener(() => LobbyManager.JoinLobby(capturedId));
        }
    }
}

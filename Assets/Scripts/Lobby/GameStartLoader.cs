using UnityEngine;
using UnityEngine.SceneManagement;

namespace RTSCL.Lobby
{
    /// <summary>Listens for NetworkSession.OnGameStartReceived and loads the SampleScene.
    /// Single source of truth for the host + client scene load.</summary>
    [DisallowMultipleComponent]
    public sealed class GameStartLoader : MonoBehaviour
    {
        private static GameStartLoader _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject(nameof(GameStartLoader));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameStartLoader>();
        }

        private void OnEnable()  => NetworkSession.OnGameStartReceived += LoadGameScene;
        private void OnDisable() => NetworkSession.OnGameStartReceived -= LoadGameScene;

        private void LoadGameScene() => SceneManager.LoadScene("SampleScene");
    }
}

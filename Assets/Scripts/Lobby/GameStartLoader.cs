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

        private void LoadGameScene()
        {
            // Push session data into the world-side bridge BEFORE the scene loads.
            RTSCL.World.Unity.WorldStartContext.LocalPlayer = NetworkSession.LocalPlayer.m_SteamID;
            RTSCL.World.Unity.WorldStartContext.GetPlayerColor = sid =>
                PlayerRegistry.GetColorForPlayer(new Steamworks.CSteamID(sid));

            // Translate PlayerSlots (List<PlayerSlot> with Steamworks types) into the
            // type-light ValueTuple array the world layer sees.
            var src = NetworkSession.PlayerSlots;
            if (src != null && src.Count > 0)
            {
                var arr = new (ulong steamId, int spawnIndex)[src.Count];
                for (int i = 0; i < src.Count; i++)
                {
                    arr[i] = (src[i].SteamId.m_SteamID, src[i].SpawnIndex);
                    // Mirror into PlayerRegistry so GetPlayerColor works after this point.
                    PlayerRegistry.Register(src[i].SteamId, src[i].SpawnIndex);
                }
                RTSCL.World.Unity.WorldStartContext.PendingSlots = arr;
            }
            else
            {
                RTSCL.World.Unity.WorldStartContext.PendingSlots = null;
            }

            // Push the seed (existing flow).
            RTSCL.World.Unity.WorldGeneratorBootstrap.PendingSeed = NetworkSession.GameSeed;

            SceneManager.LoadScene("SampleScene");
        }
    }
}

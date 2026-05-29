// GameStartLoader.cs
// Role: DontDestroyOnLoad singleton that bridges the lobby layer and the game world layer
//       at the moment a multiplayer game starts. It is the single source of truth for
//       both host and client scene loads.
//
// Trigger:
//   NetworkSession.OnGameStartReceived fires from:
//     • LobbyPanel.OnStart() (host path, after PackGameStart + SendToAll)
//     • NetworkManager.RouteMessage (client path, after TryUnpackGameStart)
//
// What LoadGameScene does:
//   1. Pushes LocalPlayer ulong into WorldStartContext (type-light, no Steamworks dep).
//   2. Registers a GetPlayerColor delegate into WorldStartContext so world-side code can
//      tint units by faction without importing Steamworks or RTSCL.Lobby.
//   3. Translates List<PlayerSlot> (Steamworks CSteamID) into the (ulong, int)[] tuple
//      array that WorldStartContext.PendingSlots expects — isolating Steamworks types from
//      the world assembly.
//   4. Mirrors each slot into PlayerRegistry so GetColorForPlayer(CSteamID) works after
//      the scene loads (e.g. for late-joining tint logic, if added later).
//   5. Writes WorldGeneratorBootstrap.PendingSeed so the world generator uses the
//      broadcast seed instead of generating a random one.
//   6. Sets NetCommandBridge.OutgoingSender = NetworkManager.SendToAll so commands
//      issued by world-side code are relayed over Steam P2P.
//   7. Calls SceneManager.LoadScene("SampleScene") — the actual transition.
//
// Solo path:
//   This class is never triggered in solo mode (Play Solo bypasses lobby + networking).
//   WorldGeneratorBootstrap.PendingSeed defaults to 0 (random), NetCommandBridge is null,
//   and WorldStartContext stays at defaults (single local player, no faction tints).
//
// Where to adjust:
//   • Adding a new session parameter: populate it in NetworkSession, extend PackGameStart /
//     TryUnpackGameStart, then read it here and push into WorldStartContext.
//   • Scene name: change the string in SceneManager.LoadScene if the game scene is renamed.
//   • Async load: replace LoadScene with LoadSceneAsync and keep a loading screen active
//     until the operation completes.

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

        /// <summary>Creates the singleton GameObject before any scene is loaded.</summary>
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
            // ── Step 1 & 2: push identity + color delegate into the world-side bridge ──
            // WorldStartContext lives in RTSCL.World.Unity which cannot reference Assembly-CSharp
            // (asmdef restriction). We push data in via public static fields / delegates.
            RTSCL.World.Unity.WorldStartContext.LocalPlayer = NetworkSession.LocalPlayer.m_SteamID;
            RTSCL.World.Unity.WorldStartContext.GetPlayerColor = sid =>
                PlayerRegistry.GetColorForPlayer(new Steamworks.CSteamID(sid));

            // ── Step 3 & 4: translate PlayerSlots and mirror into PlayerRegistry ────────
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

            // ── Step 5: push the seed ─────────────────────────────────────────────────
            // Push the seed (existing flow).
            RTSCL.World.Unity.WorldGeneratorBootstrap.PendingSeed = NetworkSession.GameSeed;

            // ── Step 6: wire the outgoing command bridge ──────────────────────────────
            // Wire the command-bridge so world-side code can send packed payloads.
            // Setting OutgoingSender to null (solo) is handled by the solo path not reaching here.
            RTSCL.World.Unity.NetCommandBridge.OutgoingSender = NetworkManager.SendToAll;

            // ── Step 7: load the game scene ───────────────────────────────────────────
            SceneManager.LoadScene("SampleScene");
        }
    }
}

// SteamSmokeTest.cs
// Role: Development-only connectivity smoke test. Logs the local player's Steam persona,
//       SteamID, app-ownership flag, and language to the console on the first frame.
//       Run in-editor to confirm SteamManager initialized correctly before writing
//       real networking code.
//
// Throwaway: delete this file (and its .meta) once Steam connectivity is verified.
// It is safe to leave in a build — it only emits log output and has no game-logic side effects.

using Steamworks;
using UnityEngine;

public sealed class SteamSmokeTest : MonoBehaviour
{
    // AfterSceneLoad (vs BeforeSceneLoad used by real singletons) is intentional:
    // SteamManager is guaranteed to have run Bootstrap() and Awake() by this point.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject(nameof(SteamSmokeTest));
        go.AddComponent<SteamSmokeTest>();
        DontDestroyOnLoad(go);
    }

    private void Start()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamSmokeTest] SteamManager not initialized — check earlier [Steamworks] errors in the console.");
            return;
        }

        // All four calls are lightweight synchronous reads — no Steam callback required.
        var personaName = SteamFriends.GetPersonaName();
        var steamId = SteamUser.GetSteamID();
        var ownsApp = SteamApps.BIsSubscribedApp(new AppId_t(SteamManager.AppId));
        var language = SteamApps.GetCurrentGameLanguage();

        Debug.Log(
            $"[SteamSmokeTest] OK — persona='{personaName}', SteamID={steamId}, " +
            $"ownsAppID({SteamManager.AppId})={ownsApp}, language='{language}'");
    }
}

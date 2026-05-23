using Steamworks;
using UnityEngine;

// Throwaway: delete this file (and its .meta) once Steam connectivity is verified.
public sealed class SteamSmokeTest : MonoBehaviour
{
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

        var personaName = SteamFriends.GetPersonaName();
        var steamId = SteamUser.GetSteamID();
        var ownsApp = SteamApps.BIsSubscribedApp(new AppId_t(SteamManager.AppId));
        var language = SteamApps.GetCurrentGameLanguage();

        Debug.Log(
            $"[SteamSmokeTest] OK — persona='{personaName}', SteamID={steamId}, " +
            $"ownsAppID({SteamManager.AppId})={ownsApp}, language='{language}'");
    }
}

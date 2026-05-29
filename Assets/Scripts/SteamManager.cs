// SteamManager.cs
// Role: Owns Steam API lifetime (init / frame-pump / shutdown) for the entire game.
//       Auto-bootstraps as a DontDestroyOnLoad singleton before any scene loads, so
//       all other managers can check SteamManager.Initialized on Awake/Start.
//
// How it fits:
//   [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] → Bootstrap() creates the GameObject.
//   Awake() calls SteamAPI.InitEx — idempotent guard via s_everInitialized ensures only
//   one real init happens even if Bootstrap runs multiple times in the editor.
//   Update() drives SteamAPI.RunCallbacks() every frame — this is the *only* pump in the
//   project; LobbyManager, NetworkManager, etc. rely on it for their Callback<T> fire.
//   OnDestroy() calls SteamAPI.Shutdown() when the singleton is finally removed.
//
// Where to adjust:
//   • App ID:         change the AppId constant (also update steam_appid.txt for dev runs).
//   • Warning level:  SteamClient.SetWarningMessageHook threshold is set inside the SDK;
//                     to filter severity filter in OnSteamWarning (0=info, 1=warning).
//   • Extra SteamAPI flags / launch config: add before SteamAPI.InitEx in Awake().

using System;
using System.Text;
using AOT;
using Steamworks;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SteamManager : MonoBehaviour
{
    /// <summary>Steam App ID for Goblino. Used by RestartAppIfNecessary and smoke tests.</summary>
    public const uint AppId = 4775350;

    private static SteamManager s_instance;
    // s_everInitialized prevents a second Init call if the object is somehow
    // re-created (e.g. editor domain reload). SteamAPI.Init is not idempotent.
    private static bool s_everInitialized;
    private bool m_initialized;
    // Must be stored as a field; the native SDK holds a raw pointer to the delegate.
    private SteamAPIWarningMessageHook_t m_warningHook;

    /// <summary>True once SteamAPI.InitEx succeeded. All other managers gate their
    /// Steam calls on this property.</summary>
    public static bool Initialized => s_instance != null && s_instance.m_initialized;

    /// <summary>Creates the singleton GameObject before any scene is loaded.
    /// Idempotent — early-exits if the instance already exists (e.g. editor re-enter play).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;
        var go = new GameObject(nameof(SteamManager));
        s_instance = go.AddComponent<SteamManager>();
        DontDestroyOnLoad(go);
    }

    // [MonoPInvokeCallback] tells IL2CPP this is a native-callback entry point — required
    // so the AOT compiler does not strip or recompile the method in a way that breaks
    // the C-function pointer the SDK stores.
    [MonoPInvokeCallback(typeof(SteamAPIWarningMessageHook_t))]
    private static void OnSteamWarning(int severity, StringBuilder message)
    {
        Debug.LogWarning(message.ToString());
    }

    private void Awake()
    {
        // If a second SteamManager were somehow created, destroy it immediately.
        // s_everInitialized guards against calling SteamAPI.Init more than once per process.
        if (s_everInitialized)
        {
            Destroy(gameObject);
            return;
        }

        // Packsize / DllCheck are Steamworks.NET compile-time sanity checks that
        // confirm the managed wrapper was built for the same struct layouts as the
        // native steam_api64.dll bundled in the project.
        if (!Packsize.Test())
        {
            Debug.LogError("[Steamworks] Packsize mismatch — wrong native library for this platform.", this);
        }

        if (!DllCheck.Test())
        {
            Debug.LogError("[Steamworks] DllCheck failed — one or more native binaries are the wrong version.", this);
        }

        try
        {
            // If the game was launched outside Steam (e.g. double-click exe), Steam relaunches
            // it through the client so achievements / overlay work. We then quit this instance.
            if (SteamAPI.RestartAppIfNecessary(new AppId_t(AppId)))
            {
                Application.Quit();
                return;
            }
        }
        catch (DllNotFoundException e)
        {
            Debug.LogError("[Steamworks] steam_api64 not found. Check the Steamworks.NET package's Plugins folder.\n" + e, this);
            Application.Quit();
            return;
        }

        // InitEx is preferred over Init because it returns a human-readable error string.
        var initResult = SteamAPI.InitEx(out string errMsg);
        if (initResult != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
        {
            Debug.LogError($"[Steamworks] InitEx failed: {initResult} — {errMsg}", this);
            return;
        }

        m_initialized = true;
        s_everInitialized = true;
    }

    private void OnEnable()
    {
        if (!m_initialized) return;
        if (m_warningHook != null) return;
        // Register warning hook once. The delegate must be stored in m_warningHook so
        // the GC cannot collect it while the native side still holds a pointer to it.
        m_warningHook = OnSteamWarning;
        SteamClient.SetWarningMessageHook(m_warningHook);
    }

    private void Update()
    {
        if (!m_initialized) return;
        // Drives all registered Callback<T> and CallResult<T> handlers each frame.
        // Every other manager (LobbyManager, NetworkManager) relies on this pump.
        SteamAPI.RunCallbacks();
    }

    private void OnDestroy()
    {
        if (s_instance != this) return;
        s_instance = null;
        if (!m_initialized) return;
        SteamAPI.Shutdown();
    }
}

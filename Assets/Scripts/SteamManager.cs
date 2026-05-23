using System;
using System.Text;
using AOT;
using Steamworks;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SteamManager : MonoBehaviour
{
    public const uint AppId = 4775350;

    private static SteamManager s_instance;
    private static bool s_everInitialized;
    private bool m_initialized;
    private SteamAPIWarningMessageHook_t m_warningHook;

    public static bool Initialized => s_instance != null && s_instance.m_initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;
        var go = new GameObject(nameof(SteamManager));
        s_instance = go.AddComponent<SteamManager>();
        DontDestroyOnLoad(go);
    }

    [MonoPInvokeCallback(typeof(SteamAPIWarningMessageHook_t))]
    private static void OnSteamWarning(int severity, StringBuilder message)
    {
        Debug.LogWarning(message.ToString());
    }

    private void Awake()
    {
        if (s_everInitialized)
        {
            Destroy(gameObject);
            return;
        }

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
        m_warningHook = OnSteamWarning;
        SteamClient.SetWarningMessageHook(m_warningHook);
    }

    private void Update()
    {
        if (!m_initialized) return;
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

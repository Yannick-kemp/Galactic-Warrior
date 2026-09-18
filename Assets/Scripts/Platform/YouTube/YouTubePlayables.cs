using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

/// <summary>
/// Facade over the YouTube Playables SDK (ytgame). Everything YouTube-specific in the game
/// goes through here, so the rest of the code only asks <see cref="IsBuild"/>.
///
/// The build is selected by the YOUTUBE_PLAYABLES scripting define (set on the WebGL target by
/// YouTubePlayablesBuilder). Real SDK calls only exist in a WebGL player; in the Editor the
/// calls are logged and <see cref="SimulatePause"/> / <see cref="SimulateAudioEnabled"/> stand
/// in for YouTube, so the whole flow can be tested in Play Mode with the define on.
///
/// The JavaScript side lives in Assets/Plugins/WebGL/YouTubePlayables.jslib and the page that
/// loads the SDK before the game in Assets/WebGLTemplates/YouTubePlayables/index.html.
/// </summary>
public static class YouTubePlayables
{
    // A property, not a const: a const would make every `if (!IsBuild)` an unreachable-code warning.
    public static bool IsBuild
    {
        get
        {
#if YOUTUBE_PLAYABLES
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>False while YouTube's mute is on. Always true outside a YouTube build.</summary>
    public static bool AudioEnabled { get; private set; } = true;

    /// <summary>True between YouTube's onPause and onResume.</summary>
    public static bool IsPausedByHost { get; private set; }

    public static event Action<bool> PauseChanged;
    public static event Action<bool> AudioEnabledChanged;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int YT_InPlayablesEnv();
    [DllImport("__Internal")] private static extern void YT_GameReady();
    [DllImport("__Internal")] private static extern string YT_GetInitialSaveData();
    [DllImport("__Internal")] private static extern void YT_SaveData(string data);
    [DllImport("__Internal")] private static extern int YT_IsAudioEnabled();
    [DllImport("__Internal")] private static extern void YT_RegisterCallbacks(Action<int> onPause, Action<int> onAudio);
    [DllImport("__Internal")] private static extern void YT_LogError(string message);
#endif

    private static bool _initialized;
    private static bool _gameReadySent;

    /// <summary>True when running inside YouTube (false when the web build is opened elsewhere).</summary>
    public static bool InPlayablesEnv
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR && YOUTUBE_PLAYABLES
            return YT_InPlayablesEnv() == 1;
#else
            return false;
#endif
        }
    }

    /// <summary>Hooks YouTube's pause and audio callbacks. Called once by YouTubePlayablesRuntime.</summary>
    public static void Initialize()
    {
        if (_initialized || !IsBuild)
            return;
        _initialized = true;

#if UNITY_WEBGL && !UNITY_EDITOR
        AudioEnabled = YT_IsAudioEnabled() == 1;
        YT_RegisterCallbacks(OnHostPause, OnHostAudio);
        // firstFrameReady is sent by index.html as soon as its loading screen is drawn.
#endif
    }

    /// <summary>
    /// Save string YouTube returned before the game started (index.html awaits loadData before
    /// creating the Unity instance). Empty for a new player or outside YouTube.
    /// </summary>
    public static string GetInitialSaveData()
    {
#if UNITY_WEBGL && !UNITY_EDITOR && YOUTUBE_PLAYABLES
        return YT_GetInitialSaveData() ?? string.Empty;
#else
        return string.Empty;
#endif
    }

    public static void SaveData(string data)
    {
#if UNITY_WEBGL && !UNITY_EDITOR && YOUTUBE_PLAYABLES
        YT_SaveData(data ?? string.Empty);
#else
        GwLogSdk("saveData (" + (data != null ? data.Length : 0) + " chars)");
#endif
    }

    /// <summary>Tells YouTube the game accepts input. Sent once, from the first interactive menu.</summary>
    public static void GameReady()
    {
        if (!IsBuild || _gameReadySent)
            return;
        _gameReadySent = true;

#if UNITY_WEBGL && !UNITY_EDITOR
        YT_GameReady();
#else
        GwLogSdk("gameReady");
#endif
    }

    public static void LogError(string message)
    {
#if UNITY_WEBGL && !UNITY_EDITOR && YOUTUBE_PLAYABLES
        YT_LogError(message ?? string.Empty);
#endif
    }

#if UNITY_EDITOR
    /// <summary>Editor stand-in for YouTube's onPause/onResume.</summary>
    public static void SimulatePause(bool paused) => SetPaused(paused);

    /// <summary>Editor stand-in for YouTube's mute button.</summary>
    public static void SimulateAudioEnabled(bool enabled) => SetAudioEnabled(enabled);

    /// <summary>Lets a test run the gameReady path again.</summary>
    public static void ResetGameReadyForTests() => _gameReadySent = false;
#endif

    public static bool GameReadySent => _gameReadySent;

    [MonoPInvokeCallback(typeof(Action<int>))]
    private static void OnHostPause(int paused) => SetPaused(paused == 1);

    [MonoPInvokeCallback(typeof(Action<int>))]
    private static void OnHostAudio(int enabled) => SetAudioEnabled(enabled == 1);

    private static void SetPaused(bool paused)
    {
        if (IsPausedByHost == paused)
            return;

        IsPausedByHost = paused;
        PauseChanged?.Invoke(paused);
    }

    private static void SetAudioEnabled(bool enabled)
    {
        if (AudioEnabled == enabled)
            return;

        AudioEnabled = enabled;
        AudioEnabledChanged?.Invoke(enabled);
    }

    private static void GwLogSdk(string what)
    {
        if (IsBuild)
            Debug.Log("[YouTubePlayables] (editor) " + what);
    }
}

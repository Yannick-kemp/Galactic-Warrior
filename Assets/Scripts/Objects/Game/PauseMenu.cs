using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Platform-agnostic pause state machine. Owns the actual freeze (timeScale, input lock,
/// global audio) and drives the shared PAUSE overlay through UIManager. It is triggered by
/// whatever input the current build uses:
///   • mobile  → the on-screen touch <see cref="PauseButtonUI"/>
///   • desktop → keyboard/gamepad via <see cref="Assets.Scripts.Objects.Game.DesktopPauseInput"/>
/// The overlay's Resume button routes back here via <see cref="Resume"/>.
/// </summary>
public static class PauseMenu
{
    public static bool IsPaused { get; private set; }

    // Fired whenever the paused state flips, so UI (e.g. the touch button icon) can refresh.
    public static event System.Action StateChanged;

    private static float _timeScaleBeforePause = 1f;
    private static bool _inputLockedBeforePause;
    private static bool _audioPausedBeforePause;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        // Static state survives scene loads; make sure we clear a lingering pause when the
        // level is torn down (Quit to menu, Settings deep-link, Retry…), otherwise the next
        // scene would start with audio frozen / timeScale at 0.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsPaused)
            return;

        AudioListener.pause = false;
        Time.timeScale = 1f;
        IsPaused = false;
        StateChanged?.Invoke();
    }

    public static void Toggle()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    public static void Pause()
    {
        if (IsPaused) return;

        _timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;

        if (InputMgr.Instance != null)
        {
            _inputLockedBeforePause = InputMgr.Instance.InputLocked;
            InputMgr.Instance.InputLocked = true;
        }

        // Freeze music/ambience/SFX from the exact sample position. Sources flagged
        // ignoreListenerPause = true keep playing (Unity behaviour).
        _audioPausedBeforePause = AudioListener.pause;
        AudioListener.pause = true;

        UIManager.Instance?.ShowPauseOverlay();

        IsPaused = true;
        StateChanged?.Invoke();
    }

    public static void Resume()
    {
        if (!IsPaused) return;

        Time.timeScale = _timeScaleBeforePause > 0f ? _timeScaleBeforePause : 1f;

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = _inputLockedBeforePause;

        AudioListener.pause = _audioPausedBeforePause;

        UIManager.Instance?.HidePauseOverlay();

        IsPaused = false;
        StateChanged?.Invoke();
    }
}

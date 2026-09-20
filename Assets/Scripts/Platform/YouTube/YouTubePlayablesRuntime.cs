using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Installs itself in YouTube builds only (YOUTUBE_PLAYABLES), before the first scene loads:
///   - restores the YouTube save into PlayerPrefs before GameMgr reads them,
///   - freezes the game on YouTube's onPause and restores it on onResume,
///   - follows YouTube's mute button,
///   - pushes the save to YouTube whenever a synced value changes,
///   - sends gameReady once the main menu can be used.
/// </summary>
public class YouTubePlayablesRuntime : MonoBehaviour
{
    // Saves are compared at this rate; a change reaches YouTube within this delay.
    private const float SaveCheckInterval = 1f;

    private string _lastSaved;
    private float _nextSaveCheck;

    private float _timeScaleBeforeHostPause = 1f;
    private bool _audioPausedBeforeHostPause;

    private int _framesSinceMenuReady = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (!YouTubePlayables.IsBuild)
            return;

#if !UNITY_EDITOR
        // Not in the Editor: there is no YouTube save there, and an empty one would wipe the
        // developer's own PlayerPrefs.
        YouTubeSaveSync.Apply(YouTubePlayables.GetInitialSaveData());
#endif
        YouTubePlayables.Initialize();

        var go = new GameObject("YouTubePlayablesRuntime");
        DontDestroyOnLoad(go);
        go.AddComponent<YouTubePlayablesRuntime>();
    }

    private void Awake()
    {
        // Snapshot of what was just restored: nothing to push until something changes.
        _lastSaved = YouTubeSaveSync.Snapshot();

        // The browser tab being hidden must not pause Unity on its own: YouTube's onPause is the
        // only pause signal allowed (the Page Visibility API is forbidden).
        Application.runInBackground = true;
    }

    private void OnEnable()
    {
        YouTubePlayables.PauseChanged += OnHostPauseChanged;
        YouTubePlayables.AudioEnabledChanged += OnHostAudioChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        YouTubePlayables.PauseChanged -= OnHostPauseChanged;
        YouTubePlayables.AudioEnabledChanged -= OnHostAudioChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        AudioMute.Apply();
    }

    private void Update()
    {
        UpdateGameReady();

        if (Time.unscaledTime >= _nextSaveCheck)
        {
            _nextSaveCheck = Time.unscaledTime + SaveCheckInterval;
            PushSaveIfChanged();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A scene load resets nothing audio-wise, but scene objects may call AudioMute.Apply
        // themselves; re-apply so YouTube's mute always wins.
        AudioMute.Apply();
    }

    // ── gameReady ────────────────────────────────────────────────────────────

    private void UpdateGameReady()
    {
        if (YouTubePlayables.GameReadySent)
            return;

        if (_framesSinceMenuReady < 0)
        {
            // The first scene is the main menu; it is usable once its UI and EventSystem exist.
            if (FindFirstObjectByType<MainMenuUI>() != null && EventSystem.current != null)
                _framesSinceMenuReady = 0;
            return;
        }

        // MainMenuUI fills its buttons one frame after Start; leave it a couple of frames.
        if (++_framesSinceMenuReady >= 3)
            YouTubePlayables.GameReady();
    }

    // ── Saves ────────────────────────────────────────────────────────────────

    public void PushSaveIfChanged()
    {
        string snapshot = YouTubeSaveSync.Snapshot();
        if (snapshot == _lastSaved)
            return;

        _lastSaved = snapshot;
        YouTubePlayables.SaveData(snapshot);
    }

    // ── Host pause / audio ───────────────────────────────────────────────────

    private void OnHostPauseChanged(bool paused)
    {
        if (paused)
        {
            _timeScaleBeforeHostPause = Time.timeScale;
            _audioPausedBeforeHostPause = AudioListener.pause;

            Time.timeScale = 0f;
            AudioListener.pause = true;

            // YouTube may close the game while it is paused: flush progress now.
            PushSaveIfChanged();
        }
        else
        {
            Time.timeScale = _timeScaleBeforeHostPause;
            AudioListener.pause = _audioPausedBeforeHostPause;
        }
    }

    private void OnHostAudioChanged(bool enabled)
    {
        AudioMute.Apply();
    }
}

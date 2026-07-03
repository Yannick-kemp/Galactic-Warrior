using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PauseButtonUI : MonoBehaviour, IPointerDownHandler
{
    [Header("Optional Pause Menu")]
    [Tooltip("Root object of an existing pause menu. Shown while paused, hidden on resume. Leave empty if none exists.")]
    [SerializeField] private GameObject pauseMenuRoot;

    [Header("Optional Icon Swap")]
    [SerializeField] private Image iconGraphic;
    [SerializeField] private Sprite pauseSprite;
    [SerializeField] private Sprite resumeSprite;

    public static bool IsPaused { get; private set; }

    private float _timeScaleBeforePause = 1f;
    private bool _inputLockedBeforePause;
    private bool _audioPausedBeforePause;

    private void OnEnable()
    {
        // Always show the icon that matches the current state when the button appears.
        RefreshIcon();
    }

    private void OnDisable()
    {
        // Scene unloads/reloads reset timeScale and input elsewhere (GameMgr), but not
        // audio. If we get disabled/destroyed while paused, make sure we don't leave
        // audio globally frozen for the next scene.
        if (IsPaused)
            AudioListener.pause = false;

        IsPaused = false;
    }

    // Block world (gameplay) input the instant the button is pressed, before the
    // warrior's HandleInput can act on the same tap. Mirrors RelicUIButton/attack
    // button convention. Covers mouse and touch via the EventSystem pointer event.
    public void OnPointerDown(PointerEventData eventData)
    {
        Warrior warrior = Warrior.Instance;
        if (warrior == null)
            warrior = FindFirstObjectByType<Warrior>();

        if (warrior != null)
            warrior.NotifyUIConsumedInput();
    }

    public void TogglePause()
    {
        if (IsPaused)
            Resume();
        else
            Pause();
    }

    public void Pause()
    {
        if (IsPaused) return;

        EnsurePauseMenu();

        _timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;

        if (InputMgr.Instance != null)
        {
            _inputLockedBeforePause = InputMgr.Instance.InputLocked;
            InputMgr.Instance.InputLocked = true;
        }

        // Pause all audio globally. Music, ambience and already-playing SFX freeze and
        // later resume from the exact sample position. AudioSources flagged with
        // ignoreListenerPause = true are intentionally left playing by Unity.
        _audioPausedBeforePause = AudioListener.pause;
        AudioListener.pause = true;

        if (pauseMenuRoot != null)
            pauseMenuRoot.SetActive(true);

        IsPaused = true;
        RefreshIcon();
    }

    public void Resume()
    {
        if (!IsPaused) return;

        Time.timeScale = _timeScaleBeforePause > 0f ? _timeScaleBeforePause : 1f;

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = _inputLockedBeforePause;

        // Resume all audio from where it was paused (restore prior listener state).
        AudioListener.pause = _audioPausedBeforePause;

        if (pauseMenuRoot != null)
            pauseMenuRoot.SetActive(false);

        IsPaused = false;
        RefreshIcon();
    }

    // If no pause menu was wired in the Inspector (the mobile/Dev build has none
    // authored), build the runtime touch pause menu on first pause and use it as the
    // menu root. When pauseMenuRoot IS assigned, this is a no-op and the authored
    // panel is used unchanged.
    private void EnsurePauseMenu()
    {
        if (pauseMenuRoot != null) return;

        TouchPauseMenu menu = TouchPauseMenu.Create(this);
        if (menu != null)
            pauseMenuRoot = menu.gameObject;
    }

    private void RefreshIcon()
    {
        if (iconGraphic == null) return;

        Sprite target = IsPaused ? resumeSprite : pauseSprite;
        if (target != null)
            iconGraphic.sprite = target;
    }
}

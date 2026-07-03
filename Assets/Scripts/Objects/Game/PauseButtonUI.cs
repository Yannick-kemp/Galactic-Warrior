using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// On-screen (touch) Pause button. The actual freeze lives in <see cref="PauseMenu"/>; this
/// component is just the mobile trigger + icon. On desktop (Steam) it hides itself — pause is
/// driven by keyboard/gamepad through DesktopPauseInput instead.
/// </summary>
public class PauseButtonUI : MonoBehaviour, IPointerDownHandler
{
    [Header("Optional Pause Menu")]
    [Tooltip("Root object of an existing pause menu. Shown while paused, hidden on resume. Leave empty if none exists.")]
    [SerializeField] private GameObject pauseMenuRoot;

    [Header("Optional Icon Swap")]
    [SerializeField] private Image iconGraphic;
    [SerializeField] private Sprite pauseSprite;
    [SerializeField] private Sprite resumeSprite;

    // Kept for compatibility with existing callers/scenes; the state now lives in PauseMenu.
    public static bool IsPaused => PauseMenu.IsPaused;

    private void Awake()
    {
#if UNITY_STANDALONE
        // Desktop build (Steam): the touch pause button is a mobile affordance. Hide it so it
        // neither shows nor eats input — DesktopPauseInput handles pause on keyboard/gamepad.
        gameObject.SetActive(false);
#endif
    }

    private void OnEnable()
    {
        PauseMenu.StateChanged += OnPauseStateChanged;
        RefreshIcon();
    }

    private void OnDisable()
    {
        PauseMenu.StateChanged -= OnPauseStateChanged;
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

    // Wired from the button's onClick in the Inspector.
    public void TogglePause() => PauseMenu.Toggle();

    public void Pause() => PauseMenu.Pause();

    public void Resume() => PauseMenu.Resume();

    private void OnPauseStateChanged()
    {
        if (pauseMenuRoot != null)
            pauseMenuRoot.SetActive(PauseMenu.IsPaused);

        RefreshIcon();
    }

    private void RefreshIcon()
    {
        if (iconGraphic == null) return;

        Sprite target = PauseMenu.IsPaused ? resumeSprite : pauseSprite;
        if (target != null)
            iconGraphic.sprite = target;
    }
}

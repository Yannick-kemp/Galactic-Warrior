using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameOverUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text scoreText;

    [SerializeField] private Button retryButton;
    [SerializeField] private Button menuButton;
    [SerializeField] private Button reviveButton;

    [Header("Pause Mode")]
    [SerializeField] private Button settingsButton;   // opens Settings (in-game popup, or menu deep-link)
    [SerializeField] private Button resumeButton;      // closes the overlay and un-pauses
    [SerializeField] private string pauseTitle = "PAUSE";
    [Tooltip("Optional in-game Settings popup, shown over the pause overlay so the level is kept. " +
             "If left empty, the Settings button falls back to loading the main-menu Settings.")]
    [SerializeField] private SettingsPopupUI inGameSettingsPopup;

    [Header("Menu Scene")]
    [SerializeField] private string menuSceneName = "menu";

    [Header("Transitions")]
    [SerializeField] private Image fullScreenFade;           // black full-screen image (alpha 0)
    [SerializeField] private float retryFadeDuration = 0.2f; // fast for platformer
    [SerializeField] private float menuFadeDuration = 0.25f;
    [SerializeField] private float clickFeedbackDelay = 0.04f;

    private Coroutine _showRoutine;
    private bool _isTransitioning;



    private void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();

        // Hook button events once
        if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);
        if (menuButton != null) menuButton.onClick.AddListener(OnMenuClicked);
        if (reviveButton != null) reviveButton.onClick.AddListener(OnReviveClicked);
        if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsClicked);
        if (resumeButton != null) resumeButton.onClick.AddListener(OnResumeClicked);

        ResetFadeOverlay();
        HideInstant(); // start hidden
    }

    public void Show(int score = -1)
    {
        if (_showRoutine != null) StopCoroutine(_showRoutine);
        _showRoutine = StartCoroutine(ShowNextFrame(score));
    }

    private IEnumerator ShowNextFrame(int score)
    {
        // Important if timeScale == 0 elsewhere; still okay with yield null
        yield return null;

        _isTransitioning = false;
        SetButtonsInteractable(true);
        ResetFadeOverlay();

        // RetriesRemaining is already post-death (the life was consumed in HandleWarriorDead),
        // so 0 here means a real game over: show it as such and block the now-useless Retry.
        int retriesLeft = GameMgr.Instance != null ? GameMgr.Instance.RetriesRemaining : 0;
        bool noRetriesLeft = retriesLeft <= 0;

        // Death layout: retry/menu/revive visible, pause-only buttons hidden.
        SetPauseButtonsVisible(false);
        SetDeathButtonsVisible(true);

        if (titleText != null) titleText.text = noRetriesLeft ? "GAME OVER" : "DEFEAT";
        if (scoreText != null)
        {
            if (score >= 0)
                scoreText.text = $"SCORE: {score}\nRetries Left: {retriesLeft}";
            else
                scoreText.text = $"Retries Left: {retriesLeft}";
        }

        if (retryButton != null) retryButton.interactable = !noRetriesLeft;

        group.alpha = 1f;
        group.blocksRaycasts = true;
        group.interactable = true;

        // Desktop gamepad/keyboard: register this DEFEAT / GAME OVER screen as the active
        // navigation context so Retry/Menu/Revive can be navigated and pressed.
        MenuNavigator.PushContext(transform);

        _showRoutine = null;
    }

    public void HideInstant()
    {
        if (group == null) group = GetComponent<CanvasGroup>();

        // Release the navigation context (covers both the DEFEAT and PAUSE hide paths).
        MenuNavigator.PopContext(transform);

        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        _isTransitioning = false;
        SetButtonsInteractable(true);
        ResetFadeOverlay();
    }

    // Shows the same overlay used for DEFEAT, but in PAUSE mode: title becomes "PAUSE",
    // retry/revive are hidden, and Resume + Settings are offered. Driven by PauseMenu.
    // No coroutine here because Time.timeScale is 0 while paused.
    public void ShowPause()
    {
        if (_showRoutine != null) { StopCoroutine(_showRoutine); _showRoutine = null; }

        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (group == null) group = GetComponent<CanvasGroup>();

        // Register as the active navigation context (desktop gamepad/keyboard).
        MenuNavigator.PushContext(transform);

        _isTransitioning = false;
        ResetFadeOverlay();

        if (titleText != null) titleText.text = pauseTitle;
        if (scoreText != null) scoreText.text = string.Empty;

        SetDeathButtonsVisible(false);
        SetPauseButtonsVisible(true);

        group.alpha = 1f;
        group.blocksRaycasts = true;
        group.interactable = true;

        // On desktop there is no touch: give the keyboard/gamepad a selected button to start
        // from, so Resume/Settings/Menu can be navigated (stick/arrows) and pressed (A/Enter).
        Button first = resumeButton != null ? resumeButton
                     : settingsButton != null ? settingsButton
                     : menuButton;
        if (first != null)
            UiNavigation.Select(first.gameObject);
    }

    // Instant close for the resume path (un-pause) — no fade so gameplay resumes immediately.
    public void HidePauseInstant()
    {
        HideInstant(); // releases the navigation context
    }

    private void OnResumeClicked()
    {
        // Restore timeScale/input/audio and hide this overlay, whatever triggered the pause
        // (touch button on mobile, keyboard/gamepad on desktop).
        PauseMenu.Resume();
    }

    private void OnSettingsClicked()
    {
        if (_isTransitioning) return;

        // Preferred (desktop-friendly): open the Settings popup over the pause overlay so the
        // level stays paused — no progress lost. Resolve a scene instance if none was wired.
        SettingsPopupUI popup = inGameSettingsPopup;
        if (popup == null)
            popup = FindFirstObjectByType<SettingsPopupUI>(FindObjectsInactive.Include);

        if (popup != null)
        {
            popup.Show();
            return;
        }

        // Fallback: no in-game popup available → deep-link to the main-menu Settings.
        StartCoroutine(SettingsTransitionRoutine());
    }

    private IEnumerator SettingsTransitionRoutine()
    {
        _isTransitioning = true;
        SetButtonsInteractable(false);

        if (settingsButton != null)
            yield return StartCoroutine(ButtonPressFX(settingsButton));

        // Deep-link: the main menu opens its Settings popup on load. LoadMenu() restores
        // timeScale and tears down the paused level.
        MainMenuUI.OpenSettingsOnLoad = true;
        GameMgr.Instance?.LoadMenu(menuSceneName);

        _isTransitioning = false;
    }

    private void SetDeathButtonsVisible(bool value)
    {
        if (retryButton != null) retryButton.gameObject.SetActive(value);
        if (reviveButton != null) reviveButton.gameObject.SetActive(value);
        // menuButton (Quit to menu) stays available in both modes.
    }

    private void SetPauseButtonsVisible(bool value)
    {
        if (settingsButton != null) settingsButton.gameObject.SetActive(value);
        if (resumeButton != null) resumeButton.gameObject.SetActive(value);
    }

    private void OnRetryClicked()
    {
        if (_isTransitioning) return;
        StartCoroutine(RetryTransitionRoutine());
    }

    private void OnMenuClicked()
    {
        if (_isTransitioning) return;
        StartCoroutine(MenuTransitionRoutine());
    }

    private void OnReviveClicked()
    {
        if (_isTransitioning) return;
        StartCoroutine(ReviveRoutine());
    }

    private IEnumerator RetryTransitionRoutine()
    {
        _isTransitioning = true;
        SetButtonsInteractable(false);
        group.blocksRaycasts = false;
        group.interactable = false;

        if (retryButton != null)
            yield return StartCoroutine(ButtonPressFX(retryButton));

        yield return StartCoroutine(FadeOverlayTo(1f, retryFadeDuration));

        Debug.LogWarning($"[GameOverUI] Retry clicked — GameMgr.Instance is null? {GameMgr.Instance == null}");
        bool ok = GameMgr.Instance?.TryRetryFromDeath() ?? false;
        Debug.LogWarning($"[GameOverUI] TryRetryFromDeath returned {ok}");

        if (!ok)
        {
            yield return StartCoroutine(FadeOverlayTo(0f, 0.12f));

            if (titleText != null)
                titleText.text = "GAME OVER";

            if (retryButton != null)
                retryButton.interactable = false;

            if (menuButton != null)
                menuButton.interactable = true;

            if (reviveButton != null)
                reviveButton.interactable = true;

            group.blocksRaycasts = true;
            group.interactable = true;
            _isTransitioning = false;
            yield break;
        }

        HideInstant();
        ResetFadeOverlay();
        _isTransitioning = false;
    }

    private IEnumerator MenuTransitionRoutine()
    {
        _isTransitioning = true;
        SetButtonsInteractable(false);

        if (menuButton != null)
            yield return StartCoroutine(ButtonPressFX(menuButton));

        if (clickFeedbackDelay > 0f)
            yield return new WaitForSecondsRealtime(clickFeedbackDelay);

        group.blocksRaycasts = false;
        group.interactable = false;

        yield return StartCoroutine(FadeOverlayTo(1f, menuFadeDuration));

        GameMgr.Instance?.LoadMenu(menuSceneName);

        _isTransitioning = false;
    }

    private IEnumerator ReviveRoutine()
    {
        _isTransitioning = true;
        SetButtonsInteractable(false);

        if (reviveButton != null)
            yield return StartCoroutine(ButtonPressFX(reviveButton));

        yield return StartCoroutine(FadeOverlayTo(1f, 0.2f));

        HideInstant();

        GameMgr.Instance?.ReviveLevel();
    }

    private IEnumerator FadeOverlayTo(float targetAlpha, float duration)
    {
        if (fullScreenFade == null || duration <= 0f)
        {
            if (fullScreenFade != null)
            {
                Color c = fullScreenFade.color;
                c.a = targetAlpha;
                fullScreenFade.color = c;
            }
            yield break;
        }

        Color color = fullScreenFade.color;
        float startAlpha = color.a;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);

            color.a = Mathf.Lerp(startAlpha, targetAlpha, k);
            fullScreenFade.color = color;

            yield return null;
        }

        color.a = targetAlpha;
        fullScreenFade.color = color;
    }

    private IEnumerator FadeGroupOut(float duration)
    {
        if (group == null || duration <= 0f)
        {
            if (group != null) group.alpha = 0f;
            yield break;
        }

        float start = group.alpha;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            group.alpha = Mathf.Lerp(start, 0f, k);
            yield return null;
        }

        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private IEnumerator ButtonPressFX(Button btn)
    {
        if (btn == null) yield break;

        Transform tr = btn.transform;
        Vector3 original = tr.localScale;
        Vector3 pressed = original * 0.93f;

        float d = 0.05f;
        float t = 0f;

        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            tr.localScale = Vector3.Lerp(original, pressed, Mathf.Clamp01(t / d));
            yield return null;
        }

        t = 0f;
        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            tr.localScale = Vector3.Lerp(pressed, original, Mathf.Clamp01(t / d));
            yield return null;
        }

        tr.localScale = original;
    }

    private void SetButtonsInteractable(bool value)
    {
        if (retryButton != null) retryButton.interactable = value;
        if (menuButton != null) menuButton.interactable = value;
        if (reviveButton != null) reviveButton.interactable = value;
        if (settingsButton != null) settingsButton.interactable = value;
        if (resumeButton != null) resumeButton.interactable = value;
    }

    private void ResetFadeOverlay()
    {
        if (fullScreenFade == null) return;

        Color c = fullScreenFade.color;
        c.a = 0f;
        fullScreenFade.color = c;

        // Usually false so it doesn't eat clicks when invisible.
        // During transitions, we already disable panel interactability.
        fullScreenFade.raycastTarget = false;
    }

    public void Hide()
    {
        MenuNavigator.PopContext(transform);
        if (_showRoutine != null) StopCoroutine(_showRoutine);
        _showRoutine = StartCoroutine(HideRoutine());
    }

    private IEnumerator HideRoutine()
    {
        // quick fade-out (or instant if you prefer)
        float t = 0f;
        float dur = 0.15f;
        float start = group.alpha;

        group.interactable = false;
        group.blocksRaycasts = false;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, 0f, t / dur);
            yield return null;
        }

        group.alpha = 0f;
        gameObject.SetActive(false); // optional
    }
}
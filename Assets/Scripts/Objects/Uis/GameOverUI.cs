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

        _showRoutine = null;
    }

    public void HideInstant()
    {
        if (group == null) group = GetComponent<CanvasGroup>();

        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        _isTransitioning = false;
        SetButtonsInteractable(true);
        ResetFadeOverlay();
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
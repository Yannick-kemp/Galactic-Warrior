using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelTransitionUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private CanvasGroup rootCanvasGroup;
    [SerializeField] private RectTransform titleGroup;

    [Header("Visuals")]
    [SerializeField] private Image darkOverlay;
    [SerializeField] private Image flashOverlay;
    [SerializeField] private TMP_Text levelTitleText;
    [SerializeField] private TMP_Text subtitleText;

    [Header("Timings")]
    [SerializeField] private float fadeInDuration = 0.45f;
    [SerializeField] private float titlePopDuration = 0.30f;
    [SerializeField] private float holdDuration = 0.90f;
    [SerializeField] private float fadeOutDuration = 0.45f;
    [SerializeField] private float flashDuration = 0.18f;

    [Header("Animation")]
    [SerializeField] private Vector3 hiddenScale = new Vector3(0.85f, 0.85f, 1f);
    [SerializeField] private Vector3 overshootScale = new Vector3(1.08f, 1.08f, 1f);
    [SerializeField] private Vector3 finalScale = Vector3.one;

    private Coroutine _transitionRoutine;
    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        ForceHiddenImmediate();
    }

    public void ForceHiddenImmediate()
    {
        IsPlaying = false;

        // IMPORTANT:
        // keep the GameObject active so coroutines can start
        // gameObject.SetActive(false);   <-- do NOT do this

        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.blocksRaycasts = false;
            rootCanvasGroup.interactable = false;
        }

        if (titleGroup != null)
            titleGroup.localScale = hiddenScale;

        if (darkOverlay != null)
        {
            Color c = darkOverlay.color;
            c.a = 0f;
            darkOverlay.color = c;
        }

        if (flashOverlay != null)
        {
            Color c = flashOverlay.color;
            c.a = 0f;
            flashOverlay.color = c;
        }
    }

    public void Play(string levelName, string subtitle = "")
    {
        if (!gameObject.activeInHierarchy)
            gameObject.SetActive(true);

        if (_transitionRoutine != null)
            StopCoroutine(_transitionRoutine);

        _transitionRoutine = StartCoroutine(PlayRoutine(levelName, subtitle));
    }

    private IEnumerator PlayRoutine(string levelName, string subtitle)
    {
        IsPlaying = true;

        if (levelTitleText != null)
            levelTitleText.text = levelName;

        if (subtitleText != null)
        {
            subtitleText.text = subtitle;
            subtitleText.gameObject.SetActive(!string.IsNullOrWhiteSpace(subtitle));
        }

        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.blocksRaycasts = true;
            rootCanvasGroup.interactable = false;
        }

        if (titleGroup != null)
            titleGroup.localScale = hiddenScale;

        if (flashOverlay != null)
        {
            yield return StartCoroutine(FadeImageAlpha(flashOverlay, 0f, 0.65f, flashDuration * 0.5f));
            yield return StartCoroutine(FadeImageAlpha(flashOverlay, 0.65f, 0f, flashDuration));
        }

        if (rootCanvasGroup != null)
            yield return StartCoroutine(FadeCanvasGroup(rootCanvasGroup, 0f, 1f, fadeInDuration));

        if (titleGroup != null)
        {
            yield return StartCoroutine(ScaleRect(titleGroup, hiddenScale, overshootScale, titlePopDuration * 0.6f));
            yield return StartCoroutine(ScaleRect(titleGroup, overshootScale, finalScale, titlePopDuration * 0.4f));
        }

        yield return new WaitForSecondsRealtime(holdDuration);

        if (rootCanvasGroup != null)
            yield return StartCoroutine(FadeCanvasGroup(rootCanvasGroup, 1f, 0f, fadeOutDuration));

        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.blocksRaycasts = false;
            rootCanvasGroup.interactable = false;
        }

        IsPlaying = false;
        _transitionRoutine = null;
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
    {
        if (group == null) yield break;

        float t = 0f;
        group.alpha = from;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            k = EaseOutCubic(k);
            group.alpha = Mathf.Lerp(from, to, k);
            yield return null;
        }

        group.alpha = to;
    }

    private IEnumerator FadeImageAlpha(Image image, float from, float to, float duration)
    {
        if (image == null) yield break;

        float t = 0f;
        Color c = image.color;
        c.a = from;
        image.color = c;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            k = EaseOutCubic(k);
            c.a = Mathf.Lerp(from, to, k);
            image.color = c;
            yield return null;
        }

        c.a = to;
        image.color = c;
    }

    private IEnumerator ScaleRect(RectTransform rect, Vector3 from, Vector3 to, float duration)
    {
        if (rect == null) yield break;

        float t = 0f;
        rect.localScale = from;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            k = EaseOutBack(k);
            rect.localScale = Vector3.LerpUnclamped(from, to, k);
            yield return null;
        }

        rect.localScale = to;
    }

    private float EaseOutCubic(float x)
    {
        return 1f - Mathf.Pow(1f - x, 3f);
    }

    private float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
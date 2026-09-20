using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Final "VICTOIRE / THE END" screen shown when the final boss (Zort) is defeated.
/// Mirrors the RewardUI pattern: CanvasGroup fade + panel scale, all driven by
/// UNSCALED time so it animates correctly during the boss-death slow-motion / pause.
/// Driven by GameMgr via UIManager.ShowVictoryScreen(...).
/// </summary>
public class VictoryUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform panelRoot;

    [Header("Texts")]
    [SerializeField] private TMP_Text titleText;     // "VICTOIRE"
    [SerializeField] private TMP_Text subtitleText;  // "Zort, Néant Originel — vaincu"
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private TMP_Text retriesText;
    [SerializeField] private TMP_Text rewardText;

    [Header("Animation")]
    [SerializeField] private Vector3 hiddenScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 shownScale = Vector3.one;
    [SerializeField] private float fadeDuration = 0.25f;

    [Header("Victory Audio")]
    [Tooltip("When the screen appears, every other sound is stopped and this track plays.")]
    [SerializeField] private AudioClip victoryMusic;
    [SerializeField] private bool loopVictoryMusic = true;
    [SerializeField, Range(0f, 1f)] private float victoryMusicVolume = 1f;
    [Tooltip("Optional. If empty, an AudioSource is created on this object at runtime.")]
    [SerializeField] private AudioSource musicSource;

    [Header("HUD to hide while shown")]
    [Tooltip("HUD objects hidden while the victory screen is up (health bar, portrait, relic " +
             "column, enemy counter…). Do NOT add WarriorUI itself — VictoryScreen is inside it.")]
    [SerializeField] private GameObject[] hudToHide;

    [Header("End Credits")]
    [Tooltip("Optional. If empty, searched in children. Plays the credits when the screen appears.")]
    [SerializeField] private VictoryCreditsSequence credits;

    private bool _visible;
    private Coroutine _routine;

    private void Awake() => HideImmediate();

    public void Show(int score, float runSeconds, int retries, int coins, int tokens)
    {
        _visible = true;

        if (titleText != null && string.IsNullOrWhiteSpace(titleText.text))
            titleText.text = "VICTOIRE";

        if (scoreText != null) scoreText.text = score.ToString("N0");
        if (timeText != null) timeText.text = FormatTime(runSeconds);
        if (retriesText != null) retriesText.text = retries.ToString();
        if (rewardText != null) rewardText.text = $"+{coins}   +{tokens}";

        gameObject.SetActive(true);

        PlayVictoryAudio();
        SetHudHidden(true);

        if (credits == null)
            credits = GetComponentInChildren<VictoryCreditsSequence>(true);
        if (credits != null)
            credits.Play();

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ShowRoutine());
    }

    private void SetHudHidden(bool hidden)
    {
        if (hudToHide == null)
            return;

        for (int i = 0; i < hudToHide.Length; i++)
        {
            if (hudToHide[i] != null)
                hudToHide[i].SetActive(!hidden);
        }
    }

    /// <summary>Stops every other sound in the scene, then plays the victory track.</summary>
    private void PlayVictoryAudio()
    {
        EnsureMusicSource();

        // Silence everything else (gameplay SFX, music, one-shots, …).
        AudioSource[] all = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i] != musicSource)
                all[i].Stop();
        }

        if (victoryMusic == null || musicSource == null)
            return;

        musicSource.clip = victoryMusic;
        musicSource.loop = loopVictoryMusic;
        musicSource.volume = victoryMusicVolume;
        musicSource.Play();
    }

    private void EnsureMusicSource()
    {
        if (musicSource != null)
            return;

        musicSource = GetComponent<AudioSource>();
        if (musicSource == null)
            musicSource = gameObject.AddComponent<AudioSource>();

        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f; // 2D
    }

    public void Hide()
    {
        if (!_visible && canvasGroup != null && canvasGroup.alpha <= 0.001f)
            return;

        _visible = false;

        SetHudHidden(false); // restore the HUD if the victory screen is dismissed

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(HideRoutine());
    }

    public void HideImmediate()
    {
        _visible = false;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (panelRoot != null)
            panelRoot.localScale = hiddenScale;
    }

    // ── Boutons (câblés via l'évènement OnClick dans l'Inspector) ──────────────
    public void OnMainMenuPressed() => GameMgr.Instance?.ReturnToMenuFromVictory();
    public void OnNewGamePlusPressed() => GameMgr.Instance?.StartNewGamePlus();

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return $"{s / 60:00}:{s % 60:00}";
    }

    private IEnumerator ShowRoutine()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
        }

        if (panelRoot != null)
            panelRoot.localScale = hiddenScale;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);

            if (canvasGroup != null)
                canvasGroup.alpha = k;

            if (panelRoot != null)
                panelRoot.localScale = Vector3.LerpUnclamped(hiddenScale, shownScale, EaseOutBack(k));

            yield return null;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        if (panelRoot != null)
            panelRoot.localScale = shownScale;

        _routine = null;
    }

    private IEnumerator HideRoutine()
    {
        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
        Vector3 startScale = panelRoot != null ? panelRoot.localScale : shownScale;

        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);

            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, k);

            if (panelRoot != null)
                panelRoot.localScale = Vector3.Lerp(startScale, hiddenScale, k);

            yield return null;
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        if (panelRoot != null)
            panelRoot.localScale = hiddenScale;

        _routine = null;
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}

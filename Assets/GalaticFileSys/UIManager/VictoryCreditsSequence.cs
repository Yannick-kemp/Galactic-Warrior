using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Data-driven end credits played on the VictoryScreen (full-screen, fade between pages):
///   1. hold a moment so the player reads the victory stats,
///   2. hide the stats, then fade through each text page (team → thanks → studio name),
///   3. fade the studio logo in and leave it on screen,
///   4. reveal the Menu / New Game+ buttons.
///
/// Text is authored in the Inspector (CreditsPage[]). Drive a single credits group (heading + body
/// TMP) per page. Triggered by VictoryUI.Show().
/// </summary>
[DisallowMultipleComponent]
public class VictoryCreditsSequence : MonoBehaviour
{
    [System.Serializable]
    public class CreditsPage
    {
        public string heading;
        [TextArea(2, 10)] public string body;
        [Tooltip("Hold time for this page in seconds (0 = use the default 'hold').")]
        public float holdOverride = 0f;
    }

    [Header("Text pages (data-driven)")]
    [SerializeField] private CreditsPage[] pages;

    [Header("Refs")]
    [Tooltip("CanvasGroup containing the heading + body texts (faded per page).")]
    [SerializeField] private CanvasGroup creditsGroup;
    [SerializeField] private TMP_Text headingText;
    [SerializeField] private TMP_Text bodyText;
    [Tooltip("Studio logo group: fades in last and stays on screen.")]
    [SerializeField] private CanvasGroup logoGroup;

    [Header("Hide / reveal")]
    [Tooltip("Hidden when credits start (VICTORY title, subtitle, portrait, stats…).")]
    [SerializeField] private GameObject[] hideWhenCreditsStart;
    [Tooltip("Shown at the very end (Menu / New Game+ buttons).")]
    [SerializeField] private GameObject[] revealAtEnd;

    [Header("Timing (unscaled)")]
    [Tooltip("Delay before the credits start, so the player can read the stats first.")]
    [SerializeField, Min(0f)] private float startDelay = 3f;
    [SerializeField, Min(0.01f)] private float fadeIn = 0.5f;
    [SerializeField, Min(0f)] private float hold = 3f;
    [SerializeField, Min(0.01f)] private float fadeOut = 0.5f;

    private Coroutine _routine;

    public void Play()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        // Buttons stay hidden until the very end.
        SetActiveAll(revealAtEnd, false);

        if (creditsGroup != null) creditsGroup.alpha = 0f;
        if (logoGroup != null) logoGroup.alpha = 0f;

        // Let the player read the victory stats first.
        if (startDelay > 0f)
            yield return new WaitForSecondsRealtime(startDelay);

        // Switch to full-screen credits.
        SetActiveAll(hideWhenCreditsStart, false);

        if (pages != null)
        {
            for (int i = 0; i < pages.Length; i++)
            {
                CreditsPage p = pages[i];
                if (p == null) continue;

                if (headingText != null) headingText.text = p.heading;
                if (bodyText != null) bodyText.text = p.body;

                yield return Fade(creditsGroup, 0f, 1f, fadeIn);

                float h = p.holdOverride > 0f ? p.holdOverride : hold;
                if (h > 0f)
                    yield return new WaitForSecondsRealtime(h);

                yield return Fade(creditsGroup, 1f, 0f, fadeOut);
            }
        }

        // Studio logo: fade in and stay.
        if (logoGroup != null)
            yield return Fade(logoGroup, 0f, 1f, fadeIn);

        // Reveal the buttons.
        SetActiveAll(revealAtEnd, true);

        _routine = null;
    }

    private static IEnumerator Fade(CanvasGroup g, float from, float to, float dur)
    {
        if (g == null) yield break;

        float t = 0f;
        float d = Mathf.Max(0.01f, dur);
        g.alpha = from;

        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            g.alpha = Mathf.Lerp(from, to, t / d);
            yield return null;
        }

        g.alpha = to;
    }

    private static void SetActiveAll(GameObject[] arr, bool active)
    {
        if (arr == null) return;

        for (int i = 0; i < arr.Length; i++)
            if (arr[i] != null)
                arr[i].SetActive(active);
    }
}

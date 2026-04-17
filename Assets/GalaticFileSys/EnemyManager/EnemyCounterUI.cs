using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EnemyCounterUI : MonoBehaviour
{
    [SerializeField] private TMP_Text countText;
    [SerializeField] private string format = "{0}/{1}";
    [SerializeField] private RectTransform animatedRoot;
    [SerializeField] private float punchScale = 1.08f;
    [SerializeField] private float punchDuration = 0.12f;
    [SerializeField] private bool debugLogs = false;

    private Coroutine pulseRoutine;
    private Coroutine bindRoutine;
    private EnemyMgr boundMgr;

    private void Reset()
    {
        if (countText == null)
            countText = GetComponentInChildren<TMP_Text>(true);

        if (animatedRoot == null)
            animatedRoot = transform as RectTransform;
    }

    private void Awake()
    {
        if (countText == null)
            countText = GetComponentInChildren<TMP_Text>(true);

        if (animatedRoot == null)
            animatedRoot = transform as RectTransform;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        StartBindingRoutine();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        StopBindingRoutine();
        Unbind();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartBindingRoutine();
    }

    private void StartBindingRoutine()
    {
        StopBindingRoutine();
        bindRoutine = StartCoroutine(BindAndRefreshRoutine());
    }

    private void StopBindingRoutine()
    {
        if (bindRoutine != null)
        {
            StopCoroutine(bindRoutine);
            bindRoutine = null;
        }
    }

    private IEnumerator BindAndRefreshRoutine()
    {
        Unbind();

        // Wait until EnemyMgr exists
        while (isActiveAndEnabled && EnemyMgr.Instance == null)
            yield return null;

        if (!isActiveAndEnabled)
            yield break;

        Bind(EnemyMgr.Instance);

        // Important:
        // EnemyMgr registers scene enemies one frame later,
        // so we wait a bit before forcing the first refresh.
        yield return null;
        yield return null;

        RefreshNow();
        bindRoutine = null;
    }

    private void Bind(EnemyMgr mgr)
    {
        if (mgr == null)
            return;

        if (boundMgr == mgr)
            return;

        Unbind();

        boundMgr = mgr;
        boundMgr.OnEnemyCounterChanged += HandleEnemyCounterChanged;

        if (debugLogs)
            Debug.Log("[EnemyCounterUI] Bound to EnemyMgr.");
    }

    private void Unbind()
    {
        if (boundMgr != null)
        {
            boundMgr.OnEnemyCounterChanged -= HandleEnemyCounterChanged;
            boundMgr = null;

            if (debugLogs)
                Debug.Log("[EnemyCounterUI] Unbound from EnemyMgr.");
        }
    }

    private void HandleEnemyCounterChanged(int remaining, int total)
    {
        if (debugLogs)
            Debug.Log($"[EnemyCounterUI] Counter event received: {remaining}/{total}");

        UpdateLabel(remaining, total);
        PlayPulse();
    }

    public void RefreshNow()
    {
        if (countText == null)
        {
            Debug.LogWarning("[EnemyCounterUI] countText is not assigned.");
            return;
        }

        if (EnemyMgr.Instance == null)
        {
            UpdateLabel(0, 0);

            if (debugLogs)
                Debug.Log("[EnemyCounterUI] RefreshNow() -> EnemyMgr.Instance is null");

            return;
        }

        int remaining = EnemyMgr.Instance.RemainingCountableEnemyCount;
        int total = EnemyMgr.Instance.TotalCountableEnemyCount;

        if (debugLogs)
            Debug.Log($"[EnemyCounterUI] RefreshNow() -> {remaining}/{total}");

        UpdateLabel(remaining, total);
    }

    private void UpdateLabel(int remaining, int total)
    {
        if (countText == null)
            return;

        countText.text = string.Format(format, remaining, total);
    }

    private void PlayPulse()
    {
        if (animatedRoot == null)
            return;

        if (pulseRoutine != null)
            StopCoroutine(pulseRoutine);

        pulseRoutine = StartCoroutine(PulseRoutine());
    }

    private IEnumerator PulseRoutine()
    {
        Vector3 baseScale = Vector3.one;
        Vector3 targetScale = Vector3.one * punchScale;

        float half = punchDuration * 0.5f;
        float t = 0f;

        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / half);
            animatedRoot.localScale = Vector3.Lerp(baseScale, targetScale, k);
            yield return null;
        }

        t = 0f;

        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / half);
            animatedRoot.localScale = Vector3.Lerp(targetScale, baseScale, k);
            yield return null;
        }

        animatedRoot.localScale = baseScale;
        pulseRoutine = null;
    }
}
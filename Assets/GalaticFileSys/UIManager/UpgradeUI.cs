using TMPro;
using UnityEngine;

public class UpgradeUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform panelRoot;

    [Header("Texts")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text currencyText;

    [Header("Upgrade Labels")]
    [SerializeField] private TMP_Text damageLabel;
    [SerializeField] private TMP_Text speedLabel;
    [SerializeField] private TMP_Text abilityLabel;

    [Header("Animation")]
    [SerializeField] private Vector3 hiddenScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 shownScale = Vector3.one;
    [SerializeField] private float fadeDuration = 0.2f;

    private bool _visible;
    private Coroutine _routine;

    private int _fakeCurrency = 120;
    private int _damageLevel = 1;
    private int _speedLevel = 1;
    private int _abilityLevel = 0;

    private void Awake()
    {
        HideImmediate();

        if (titleText != null && string.IsNullOrWhiteSpace(titleText.text))
            titleText.text = "UPGRADE YOUR WARRIOR";

        RefreshTexts();
    }

    public void Show()
    {
        _visible = true;
        RefreshTexts();

        gameObject.SetActive(true);

        if (_routine != null)
            StopCoroutine(_routine);

        _routine = StartCoroutine(ShowRoutine());
    }

    public void Hide()
    {
        if (!_visible && canvasGroup != null && canvasGroup.alpha <= 0.001f)
            return;

        _visible = false;

        if (_routine != null)
            StopCoroutine(_routine);

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

    private System.Collections.IEnumerator ShowRoutine()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = true;
        }

        if (panelRoot != null)
            panelRoot.localScale = hiddenScale;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            float eased = EaseOutBack(k);

            if (canvasGroup != null)
                canvasGroup.alpha = k;

            if (panelRoot != null)
                panelRoot.localScale = Vector3.LerpUnclamped(hiddenScale, shownScale, eased);

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

    private System.Collections.IEnumerator HideRoutine()
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
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (panelRoot != null)
            panelRoot.localScale = hiddenScale;

        _routine = null;
    }

    private void RefreshTexts()
    {
        if (currencyText != null)
            currencyText.text = $"Currency: {_fakeCurrency}";

        if (damageLabel != null)
            damageLabel.text = $"Damage Lv {_damageLevel}";

        if (speedLabel != null)
            speedLabel.text = $"Speed Lv {_speedLevel}";

        if (abilityLabel != null)
            abilityLabel.text = _abilityLevel <= 0
                ? "Ability Locked"
                : $"Ability Lv {_abilityLevel}";
    }

    public void OnUpgradeDamagePressed()
    {
        const int cost = 50;
        if (_fakeCurrency < cost) return;

        _fakeCurrency -= cost;
        _damageLevel++;
        RefreshTexts();
    }

    public void OnUpgradeSpeedPressed()
    {
        const int cost = 40;
        if (_fakeCurrency < cost) return;

        _fakeCurrency -= cost;
        _speedLevel++;
        RefreshTexts();
    }

    public void OnUpgradeAbilityPressed()
    {
        const int cost = 80;
        if (_fakeCurrency < cost) return;

        _fakeCurrency -= cost;
        _abilityLevel = Mathf.Max(1, _abilityLevel + 1);
        RefreshTexts();
    }

    public void OnContinuePressed()
    {
        UIManager.Instance?.HideUpgradeScreen();

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = false;
    }

    private float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
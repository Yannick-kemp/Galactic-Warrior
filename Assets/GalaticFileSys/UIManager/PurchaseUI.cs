using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PurchaseUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform panelRoot;

    [Header("Texts")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text priceText;

    [Header("Buttons")]
    [SerializeField] private Button buyButton;
    [SerializeField] private Button noThanksButton;

    [Header("Animation")]
    [SerializeField] private Vector3 hiddenScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 shownScale = Vector3.one;
    [SerializeField] private float fadeDuration = 0.2f;

    private bool _visible;
    private Coroutine _routine;

    private void Awake()
    {
        HideImmediate();

        if (titleText != null && string.IsNullOrWhiteSpace(titleText.text))
            titleText.text = "UNLOCK YOUR POWER";

        if (descriptionText != null && string.IsNullOrWhiteSpace(descriptionText.text))
            descriptionText.text = "Unlock Level 2, new progression, rewards, and upgrades.";

        if (priceText != null && string.IsNullOrWhiteSpace(priceText.text))
            priceText.text = "€3.99";
    }

    public void ConfigureOffer(string title, string description, string price = null)
    {
        if (titleText != null && !string.IsNullOrWhiteSpace(title))
            titleText.text = title;

        if (descriptionText != null && !string.IsNullOrWhiteSpace(description))
            descriptionText.text = description;

        if (priceText != null && !string.IsNullOrWhiteSpace(price))
            priceText.text = price;
    }

    public void Show()
    {
        if (_visible) return;
        _visible = true;

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

    public void OnBuyPressed()
    {
        GameMgr.Instance?.OnPurchaseConfirmed();
    }

    public void OnNoThanksPressed()
    {
        GameMgr.Instance?.OnPurchaseDeclined();
    }

    private float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
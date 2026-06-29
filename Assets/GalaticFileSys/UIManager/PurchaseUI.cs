using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Purchasing;
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

    [Header("Store")]
    [SerializeField] private string productId = "premium_pass";
    [SerializeField] private string loadingPriceText = "...";
    [SerializeField] private bool replaceTitleFromStore = false;
    [SerializeField] private bool replaceDescriptionFromStore = false;

    [Header("Animation")]
    [SerializeField] private Vector3 hiddenScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 shownScale = Vector3.one;
    [SerializeField] private float fadeDuration = 0.2f;

    private StoreController storeController;
    private Product fetchedProduct;

    private bool _visible;
    private Coroutine _routine;
    private bool _unlocked; // guards against unlocking twice (restore + buy + already-owned)

    private void Awake()
    {
        ResolveReferences();

        if (priceText != null)
            priceText.text = loadingPriceText;

        HideImmediate();
    }

    private async void Start()
    {
        if (buyButton != null)
        {
            buyButton.onClick.RemoveListener(OnBuyPressed);
            buyButton.onClick.AddListener(OnBuyPressed);
        }

        if (noThanksButton != null)
        {
            noThanksButton.onClick.RemoveListener(OnNoThanksPressed);
            noThanksButton.onClick.AddListener(OnNoThanksPressed);
        }

        try
        {
            storeController = UnityIAPServices.StoreController();

            storeController.OnProductsFetched += OnProductsFetched;
            storeController.OnPurchasePending += OnPurchasePending;
            storeController.OnPurchaseFailed += OnPurchaseFailed;
            storeController.OnPurchasesFetched += OnPurchasesFetched;

            Debug.Log("[PurchaseUI] Connecting to store...");
            await storeController.Connect();
            Debug.Log("[PurchaseUI] Store connected.");

            var productsToFetch = new List<ProductDefinition>
        {
            new(productId, ProductType.NonConsumable)
        };

            Debug.Log($"[PurchaseUI] FetchProducts for {productId}");
            storeController.FetchProducts(productsToFetch);

            // Restore: if this non-consumable was already bought (e.g. a previous
            // closed-testing purchase), Google won't let it be re-bought. Query the
            // existing purchases so an owner is unlocked automatically.
            Debug.Log("[PurchaseUI] FetchPurchases (restore)...");
            storeController.FetchPurchases();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PurchaseUI] IAP init failed: {ex}");
        }
    }

    private void OnDestroy()
    {
        if (storeController == null)
            return;

        storeController.OnProductsFetched -= OnProductsFetched;
        storeController.OnPurchasePending -= OnPurchasePending;
        storeController.OnPurchaseFailed -= OnPurchaseFailed;
        storeController.OnPurchasesFetched -= OnPurchasesFetched;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveReferences();
    }
#endif

    private void ResolveReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (panelRoot == null)
            panelRoot = transform as RectTransform;
    }

    public void ConfigureOffer(string title, string description, string fallbackPrice = null)
    {
        if (titleText != null && !string.IsNullOrWhiteSpace(title))
            titleText.text = title;

        if (descriptionText != null && !string.IsNullOrWhiteSpace(description))
            descriptionText.text = description;

        // The store price is the source of truth: if it was already fetched,
        // keep it and ignore the hardcoded fallback. Otherwise show the fallback
        // (or the loading placeholder) until OnProductsFetched arrives.
        string storePrice = fetchedProduct?.metadata?.localizedPriceString;
        if (!string.IsNullOrWhiteSpace(storePrice))
            SetPriceText(storePrice);
        else
            SetPriceText(!string.IsNullOrWhiteSpace(fallbackPrice) ? fallbackPrice : loadingPriceText);
    }

    private void OnProductsFetched(List<Product> products)
    {
        Debug.Log($"[PurchaseUI] OnProductsFetched count={products?.Count ?? 0}");

        foreach (var product in products)
        {
            Debug.Log($"[PurchaseUI] fetched id={product?.definition?.id}");

            if (product.definition != null && product.definition.id == productId)
            {
                fetchedProduct = product;

                if (product.metadata == null)
                {
                    Debug.LogWarning("[PurchaseUI] metadata is null");
                    return;
                }

                if (priceText != null)
                    priceText.text = product.metadata.localizedPriceString;

                Debug.Log($"[PurchaseUI] Price loaded: {product.metadata.localizedPriceString}");
                return;
            }
        }

        Debug.LogWarning($"[PurchaseUI] Product not fetched: {productId}");
    }

    public void OnBuyPressed()
    {
        if (storeController == null)
        {
            Debug.LogWarning("[PurchaseUI] StoreController is null.");
            return;
        }

        Debug.Log($"[PurchaseUI] Starting purchase for {productId}");
        storeController.PurchaseProduct(productId);
    }

    private void OnPurchasePending(PendingOrder order)
    {
        Debug.Log("[PurchaseUI] Purchase pending/success received.");

        // Unlock only here, after purchase succeeds.
        Unlock();

        storeController.ConfirmPurchase(order);
    }

    private void OnPurchaseFailed(FailedOrder order)
    {
        Debug.LogWarning($"[PurchaseUI] Purchase failed: {order?.FailureReason} - {order?.Details}");

        // "Vous possédez déjà cet article" / Google ITEM_ALREADY_OWNED surfaces here
        // as DuplicateTransaction. The user already paid for it (e.g. a previous
        // closed-testing purchase), so treat it as a successful unlock instead of
        // leaving them stuck on the buy screen.
        if (order != null && order.FailureReason == PurchaseFailureReason.DuplicateTransaction)
        {
            Debug.Log("[PurchaseUI] Item already owned -> unlocking.");
            Unlock();
        }
    }

    // Called when restoring existing purchases (FetchPurchases). If the product was
    // already bought, unlock without prompting and confirm any pending order.
    private void OnPurchasesFetched(Orders orders)
    {
        if (orders == null)
            return;

        Debug.Log($"[PurchaseUI] OnPurchasesFetched confirmed={orders.ConfirmedOrders?.Count ?? 0} pending={orders.PendingOrders?.Count ?? 0}");

        if (orders.ConfirmedOrders != null)
        {
            foreach (var order in orders.ConfirmedOrders)
            {
                if (CartContainsProduct(order?.CartOrdered))
                {
                    Debug.Log("[PurchaseUI] Restored owned product (confirmed) -> unlocking.");
                    Unlock();
                    return;
                }
            }
        }

        if (orders.PendingOrders != null)
        {
            foreach (var order in orders.PendingOrders)
            {
                if (CartContainsProduct(order?.CartOrdered))
                {
                    Debug.Log("[PurchaseUI] Restored owned product (pending) -> unlocking.");
                    Unlock();
                    storeController.ConfirmPurchase(order);
                    return;
                }
            }
        }
    }

    private bool CartContainsProduct(ICart cart)
    {
        if (cart == null)
            return false;

        var items = cart.Items();
        if (items == null)
            return false;

        foreach (var item in items)
        {
            if (item?.Product?.definition != null && item.Product.definition.id == productId)
                return true;
        }

        return false;
    }

    private void Unlock()
    {
        if (_unlocked)
            return;

        _unlocked = true;
        GameMgr.Instance?.OnPurchaseConfirmed();
    }

    public void OnNoThanksPressed()
    {
        GameMgr.Instance?.OnPurchaseDeclined();
    }

    public void Show()
    {
        if (_routine != null)
            StopCoroutine(_routine);

        _visible = true;
        gameObject.SetActive(true);
        _routine = StartCoroutine(ShowRoutine());
    }

    public void Hide()
    {
        if (_routine != null)
            StopCoroutine(_routine);

        _visible = false;
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

    private IEnumerator ShowRoutine()
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
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (panelRoot != null)
            panelRoot.localScale = hiddenScale;

        _routine = null;
    }

    private void SetPriceText(string value)
    {
        if (priceText != null)
            priceText.text = string.IsNullOrWhiteSpace(value) ? loadingPriceText : value;
    }

    private float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
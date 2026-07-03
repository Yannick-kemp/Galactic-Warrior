using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [Header("Scene Names")]
    [SerializeField] private string relicsScene = "Relics";
    [SerializeField] private string upgradesScene = "Upgrades";

    [Header("Main State Groups")]
    [SerializeField] private GameObject demoButtonsGroup;
    [SerializeField] private GameObject fullButtonsGroup;
    [SerializeField] private GameObject metaButtonsGroup;
    [SerializeField] private GameObject footerButtonsGroup;

    [Header("Header Texts")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text modeBadgeText;
    [SerializeField] private TMP_Text subtitleText;

    [Header("Continue Area")]
    [SerializeField] private TMP_Text continueSubtitleText;

    [Header("Progress Card")]
    [SerializeField] private TMP_Text cardTitleText;
    [SerializeField] private TMP_Text cardSceneText;
    [SerializeField] private TMP_Text cardProgressText;

    [Header("Popups / Panels")]
    [SerializeField] private LevelSelectPanelUI levelSelectPanel;
    [SerializeField] private SettingsPopupUI settingsPopup;
    [SerializeField] private GameObject creditsPanel;

    [Header("Menu Purchase Popup")]
    [SerializeField] private PurchaseUI menuPurchaseUI;
    [SerializeField] private string menuUnlockTitle = "UNLOCK THE FULL GAME";
    [TextArea]
    [SerializeField] private string menuUnlockDescription = "Unlock Level 1, new progression, rewards, and upgrades.";

    [Header("Desktop navigation (Steam)")]
    [Tooltip("Button focused first when the menu opens on desktop, so a gamepad/keyboard can " +
             "navigate. If empty, the first navigable button found is used.")]
    [SerializeField] private GameObject desktopFirstSelected;

    // Set by the in-level PAUSE overlay (GameOverUI) before it loads the menu scene, so the
    // Settings popup opens automatically once the main menu finishes its first Refresh.
    public static bool OpenSettingsOnLoad;

    [Header("Optional Buttons To Toggle")]
    [SerializeField] private GameObject unlockFullGameButton;
    [SerializeField] private GameObject continueButton;
    [SerializeField] private GameObject levelSelectButton;
    [SerializeField] private GameObject newGameButton;

    private void Start()
    {
        StartCoroutine(RefreshNextFrame());
    }

    private void OnEnable()
    {
        MenuNavigator.PushContext(NavRoot());
        StartCoroutine(RefreshNextFrame());
    }

    private void OnDisable()
    {
        MenuNavigator.PopContext(NavRoot());
    }

    // Root that scopes gamepad/keyboard navigation to the menu (the Canvas holding the buttons).
    // IMPORTANT: this controller (MenuController) lives at the SCENE ROOT, outside the UI Canvas,
    // so GetComponentInParent<Canvas>() on 'this' returns null. If we returned our own transform
    // the navigator would scope to an empty root and never select a button (menu unnavigable).
    // Resolve the Canvas from an actual menu element (button groups, searched incl. inactive so it
    // works whether the demo or full group is showing), then fall back to the scene's Canvas.
    private Transform NavRoot()
    {
        Canvas canvas = GetComponentInParent<Canvas>();

        if (canvas == null && demoButtonsGroup != null)
            canvas = demoButtonsGroup.GetComponentInParent<Canvas>(true);
        if (canvas == null && fullButtonsGroup != null)
            canvas = fullButtonsGroup.GetComponentInParent<Canvas>(true);
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();

        return canvas != null ? canvas.transform : transform;
    }

    private System.Collections.IEnumerator RefreshNextFrame()
    {
        yield return null;
        Refresh();

#if UNITY_STANDALONE || UNITY_WSA || UNITY_GAMECORE || UNITY_EDITOR
        // Desktop: hand focus to a menu button so a gamepad/keyboard can drive the UI. If the
        // Settings popup auto-opened (pause deep-link), it manages its own focus — don't steal.
        if (settingsPopup == null || !settingsPopup.gameObject.activeSelf)
        {
            GameObject sel = (desktopFirstSelected != null && desktopFirstSelected.activeInHierarchy)
                ? desktopFirstSelected
                : UiNavigation.FindFirstSelectable(NavRoot());   // search the Canvas, not this root-level controller
            UiNavigation.Select(sel);
        }
#endif
    }

    public void Refresh()
    {
        if (GameMgr.Instance == null)
        {
            Debug.LogWarning("[MainMenuUI] Refresh() skipped because GameMgr is still null.");
            return;
        }

        var gm = GameMgr.Instance;
        bool purchased = gm.HasCampaignPurchase;

        if (titleText != null)
            titleText.text = "GALACTIC WARRIOR";

        if (modeBadgeText != null)
            modeBadgeText.text = purchased ? "FULL GAME" : "DEMO";

        if (subtitleText != null)
            subtitleText.text = purchased
                ? "Welcome back, Warrior."
                : "Defeat the boss to unlock the next chapter.";

        if (demoButtonsGroup != null)
            demoButtonsGroup.SetActive(!purchased);

        if (fullButtonsGroup != null)
            fullButtonsGroup.SetActive(purchased);

        if (metaButtonsGroup != null)
            metaButtonsGroup.SetActive(true);

        if (footerButtonsGroup != null)
            footerButtonsGroup.SetActive(true);

        if (unlockFullGameButton != null)
            unlockFullGameButton.SetActive(!purchased);

        if (continueButton != null)
            continueButton.SetActive(purchased);

        if (levelSelectButton != null)
            levelSelectButton.SetActive(purchased);

        if (newGameButton != null)
            newGameButton.SetActive(purchased);

        if (continueSubtitleText != null)
        {
            if (purchased && gm != null)
                continueSubtitleText.text = $"Continue - {gm.GetContinueSceneDisplayName()}";
            else
                continueSubtitleText.text = string.Empty;
        }

        if (cardTitleText != null)
            cardTitleText.text = purchased ? "CURRENT PROGRESS" : "NEXT CHAPTER";

        if (cardSceneText != null)
        {
            if (purchased && gm != null)
                cardSceneText.text = $"Continue at: {gm.GetContinueSceneDisplayName()}";
            else
                cardSceneText.text = "Age Of Ice";
        }

        if (cardProgressText != null)
        {
            if (gm == null)
            {
                cardProgressText.text = string.Empty;
            }
            else if (purchased)
            {
                int current = gm.HighestReachedSceneIndex + 1;
                int total = Mathf.Max(1, gm.CampaignSceneCount);
                cardProgressText.text = $"Progress: {current}/{total} chapters available";
            }
            else
            {
                cardProgressText.text = "Locked until purchase";
            }
        }

        if (levelSelectPanel != null)
            levelSelectPanel.HideImmediate();

        if (menuPurchaseUI == null)
            menuPurchaseUI = FindFirstObjectByType<PurchaseUI>(FindObjectsInactive.Include);

        // Deep-link from the in-level PAUSE overlay: open Settings once, then clear the flag
        // so a normal return to the menu doesn't reopen it.
        if (OpenSettingsOnLoad)
        {
            OpenSettingsOnLoad = false;
            Settings();
        }
    }

    public void PlayDemo()
    {
        GameMgr.Instance?.StartNewGame();
    }

    public void Continue()
    {
        GameMgr.Instance?.ContinueGame();
    }

    public void NewGame()
    {
        GameMgr.Instance?.StartNewGame();
    }

    public void OpenLevelSelect()
    {
        levelSelectPanel?.Show();
    }

    public void OpenUnlockFullGameOffer()
    {
        if (GameMgr.Instance == null)
        {
            Debug.LogWarning("[MainMenuUI] GameMgr.Instance is null.");
            return;
        }

        if (GameMgr.Instance.HasCampaignPurchase)
        {
            Refresh();
            return;
        }

        if (menuPurchaseUI == null)
            menuPurchaseUI = FindFirstObjectByType<PurchaseUI>(FindObjectsInactive.Include);

        if (menuPurchaseUI == null)
        {
            Debug.LogWarning("[MainMenuUI] Menu PurchaseUI not found in scene.");
            return;
        }

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = true;

        menuPurchaseUI.ConfigureOffer(
            menuUnlockTitle,
            menuUnlockDescription,
            GameMgr.Instance.PurchasePriceText
        );

        menuPurchaseUI.Show();
    }

    public void Relics()
    {
        SceneManager.LoadScene(relicsScene);
    }

    public void Upgrades()
    {
        SceneManager.LoadScene(upgradesScene);
    }

    public void Settings()
    {
        if (settingsPopup != null)
            settingsPopup.Show();
        else
            Debug.LogWarning("[MainMenuUI] SettingsPopupUI reference is missing.");
    }

    public void Credits()
    {
        if (creditsPanel != null)
            creditsPanel.SetActive(true);
        else
            Debug.Log("[MainMenuUI] Credits panel not assigned.");
    }

    public void Exit()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // Keep this name too, so old Inspector hookups still work.
    public void UnlockFullGameForTesting()
    {
        OpenUnlockFullGameOffer();
    }
}
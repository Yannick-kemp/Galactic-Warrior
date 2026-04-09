using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [Header("Scene Names")]
    [SerializeField] private string relicsScene = "Relics";
    [SerializeField] private string upgradesScene = "Upgrades";

    [Header("Main State Groups")]
    [SerializeField] private GameObject demoButtonsGroup;       // SafeArea/MainMenuCard/ContentRow/LeftColumn/DemoButtonsGroup
    [SerializeField] private GameObject fullButtonsGroup;       // SafeArea/MainMenuCard/ContentRow/LeftColumn/FullButtonsGroup
    [SerializeField] private GameObject metaButtonsGroup;       // SafeArea/MainMenuCard/ContentRow/LeftColumn/MetaButtonsGroup
    [SerializeField] private GameObject footerButtonsGroup;     // SafeArea/MainMenuCard/ContentRow/LeftColumn/FooterButtonsGroup

    [Header("Header Texts")]
    [SerializeField] private TMP_Text titleText;                // SafeArea/MainMenuCard/Header/TitleText
    [SerializeField] private TMP_Text modeBadgeText;            // SafeArea/MainMenuCard/Header/ModeBadgeText
    [SerializeField] private TMP_Text subtitleText;             // SafeArea/MainMenuCard/Header/SubtitleText

    [Header("Continue Area")]
    [SerializeField] private TMP_Text continueSubtitleText;     // SafeArea/MainMenuCard/ContentRow/LeftColumn/FullButtonsGroup/ContinueSubtitleText

    [Header("Progress Card")]
    [SerializeField] private TMP_Text cardTitleText;            // SafeArea/MainMenuCard/ContentRow/ProgressCard/CardTitleText
    [SerializeField] private TMP_Text cardSceneText;            // SafeArea/MainMenuCard/ContentRow/ProgressCard/CardSceneText
    [SerializeField] private TMP_Text cardProgressText;         // SafeArea/MainMenuCard/ContentRow/ProgressCard/CardProgressText

    [Header("Popups / Panels")]
    [SerializeField] private LevelSelectPanelUI levelSelectPanel;  // SafeArea/LevelSelectPopupRoot
    [SerializeField] private SettingsPopupUI settingsPopup;        // SafeArea/SettingsPopupRoot
    [SerializeField] private GameObject creditsPanel;              // optional

    [Header("Optional Buttons To Toggle")]
    [SerializeField] private GameObject unlockFullGameButton;      // optional: Btn_UnlockFullGame
    [SerializeField] private GameObject continueButton;            // optional: Btn_Continue
    [SerializeField] private GameObject levelSelectButton;         // optional: Btn_LevelSelect
    [SerializeField] private GameObject newGameButton;             // optional: Btn_NewGame

    private void Start()
    {
        Refresh();
    }

    private void OnEnable()
    {
        Refresh();
    }

    public void Refresh()
    {
        var gm = GameMgr.Instance;
        bool purchased = gm != null && gm.HasCampaignPurchase;

        // Header
        if (titleText != null)
            titleText.text = "GALACTIC WARRIOR";

        if (modeBadgeText != null)
            modeBadgeText.text = purchased ? "FULL GAME" : "DEMO";

        if (subtitleText != null)
            subtitleText.text = purchased
                ? "Welcome back, Warrior."
                : "Defeat the boss to unlock the next chapter.";

        // State groups
        if (demoButtonsGroup != null)
            demoButtonsGroup.SetActive(!purchased);

        if (fullButtonsGroup != null)
            fullButtonsGroup.SetActive(purchased);

        if (metaButtonsGroup != null)
            metaButtonsGroup.SetActive(true);

        if (footerButtonsGroup != null)
            footerButtonsGroup.SetActive(true);

        // Optional direct button references
        if (unlockFullGameButton != null)
            unlockFullGameButton.SetActive(!purchased);

        if (continueButton != null)
            continueButton.SetActive(purchased);

        if (levelSelectButton != null)
            levelSelectButton.SetActive(purchased);

        if (newGameButton != null)
            newGameButton.SetActive(purchased);

        // Continue subtitle
        if (continueSubtitleText != null)
        {
            if (purchased && gm != null)
                continueSubtitleText.text = $"Continue - {gm.GetContinueSceneDisplayName()}";
            else
                continueSubtitleText.text = string.Empty;
        }

        // Progress card
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

        // Ensure popup closed when menu appears
        if (levelSelectPanel != null)
            levelSelectPanel.HideImmediate();
    }

    // -------------------------
    // Main actions
    // -------------------------

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

    // -------------------------
    // Temporary testing action
    // -------------------------

    public void UnlockFullGameForTesting()
    {
        if (GameMgr.Instance == null)
        {
            Debug.LogWarning("[MainMenuUI] GameMgr.Instance is null. Cannot unlock full game.");
            return;
        }

        GameMgr.Instance.UnlockLevel2();
        Refresh();
    }
}
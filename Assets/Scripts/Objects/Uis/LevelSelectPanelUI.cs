using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelSelectPanelUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;      // LevelSelectPopupRoot CanvasGroup
    [SerializeField] private GameObject popupCard;         // LevelSelectPopupRoot/PopupCard (optional)

    [Header("Title")]
    [SerializeField] private TMP_Text titleText;           // PopupCard/TopBar/TitleText

    [Header("Demo ")]
    [SerializeField] private Button level1Button;          // PopupCard/Btn_Level1
    [SerializeField] private TMP_Text level1TitleText;     // child inside Btn_Level1
    [SerializeField] private TMP_Text level1SubtitleText;  // child inside Btn_Level1

    [Header("Level 2")]
    [SerializeField] private Button level2Button;          // PopupCard/Btn_Level2
    [SerializeField] private TMP_Text level2TitleText;     // child inside Btn_Level2
    [SerializeField] private TMP_Text level2SubtitleText;  // child inside Btn_Level2
    [SerializeField] private GameObject level2LockOverlay; // optional child inside Btn_Level2
    [SerializeField] private TMP_Text level2LockText;      // optional TMP inside LockOverlay

    private void Awake()
    {
        HideImmediate();
    }

    public void Show()
    {
        RefreshState();

        gameObject.SetActive(true);

        if (popupCard != null)
            popupCard.SetActive(true);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    public void Hide()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (popupCard != null)
            popupCard.SetActive(false);

        gameObject.SetActive(false);
    }

    public void HideImmediate()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (popupCard != null)
            popupCard.SetActive(false);

        gameObject.SetActive(false);
    }

    public void RefreshState()
    {
        if (titleText != null)
            titleText.text = "CHOOSE CHAPTER";

        // Level 1
        if (level1TitleText != null)
            level1TitleText.text = "Close";

        if (level1SubtitleText != null)
            level1SubtitleText.text = "The Warrior - Replay from the beginning";

        if (level1Button != null)
            level1Button.interactable = true;

        // Level 2
        bool level2Unlocked = GameMgr.Instance != null && GameMgr.Instance.IsSceneUnlockedForMenu(1);

        if (level2TitleText != null)
            level2TitleText.text = "LEVEL 1";

        if (level2SubtitleText != null)
        {
            level2SubtitleText.text = level2Unlocked
                ? "Age Of Ice - Continue your journey"
                : "Age Of Ice - Locked";
        }

        if (level2Button != null)
            level2Button.interactable = level2Unlocked;

        if (level2LockOverlay != null)
            level2LockOverlay.SetActive(!level2Unlocked);

        if (level2LockText != null)
            level2LockText.text = level2Unlocked ? string.Empty : "LOCKED";
    }

    public void LoadLevel1()
    {
        Hide();
        GameMgr.Instance?.LoadCampaignSceneFromMenu(0);
    }

    public void LoadLevel2()
    {
        if (GameMgr.Instance == null)
            return;

        if (!GameMgr.Instance.IsSceneUnlockedForMenu(1))
            return;

        Hide();
        GameMgr.Instance.LoadCampaignSceneFromMenu(1);
    }
}
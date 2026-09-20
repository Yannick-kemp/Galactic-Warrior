using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// The CHOOSE CHAPTER popup: a demo row, then a collapsed chapter selector.
///
/// Closed, the selector shows the lowest unlocked chapter, the one nearest the demo. Tapping it
/// drops down every unlocked chapter so the player can jump to any of them. Tapping it again, or
/// picking an entry, closes the list.
///
/// The dropdown lists ALL unlocked chapters, including the one already shown on the header row.
/// A dropdown that omitted its own current value would leave that chapter unreachable: the header
/// tap opens the list instead of launching, so LEVEL 1 has to be in the list to stay playable.
/// The one exception is a save with a single unlocked chapter, where there is nothing to choose
/// from and the header launches directly rather than opening a one-item list.
///
/// Rows are cloned from <see cref="chapterTemplate"/> rather than wired in the scene: the campaign
/// length lives in GameMgr's campaignSceneOrder, and hand-wired buttons stop covering it the day a
/// level is added. The scene's old Btn_Level3 and Btn_Level4 were inactive, unbound and missing
/// their subtitle line, so they are referenced only to be hidden.
/// </summary>
public class LevelSelectPanelUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;      // LevelSelectPopupRoot CanvasGroup
    [SerializeField] private GameObject popupCard;         // LevelSelectPopupRoot/PopupCard (optional)

    [Header("Title")]
    [SerializeField] private TMP_Text titleText;           // PopupCard/TopBar/TitleText

    [Header("Chapter rows")]
    [Tooltip("Row for campaign index 0, the demo. Always unlocked.")]
    [FormerlySerializedAs("level1Button")]
    [SerializeField] private Button demoButton;

    [Tooltip("Header row of the selector, and the template cloned for each dropdown entry.")]
    [FormerlySerializedAs("level2Button")]
    [SerializeField] private Button chapterTemplate;

    [Tooltip("Rows left over from when the popup was hand-wired. Hidden at runtime.")]
    [SerializeField] private GameObject[] legacyRows;

    [Header("Dropdown")]
    [Tooltip("Appended to the header label when more than one chapter is unlocked.")]
    [SerializeField] private string collapsedMarker = "  ▼";
    [SerializeField] private string expandedMarker = "  ▲";

    [Tooltip("Header label while the list is open. Must not look like a chapter row.")]
    [SerializeField] private string openLabel = "CHOOSE A CHAPTER";

    [Tooltip("Vertical gap between two compact option rows, in canvas units.")]
    [SerializeField] private float optionGap = 10f;

    [Tooltip("Room kept under the last option when the card grows.")]
    [SerializeField] private float cardBottomMargin = 24f;

    private float OptionGap { get { return Mathf.Max(0f, optionGap); } }
    private float CardBottomMargin { get { return Mathf.Max(0f, cardBottomMargin); } }

    private readonly List<GameObject> _dropdownRows = new List<GameObject>();
    private readonly List<int> _unlockedChapters = new List<int>();
    private bool _expanded;

    // Captured before anything can grow the card, so collapsing always restores the authored size.
    private float _cardBaseHeight = -1f;

    // The popup is saved inactive in the scene, so its Awake first runs INSIDE Show()'s
    // SetActive(true). Awake must not hide it then, or the first open shows nothing.
    private bool _opening;

    private void Awake()
    {
        CacheCardHeight();

        if (!_opening)
            HideImmediate();
    }

    private RectTransform CardRect
    {
        get { return chapterTemplate != null ? chapterTemplate.transform.parent as RectTransform : null; }
    }

    private void CacheCardHeight()
    {
        if (_cardBaseHeight > 0f) return;

        RectTransform card = CardRect;
        if (card != null)
            _cardBaseHeight = card.sizeDelta.y;
    }

    public void Show()
    {
        // Always opens closed: the popup is a menu, not a place that remembers where you were.
        _expanded = false;

        // Activate BEFORE RefreshState: the first activation runs Awake, which caches the
        // authored card height — it must see the card before any row grows it.
        _opening = true;
        gameObject.SetActive(true);
        _opening = false;

        RefreshState();

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
        Hide();
    }

    public void RefreshState()
    {
        if (titleText != null)
            titleText.text = "CHOOSE CHAPTER";

        GameMgr mgr = FindGameMgr();

        HideLegacyRows();
        ClearDropdownRows();
        CollectUnlockedChapters(mgr);

        BuildDemoRow(mgr);

        if (chapterTemplate == null)
            return;

        // No chapter unlocked yet: only the demo is offered.
        if (_unlockedChapters.Count == 0)
        {
            chapterTemplate.gameObject.SetActive(false);
            return;
        }

        BuildHeaderRow(mgr);

        if (_expanded)
        {
            BuildDropdownRows(mgr);
        }
        else
        {
            RestoreCardHeight();
        }
    }

    private void CollectUnlockedChapters(GameMgr mgr)
    {
        _unlockedChapters.Clear();
        if (mgr == null) return;

        // Index 0 is the demo and has its own row, so the selector starts at 1.
        for (int index = 1; index < mgr.CampaignSceneCount; index++)
        {
            if (mgr.IsSceneUnlockedForMenu(index))
                _unlockedChapters.Add(index);
        }
    }

    private void BuildDemoRow(GameMgr mgr)
    {
        if (demoButton == null) return;

        string demoName = mgr != null ? mgr.GetCampaignSceneDisplayName(0) : "Warrior";

        FillRow(demoButton, "Demo", demoName + " - Replay from the beginning", true);
        BindLoad(demoButton, 0);
        demoButton.gameObject.SetActive(true);
    }

    private void BuildHeaderRow(GameMgr mgr)
    {
        int headerIndex = _unlockedChapters[0];   // lowest unlocked, the one nearest the demo
        bool hasChoice = _unlockedChapters.Count > 1;

        if (!hasChoice)
        {
            // Nothing to choose from: the row simply is that chapter.
            FillRow(
                chapterTemplate,
                "LEVEL " + headerIndex,
                mgr.GetCampaignSceneDisplayName(headerIndex) + " - Continue your journey",
                true);

            chapterTemplate.gameObject.SetActive(true);
            BindLoad(chapterTemplate, headerIndex);
            return;
        }

        if (_expanded)
        {
            // While the list is open the header must NOT read like a chapter. It used to say
            // "LEVEL 1 ▲" with an identical "LEVEL 1" option directly underneath, so tapping the
            // one that looked like level 1 collapsed the list instead of launching it, and the
            // selector felt broken. Open, this row is only the way to close the list.
            FillRow(
                chapterTemplate,
                openLabel + expandedMarker,
                "Pick a chapter below",
                true);
        }
        else
        {
            FillRow(
                chapterTemplate,
                "LEVEL " + headerIndex + collapsedMarker,
                mgr.GetCampaignSceneDisplayName(headerIndex) + " - Continue your journey",
                true);
        }

        chapterTemplate.gameObject.SetActive(true);
        BindToggle(chapterTemplate);
    }

    /// <summary>
    /// Rows are placed by hand, NOT by a layout group: PopupCard carries a VerticalLayoutGroup but
    /// it is DISABLED, and every row sits at an authored anchoredPosition. A clone therefore keeps
    /// the header's exact position, which stacked the whole list on one line and made the text
    /// unreadable. Positions are derived from the header's own rect so re-authoring the popup does
    /// not silently break this.
    ///
    /// Options are compact, one line each: the two-line shape belongs to the closed selector, and
    /// a full-height list does not fit the card. That is also what a dropdown normally looks like.
    /// </summary>
    private void BuildDropdownRows(GameMgr mgr)
    {
        RectTransform header = (RectTransform)chapterTemplate.transform;
        Transform parent = header.parent;
        int headerSibling = header.GetSiblingIndex();

        float rowHeight = header.rect.height;
        float step = rowHeight + OptionGap;
        Vector2 basePos = header.anchoredPosition;

        for (int i = 0; i < _unlockedChapters.Count; i++)
        {
            int index = _unlockedChapters[i];

            Button row = Instantiate(chapterTemplate, parent);
            row.name = "Btn_ChapterOption" + index;
            row.transform.SetSiblingIndex(headerSibling + 1 + i);
            _dropdownRows.Add(row.gameObject);

            var rt = (RectTransform)row.transform;
            rt.anchoredPosition = new Vector2(basePos.x, basePos.y - step * (i + 1));

            FillCompactRow(row, "LEVEL " + index + "   " + mgr.GetCampaignSceneDisplayName(index));
            BindLoad(row, index);
            row.gameObject.SetActive(true);
        }

        GrowCardFor(_unlockedChapters.Count, basePos.y, rowHeight, step);
    }

    /// <summary>
    /// The card is centre-anchored with a fixed size and its rows hang from the top edge, so making
    /// it taller pushes the extra room downwards where the list needs it. Clamped to the popup root
    /// so a long campaign cannot push the card off screen.
    /// </summary>
    private void GrowCardFor(int optionCount, float headerY, float rowHeight, float step)
    {
        RectTransform card = CardRect;
        if (card == null) return;

        CacheCardHeight();

        float lastRowBottom = Mathf.Abs(headerY - step * optionCount) + rowHeight * 0.5f + CardBottomMargin;
        float wanted = Mathf.Max(_cardBaseHeight, lastRowBottom);

        var root = card.parent as RectTransform;
        if (root != null && root.rect.height > 0f)
            wanted = Mathf.Min(wanted, root.rect.height);

        card.sizeDelta = new Vector2(card.sizeDelta.x, wanted);
    }

    private void RestoreCardHeight()
    {
        RectTransform card = CardRect;
        if (card == null || _cardBaseHeight <= 0f) return;

        card.sizeDelta = new Vector2(card.sizeDelta.x, _cardBaseHeight);
    }

    /// <summary>One-line option: the subtitle object is switched off rather than emptied, so the
    /// row keeps the authored bar and simply takes no vertical room below it.</summary>
    private static void FillCompactRow(Button row, string label)
    {
        if (row == null) return;

        row.interactable = true;

        TMP_Text bar = row.GetComponentInChildren<TMP_Text>(true);
        if (bar == null) return;

        bar.text = label;

        for (int i = 0; i < bar.transform.childCount; i++)
        {
            TMP_Text sub = bar.transform.GetChild(i).GetComponent<TMP_Text>();
            if (sub != null)
                sub.gameObject.SetActive(false);
        }
    }

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        RefreshState();
    }

    /// <summary>
    /// A row carries its bar label as a direct TMP child, and the big subtitle line as a TMP child
    /// of that label. Cloning keeps that shape, so both are found the same way on every row.
    /// </summary>
    private static void FillRow(Button row, string barLabel, string subtitle, bool interactable)
    {
        if (row == null) return;

        row.interactable = interactable;

        TMP_Text label = row.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;

        label.text = barLabel;

        for (int i = 0; i < label.transform.childCount; i++)
        {
            TMP_Text sub = label.transform.GetChild(i).GetComponent<TMP_Text>();
            if (sub == null) continue;

            sub.text = subtitle;
            return;
        }
    }

    /// <summary>
    /// Replaces the whole click event rather than calling RemoveAllListeners, which only drops
    /// listeners added from code. The scene wires LoadLevel2 persistently on the template, and a
    /// clone inherits it — every dropdown entry would have loaded chapter 1.
    /// </summary>
    private void BindLoad(Button row, int index)
    {
        if (row == null) return;

        row.onClick = new Button.ButtonClickedEvent();
        row.onClick.AddListener(() => LoadChapter(index));
    }

    private void BindToggle(Button row)
    {
        if (row == null) return;

        row.onClick = new Button.ButtonClickedEvent();
        row.onClick.AddListener(ToggleExpanded);
    }

    private void ClearDropdownRows()
    {
        for (int i = 0; i < _dropdownRows.Count; i++)
        {
            if (_dropdownRows[i] == null) continue;

            // Deactivated first: Destroy only takes effect at the end of the frame, and the rows
            // are rebuilt immediately after this.
            _dropdownRows[i].SetActive(false);
            Destroy(_dropdownRows[i]);
        }

        _dropdownRows.Clear();
    }

    private void HideLegacyRows()
    {
        if (legacyRows == null) return;

        for (int i = 0; i < legacyRows.Length; i++)
        {
            if (legacyRows[i] != null)
                legacyRows[i].SetActive(false);
        }
    }

    public void LoadChapter(int index)
    {
        GameMgr mgr = FindGameMgr();
        if (mgr == null) return;

        if (!mgr.IsSceneUnlockedForMenu(index))
            return;

        Hide();
        mgr.LoadCampaignSceneFromMenu(index);
    }

    // Kept because the scene still binds them persistently: if this component ever fails before
    // RefreshState runs, the first two rows keep working on their own.
    public void LoadLevel1()
    {
        LoadChapter(0);
    }

    public void LoadLevel2()
    {
        LoadChapter(1);
    }

    /// <summary>
    /// GameMgr.Instance can be null while a GameMgr exists in the scene, which was observed in play
    /// mode, so the singleton alone is not enough to reach it.
    /// </summary>
    private static GameMgr FindGameMgr()
    {
        return GameMgr.Instance != null ? GameMgr.Instance : FindFirstObjectByType<GameMgr>();
    }
}

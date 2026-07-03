using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsPopupUI : MonoBehaviour
{
    [SerializeField] private Button closeButton;
    [SerializeField] private Button muteButton;
    [SerializeField] private Image muteIcon;

    [Header("Icon colors")]
    [SerializeField] private Color unmutedColor = Color.white;
    [SerializeField] private Color mutedColor = new Color(0.25f, 0.25f, 0.25f, 1f);

    [Header("Control Scheme (optional)")]
    [SerializeField] private Button controlSchemeButton;
    [SerializeField] private TMP_Text controlSchemeLabel;
    [SerializeField] private string tapLabel = "Controls: Tap";
    [SerializeField] private string directLabel = "Controls: Joystick";

    [Header("Handedness (joystick mode only)")]
    [SerializeField] private Button handednessButton;
    [SerializeField] private TMP_Text handednessLabel;
    [SerializeField] private string rightHandedLabel = "Layout: Right-handed";
    [SerializeField] private string leftHandedLabel = "Layout: Left-handed";

    [Header("Auto-build layout (fractions of the popup panel)")]
    [Tooltip("If no buttons are wired, build them at runtime inside this popup.")]
    [SerializeField] private bool autoBuildButtons = true;
    [Tooltip("Button width as a fraction of the panel width.")]
    [SerializeField, Range(0.3f, 0.95f)] private float buttonWidthFraction = 0.72f;
    [Tooltip("Button height as a fraction of the panel height.")]
    [SerializeField, Range(0.08f, 0.30f)] private float buttonHeightFraction = 0.14f;
    [Tooltip("Bottom margin (fraction of panel height) below the lowest button.")]
    [SerializeField, Range(0f, 0.25f)] private float bottomMarginFraction = 0.07f;

    private bool muted;
    private bool _autoScheme;
    private bool _autoHand;

    private void Awake()
    {
        closeButton.onClick.AddListener(Hide);
        muteButton.onClick.AddListener(ToggleMute);

        if (autoBuildButtons)
        {
            if (controlSchemeButton == null)
            {
                BuildRuntimeButton("ControlSchemeButton", out controlSchemeButton, out controlSchemeLabel);
                _autoScheme = true;
            }

            if (handednessButton == null)
            {
                BuildRuntimeButton("HandednessButton", out handednessButton, out handednessLabel);
                _autoHand = true;
            }
        }

        if (controlSchemeButton != null)
            controlSchemeButton.onClick.AddListener(ToggleControlScheme);

        if (handednessButton != null)
            handednessButton.onClick.AddListener(ToggleHandedness);

        AudioMute.Apply();
        RefreshIcon();
        RefreshControlSchemeLabel();
        RefreshHandednessLabel();
        LayoutButtons();
        Hide();
    }

    // Builds a labeled toggle button inside the popup so a choice appears with no manual
    // UI wiring. Skipped for any field already assigned in the Inspector. Positioning is
    // done by LayoutButtons() (relative to the panel size), not here.
    private void BuildRuntimeButton(string name, out Button button, out TMP_Text label)
    {
        button = null;
        label = null;

        RectTransform parent = transform as RectTransform;
        if (parent == null)
            return;

        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

        var img = go.GetComponent<Image>();
        img.sprite = RuntimeSprites.Solid();
        img.color = new Color(0f, 0f, 0f, 0.55f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(12f, 6f);
        lrt.offsetMax = new Vector2(-12f, -6f);

        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 18f;
        tmp.fontSizeMax = 40f;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        button = go.GetComponent<Button>();
        label = tmp;
    }

    // Positions the auto-built buttons in the lower part of the popup panel, sized as a
    // fraction of the panel — so they always sit inside the frame regardless of its size.
    private void LayoutButtons()
    {
        RectTransform parent = transform as RectTransform;
        if (parent == null)
            return;

        float w = parent.rect.width;
        float h = parent.rect.height;
        if (w <= 1f || h <= 1f)
            return;

        Vector2 size = new Vector2(w * buttonWidthFraction, h * buttonHeightFraction);
        float margin = h * bottomMarginFraction;
        float gap = size.y * 0.25f;

        float lowerY = -h * 0.5f + margin + size.y * 0.5f;
        float upperY = lowerY + size.y + gap;

        // If only one auto button is shown (tap mode hides handedness), center it lower.
        if (_autoScheme)
            SetButtonRect(controlSchemeButton, size, new Vector2(0f, upperY));
        if (_autoHand)
            SetButtonRect(handednessButton, size, new Vector2(0f, lowerY));
    }

    private static void SetButtonRect(Button b, Vector2 size, Vector2 anchoredPos)
    {
        if (b == null)
            return;

        var rt = b.transform as RectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
    }

    public void Show()
    {
        gameObject.SetActive(true);
        LayoutButtons(); // panel rect is valid once shown

        // Becomes the top navigation context; closing pops back to the pause/menu underneath.
        MenuNavigator.PushContext(transform);
    }

    public void Hide()
    {
        MenuNavigator.PopContext(transform);
        gameObject.SetActive(false);
    }

    private void ToggleMute()
    {
        AudioMute.Toggle();
        RefreshIcon();
    }

    private void RefreshIcon()
    {
        if (muteIcon != null)
            muteIcon.color = AudioMute.IsMuted ? mutedColor : unmutedColor;
    }

    private void ToggleControlScheme()
    {
        ControlScheme.Toggle();
        RefreshControlSchemeLabel();
    }

    private void RefreshControlSchemeLabel()
    {
        // On Xbox (GameCore) the scheme is locked to Direct (no touch/mouse), so the toggle
        // would be a dead/soft-lock control — hide it entirely. Desktop/mobile keep it.
        if (controlSchemeButton != null)
            controlSchemeButton.gameObject.SetActive(ControlScheme.CanChoose);

        if (controlSchemeLabel != null)
            controlSchemeLabel.text = ControlScheme.IsDirect ? directLabel : tapLabel;

        // Handedness only matters for the joystick — hide it in tap mode.
        if (handednessButton != null)
            handednessButton.gameObject.SetActive(ControlScheme.IsDirect);
    }

    private void ToggleHandedness()
    {
        ControlHandedness.Toggle();
        RefreshHandednessLabel();
    }

    private void RefreshHandednessLabel()
    {
        if (handednessLabel != null)
            handednessLabel.text = ControlHandedness.IsLeft ? leftHandedLabel : rightHandedLabel;
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-built touch pause menu for the mobile (Dev) build. The on-screen Pause
/// button (PauseButtonUI) opens this panel; there is no authored prefab UI to wire,
/// so the whole overlay is constructed in code following the project convention
/// (RuntimeSprites + BuildRuntimeButton, like SettingsPopupUI / DirectControlHud).
///
/// Layout:
///   - PAUSE view    : Resume, Settings
///   - SETTINGS view : Sound + control-scheme + handedness toggles (same underlying
///                     statics as the MainMenu SettingsPopupUI) + Back.
/// Closing Settings returns to the PAUSE view; Resume hands back to PauseButtonUI.
///
/// The overlay is parented to the ROOT canvas (never under a SafeArea) so it is not
/// clipped to the safe-area rect — same pitfall the DEFEAT overlay hit.
/// </summary>
public class TouchPauseMenu : MonoBehaviour
{
    private PauseButtonUI _owner;

    private GameObject _pauseView;
    private GameObject _settingsView;

    private TMP_Text _soundLabel;
    private TMP_Text _schemeLabel;
    private Button _handednessButton;
    private TMP_Text _handednessLabel;

    // Sizes are in canvas reference units; the CanvasScaler handles device scaling, so
    // everything is anchored to center with fixed sizes (no dependency on canvas rect).
    private const float ButtonW = 560f;
    private const float ButtonH = 110f;
    private const float TitleH = 110f;

    /// <summary>
    /// Builds the overlay under the owner's root canvas and returns it, hidden. The
    /// caller (PauseButtonUI) drives visibility via SetActive on this GameObject.
    /// </summary>
    public static TouchPauseMenu Create(PauseButtonUI owner)
    {
        if (owner == null)
            return null;

        Transform canvasRoot = ResolveCanvasRoot(owner.transform);
        if (canvasRoot == null)
            return null;

        var go = new GameObject("TouchPauseMenu", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(canvasRoot, false);
        StretchFull(rt);
        rt.SetAsLastSibling(); // render on top of the rest of the HUD

        var menu = go.AddComponent<TouchPauseMenu>();
        menu._owner = owner;
        menu.Build();
        go.SetActive(false);
        return menu;
    }

    private void Build()
    {
        // Dark scrim that also blocks clicks on the frozen gameplay behind it.
        var scrim = new GameObject("Scrim", typeof(RectTransform), typeof(Image));
        var scrimRt = scrim.GetComponent<RectTransform>();
        scrimRt.SetParent(transform, false);
        StretchFull(scrimRt);
        var scrimImg = scrim.GetComponent<Image>();
        scrimImg.sprite = RuntimeSprites.Solid();
        scrimImg.color = new Color(0f, 0f, 0f, 0.72f);
        scrimImg.raycastTarget = true;

        // ---- PAUSE view ----
        _pauseView = MakeView("PauseView");
        MakeTitle(_pauseView.transform, "PAUSE", 220f);
        MakeButton(_pauseView.transform, "Resume", 60f, OnResume);
        MakeButton(_pauseView.transform, "Settings", -80f, ShowSettings);

        // ---- SETTINGS view (hidden until Settings is pressed) ----
        _settingsView = MakeView("SettingsView");
        MakeTitle(_settingsView.transform, "SETTINGS", 260f);
        MakeButton(_settingsView.transform, "Sound", 110f, OnToggleSound, out _soundLabel);
        MakeButton(_settingsView.transform, "Controls", -10f, OnToggleScheme, out _schemeLabel);
        _handednessButton = MakeButton(_settingsView.transform, "Layout", -130f, OnToggleHandedness, out _handednessLabel);
        MakeButton(_settingsView.transform, "Back", -270f, ShowPause);
        _settingsView.SetActive(false);
    }

    private void OnEnable()
    {
        // Always open on the PAUSE view with fresh labels each time the menu appears.
        ShowPause();
    }

    // --- View switching ---

    private void ShowPause()
    {
        if (_pauseView != null) _pauseView.SetActive(true);
        if (_settingsView != null) _settingsView.SetActive(false);
    }

    private void ShowSettings()
    {
        if (_pauseView != null) _pauseView.SetActive(false);
        if (_settingsView != null) _settingsView.SetActive(true);
        RefreshSettingsLabels();
    }

    // --- Actions ---

    private void OnResume()
    {
        // Hand back to PauseButtonUI, which restores timeScale/audio/input and hides us.
        if (_owner != null)
            _owner.Resume();
        else
            gameObject.SetActive(false);
    }

    private void OnToggleSound()
    {
        AudioMute.Toggle();
        RefreshSettingsLabels();
    }

    private void OnToggleScheme()
    {
        ControlScheme.Toggle();
        RefreshSettingsLabels();
    }

    private void OnToggleHandedness()
    {
        ControlHandedness.Toggle();
        RefreshSettingsLabels();
    }

    private void RefreshSettingsLabels()
    {
        if (_soundLabel != null)
            _soundLabel.text = AudioMute.IsMuted ? "Sound: Off" : "Sound: On";

        if (_schemeLabel != null)
            _schemeLabel.text = ControlScheme.IsDirect ? "Controls: Joystick" : "Controls: Tap";

        // Handedness only matters for the on-screen joystick — hide it in tap mode.
        if (_handednessButton != null)
            _handednessButton.gameObject.SetActive(ControlScheme.IsDirect);

        if (_handednessLabel != null)
            _handednessLabel.text = ControlHandedness.IsLeft ? "Layout: Left-handed" : "Layout: Right-handed";
    }

    // --- Runtime UI builders ---

    private GameObject MakeView(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        StretchFull(rt);
        return go;
    }

    private void MakeTitle(Transform parent, string text, float y)
    {
        var go = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Center(rt, new Vector2(ButtonW, TitleH), new Vector2(0f, y));

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 32f;
        tmp.fontSizeMax = 72f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
    }

    private Button MakeButton(Transform parent, string label, float y, UnityEngine.Events.UnityAction onClick)
    {
        return MakeButton(parent, label, y, onClick, out _);
    }

    private Button MakeButton(Transform parent, string label, float y,
        UnityEngine.Events.UnityAction onClick, out TMP_Text labelText)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Center(rt, new Vector2(ButtonW, ButtonH), new Vector2(0f, y));

        var img = go.GetComponent<Image>();
        img.sprite = RuntimeSprites.Solid();
        img.color = new Color(0f, 0f, 0f, 0.55f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(16f, 8f);
        lrt.offsetMax = new Vector2(-16f, -8f);

        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 22f;
        tmp.fontSizeMax = 44f;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        var button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);

        labelText = tmp;
        return button;
    }

    // --- Helpers ---

    private static void Center(RectTransform rt, Vector2 size, Vector2 anchoredPos)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static Transform ResolveCanvasRoot(Transform from)
    {
        Canvas canvas = from.GetComponentInParent<Canvas>();
        if (canvas != null)
            return canvas.rootCanvas.transform;

        // No canvas in the hierarchy (unexpected for a UI button) — make a fallback one.
        var go = new GameObject("PauseMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 999;
        return go.transform;
    }
}

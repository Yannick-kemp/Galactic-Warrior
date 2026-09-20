using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shown in the web demo when the last demo chapter is cleared. YouTube requires the game to say
/// clearly that there is no more content; there it must NOT point to a store or any link. The
/// polymart.be build (WebDemo.IsSite) adds a button to the full game on Google Play.
///
/// Built entirely in code on its own overlay canvas (same convention as TouchPauseMenu), so it
/// does not depend on the scene HUD, which may be hidden or destroyed at that moment.
/// </summary>
public class DemoEndScreen : MonoBehaviour
{
    private const float PanelW = 900f;
    private const float ButtonW = 560f;
    private const float ButtonH = 110f;

    private System.Action _onMainMenu;

    public static DemoEndScreen Show(System.Action onMainMenu)
    {
        // Single entry point for "the demo was cleared": the site counts the run here.
        WebDemo.ReportGameCompleted();

        var go = new GameObject("DemoEndScreen", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000; // above every HUD canvas

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (EventSystem.current == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var screen = go.AddComponent<DemoEndScreen>();
        screen._onMainMenu = onMainMenu;
        screen.Build();
        return screen;
    }

    private void Build()
    {
        var scrim = new GameObject("Scrim", typeof(RectTransform), typeof(Image));
        var scrimRt = scrim.GetComponent<RectTransform>();
        scrimRt.SetParent(transform, false);
        scrimRt.anchorMin = Vector2.zero;
        scrimRt.anchorMax = Vector2.one;
        scrimRt.offsetMin = scrimRt.offsetMax = Vector2.zero;
        var scrimImg = scrim.GetComponent<Image>();
        scrimImg.sprite = RuntimeSprites.Solid();
        scrimImg.color = new Color(0f, 0f, 0f, 0.8f);
        scrimImg.raycastTarget = true; // blocks the frozen gameplay behind

        MakeText("Title", "END OF THE DEMO", new Vector2(PanelW, 120f), new Vector2(0f, 250f), 40f, 80f, true);

        if (WebDemo.IsSite)
        {
            MakeText("Body",
                "You have finished the demo. Thanks for playing, Warrior!\nThe full game continues on Android.",
                new Vector2(PanelW, 200f), new Vector2(0f, 80f), 24f, 44f, false);

            // Listed first so a gamepad lands on it (MenuNavigator selects the first button).
            MakeButton("Get the full game on Google Play", new Vector2(0f, -110f), WebDemo.OpenFullGame, highlighted: true);
            MakeButton("Main Menu", new Vector2(0f, -250f), OnMainMenu);
        }
        else
        {
            MakeText("Body",
                "You have finished the demo: there is no more to play here.\nThanks for playing, Warrior!",
                new Vector2(PanelW, 200f), new Vector2(0f, 80f), 24f, 44f, false);

            MakeButton("Main Menu", new Vector2(0f, -150f), OnMainMenu);
        }
    }

    private void OnMainMenu()
    {
        Destroy(gameObject);
        _onMainMenu?.Invoke();
    }

    private void MakeText(string name, string text, Vector2 size, Vector2 pos, float min, float max, bool bold)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        Center(rt, size, pos);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = min;
        tmp.fontSizeMax = max;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
    }

    private void MakeButton(string label, Vector2 pos, UnityEngine.Events.UnityAction onClick, bool highlighted = false)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        Center(rt, new Vector2(highlighted ? ButtonW * 1.35f : ButtonW, ButtonH), pos);

        var img = go.GetComponent<Image>();
        img.sprite = RuntimeSprites.Solid();
        // Main call to action in Google Play green, the rest in translucent white.
        img.color = highlighted ? new Color(0.0f, 0.53f, 0.33f, 0.95f) : new Color(1f, 1f, 1f, 0.18f);

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

        go.GetComponent<Button>().onClick.AddListener(onClick);
    }

    private static void Center(RectTransform rt, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }
}

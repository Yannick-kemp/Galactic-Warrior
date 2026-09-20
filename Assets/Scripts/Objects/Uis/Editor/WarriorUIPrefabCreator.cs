#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class WarriorUIPrefabCreator
{
    [MenuItem("Tools/UI/Create Warrior UI Prefab")]
    public static void CreatePrefab()
    {
        EnsureFolder("Assets/UI");
        EnsureFolder("Assets/UI/Prefabs");

        // Canvas
        GameObject canvasGO = new GameObject("WarriorUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // EventSystem (si absent)
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // SafeArea
        GameObject safe = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
        safe.transform.SetParent(canvasGO.transform, false);
        Stretch(safe.GetComponent<RectTransform>());

        // HUD Root (Top-Left)
        GameObject hud = new GameObject("HUD", typeof(RectTransform));
        hud.transform.SetParent(safe.transform, false);
        var hudRT = hud.GetComponent<RectTransform>();
        hudRT.anchorMin = new Vector2(0f, 1f);
        hudRT.anchorMax = new Vector2(0f, 1f);
        hudRT.pivot = new Vector2(0f, 1f);
        hudRT.anchoredPosition = new Vector2(150f, -20f); // d�cal� pour ne pas chevaucher ta colonne reliques
        hudRT.sizeDelta = new Vector2(450f, 140f);         // se termine juste apr�s le bloc Retries (c�ur + x/x)

        // Panel
        GameObject panel = NewImage("WarriorPanel", hud.transform, new Color(0f, 0f, 0f, 0.35f));
        var panelRT = panel.GetComponent<RectTransform>();
        Stretch(panelRT);
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        // Portrait (mask)
        GameObject portraitMask = NewImage("PortraitMask", panel.transform, new Color(1f, 1f, 1f, 0.08f));
        var pmRT = portraitMask.GetComponent<RectTransform>();
        pmRT.anchorMin = new Vector2(0f, 0.5f);
        pmRT.anchorMax = new Vector2(0f, 0.5f);
        pmRT.pivot = new Vector2(0f, 0.5f);
        pmRT.anchoredPosition = new Vector2(14f, 0f);
        pmRT.sizeDelta = new Vector2(92f, 92f);
        portraitMask.AddComponent<Mask>().showMaskGraphic = true;

        GameObject portrait = NewImage("Portrait", portraitMask.transform, new Color(1f, 1f, 1f, 1f));
        Stretch(portrait.GetComponent<RectTransform>());

        // Bars container
        GameObject bars = new GameObject("Bars", typeof(RectTransform), typeof(VerticalLayoutGroup));
        bars.transform.SetParent(panel.transform, false);
        var barsRT = bars.GetComponent<RectTransform>();
        barsRT.anchorMin = new Vector2(0f, 0.5f);
        barsRT.anchorMax = new Vector2(0f, 0.5f);
        barsRT.pivot = new Vector2(0f, 0.5f);
        barsRT.anchoredPosition = new Vector2(120f, 8f);
        barsRT.sizeDelta = new Vector2(260f, 60f);

        var vlg = bars.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        // HP bar (bg + fill)
        var hpBg = NewImage("HP_Bg", bars.transform, new Color(0f, 0f, 0f, 0.55f));
        hpBg.GetComponent<RectTransform>().sizeDelta = new Vector2(260f, 18f);
        var hpFill = NewFilledImage("HP_Fill", hpBg.transform, Image.FillMethod.Horizontal, new Color(0.2f, 1f, 0.2f, 0.9f));

        // Shield bar
        var shBg = NewImage("Shield_Bg", bars.transform, new Color(0f, 0f, 0f, 0.45f));
        shBg.GetComponent<RectTransform>().sizeDelta = new Vector2(260f, 12f);
        var shFill = NewFilledImage("Shield_Fill", shBg.transform, Image.FillMethod.Horizontal, new Color(0.35f, 0.95f, 1f, 0.85f));

        // HP text (optionnel)
        GameObject hpTxtGO = new GameObject("HP_Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        hpTxtGO.transform.SetParent(panel.transform, false);
        var hpTxtRT = hpTxtGO.GetComponent<RectTransform>();
        hpTxtRT.anchorMin = new Vector2(0f, 0.5f);
        hpTxtRT.anchorMax = new Vector2(0f, 0.5f);
        hpTxtRT.pivot = new Vector2(0f, 0.5f);
        hpTxtRT.anchoredPosition = new Vector2(120f, -36f);
        hpTxtRT.sizeDelta = new Vector2(260f, 26f);

        var hpTMP = hpTxtGO.GetComponent<TextMeshProUGUI>();
        hpTMP.text = "100/100";
        hpTMP.fontSize = 22;
        hpTMP.color = Color.white;

        // Retries (cœur + "x/x") — au bout droit de la barre de vie, sur la même ligne.
        // Bars : x 120→380 ; HP bar dans le haut du bloc → on s'aligne sur cette ligne (y≈24).
        GameObject retriesGroup = new GameObject("RetriesGroup", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        retriesGroup.transform.SetParent(panel.transform, false);
        var rgRT = retriesGroup.GetComponent<RectTransform>();
        rgRT.anchorMin = new Vector2(0f, 0.5f);
        rgRT.anchorMax = new Vector2(0f, 0.5f);
        rgRT.pivot = new Vector2(0f, 0.5f);
        rgRT.anchoredPosition = new Vector2(384f, 24f);    // juste après la barre HP, à son niveau
        rgRT.sizeDelta = new Vector2(62f, 22f);

        var rgHlg = retriesGroup.GetComponent<HorizontalLayoutGroup>();
        rgHlg.spacing = 4;
        rgHlg.childAlignment = TextAnchor.MiddleCenter;
        rgHlg.childForceExpandHeight = false;
        rgHlg.childForceExpandWidth = false;

        var heartIcon = NewImage("HeartIcon", retriesGroup.transform, new Color(1f, 1f, 1f, 1f));
        heartIcon.GetComponent<RectTransform>().sizeDelta = new Vector2(24f, 24f);
        heartIcon.GetComponent<Image>().preserveAspect = true; // sprite cœur à assigner dans l'Inspector

        var retriesTMP = NewTMP("RetriesText", retriesGroup.transform, "3/3", 22);
        retriesTMP.alignment = TextAlignmentOptions.MidlineLeft;
        retriesTMP.GetComponent<RectTransform>().sizeDelta = new Vector2(48f, 24f);

        // Cooldowns radials (� droite)
        GameObject cds = new GameObject("Cooldowns", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        cds.transform.SetParent(panel.transform, false);
        var cdsRT = cds.GetComponent<RectTransform>();
        cdsRT.anchorMin = new Vector2(1f, 0.5f);
        cdsRT.anchorMax = new Vector2(1f, 0.5f);
        cdsRT.pivot = new Vector2(1f, 0.5f);
        cdsRT.anchoredPosition = new Vector2(-14f, 0f);
        cdsRT.sizeDelta = new Vector2(120f, 80f);

        var hlg = cds.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandHeight = false;
        hlg.childForceExpandWidth = false;

        var sprintCd = NewFilledImage("SprintCD", cds.transform, Image.FillMethod.Radial360, new Color(0f, 0f, 0f, 0.6f));
        sprintCd.GetComponent<RectTransform>().sizeDelta = new Vector2(44, 44);

        var atk2Cd = NewFilledImage("Attack2CD", cds.transform, Image.FillMethod.Radial360, new Color(0f, 0f, 0f, 0.6f));
        atk2Cd.GetComponent<RectTransform>().sizeDelta = new Vector2(44, 44);

        // HUD script + wiring
        var hudScript = hud.AddComponent<WarriorHudUI>();
        AssignPrivateField(hudScript, "hpFill", hpFill);
        AssignPrivateField(hudScript, "shieldFill", shFill);
        AssignPrivateField(hudScript, "hpText", hpTMP);
        AssignPrivateField(hudScript, "sprintCdFill", sprintCd);
        AssignPrivateField(hudScript, "attack2CdFill", atk2Cd);
        AssignPrivateField(hudScript, "retriesText", retriesTMP);
        AssignPrivateField(hudScript, "heartIcon", heartIcon.GetComponent<Image>());

        // GameOver Overlay
        GameObject overlay = new GameObject("GameOverOverlay", typeof(RectTransform), typeof(CanvasGroup), typeof(GameOverUI));
        overlay.transform.SetParent(safe.transform, false);
        Stretch(overlay.GetComponent<RectTransform>());

        var cg = overlay.GetComponent<CanvasGroup>();
        cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;

        var fade = NewImage("Fade", overlay.transform, new Color(0f, 0f, 0f, 0.65f));
        Stretch(fade.GetComponent<RectTransform>());

        var goPanel = NewImage("Panel", overlay.transform, new Color(0.05f, 0.05f, 0.08f, 0.9f));
        var goPanelRT = goPanel.GetComponent<RectTransform>();
        goPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
        goPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
        goPanelRT.pivot = new Vector2(0.5f, 0.5f);
        goPanelRT.sizeDelta = new Vector2(520f, 320f);

        var title = NewTMP("Title", goPanel.transform, "DEFEAT", 56);
        title.alignment = TextAlignmentOptions.Center;
        SetAnchored(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(480f, 70f));

        var score = NewTMP("Score", goPanel.transform, "SCORE: 0", 28);
        score.alignment = TextAlignmentOptions.Center;
        SetAnchored(score.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(480f, 50f));

        // Buttons row
        GameObject btnRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        btnRow.transform.SetParent(goPanel.transform, false);
        var btnRT = btnRow.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 0f);
        btnRT.anchorMax = new Vector2(0.5f, 0f);
        btnRT.pivot = new Vector2(0.5f, 0f);
        btnRT.anchoredPosition = new Vector2(0f, 24f);
        btnRT.sizeDelta = new Vector2(480f, 90f);

        var btnLayout = btnRow.GetComponent<HorizontalLayoutGroup>();
        btnLayout.spacing = 14;
        btnLayout.childAlignment = TextAnchor.MiddleCenter;
        btnLayout.childForceExpandWidth = false;

        Button retry = NewButton("Retry", btnRow.transform, "RETRY");
        Button menu = NewButton("Menu", btnRow.transform, "MENU");
        Button revive = NewButton("Revive", btnRow.transform, "REVIVE");

        // Wire GameOverUI
        var go = overlay.GetComponent<GameOverUI>();
        AssignPrivateField(go, "group", cg);
        AssignPrivateField(go, "titleText", title);
        AssignPrivateField(go, "scoreText", score);
        AssignPrivateField(go, "retryButton", retry);
        AssignPrivateField(go, "menuButton", menu);
        AssignPrivateField(go, "reviveButton", revive);

        // UIManager (optionnel)
        GameObject uiMgr = new GameObject("UIManager", typeof(UIManager));
        AssignPrivateField(uiMgr.GetComponent<UIManager>(), "gameOver", go);

        // Save prefab
        string path = "Assets/UI/Prefabs/WarriorUI.prefab";
        PrefabUtility.SaveAsPrefabAsset(canvasGO, path);

        Object.DestroyImmediate(canvasGO);
        Object.DestroyImmediate(uiMgr);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Warrior UI Prefab created: {path}");
    }

    // ---------- Helpers ----------
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
        string name = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static GameObject NewImage(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = c;
        return go;
    }

    private static Image NewFilledImage(string name, Transform parent, Image.FillMethod method, Color c)
    {
        var go = NewImage(name, parent, c);
        var img = go.GetComponent<Image>();
        img.type = Image.Type.Filled;
        img.fillMethod = method;
        img.fillAmount = 0f;
        Stretch(go.GetComponent<RectTransform>());
        return img;
    }

    private static TextMeshProUGUI NewTMP(string name, Transform parent, string text, int size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var t = go.AddComponent<TextMeshProUGUI>(); //correct UI TMP component
        t.text = text;
        t.fontSize = size;
        t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.NoWrap;

        return t;
    }

    private static void SetAnchored(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static Button NewButton(string name, Transform parent, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(140f, 56f);

        var img = go.GetComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);

        var b = go.GetComponent<Button>();

        var txt = NewTMP("Text", go.transform, label, 26);
        txt.alignment = TextAlignmentOptions.Center;
        Stretch(txt.rectTransform);

        return b;
    }

    private static void AssignPrivateField(Object obj, string fieldName, Object value)
    {
        var f = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (f != null) f.SetValue(obj, value);
    }
}
#endif
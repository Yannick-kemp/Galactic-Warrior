using UnityEngine;

/// <summary>
/// Esc closes the top-most dialog, one level per press: purchase offer, settings popup,
/// chapter select, then the pause menu (Settings view first, then Resume). In gameplay
/// with nothing open, Esc opens the pause menu.
///
/// YouTube Playables asks that dialogs close with Esc. Only Input.GetKeyDown is read, so
/// the browser still receives the key (no preventDefault from our side).
///
/// Skipped on Android/iOS: there KeyCode.Escape is the system Back button, and the shipped
/// mobile build keeps its current behaviour.
/// </summary>
public class EscapeKeyHandler : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.IPhonePlayer)
            return;

        var go = new GameObject("EscapeKeyHandler");
        DontDestroyOnLoad(go);
        go.AddComponent<EscapeKeyHandler>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            HandleEscape();
    }

    /// <summary>One Esc press. Public so it can be driven without a real keyboard.</summary>
    public static void HandleEscape()
    {
        // Searched only on the key press, never per frame.
        // Faded-out purchase screens stay active, so pick the one actually showing.
        foreach (PurchaseUI purchase in FindObjectsByType<PurchaseUI>(FindObjectsSortMode.None))
        {
            if (purchase.IsVisible)
            {
                purchase.OnNoThanksPressed();
                return;
            }
        }

        SettingsPopupUI settings = FindFirstObjectByType<SettingsPopupUI>();
        if (settings != null)
        {
            settings.Hide();
            return;
        }

        LevelSelectPanelUI levelSelect = FindFirstObjectByType<LevelSelectPanelUI>();
        if (levelSelect != null)
        {
            levelSelect.Hide();
            return;
        }

        if (PauseButtonUI.IsPaused)
        {
            TouchPauseMenu menu = FindFirstObjectByType<TouchPauseMenu>();
            if (menu != null)
            {
                menu.HandleBack();
                return;
            }

            // Authored pause panel (no TouchPauseMenu): plain resume.
            PauseButtonUI pausedButton = FindFirstObjectByType<PauseButtonUI>();
            if (pausedButton != null)
                pausedButton.Resume();
            return;
        }

        TryOpenPause();
    }

    private static void TryOpenPause()
    {
        // Only where the on-screen Pause button is actually offered.
        PauseButtonUI button = FindFirstObjectByType<PauseButtonUI>();
        if (button == null)
            return;

        // Something else already froze the game (tutorial gate, game over, cinematic).
        // Pausing there would store timeScale 1 and Resume would break that freeze.
        if (Time.timeScale <= 0f)
            return;
        if (InputMgr.Instance != null && InputMgr.Instance.InputLocked)
            return;

        button.Pause();
    }
}

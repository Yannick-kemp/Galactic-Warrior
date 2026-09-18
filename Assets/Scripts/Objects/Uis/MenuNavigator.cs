using Assets.Scripts.Objects.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Gamepad navigation of the menus in the web build: stick / D-pad moves the selection,
/// A confirms, B goes back (same as Esc: EscapeKeyHandler), Start pauses (see EscapeKeyHandler).
///
/// Ported from the DevXbox/DevSteam MenuNavigator (the EventSystem module does not read the
/// gamepad stick in this project, so the pad is read directly and the selection driven here;
/// the selected button is scaled up so the focus is visible). Difference: instead of every menu
/// registering itself, the navigator finds the top-most open menu on its own, so no menu code
/// had to change.
///
/// Idle unless a controller is connected: mouse and touch players never see a selection.
/// </summary>
public class MenuNavigator : MonoBehaviour
{
    private const float MoveRepeatDelay = 0.20f; // seconds between steps while a direction is held
    private const float Deadzone = 0.5f;
    private const float HighlightScale = 1.08f;
    private const float ContextRefreshDelay = 0.25f;

    private float _nextMoveTime;
    private bool _movePrimed = true;

    private GameObject _highlighted;
    private Vector3 _highlightBaseScale = Vector3.one;

    private Transform _context;
    private float _nextContextRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (!GamepadSupport.Enabled)
            return;

        var go = new GameObject("MenuNavigator");
        DontDestroyOnLoad(go);
        go.AddComponent<MenuNavigator>();
    }

    private void Update()
    {
        EventSystem es = EventSystem.current;

#if ENABLE_INPUT_SYSTEM
        var pad = GamepadSupport.Pad;
#else
        object pad = null;
#endif
        if (pad == null || es == null)
        {
            Release(es);
            return;
        }

        Transform ctx = CurrentContext();
        if (ctx == null)
        {
            // Gameplay: no menu open, the pad drives the Warrior.
            Release(es);
            return;
        }

        // The navigator owns directional input while a pad is in use (no double moves).
        es.sendNavigationEvents = false;

        GameObject selGo = es.currentSelectedGameObject;
        Selectable sel = selGo != null ? selGo.GetComponent<Selectable>() : null;
        bool valid = sel != null && sel.IsInteractable() && selGo.activeInHierarchy && IsUnder(selGo.transform, ctx);
        if (!valid)
        {
            selGo = FirstSelectable(ctx);
            es.SetSelectedGameObject(selGo);
            sel = selGo != null ? selGo.GetComponent<Selectable>() : null;
        }

#if ENABLE_INPUT_SYSTEM
        Vector2 move = pad.leftStick.ReadValue() + pad.dpad.ReadValue();
        if (move.magnitude < Deadzone)
        {
            _movePrimed = true;
        }
        else if (_movePrimed || Time.unscaledTime >= _nextMoveTime)
        {
            _movePrimed = false;
            _nextMoveTime = Time.unscaledTime + MoveRepeatDelay;
            if (sel != null)
                MoveSelection(es, sel, move, ctx);
        }

        selGo = es.currentSelectedGameObject;
        if (pad.buttonSouth.wasPressedThisFrame && selGo != null)       // A / Cross
            ExecuteEvents.Execute(selGo, new BaseEventData(es), ExecuteEvents.submitHandler);
        else if (pad.buttonEast.wasPressedThisFrame)                    // B / Circle
            EscapeKeyHandler.HandleEscape();
#endif

        ApplyHighlight(es.currentSelectedGameObject);
    }

    private void Release(EventSystem es)
    {
        if (_highlighted == null)
            return;

        if (es != null && es.currentSelectedGameObject == _highlighted)
            es.SetSelectedGameObject(null);
        ClearHighlight();
        if (es != null)
            es.sendNavigationEvents = true;
    }

    // ── Which menu is open ─────────────────────────────────────────────────────────

    private Transform CurrentContext()
    {
        // Looked up a few times a second, not every frame (Find* calls).
        if (Time.unscaledTime < _nextContextRefresh && (_context == null || _context.gameObject.activeInHierarchy))
            return _context;
        _nextContextRefresh = Time.unscaledTime + ContextRefreshDelay;
        _context = FindTopMenu();
        return _context;
    }

    // Top-most first: the same order EscapeKeyHandler closes them.
    private static Transform FindTopMenu()
    {
        DemoEndScreen demoEnd = FindFirstObjectByType<DemoEndScreen>();
        if (demoEnd != null)
            return demoEnd.transform;

        SettingsPopupUI settings = FindFirstObjectByType<SettingsPopupUI>();
        if (settings != null)
            return settings.transform;

        LevelSelectPanelUI levels = FindFirstObjectByType<LevelSelectPanelUI>();
        if (levels != null)
            return levels.transform;

        TouchPauseMenu pause = FindFirstObjectByType<TouchPauseMenu>();
        if (pause != null)
            return pause.transform;

        foreach (GameOverUI gameOver in FindObjectsByType<GameOverUI>(FindObjectsSortMode.None))
        {
            if (gameOver.IsShown)
                return gameOver.transform;
        }

        MainMenuUI mainMenu = FindFirstObjectByType<MainMenuUI>();
        if (mainMenu != null)
            return mainMenu.NavigationRoot;

        return null;
    }

    // ── Selection ──────────────────────────────────────────────────────────────────

    private static void MoveSelection(EventSystem es, Selectable current, Vector2 move, Transform ctx)
    {
        Selectable next;
        if (Mathf.Abs(move.x) > Mathf.Abs(move.y))
            next = move.x > 0f ? current.FindSelectableOnRight() : current.FindSelectableOnLeft();
        else
            next = move.y > 0f ? current.FindSelectableOnUp() : current.FindSelectableOnDown();

        // Automatic navigation may point at a button of another canvas (e.g. HUD behind a menu).
        if (next != null && next.IsInteractable() && IsUnder(next.transform, ctx))
            es.SetSelectedGameObject(next.gameObject);
    }

    private static GameObject FirstSelectable(Transform root)
    {
        foreach (Selectable s in root.GetComponentsInChildren<Selectable>(false))
        {
            if (s != null && s.IsInteractable() && s.gameObject.activeInHierarchy &&
                s.navigation.mode != Navigation.Mode.None)
                return s.gameObject;
        }
        return null;
    }

    private static bool IsUnder(Transform child, Transform root)
    {
        for (Transform t = child; t != null; t = t.parent)
            if (t == root) return true;
        return false;
    }

    private void ApplyHighlight(GameObject go)
    {
        if (go == _highlighted)
            return;

        ClearHighlight();

        _highlighted = go;
        if (_highlighted != null)
        {
            _highlightBaseScale = _highlighted.transform.localScale;
            _highlighted.transform.localScale = _highlightBaseScale * HighlightScale;
        }
    }

    private void ClearHighlight()
    {
        if (_highlighted != null)
            _highlighted.transform.localScale = _highlightBaseScale;
        _highlighted = null;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Manual gamepad/keyboard UI navigation for desktop (Steam). The built-in input-module
/// navigation doesn't pick up the gamepad stick in this project, so this reads the devices
/// directly and drives the EventSystem selection itself.
///
/// A <b>context stack</b> makes it robust: each menu registers its root while open
/// (<see cref="PushContext"/>/<see cref="PopContext"/>), which (a) guarantees a button is
/// selected whenever a menu is up — no reliance on panels selecting at the right time — and
/// (b) keeps the navigator idle during gameplay (no context = no stray selection). The
/// selected element is scaled up so the focus is visible regardless of the Button's colors.
/// Self-bootstraps; inert on mobile builds.
/// </summary>
public class MenuNavigator : MonoBehaviour
{
    private const float MoveRepeatDelay = 0.20f;   // seconds between steps while a direction is held
    private const float Deadzone = 0.5f;
    private const float HighlightScale = 1.08f;

    private static MenuNavigator _instance;
    private static readonly List<Transform> _contexts = new List<Transform>();

    private float _nextMoveTime;
    private bool _movePrimed = true;

    private GameObject _highlighted;
    private Vector3 _highlightBaseScale = Vector3.one;

    // ── Context stack ────────────────────────────────────────────────────────────────────
    public static void PushContext(Transform root)
    {
        if (root == null) return;
        _contexts.Remove(root);   // move to top if already present
        _contexts.Add(root);
    }

    public static void PopContext(Transform root)
    {
        _contexts.Remove(root);
    }

    private static Transform CurrentContext()
    {
        for (int i = _contexts.Count - 1; i >= 0; i--)
        {
            if (_contexts[i] == null) { _contexts.RemoveAt(i); continue; }
            if (_contexts[i].gameObject.activeInHierarchy) return _contexts[i];
        }
        return null;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_STANDALONE || UNITY_EDITOR
        if (_instance != null) return;

        var go = new GameObject("MenuNavigator");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<MenuNavigator>();
#endif
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
#if UNITY_STANDALONE || UNITY_EDITOR
        if (_instance != this) return;

        Transform ctx = CurrentContext();
        if (ctx == null)
        {
            // No menu open (gameplay): drop any leftover selection/highlight and stay idle.
            EventSystem cur = EventSystem.current;
            if (cur != null && cur.currentSelectedGameObject != null) cur.SetSelectedGameObject(null);
            ClearHighlight();
            return;
        }

        // A menu is open: ensure there IS an active EventSystem (desktop scenes may not ship
        // one, since DirectControlHud only builds one on mobile). The manual navigator owns
        // navigation, so also silence the module's own nav events.
        EventSystem es = UiNavigation.EnsureEventSystem();
        if (es == null) return;
        es.sendNavigationEvents = false;

        // Guarantee a valid selection inside the current context.
        GameObject selGo = es.currentSelectedGameObject;
        Selectable sel = selGo != null ? selGo.GetComponent<Selectable>() : null;
        bool valid = sel != null && sel.IsInteractable() && selGo.activeInHierarchy && IsUnder(selGo.transform, ctx);
        if (!valid)
        {
            selGo = FirstSelectable(ctx);
            if (selGo != null) es.SetSelectedGameObject(selGo);
            sel = selGo != null ? selGo.GetComponent<Selectable>() : null;
        }

        // Movement (debounced, with re-center priming).
        Vector2 move = ReadMove();
        if (move.magnitude < Deadzone)
        {
            _movePrimed = true;
        }
        else if (_movePrimed || Time.unscaledTime >= _nextMoveTime)
        {
            _movePrimed = false;
            _nextMoveTime = Time.unscaledTime + MoveRepeatDelay;
            if (sel != null) MoveSelection(es, sel, move);
        }

        // Submit / Cancel on the (possibly updated) selection.
        selGo = es.currentSelectedGameObject;
        if (selGo != null)
        {
            if (SubmitPressed())
                ExecuteEvents.Execute(selGo, new BaseEventData(es), ExecuteEvents.submitHandler);
            else if (CancelPressed())
                ExecuteEvents.Execute(selGo, new BaseEventData(es), ExecuteEvents.cancelHandler);
        }

        ApplyHighlight(es.currentSelectedGameObject);
#endif
    }

#if UNITY_STANDALONE || UNITY_EDITOR
    private static void MoveSelection(EventSystem es, Selectable current, Vector2 move)
    {
        Selectable next;
        if (Mathf.Abs(move.x) > Mathf.Abs(move.y))
            next = move.x > 0f ? current.FindSelectableOnRight() : current.FindSelectableOnLeft();
        else
            next = move.y > 0f ? current.FindSelectableOnUp() : current.FindSelectableOnDown();

        if (next != null && next.IsInteractable())
            es.SetSelectedGameObject(next.gameObject);
    }

    private static GameObject FirstSelectable(Transform root)
    {
        var selectables = root.GetComponentsInChildren<Selectable>(false);
        foreach (var s in selectables)
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
        if (go == _highlighted) return;

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

    private static Vector2 ReadMove()
    {
        Vector2 v = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        Gamepad gp = Gamepad.current;
        if (gp != null)
        {
            v += gp.leftStick.ReadValue();
            v += gp.dpad.ReadValue();
        }

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) v.x -= 1f;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) v.x += 1f;
            if (kb.upArrowKey.isPressed || kb.wKey.isPressed) v.y += 1f;
            if (kb.downArrowKey.isPressed || kb.sKey.isPressed) v.y -= 1f;
        }
#else
        v.x = Input.GetAxisRaw("Horizontal");
        v.y = Input.GetAxisRaw("Vertical");
#endif
        return v;
    }

    private static bool SubmitPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Gamepad gp = Gamepad.current;
        if (gp != null && gp.buttonSouth.wasPressedThisFrame) return true;   // A / Cross

        Keyboard kb = Keyboard.current;
        if (kb != null && (kb.enterKey.wasPressedThisFrame ||
                           kb.numpadEnterKey.wasPressedThisFrame ||
                           kb.spaceKey.wasPressedThisFrame)) return true;
#else
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetKeyDown(KeyCode.Space)) return true;
#endif
        return false;
    }

    private static bool CancelPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Gamepad gp = Gamepad.current;
        if (gp != null && gp.buttonEast.wasPressedThisFrame) return true;    // B / Circle
#endif
        return false;
    }
#endif
}

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Gamepad/keyboard UI navigation helpers for desktop (Steam). On mobile the UI is driven by
/// touch and needs no selection; on desktop a menu is unusable without (a) directional input
/// that reads the gamepad stick and (b) a currently-selected button to navigate from.
///
/// The built-in input-module navigation is unreliable here (the legacy StandaloneInputModule
/// doesn't read the gamepad stick), so <see cref="MenuNavigator"/> drives movement + submit
/// manually and we DISABLE the EventSystem's own navigation to avoid double events. Mouse/
/// pointer clicks still go through the module. Call <see cref="Select"/> when a panel opens.
/// </summary>
public static class UiNavigation
{
    public static EventSystem EnsureEventSystem()
    {
        // Prefer the active/current one. FindFirstObjectByType returns active objects only, so a
        // leftover DISABLED EventSystem is ignored and we create a fresh active one instead.
        EventSystem es = EventSystem.current;
        if (es == null) es = Object.FindFirstObjectByType<EventSystem>();

        if (es == null)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es = go.GetComponent<EventSystem>();
        }
        else
        {
            if (!es.enabled) es.enabled = true;
            // Keep a module for mouse/pointer (works under the "Both" input backend).
            if (es.GetComponent<BaseInputModule>() == null)
                es.gameObject.AddComponent<StandaloneInputModule>();
        }

#if UNITY_STANDALONE || UNITY_WSA || UNITY_GAMECORE || UNITY_EDITOR
        // MenuNavigator owns directional + submit input on desktop; silence the module's
        // built-in navigation so moves/submits don't fire twice. Pointer events are unaffected.
        es.sendNavigationEvents = false;
#endif
        return es;
    }

    // Selects a UI object so gamepad/keyboard navigation has a starting point.
    public static void Select(GameObject target)
    {
        if (target == null) return;

        EventSystem es = EnsureEventSystem();
        es.SetSelectedGameObject(null);
        es.SetSelectedGameObject(target);
    }

    // First interactable, active, navigable Selectable under a root (hierarchy order).
    public static GameObject FindFirstSelectable(Component root)
    {
        if (root == null) return null;

        var selectables = root.GetComponentsInChildren<Selectable>(false);
        foreach (var s in selectables)
        {
            if (s != null && s.IsInteractable() && s.gameObject.activeInHierarchy &&
                s.navigation.mode != Navigation.Mode.None)
                return s.gameObject;
        }
        return null;
    }
}

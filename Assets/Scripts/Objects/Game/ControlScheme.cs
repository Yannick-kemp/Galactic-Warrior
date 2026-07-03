using UnityEngine;

/// <summary>
/// Persisted player choice between the two control schemes, selected from the
/// Settings popup. Source of truth for both the tap path (Warrior.HandleInput
/// early-returns when Direct is selected) and the on-screen joystick HUD
/// (DirectControlHud shows itself only when Direct is selected).
///
/// Mirrors the AudioMute static + PlayerPrefs pattern already used in the project.
/// </summary>
public static class ControlScheme
{
    public enum Mode
    {
        Tap = 0,     // original tap-to-navigate (auto A* nav) — default
        Direct = 1,  // CoD-Mobile style joystick + action buttons
    }

    private const string KEY = "control_scheme";

    // Platform default when the player has not made an explicit choice yet: desktop (Steam)
    // and Xbox (GameCore) default to Direct (joystick/keyboard/gamepad held-direction), like
    // a standard PC/console platformer; mobile keeps the tap-to-navigate default. An explicit
    // Settings toggle writes KEY and overrides this on platforms that allow switching.
    private static int DefaultMode =>
#if UNITY_STANDALONE || UNITY_GAMECORE
        (int)Mode.Direct;
#else
        (int)Mode.Tap;
#endif

    // Xbox has no touch/mouse, so the tap-to-navigate scheme is unplayable there. The scheme is
    // HARD-LOCKED to Direct on console: Current ignores any persisted PlayerPrefs and Set/Toggle
    // become no-ops, so a stale mobile/desktop save can never boot Xbox into Tap. This covers
    // both the GDK/GameCore path (UNITY_GAMECORE) and the UWP/Dev-Mode test path (UNITY_WSA).
    public static Mode Current =>
#if UNITY_GAMECORE || UNITY_WSA
        Mode.Direct;
#else
        (Mode)PlayerPrefs.GetInt(KEY, DefaultMode);
#endif

    public static bool IsTap => Current == Mode.Tap;
    public static bool IsDirect => Current == Mode.Direct;

    // True when the platform allows the player to switch schemes (used by SettingsPopupUI to
    // hide the control-scheme toggle on Xbox, where the scheme is locked to Direct).
    public static bool CanChoose =>
#if UNITY_GAMECORE || UNITY_WSA
        false;
#else
        true;
#endif

    public static void Set(Mode mode)
    {
        if (!CanChoose)
            return;

        PlayerPrefs.SetInt(KEY, (int)mode);
        PlayerPrefs.Save();
    }

    public static void Toggle() => Set(IsDirect ? Mode.Tap : Mode.Direct);
}

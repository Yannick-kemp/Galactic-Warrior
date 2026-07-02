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
    // defaults to Direct (joystick/keyboard/gamepad held-direction), like a standard PC
    // platformer; mobile keeps the tap-to-navigate default. An explicit Settings toggle
    // writes KEY and overrides this on any platform.
    private static int DefaultMode =>
#if UNITY_STANDALONE
        (int)Mode.Direct;
#else
        (int)Mode.Tap;
#endif

    public static Mode Current => (Mode)PlayerPrefs.GetInt(KEY, DefaultMode);

    public static bool IsTap => Current == Mode.Tap;
    public static bool IsDirect => Current == Mode.Direct;

    public static void Set(Mode mode)
    {
        PlayerPrefs.SetInt(KEY, (int)mode);
        PlayerPrefs.Save();
    }

    public static void Toggle() => Set(IsDirect ? Mode.Tap : Mode.Direct);
}

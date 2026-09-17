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
        Tap = 0,     // original tap-to-navigate (auto A* nav) — default on non-touch
        Direct = 1,  // CoD-Mobile style joystick + action buttons — default on touch
    }

    private const string KEY = "control_scheme";

    /// <summary>
    /// Fallback used only when the player hasn't chosen yet. Touch devices
    /// (phones/tablets) default to the on-screen joystick; every other platform
    /// keeps the original Tap default. A saved choice always overrides this, so
    /// existing players and the Tap option are unaffected.
    /// </summary>
    private static Mode DefaultMode => Application.isMobilePlatform ? Mode.Direct : Mode.Tap;

    /// <summary>
    /// Desktop browser (WebGL without touch): the joystick needs a held pointer PLUS a
    /// second press for jump/attack, which a lone mouse cannot do — running over a gap
    /// becomes impossible. YouTube Playables requires every interaction to work with the
    /// mouse alone, so Tap is forced there and the choice is hidden from the settings.
    /// Mobile browsers keep the normal choice.
    /// </summary>
    private static bool ForcesTap
#if UNITY_WEBGL && !UNITY_EDITOR
        => !Application.isMobilePlatform;
#else
        => false;
#endif

    /// <summary>False when the platform imposes the scheme; UI hides its toggle.</summary>
    public static bool IsSelectable => !ForcesTap;

    public static Mode Current => ForcesTap ? Mode.Tap : (Mode)PlayerPrefs.GetInt(KEY, (int)DefaultMode);

    public static bool IsTap => Current == Mode.Tap;
    public static bool IsDirect => Current == Mode.Direct;

    public static void Set(Mode mode)
    {
        if (ForcesTap)
            return;

        PlayerPrefs.SetInt(KEY, (int)mode);
        PlayerPrefs.Save();
    }

    public static void Toggle() => Set(IsDirect ? Mode.Tap : Mode.Direct);
}

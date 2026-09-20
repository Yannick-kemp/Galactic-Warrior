using UnityEngine;

/// <summary>
/// Persisted left/right-handed layout choice for the on-screen joystick controller,
/// selected from the Settings popup. Right-handed (default) = joystick on the left,
/// action buttons on the right. Left-handed = mirrored.
///
/// Same static + PlayerPrefs pattern as ControlScheme / AudioMute.
/// </summary>
public static class ControlHandedness
{
    public enum Hand
    {
        Right = 0, // joystick left, buttons right — default on non-touch
        Left = 1,  // mirrored — default on touch
    }

    private const string KEY = "control_handedness";

    /// <summary>
    /// Fallback used only when the player hasn't chosen yet. Touch devices default
    /// to the left-handed joystick layout; every other platform keeps the original
    /// Right default. A saved choice always overrides this. Handedness only affects
    /// the on-screen joystick HUD (Direct scheme), so non-touch is unaffected.
    /// </summary>
    private static Hand DefaultHand => Application.isMobilePlatform ? Hand.Left : Hand.Right;

    public static Hand Current => (Hand)PlayerPrefs.GetInt(KEY, (int)DefaultHand);

    public static bool IsRight => Current == Hand.Right;
    public static bool IsLeft => Current == Hand.Left;

    public static void Set(Hand hand)
    {
        PlayerPrefs.SetInt(KEY, (int)hand);
        PlayerPrefs.Save();
    }

    public static void Toggle() => Set(IsLeft ? Hand.Right : Hand.Left);
}

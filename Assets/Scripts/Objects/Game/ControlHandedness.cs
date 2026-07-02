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
        Right = 0, // joystick left, buttons right — default
        Left = 1,  // mirrored
    }

    private const string KEY = "control_handedness";

    public static Hand Current => (Hand)PlayerPrefs.GetInt(KEY, (int)Hand.Right);

    public static bool IsRight => Current == Hand.Right;
    public static bool IsLeft => Current == Hand.Left;

    public static void Set(Hand hand)
    {
        PlayerPrefs.SetInt(KEY, (int)hand);
        PlayerPrefs.Save();
    }

    public static void Toggle() => Set(IsLeft ? Hand.Right : Hand.Left);
}

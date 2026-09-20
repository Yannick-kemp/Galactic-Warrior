using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Physical gamepad (Xbox, PlayStation, generic "standard" layout) for the web build.
/// Browsers expose controllers through the Gamepad API, which Unity's Input System reads;
/// the mapping below mirrors the DevXbox branch so the pad behaves the same everywhere:
///
///   Left stick / D-pad (menus)  move / navigate      A  jump / confirm
///   X  attack                   Y  fire Ice-Ball     B  back
///   Start  pause                D-pad ↓ ← →  Shield / Sprint / Key
///   (Health and Memory trigger on their own: no button)
///   LB  Power Combo             RB  arm Ice-Ball (then aim with the stick)
///
/// Only active in WebGL: the shipped Android build keeps its touch-only behaviour.
/// A browser reports a controller only after one of its buttons was pressed on the page.
/// </summary>
public static class GamepadSupport
{
    public static bool Enabled
    {
        get
        {
#if UNITY_WEBGL && ENABLE_INPUT_SYSTEM
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>True when a controller is connected and gamepad support is on for this platform.</summary>
    public static bool Connected
    {
        get
        {
#if UNITY_WEBGL && ENABLE_INPUT_SYSTEM
            return Gamepad.current != null;
#else
            return false;
#endif
        }
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>The connected controller, or null (always null when support is off).</summary>
    public static Gamepad Pad => Connected ? Gamepad.current : null;
#endif
}

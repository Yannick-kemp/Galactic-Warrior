#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only helper to reset the persistent boss-relic counter for testing.
/// Menu: Tools ▸ Galactic Warrior ▸ Reset Boss Relics.
///
/// Resets BOTH the Editor and the build installed on the connected device, because testing the
/// boss relic rise on the phone hit the same trap as in the Editor: GrantBossRelicSequence skips
/// silently for a boss already in the defeated set, so the animation simply never plays and it
/// looks like a bug.
///
/// The device half has two paths, because a release build cannot be touched surgically:
///   - development build: `run-as` reaches the app's shared_prefs and only the relic key is
///     dropped, everything else on the device survives.
///   - release build: `run-as` is refused ("package not debuggable"). The only remaining lever
///     without root is `pm clear`, which wipes ALL app data. That is destructive enough to require
///     an explicit confirmation, so it is never done silently.
/// </summary>
public static class ResetBossRelicsMenu
{
    private const string BossRelicsKey = "GW_BossRelicsDefeated";

    [MenuItem("Tools/Galactic Warrior/Reset Boss Relics")]
    public static void ResetBossRelics()
    {
        ResetInEditor();
        ResetOnDevice();
    }

    [MenuItem("Tools/Galactic Warrior/Reset Boss Relics (Editor only)")]
    public static void ResetBossRelicsEditorOnly()
    {
        ResetInEditor();
    }

    // --- Editor ------------------------------------------------------------

    private static void ResetInEditor()
    {
        if (Application.isPlaying && GameMgr.Instance != null)
        {
            // Clears the in-memory set AND the disk key, and refreshes the RelicMemory CountText.
            GameMgr.Instance.ClearBossRelics();
        }
        else
        {
            PlayerPrefs.DeleteKey(BossRelicsKey);
            PlayerPrefs.Save();
        }

        Debug.Log("[ResetBossRelicsMenu] Editor boss relics reset to 0.");
    }

    // --- Device ------------------------------------------------------------

    private static void ResetOnDevice()
    {
        string pkg = AndroidDeployToUser0.PackageName;

        if (string.IsNullOrEmpty(AndroidDeployToUser0.GetAdbPath()))
        {
            Debug.LogWarning("[ResetBossRelicsMenu] adb.exe not found — device left untouched.");
            return;
        }

        string serial = AndroidDeployToUser0.GetSingleDeviceSerial();
        if (string.IsNullOrEmpty(serial))
        {
            Debug.LogWarning("[ResetBossRelicsMenu] No device connected — device left untouched.");
            return;
        }

        // The running app holds PlayerPrefs in memory and rewrites the file when it stops, which
        // would undo the reset. Stop it first, whichever path we then take.
        AndroidDeployToUser0.RunAdb($"-s {serial} shell am force-stop --user 0 {pkg}");

        if (TryRemoveKeyWithRunAs(serial, pkg))
            return;

        OfferFullClear(serial, pkg);
    }

    /// <summary>
    /// Surgical path, development builds only. Returns false when the build is not debuggable, or
    /// when the prefs file cannot be found, so the caller can fall back.
    /// </summary>
    private static bool TryRemoveKeyWithRunAs(string serial, string pkg)
    {
        var (code, stdout, stderr) = AndroidDeployToUser0.RunAdb(
            $"-s {serial} shell run-as {pkg} ls shared_prefs");

        string combined = stdout + stderr;

        if (code != 0 || combined.IndexOf("not debuggable", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Debug.Log("[ResetBossRelicsMenu] Installed build is not debuggable, so its data cannot " +
                      "be edited key by key.");
            return false;
        }

        // Unity names it "<package>.v2.playerprefs.xml", but that has changed across versions, so
        // the file is looked up rather than assumed.
        string prefsFile = combined
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.IndexOf("playerprefs", StringComparison.OrdinalIgnoreCase) >= 0);

        if (string.IsNullOrEmpty(prefsFile))
        {
            Debug.LogWarning($"[ResetBossRelicsMenu] No playerprefs file in shared_prefs of {pkg}. " +
                             "Nothing to reset on device (the app may never have saved yet).");
            return true;   // handled: there is genuinely nothing to remove
        }

        var (delCode, _, delErr) = AndroidDeployToUser0.RunAdb(
            $"-s {serial} shell run-as {pkg} sed -i /{BossRelicsKey}/d shared_prefs/{prefsFile}");

        if (delCode != 0)
        {
            Debug.LogWarning($"[ResetBossRelicsMenu] Could not edit {prefsFile} (exit {delCode}).\n{delErr}");
            return false;
        }

        Debug.Log($"[ResetBossRelicsMenu] Device boss relics reset: dropped {BossRelicsKey} from " +
                  $"{prefsFile} on {serial}. Other saved data untouched.");
        return true;
    }

    /// <summary>
    /// Fallback for release builds. Never silent: wiping every preference also loses the tutorial
    /// flag, the control scheme and the scores, which is a much bigger change than the caller asked
    /// for.
    /// </summary>
    private static void OfferFullClear(string serial, string pkg)
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Reset boss relics on device",
            $"The build installed on {serial} is a release build, so only the boss relic key " +
            "cannot be removed on its own.\n\n" +
            "The only way left is to clear ALL of the app's data on the phone. That also resets " +
            "the tutorial, the settings, the control scheme and the scores.\n\n" +
            "To reset only the relics, deploy a Development Build instead: run-as then reaches the " +
            "app's data and nothing else is lost.\n\n" +
            "Clear everything?",
            "Clear all app data",
            "Leave the device alone");

        if (!confirmed)
        {
            Debug.Log("[ResetBossRelicsMenu] Device left untouched. Install a development build to " +
                      "reset the relic key on its own.");
            return;
        }

        // --user 0 because this project's test phone carries a work profile, so a user-less pm
        // command resolves against user 10. That alone is not always enough, see below.
        var (code, stdout, stderr) = AndroidDeployToUser0.RunAdb(
            $"-s {serial} shell pm clear --user 0 {pkg}");

        string output = stdout + stderr;

        // adb returns 0 even when the shell command inside failed, so the output has to be read.
        bool success = code == 0 &&
                       output.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0;

        if (success)
        {
            Debug.Log($"[ResetBossRelicsMenu] All app data cleared on {serial}.");
            return;
        }

        // Some OEM ROMs, ColorOS on this project's OPPO test device among them, refuse
        // CLEAR_APP_USER_DATA to the adb shell user whatever the target user id. Nothing in this
        // menu can work around that, so say so plainly instead of printing a Java stack trace that
        // reads like a bug on our side.
        bool blockedByRom = output.IndexOf("CLEAR_APP_USER_DATA", StringComparison.OrdinalIgnoreCase) >= 0;

        if (blockedByRom)
        {
            Debug.LogError(
                "[ResetBossRelicsMenu] This device refuses 'pm clear' from adb: the shell user is " +
                "denied CLEAR_APP_USER_DATA. That is an OEM restriction, not a wrong command, and " +
                "no adb flag gets around it.\n" +
                "Use a Development Build instead ('Build APK + Deploy to User 0' with Development " +
                "Build ticked): run-as then reaches the app's data and only the relic key is " +
                "removed, with nothing else lost.\n" +
                "Failing that, clear the app's data by hand from the phone's app settings.");
            return;
        }

        Debug.LogError($"[ResetBossRelicsMenu] 'pm clear --user 0' failed (exit {code}).\n{output}");
    }
}
#endif

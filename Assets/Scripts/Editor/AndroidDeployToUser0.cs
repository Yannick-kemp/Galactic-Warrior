#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// One-click Android deploy that bypasses Unity's "Build And Run" package check.
///
/// On devices with a secondary user space (work profile, Second Space, dual apps),
/// Unity's user-less `pm list packages` resolves against user 10 and fails with
/// java.lang.SecurityException: "Shell does not have permission to access user 10",
/// aborting the deploy even though the app installs fine on the owner (user 0).
///
/// These menu items build the APK and run `adb install -r --user 0` + launch
/// directly, so the work profile is never touched.
/// Menu: Tools ▸ Galactic Warrior ▸ Android ▸ ...
/// </summary>
public static class AndroidDeployToUser0
{
    private const string PackageName = "com.polymart.GalacticWarrior";
    private const string ApkRelativePath = "Builds/Android/Galactic-Warrior.apk";

    [MenuItem("Tools/Galactic Warrior/Android/Build APK + Deploy to User 0")]
    public static void BuildAndDeploy()
    {
        string apkPath = BuildApk();
        if (apkPath == null)
            return; // build failed/cancelled; BuildApk already logged.

        if (InstallToUser0(apkPath))
            LaunchApp();
    }

    [MenuItem("Tools/Galactic Warrior/Android/Deploy Last APK to User 0 (no rebuild)")]
    public static void DeployLastApk()
    {
        string apkPath = Path.GetFullPath(Path.Combine(GetProjectRoot(), ApkRelativePath));
        if (!File.Exists(apkPath))
        {
            Debug.LogError($"[AndroidDeploy] No APK at {apkPath}. Run 'Build APK + Deploy to User 0' first.");
            return;
        }

        if (InstallToUser0(apkPath))
            LaunchApp();
    }

    // --- Build -------------------------------------------------------------

    private static string BuildApk()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[AndroidDeploy] No enabled scenes in Build Settings. Add scenes before building.");
            return null;
        }

        string apkPath = Path.GetFullPath(Path.Combine(GetProjectRoot(), ApkRelativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(apkPath));

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = apkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None, // mirror current Player Settings; no forced dev build.
        };

        Debug.Log($"[AndroidDeploy] Building {scenes.Length} scene(s) -> {apkPath}");
        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[AndroidDeploy] Build {report.summary.result} ({report.summary.totalErrors} errors).");
            return null;
        }

        Debug.Log($"[AndroidDeploy] Build succeeded in {report.summary.totalTime}.");
        return apkPath;
    }

    // --- Install / launch --------------------------------------------------

    private static bool InstallToUser0(string apkPath)
    {
        string serial = GetSingleDeviceSerial();
        if (serial == null)
            return false;

        Debug.Log($"[AndroidDeploy] Installing to user 0 on {serial}...");
        var (code, stdout, stderr) = RunAdb($"-s {serial} install -r --user 0 \"{apkPath}\"");

        // adb prints "Success" to stdout on success; surface failures clearly.
        bool ok = code == 0 && stdout.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!ok)
        {
            Debug.LogError($"[AndroidDeploy] Install failed (exit {code}).\n{stdout}\n{stderr}");
            return false;
        }

        Debug.Log("[AndroidDeploy] Install succeeded.");
        return true;
    }

    private static void LaunchApp()
    {
        string serial = GetSingleDeviceSerial();
        if (serial == null)
            return;

        // monkey launches the LAUNCHER activity without us needing to know the
        // activity class name (UnityPlayerActivity vs UnityPlayerGameActivity).
        var (code, stdout, stderr) = RunAdb(
            $"-s {serial} shell monkey -p {PackageName} --user 0 -c android.intent.category.LAUNCHER 1");

        if (code != 0)
            Debug.LogWarning($"[AndroidDeploy] Launch command returned exit {code}.\n{stdout}\n{stderr}");
        else
            Debug.Log($"[AndroidDeploy] Launched {PackageName} on user 0.");
    }

    // --- ADB plumbing ------------------------------------------------------

    private static string GetSingleDeviceSerial()
    {
        var (code, stdout, stderr) = RunAdb("devices");
        if (code != 0)
        {
            Debug.LogError($"[AndroidDeploy] 'adb devices' failed (exit {code}).\n{stderr}");
            return null;
        }

        // Skip the "List of devices attached" header; keep lines ending in "device".
        var serials = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(l => l.Split('\t'))
            .Where(parts => parts.Length >= 2 && parts[1].Trim() == "device")
            .Select(parts => parts[0].Trim())
            .ToList();

        if (serials.Count == 0)
        {
            Debug.LogError("[AndroidDeploy] No authorized device found. Plug in the phone and accept the USB debugging prompt.");
            return null;
        }

        if (serials.Count > 1)
            Debug.LogWarning($"[AndroidDeploy] Multiple devices ({string.Join(", ", serials)}); using {serials[0]}.");

        return serials[0];
    }

    private static (int code, string stdout, string stderr) RunAdb(string args)
    {
        string adb = GetAdbPath();
        if (adb == null)
            return (-1, "", "adb.exe not found.");

        var psi = new ProcessStartInfo
        {
            FileName = adb,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using (var p = new Process { StartInfo = psi })
        {
            p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return (p.ExitCode, stdout.ToString(), stderr.ToString());
        }
    }

    private static string GetAdbPath()
    {
        var candidates = new List<string>();

        // 1) SDK configured in Editor preferences (External Tools).
        string sdk = EditorPrefs.GetString("AndroidSdkRoot");
        if (!string.IsNullOrEmpty(sdk))
            candidates.Add(Path.Combine(sdk, "platform-tools", "adb.exe"));

        // 2) Embedded SDK shipped with the Android module.
        candidates.Add(Path.Combine(EditorApplication.applicationContentsPath,
            "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe"));

        foreach (string c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        Debug.LogError("[AndroidDeploy] Could not locate adb.exe. Set the Android SDK path in " +
                       "Edit ▸ Preferences ▸ External Tools.");
        return null;
    }

    private static string GetProjectRoot()
    {
        // Application.dataPath is "<project>/Assets".
        return Directory.GetParent(Application.dataPath).FullName;
    }
}
#endif

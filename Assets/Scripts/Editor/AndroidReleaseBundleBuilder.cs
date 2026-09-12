#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Scripted entry point for the Google Play release bundle (.aab).
///
/// Why this exists: the release build could only be triggered from the Editor UI or through the
/// MCP bridge. When the bridge is down (or the Editor must stay closed) there was no way to
/// produce the exact same artifact. This exposes one method that applies the release settings
/// explicitly, so a command-line build and a menu build cannot drift apart:
///
///   Unity.exe -quit -batchmode -nographics -projectPath "&lt;project&gt;" \
///             -executeMethod AndroidReleaseBundleBuilder.BuildReleaseAab \
///             -buildOutput "Builds/GalacticWarrior-Release-v48.aab" -logFile -
///
/// Signing is NOT handled here: AndroidSigningPreprocessor applies the release keystore from the
/// gitignored keystore.properties on every Android build, batch mode included.
///
/// Texture compression is NOT decided here either: AndroidTextureCompressionSettings owns the
/// format list and is applied from this method, so the menu build and the batch build agree.
/// </summary>
public static class AndroidReleaseBundleBuilder
{
    private const string OutputArgument = "-buildOutput";

    [MenuItem("Tools/Galactic Warrior/Android/Build Release AAB")]
    public static void BuildReleaseAabFromMenu()
    {
        BuildReleaseAab();
    }

    public static void BuildReleaseAab()
    {
        string outputPath = ResolveOutputPath();

        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        // Release bundle, never a development build: these are the settings the Play Console
        // expects, applied explicitly so a stale Editor state cannot leak into the artifact.
        EditorUserBuildSettings.buildAppBundle = true;
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.allowDebugging = false;
        EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android);
        }

        // Texture compression is asserted here for the same reason as the flags above: it decides
        // how much memory every sprite costs on device, and a stale Editor state must not decide it.
        // A change here triggers a full texture reimport before the build starts.
        if (AndroidTextureCompressionSettings.Apply())
            Debug.Log("[ReleaseAAB] Texture compression formats changed — reimporting textures first.");

        string[] scenes = GetEnabledScenes();
        if (scenes.Length == 0)
            throw new BuildFailedException("[ReleaseAAB] No enabled scene in Build Settings.");

        Debug.Log(
            $"[ReleaseAAB] Building versionCode={PlayerSettings.Android.bundleVersionCode} " +
            $"version={PlayerSettings.bundleVersion} scenes={scenes.Length} -> {outputPath}");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        string message =
            $"[ReleaseAAB] {summary.result} — {outputPath} " +
            $"({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime.TotalSeconds:F0}s, " +
            $"errors={summary.totalErrors}, warnings={summary.totalWarnings})";

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log(message);

            if (Application.isBatchMode)
                EditorApplication.Exit(0);

            return;
        }

        Debug.LogError(message);

        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }

    /// <summary>
    /// -buildOutput wins; otherwise the artifact is named from the current versionCode, which is
    /// the convention already used in Builds/ (GalacticWarrior-Release-v48.aab).
    /// </summary>
    private static string ResolveOutputPath()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], OutputArgument, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return $"Builds/GalacticWarrior-Release-v{PlayerSettings.Android.bundleVersionCode}.aab";
    }

    private static string[] GetEnabledScenes()
    {
        List<string> scenes = new List<string>();

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene != null && scene.enabled && !string.IsNullOrEmpty(scene.path))
                scenes.Add(scene.path);
        }

        return scenes.ToArray();
    }
}
#endif

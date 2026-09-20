#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Forces release signing for every Android build (Build, Build And Run, and the
/// Tools ▸ Galactic Warrior ▸ Android deploy menu).
///
/// Why this exists: Unity never persists keystore/key passwords to disk, so a
/// fresh Editor session falls back to the Android debug keystore — which the
/// Google Play Console rejects ("signed in debug mode"). This preprocessor reads
/// the gitignored keystore.properties at the project root and applies the keystore
/// path, alias, and passwords just before the build runs.
///
/// keystore.properties is NOT committed. If it is missing the build is aborted
/// with a clear message rather than silently producing a debug-signed artifact.
/// </summary>
public class AndroidSigningPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    private const string PropertiesFileName = "keystore.properties";

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
            return;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string propsPath = Path.Combine(projectRoot, PropertiesFileName);

        if (!File.Exists(propsPath))
        {
            throw new BuildFailedException(
                $"[AndroidSigning] {PropertiesFileName} not found at {propsPath}. " +
                "This file holds the release keystore credentials and is gitignored, " +
                "so it must be recreated on each machine. Without it the build would be " +
                "debug-signed and rejected by Google Play.");
        }

        Dictionary<string, string> props = ParseProperties(propsPath);

        string storeFile = GetRequired(props, "storeFile", propsPath);
        string keyAlias = GetRequired(props, "keyAlias", propsPath);
        string storePassword = GetRequired(props, "storePassword", propsPath);
        string keyPassword = GetRequired(props, "keyPassword", propsPath);

        // Resolve a relative storeFile against the project root.
        string keystorePath = Path.IsPathRooted(storeFile)
            ? storeFile
            : Path.Combine(projectRoot, storeFile);

        if (!File.Exists(keystorePath))
        {
            throw new BuildFailedException(
                $"[AndroidSigning] Keystore not found at {keystorePath} " +
                $"(storeFile='{storeFile}' in {PropertiesFileName}).");
        }

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = keystorePath;
        PlayerSettings.Android.keystorePass = storePassword;
        PlayerSettings.Android.keyaliasName = keyAlias;
        PlayerSettings.Android.keyaliasPass = keyPassword;

        Debug.Log($"[AndroidSigning] Release signing applied: keystore='{Path.GetFileName(keystorePath)}', alias='{keyAlias}'.");
    }

    private static string GetRequired(Dictionary<string, string> props, string key, string propsPath)
    {
        if (!props.TryGetValue(key, out string value) || string.IsNullOrEmpty(value))
        {
            throw new BuildFailedException(
                $"[AndroidSigning] Missing '{key}' in {propsPath}.");
        }
        return value;
    }

    private static Dictionary<string, string> ParseProperties(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            string key = line.Substring(0, eq).Trim();
            // Value is everything after the first '=' (passwords may contain '=' or '?').
            string value = line.Substring(eq + 1).Trim();
            result[key] = value;
        }
        return result;
    }
}
#endif

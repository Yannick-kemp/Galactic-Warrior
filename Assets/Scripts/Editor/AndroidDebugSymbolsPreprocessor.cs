#if UNITY_EDITOR && UNITY_ANDROID
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Unity.Android.Types;

/// <summary>
/// Forces native debug-symbol generation for every Android build so the produced
/// symbols.zip can be uploaded to the Google Play Console (App bundle explorer ▸
/// Downloads ▸ native debug symbols), making native crashes and ANRs readable.
///
/// Why this exists: in Unity 6 the symbol level lives in
/// Android.UserBuildSettings.DebugSymbols.level, which is stored in the per-machine
/// EditorUserBuildSettings (gitignored) — NOT in ProjectSettings.asset. A fresh
/// Editor session therefore defaults to no/partial symbols, and Play warns that
/// the bundle has native code with no debug symbols. This preprocessor pins the
/// level just before the build so it is guaranteed on every machine.
///
/// SymbolTable is enough to symbolicate native call stacks. Use Full if you also
/// want source line numbers (DWARF), at the cost of a much larger symbols.zip.
/// </summary>
public class AndroidDebugSymbolsPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    private const DebugSymbolLevel Level = DebugSymbolLevel.SymbolTable;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
            return;

        UserBuildSettings.DebugSymbols.level = Level;

        Debug.Log($"[AndroidDebugSymbols] Debug symbol level set to {Level}; " +
                  "a symbols.zip will be produced next to the .aab/.apk for Play Console upload.");
    }
}
#endif

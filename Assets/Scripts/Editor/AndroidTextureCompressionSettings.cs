#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Single source of truth for the Android texture compression formats.
///
/// Why this exists: the format used to be ETC, picked because crunch only works with ETC/DXT and
/// the release bundle had to shrink. ETC — like ETC2 — can only block-compress a texture whose
/// width and height are both multiples of 4, and 473 of the 628 character frames are not. Those
/// fall back to an uncompressed format at runtime, which is the single largest memory cost in the
/// game: a gameplay scene references roughly 130 MB of texture even before that fallback.
///
/// ASTC has no multiple-of-4 constraint and stores RGBA at 3.56 bits per pixel in 6x6 blocks,
/// against 8 for ETC2.
///
/// ASTC cannot be crunched, so the first attempt shipped ASTC + ETC2 through texture compression
/// targeting, on the assumption that the crunched ETC2 set would be the cheap one. Unzipping the
/// resulting bundle disproved that: the ASTC variant was 39.6 MB and the ETC2 variant 80.7 MB.
/// Crunch cannot make up the difference because ETC2 leaves the non-multiple-of-4 sprites
/// uncompressed in the first place. ASTC is therefore both the smaller download AND the cheaper
/// format in memory, and the ETC2 half was pure overhead: it doubled the uploaded bundle to serve
/// the minority of devices that lack ASTC.
///
/// So the list is ASTC alone. The bundle returns to roughly its pre-change size while every sprite
/// stays compressed. The accepted cost is that Play will not offer the game to devices without
/// ASTC support, which in practice means low-end Android 6 and 7 hardware.
///
/// Order still matters if a second format is ever added back: the first entry is what a build uses
/// when targeting cannot apply, a plain APK or a local Build And Run, so ASTC must stay first.
/// </summary>
public static class AndroidTextureCompressionSettings
{
    public static readonly TextureCompressionFormat[] ReleaseFormats =
    {
        TextureCompressionFormat.ASTC,
    };

    [MenuItem("Tools/Galactic Warrior/Android/Apply Texture Compression (ASTC)")]
    public static void ApplyFromMenu()
    {
        if (Apply())
        {
            Debug.Log(
                $"[TextureCompression] Applied {Describe(ReleaseFormats)}. " +
                "Unity now reimports every texture — this takes a while and is expected.");
        }
        else
        {
            Debug.Log($"[TextureCompression] Already {Describe(ReleaseFormats)}, nothing to do.");
        }
    }

    [MenuItem("Tools/Galactic Warrior/Android/Log Texture Compression")]
    public static void LogCurrent()
    {
        Debug.Log($"[TextureCompression] Android formats = {Describe(PlayerSettings.Android.textureCompressionFormats)}");
    }

    /// <summary>
    /// Returns true when the setting actually changed, which is also when a texture reimport is
    /// about to run. Callers that build straight after should expect the delay.
    /// </summary>
    public static bool Apply()
    {
        TextureCompressionFormat[] current = PlayerSettings.Android.textureCompressionFormats;

        if (current != null && current.SequenceEqual(ReleaseFormats))
            return false;

        PlayerSettings.Android.textureCompressionFormats = ReleaseFormats;
        AssetDatabase.SaveAssets();
        return true;
    }

    private static string Describe(TextureCompressionFormat[] formats)
    {
        if (formats == null || formats.Length == 0)
            return "(none)";

        return string.Join(" > ", formats.Select(f => f.ToString()));
    }
}
#endif

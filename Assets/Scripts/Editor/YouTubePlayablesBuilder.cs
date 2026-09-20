using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the web demo (WebGL, demo chapter only) in one of two flavours and checks the bundle
/// against YouTube's hard limits (the zipped files also keep the site build host-agnostic):
///   YouTube  define YOUTUBE_PLAYABLES, template YouTubePlayables (YouTube SDK loaded first)
///   Site     define POLYMART_SITE, template PolymartSite (polymart.be, Google Play button at the end)
///
/// Menu: Galactic Warrior > YouTube Playables.
/// CLI : -executeMethod YouTubePlayablesBuilder.BuildFromCommandLine [-ytOutput path] [-webFlavor Site]
///
/// The active platform must already be WebGL: switching reimports the whole project, which
/// is left to the developer on purpose (disk space and time).
/// </summary>
public static class YouTubePlayablesBuilder
{
    public enum Flavor { YouTube, Site }

    public const string Define = "YOUTUBE_PLAYABLES";
    public const string SiteDefine = "POLYMART_SITE";

    private static string DefineOf(Flavor flavor) => flavor == Flavor.Site ? SiteDefine : Define;
    private static string TemplateOf(Flavor flavor) =>
        flavor == Flavor.Site ? "PROJECT:PolymartSite" : "PROJECT:YouTubePlayables";

    // Only the demo ships; AgeOfIce, LandOfFire and Redemption stay out of the bundle.
    private static readonly string[] Scenes =
    {
        "Assets/Scenes/menu.unity",
        "Assets/Scenes/WarriorScene.unity",
    };

    // YouTube Playables limits (stability and performance requirements).
    private const long MaxFileBytes = 30L * 1024 * 1024;
    private const long MaxTotalBytes = 250L * 1024 * 1024;
    private const int MaxFileCount = 8000;
    private static readonly Regex AllowedFileName = new Regex(@"^[A-Za-z0-9_.\-]+$");

    private const string MenuRoot = "Galactic Warrior/YouTube Playables/";

    [MenuItem(MenuRoot + "Apply WebGL settings (YouTube)")]
    public static void ApplyWebGLSettingsMenu() => ApplyWebGLSettings(Flavor.YouTube);

    [MenuItem(MenuRoot + "Apply WebGL settings (polymart.be site)")]
    public static void ApplySiteWebGLSettingsMenu() => ApplyWebGLSettings(Flavor.Site);

    /// <summary>
    /// Returns true when the WebGL defines changed (scripts must recompile before building).
    /// The two flavours are exclusive: selecting one removes the other's define.
    /// </summary>
    public static bool ApplyWebGLSettings(Flavor flavor = Flavor.YouTube)
    {
        Flavor other = flavor == Flavor.Site ? Flavor.YouTube : Flavor.Site;
        bool definesChanged = RemoveDefine(NamedBuildTarget.WebGL, DefineOf(other));
        definesChanged |= AddDefine(NamedBuildTarget.WebGL, DefineOf(flavor));

        PlayerSettings.WebGL.template = TemplateOf(flavor);
        // The Unity wrapper and YouTube's hosting do not support Unity's gzip/brotli output;
        // YouTube compresses over HTTP itself.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.nameFilesAsHashes = false;
        // Browser cache of the bundle is YouTube's business, not the game's IndexedDB.
        PlayerSettings.WebGL.dataCaching = false;
        PlayerSettings.WebGL.showDiagnostics = false;
        // Small .wasm (the default, BuildTimes, produced 58.7 MiB for a 30 MiB per-file limit).
        // Not DiskSizeLTO: its link step ran over an hour on this 16 GB machine and the Editor crashed.
        SetWebGLCodeOptimization("DiskSize");
        // Not Il2CppCodeGeneration.OptimizeSize: with it the wasm link step alone ran 1 h 34 on this
        // machine and the build never finished.
        PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSpeed);
        // YouTube: only its onPause may pause the game (Page Visibility API is forbidden).
        // polymart.be: a hidden tab pauses the game, like any web game (saves battery).
        PlayerSettings.runInBackground = flavor == Flavor.YouTube;

        AssetDatabase.SaveAssets();
        Debug.Log("[YouTubePlayablesBuilder] WebGL settings applied for " + flavor + " (define " + DefineOf(flavor) +
                  ", template " + TemplateOf(flavor) + ", no compression).");
        return definesChanged;
    }

    [MenuItem(MenuRoot + "Build (YouTube)")]
    public static void BuildFromMenu() => BuildFromMenu(Flavor.YouTube);

    [MenuItem(MenuRoot + "Build (polymart.be site)")]
    public static void BuildSiteFromMenu() => BuildFromMenu(Flavor.Site);

    private static void BuildFromMenu(Flavor flavor)
    {
        string output = EditorUtility.SaveFolderPanel(flavor + " web demo build folder", DefaultOutput(flavor), "");
        if (string.IsNullOrEmpty(output))
            return;

        Build(output, flavor);
    }

    public static void BuildFromCommandLine()
    {
        Flavor flavor = Flavor.YouTube;
        string output = null;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-ytOutput")
                output = args[i + 1];
            if (args[i] == "-webFlavor")
                flavor = (Flavor)Enum.Parse(typeof(Flavor), args[i + 1], true);
        }

        bool ok = Build(output ?? DefaultOutput(flavor), flavor);
        if (Application.isBatchMode)
            EditorApplication.Exit(ok ? 0 : 1);
    }

    public static bool Build(string outputFolder, Flavor flavor = Flavor.YouTube)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            Debug.LogError("[YouTubePlayablesBuilder] Active platform is " + EditorUserBuildSettings.activeBuildTarget +
                           ". Switch to WebGL first (File > Build Profiles), it reimports the whole project.");
            return false;
        }

        // A define change forces a script recompile, which aborts a build started in the same call.
        if (ApplyWebGLSettings(flavor) || EditorApplication.isCompiling)
        {
            Debug.LogWarning("[YouTubePlayablesBuilder] WebGL defines switched to " + DefineOf(flavor) + ": scripts are " +
                             "recompiling. Run the build again once compilation is over.");
            return false;
        }

        // ASTC has no multiple-of-4 rule: with DXT most character frames stayed uncompressed RGBA.
        // Changing it reimports every texture, so it is done here once and the build stops.
        if (EnsureWebGLTexturesAstc())
        {
            Debug.LogWarning("[YouTubePlayablesBuilder] WebGL textures switched to ASTC: textures are " +
                             "reimporting. Run the build again once the import is over.");
            return false;
        }

        if (Directory.Exists(outputFolder))
        {
            Directory.Delete(outputFolder, true);

            // Windows deletes lazily while something still holds a handle (e.g. a local web server
            // serving the previous build): the copy step then failed after 20 min of compiling.
            for (int i = 0; i < 50 && Directory.Exists(outputFolder); i++)
                System.Threading.Thread.Sleep(100);

            if (Directory.Exists(outputFolder))
            {
                Debug.LogError("[YouTubePlayablesBuilder] Could not delete " + outputFolder +
                               ". Close whatever uses it (local web server, explorer) and build again.");
                return false;
            }
        }
        Directory.CreateDirectory(outputFolder);

        var options = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = outputFolder,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        };

        // The Unity splash screen is NOT turned off here: it is a project-wide setting shared with
        // Android, and an Editor crash mid-build once left it disabled for every platform.
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("[YouTubePlayablesBuilder] Build failed: " + report.summary.result);
            return false;
        }

        if (!ZipOversizedBuildFiles(outputFolder))
            return false;

        bool withinLimits = CheckBundle(outputFolder, out string summary);
        File.WriteAllText(Path.Combine(outputFolder, "..", Path.GetFileName(outputFolder) + "-report.txt"), summary);

        if (withinLimits && flavor == Flavor.Site)
        {
            Debug.Log("[YouTubePlayablesBuilder] polymart.be build OK: copy " + outputFolder +
                      " to www/games/galactic-warrior/play/.\n" + summary);
        }
        else if (withinLimits)
        {
            string zip = outputFolder.TrimEnd('/', '\\') + ".zip";
            ZipFolder(outputFolder, zip);
            Debug.Log("[YouTubePlayablesBuilder] Build OK, within YouTube limits. Upload zip: " + zip + "\n" + summary);
        }
        else
        {
            Debug.LogError("[YouTubePlayablesBuilder] Build done but OUTSIDE YouTube limits:\n" + summary);
        }

        return withinLimits;
    }

    /// <summary>
    /// YouTube refuses any file of 30 MiB or more. Even optimised, the .wasm (55.8 MiB) and the
    /// .data (41.2 MiB) exceed it, but both compress well (15.0 and 29.6 MiB). Each oversized file
    /// under Build/ is replaced by "name.zip" and listed in index.html (ZIPPED_FILES), whose loader
    /// inflates it in the browser. The certification FAQ allows zip for exactly this case.
    /// </summary>
    public static bool ZipOversizedBuildFiles(string outputFolder)
    {
        string buildDir = Path.Combine(outputFolder, "Build");
        string indexPath = Path.Combine(outputFolder, "index.html");
        var zipped = new List<string>();

        foreach (string file in Directory.GetFiles(buildDir))
        {
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length < MaxFileBytes)
                continue;

            string name = Path.GetFileName(file);
            string zipPath = file + ".zip";
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using (FileStream stream = File.Create(zipPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
                using (Stream entryStream = entry.Open())
                using (FileStream source = File.OpenRead(file))
                    source.CopyTo(entryStream);
            }

            File.Delete(file);
            zipped.Add(name);
        }

        if (zipped.Count == 0)
            return true;

        const string marker = "var ZIPPED_FILES = [];";
        string html = File.ReadAllText(indexPath);
        if (!html.Contains(marker))
        {
            Debug.LogError("[YouTubePlayablesBuilder] index.html has no '" + marker + "': the zipped files " +
                           string.Join(", ", zipped) + " could not be loaded. Is the YouTubePlayables template selected?");
            return false;
        }

        string list = "[" + string.Join(", ", zipped.Select(n => "'" + n + "'")) + "]";
        File.WriteAllText(indexPath, html.Replace(marker, "var ZIPPED_FILES = " + list + ";"));
        Debug.Log("[YouTubePlayablesBuilder] Zipped to fit the 30 MiB file limit: " + string.Join(", ", zipped));
        return true;
    }

    /// <summary>Checks file sizes, count and names against YouTube's hard limits.</summary>
    public static bool CheckBundle(string folder, out string summary)
    {
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.Length)
            .ToList();

        long total = files.Sum(f => f.Length);
        var problems = new List<string>();
        var sb = new StringBuilder();

        sb.AppendLine("YouTube Playables bundle: " + folder);
        sb.AppendLine($"Files: {files.Count} (max {MaxFileCount})");
        sb.AppendLine($"Total: {Mb(total)} (max {Mb(MaxTotalBytes)})");
        sb.AppendLine("Largest files:");

        foreach (FileInfo f in files.Take(10))
            sb.AppendLine($"  {Mb(f.Length),10}  {Relative(folder, f.FullName)}");

        foreach (FileInfo f in files)
        {
            if (f.Length >= MaxFileBytes)
                problems.Add($"{Relative(folder, f.FullName)} is {Mb(f.Length)} (each file must be under 30 MiB)");
            if (!AllowedFileName.IsMatch(f.Name))
                problems.Add($"{Relative(folder, f.FullName)}: file name must use only letters, digits, '_', '-', '.'");
        }

        if (files.Count > MaxFileCount)
            problems.Add($"{files.Count} files (max {MaxFileCount})");
        if (total >= MaxTotalBytes)
            problems.Add($"total {Mb(total)} (max 250 MiB)");

        sb.AppendLine(problems.Count == 0 ? "Hard limits: OK" : "Hard limits: FAILED");
        foreach (string p in problems)
            sb.AppendLine("  - " + p);

        sb.AppendLine("Not measured here: initial bundle (<30 MiB MUST, <15 MiB SHOULD), load time, memory (<512 MB).");

        summary = sb.ToString();
        return problems.Count == 0;
    }

    private static void ZipFolder(string folder, string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using (FileStream stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                string entryName = Relative(folder, file).Replace('\\', '/');
                ZipArchiveEntry entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
                using (Stream entryStream = entry.Open())
                using (FileStream source = File.OpenRead(file))
                    source.CopyTo(entryStream);
            }
        }
    }

    /// <summary>
    /// Makes ASTC the WebGL default texture format. Returns true when it had to change (reimport).
    ///
    /// EditorUserBuildSettings.webGLBuildSubtarget does NOT stick on Unity 6: it fell back to
    /// Generic and the build kept DXT (700x450 frames stayed RGBA32). "Generic" means the per-platform
    /// default stored in ProjectSettings (m_BuildTargetDefaultTextureCompressionFormat), the same
    /// list Android uses; for WebGL Unity only exposes it through an internal method.
    /// </summary>
    private static bool EnsureWebGLTexturesAstc()
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic;
        var get = typeof(PlayerSettings).GetMethod("GetTextureCompressionFormatsImpl", flags);
        var set = typeof(PlayerSettings).GetMethod("SetTextureCompressionFormatsImpl", flags);
        if (get == null || set == null)
        {
            Debug.LogWarning("[YouTubePlayablesBuilder] WebGL texture format API not found; textures keep their format.");
            return false;
        }

        var current = (TextureCompressionFormat[])get.Invoke(null, new object[] { BuildTarget.WebGL });
        bool changed = false;

        if (current == null || current.Length != 1 || current[0] != TextureCompressionFormat.ASTC)
        {
            set.Invoke(null, new object[] { BuildTarget.WebGL, new[] { TextureCompressionFormat.ASTC } });
            changed = true;
        }

        if (EditorUserBuildSettings.webGLBuildSubtarget != WebGLTextureSubtarget.Generic)
        {
            EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.Generic;
            changed = true;
        }

        if (changed)
            AssetDatabase.SaveAssets();
        return changed;
    }

    // UnityEditor.WebGL.UserBuildSettings only exists with the WebGL module installed: reached by
    // reflection so this file still compiles on a machine without it.
    private static void SetWebGLCodeOptimization(string value)
    {
        Type settings = Type.GetType("UnityEditor.WebGL.UserBuildSettings, UnityEditor.WebGL.Extensions");
        var property = settings?.GetProperty("codeOptimization");
        if (property == null)
        {
            Debug.LogWarning("[YouTubePlayablesBuilder] WebGL code optimization setting not found; .wasm keeps its size.");
            return;
        }

        property.SetValue(null, Enum.Parse(property.PropertyType, value));
    }

    // BuildWeb/ at the project root (git-ignored), where the web builds are kept.
    private static string DefaultOutput(Flavor flavor = Flavor.YouTube)
    {
        return Path.GetFullPath(flavor == Flavor.Site ? "BuildWeb/GalacticWarrior-Site" : "BuildWeb/GalacticWarrior-YouTube");
    }

    // ── Define helpers (also used to test the YouTube flow in the Editor) ─────

    [MenuItem(MenuRoot + "Editor test mode/Enable on current platform")]
    public static void EnableEditorTestMode()
    {
        AddDefine(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), Define);
    }

    [MenuItem(MenuRoot + "Editor test mode/Disable on current platform")]
    public static void DisableEditorTestMode()
    {
        var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        RemoveDefine(target, Define);
        RemoveDefine(target, SiteDefine);
    }

    private static bool AddDefine(NamedBuildTarget target, string define)
    {
        var defines = PlayerSettings.GetScriptingDefineSymbols(target)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (defines.Contains(define))
            return false;
        defines.Add(define);
        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        return true;
    }

    private static bool RemoveDefine(NamedBuildTarget target, string define)
    {
        var defines = PlayerSettings.GetScriptingDefineSymbols(target)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!defines.Remove(define))
            return false;
        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        return true;
    }

    private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.00") + " MiB";

    private static string Relative(string root, string path) =>
        path.Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length).TrimStart('\\', '/');
}

/// <summary>
/// Refuses any non-WebGL build while a web demo define is on for that platform: a store build
/// with YOUTUBE_PLAYABLES or POLYMART_SITE would give the whole campaign away and drop the purchase.
/// </summary>
public class YouTubeDefineBuildGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.WebGL)
            return;

        NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);
        string[] defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';');
        foreach (string demoDefine in new[] { YouTubePlayablesBuilder.Define, YouTubePlayablesBuilder.SiteDefine })
        {
            if (defines.Contains(demoDefine))
                throw new BuildFailedException(
                    demoDefine + " is enabled for " + report.summary.platform +
                    ". Use Galactic Warrior > YouTube Playables > Editor test mode > Disable before building.");
        }
    }
}

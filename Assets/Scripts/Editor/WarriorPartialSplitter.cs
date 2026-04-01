#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class WarriorPartialSplitter
{
    [MenuItem("Tools/Warrior/Split Selected Warrior Into Partials")]
    public static void SplitSelected()
    {
        var script = Selection.activeObject as MonoScript;
        if (script == null)
        {
            EditorUtility.DisplayDialog("Warrior Splitter", "Select Warrior.cs in the Project window first.", "OK");
            return;
        }

        string path = AssetDatabase.GetAssetPath(script);
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("Warrior Splitter", "Selection is not a .cs file.", "OK");
            return;
        }

        string fileName = Path.GetFileNameWithoutExtension(path);
        if (!string.Equals(fileName, "Warrior", StringComparison.OrdinalIgnoreCase))
        {
            bool cont = EditorUtility.DisplayDialog(
                "Warrior Splitter",
                $"Selected file is '{fileName}.cs'.\n\nThis tool is intended for Warrior.cs.\nContinue anyway?",
                "Continue", "Cancel");
            if (!cont) return;
        }

        string dir = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? "Assets";
        string text = File.ReadAllText(path);

        // Basic sanity: must contain class Warrior
        if (!text.Contains("class Warrior"))
        {
            EditorUtility.DisplayDialog("Warrior Splitter", "Could not find 'class Warrior' in the selected file.", "OK");
            return;
        }

        // Extract header (using + namespace line + opening braces are kept in core)
        var header = ExtractUsings(text);
        var ns = ExtractNamespace(text);
        if (string.IsNullOrEmpty(ns))
        {
            EditorUtility.DisplayDialog("Warrior Splitter", "Could not detect namespace. Make sure the file contains a 'namespace ...' line.", "OK");
            return;
        }

        // Regions to split (key = region title, value = output filename)
        // IMPORTANT: titles must match your #region lines (case-sensitive-ish on match we do)
        var map = new Dictionary<string, string>
        {
            { "Input / Movement / Idle", "Warrior.Input.cs" },
            { "Attack / FX / Damage", "Warrior.Combat.cs" },
            { "UI Attack Entrypoints / Relic Attack2", "Warrior.Combat.cs" },
            { "Hit Reaction", "Warrior.Combat.cs" },

            { "Collision / Bounce / Contact Blocking", "Warrior.Collision.cs" },

            { "Relic Effects", "Warrior.Sprint.cs" }, // your sprint methods are under this region
            { "Shield Logic", "Warrior.Shield.cs" },

            { "Health / Misc", "Warrior.Death.cs" },
            { "warrior state query (for external callers, e.g. relics)", "Warrior.Death.cs" },
            { "restart game", "Warrior.Death.cs" },

            { "sound", "Warrior.Audio.cs" },
            { "Gizmos", "Warrior.Gizmos.cs" },

            { "spectacular action scoring", "Warrior.Combat.cs" },
        };

        // Split into lines so we can remove blocks cleanly
        var lines = SplitLines(text);

        // Find all region blocks we can extract
        var foundBlocks = new List<RegionBlock>();
        foreach (var kv in map)
        {
            var regionName = kv.Key;
            var outFile = kv.Value;

            if (TryFindRegionBlock(lines, regionName, out var block))
            {
                block.OutputFile = outFile;
                block.RegionName = regionName;
                foundBlocks.Add(block);
            }
            else
            {
                Debug.LogWarning($"[WarriorSplitter] Region not found: #region {regionName}");
            }
        }

        if (foundBlocks.Count == 0)
        {
            EditorUtility.DisplayDialog("Warrior Splitter", "No matching regions were found. Check the region names in the splitter map.", "OK");
            return;
        }

        // Remove extracted blocks from core (remove from bottom to top)
        foreach (var b in foundBlocks.OrderByDescending(b => b.StartLine))
            lines.RemoveRange(b.StartLine, b.EndLine - b.StartLine + 1);

        string coreText = JoinLines(lines);

        // Ensure class Warrior is partial in core
        coreText = MakeWarriorClassPartial(coreText);

        // Build file bodies for each partial file
        var grouped = foundBlocks
            .GroupBy(b => b.OutputFile)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.StartLine).ToList());

        // Backup original so it doesn't compile (avoid duplicate Warrior class)
        string backupPath = path + ".bak";
        if (!File.Exists(backupPath))
        {
            File.Copy(path, backupPath);
        }
        else
        {
            // keep latest backup
            File.Copy(path, backupPath, overwrite: true);
        }

        // Write core back to Warrior.cs
        File.WriteAllText(path, coreText, Encoding.UTF8);

        // Write partials
        foreach (var kv in grouped)
        {
            string outPath = (dir + "/" + kv.Key).Replace("\\", "/");
            string body = BuildPartialFile(header, ns, kv.Value);

            // Ensure file exists/overwrite
            File.WriteAllText(outPath, body, Encoding.UTF8);
        }

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Warrior Splitter",
            "Done!\n\n- Original backed up as Warrior.cs.bak\n- Warrior.cs updated to partial + core only\n- Partial files generated next to it",
            "OK"
        );
    }

    // ----------------------------
    // Helpers
    // ----------------------------

    private static string ExtractUsings(string text)
    {
        var lines = SplitLines(text);
        var sb = new StringBuilder();
        foreach (var l in lines)
        {
            var t = l.TrimStart();
            if (t.StartsWith("using "))
                sb.AppendLine(l);
            else if (t.StartsWith("namespace "))
                break;
        }
        return sb.ToString().TrimEnd();
    }

    private static string ExtractNamespace(string text)
    {
        var lines = SplitLines(text);
        foreach (var l in lines)
        {
            var t = l.TrimStart();
            if (t.StartsWith("namespace "))
            {
                // namespace X.Y.Z
                var ns = t.Substring("namespace ".Length).Trim();
                // strip trailing { if on same line
                int brace = ns.IndexOf("{", StringComparison.Ordinal);
                if (brace >= 0) ns = ns.Substring(0, brace).Trim();
                return ns;
            }
        }
        return null;
    }

    private static List<string> SplitLines(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();
    }

    private static string JoinLines(List<string> lines)
    {
        return string.Join("\n", lines);
    }

    private static string MakeWarriorClassPartial(string core)
    {
        // Replace first occurrence only
        // public class Warrior : ...  -> public partial class Warrior : ...
        int idx = core.IndexOf("public class Warrior", StringComparison.Ordinal);
        if (idx >= 0)
            return core.Replace("public class Warrior", "public partial class Warrior");
        // If it's not public, also try "class Warrior"
        idx = core.IndexOf("class Warrior", StringComparison.Ordinal);
        if (idx >= 0 && !core.Contains("partial class Warrior"))
            return core.Replace("class Warrior", "partial class Warrior");
        return core;
    }

    private static bool TryFindRegionBlock(List<string> lines, string regionName, out RegionBlock block)
    {
        block = default;

        // Find start
        int start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("#region ", StringComparison.Ordinal) && t.Contains(regionName))
            {
                start = i;
                break;
            }
        }

        if (start < 0) return false;

        // Find matching end with nesting
        int depth = 0;
        int end = -1;

        for (int i = start; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("#region ", StringComparison.Ordinal)) depth++;
            else if (t.StartsWith("#endregion", StringComparison.Ordinal))
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    break;
                }
            }
        }

        if (end < 0) return false;

        block = new RegionBlock
        {
            StartLine = start,
            EndLine = end,
            Lines = lines.GetRange(start, end - start + 1),
            RegionName = regionName
        };
        return true;
    }

    private static string BuildPartialFile(string usings, string ns, List<RegionBlock> blocks)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(usings))
        {
            sb.AppendLine(usings.TrimEnd());
            sb.AppendLine();
        }

        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        sb.AppendLine("    public partial class Warrior : CharacterController");
        sb.AppendLine("    {");

        foreach (var b in blocks)
        {
            sb.AppendLine();
            // indent region block by 8 spaces (class indentation)
            foreach (var line in b.Lines)
                sb.AppendLine("        " + line);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private struct RegionBlock
    {
        public int StartLine;
        public int EndLine;
        public List<string> Lines;
        public string RegionName;
        public string OutputFile;
    }
}
#endif
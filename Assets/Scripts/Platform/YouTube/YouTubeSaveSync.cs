using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// YouTube Playables must save progress through ytgame.game.saveData, not only the browser's
/// storage. The game keeps using PlayerPrefs everywhere; this class mirrors the known keys to
/// one JSON string for YouTube, and writes them back into PlayerPrefs at startup.
///
/// Add a key here whenever new progress is stored in PlayerPrefs, or it will not follow the
/// player between devices on YouTube.
/// </summary>
public static class YouTubeSaveSync
{
    private enum Kind { Int, Float, String }

    private static readonly (string key, Kind kind)[] Keys =
    {
        // Progression (GameMgr)
        ("GW_HighestReachedSceneIndex", Kind.Int),
        ("GW_ContinueSceneIndex", Kind.Int),
        ("GW_TutorialCompleted", Kind.Int),
        ("GW_GameCompleted", Kind.Int),
        ("GW_BossRelicsDefeated", Kind.String),

        // Checkpoint (GameMgr)
        ("GW_HasCheckpoint", Kind.Int),
        ("GW_CheckpointScene", Kind.String),
        ("GW_CheckpointX", Kind.Float),
        ("GW_CheckpointY", Kind.Float),
        ("GW_CheckpointZ", Kind.Float),

        // Settings
        ("audio_muted", Kind.Int),
        ("control_scheme", Kind.Int),
        ("control_handedness", Kind.Int),
    };

    // Bump when the format changes; Apply must keep reading every older version.
    private const int FormatVersion = 1;

    [Serializable]
    private class Entry
    {
        public string k;
        public string t;
        public string v;
    }

    [Serializable]
    private class SaveFile
    {
        public int version;
        public List<Entry> entries = new List<Entry>();
    }

    /// <summary>Current PlayerPrefs values of the synced keys, as the string sent to YouTube.</summary>
    public static string Snapshot()
    {
        var file = new SaveFile { version = FormatVersion };

        foreach (var (key, kind) in Keys)
        {
            if (!PlayerPrefs.HasKey(key))
                continue;

            string value;
            switch (kind)
            {
                case Kind.Int: value = PlayerPrefs.GetInt(key).ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case Kind.Float: value = PlayerPrefs.GetFloat(key).ToString("R", System.Globalization.CultureInfo.InvariantCulture); break;
                default: value = PlayerPrefs.GetString(key); break;
            }

            file.entries.Add(new Entry { k = key, t = kind.ToString(), v = value });
        }

        return JsonUtility.ToJson(file);
    }

    /// <summary>
    /// Replaces the synced PlayerPrefs with the YouTube save. The YouTube save is the source of
    /// truth: an empty one (new player) clears whatever this browser had stored before.
    /// Returns false when the string could not be read, in which case nothing is touched.
    /// </summary>
    public static bool Apply(string data)
    {
        SaveFile file = null;

        if (!string.IsNullOrEmpty(data))
        {
            try
            {
                file = JsonUtility.FromJson<SaveFile>(data);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YouTubeSaveSync] Unreadable save, keeping local data: " + e.Message);
                return false;
            }

            if (file == null)
                return false;
        }

        var values = new Dictionary<string, Entry>();
        if (file != null && file.entries != null)
        {
            foreach (Entry e in file.entries)
            {
                if (e != null && !string.IsNullOrEmpty(e.k))
                    values[e.k] = e;
            }
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;

        foreach (var (key, kind) in Keys)
        {
            if (!values.TryGetValue(key, out Entry entry))
            {
                PlayerPrefs.DeleteKey(key);
                continue;
            }

            switch (kind)
            {
                case Kind.Int:
                    if (int.TryParse(entry.v, System.Globalization.NumberStyles.Integer, inv, out int i))
                        PlayerPrefs.SetInt(key, i);
                    break;
                case Kind.Float:
                    if (float.TryParse(entry.v, System.Globalization.NumberStyles.Float, inv, out float f))
                        PlayerPrefs.SetFloat(key, f);
                    break;
                default:
                    PlayerPrefs.SetString(key, entry.v ?? string.Empty);
                    break;
            }
        }

        PlayerPrefs.Save();
        return true;
    }
}

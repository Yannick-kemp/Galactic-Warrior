using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// The free web demo, in its two flavours (scripting defines set by YouTubePlayablesBuilder):
///   YOUTUBE_PLAYABLES  YouTube Playables: YouTube SDK, no link to anything outside YouTube.
///   POLYMART_SITE      polymart.be: no YouTube SDK, the demo end offers the full game on Google Play.
/// Both ship the demo chapter only and sell nothing in the game.
/// </summary>
public static class WebDemo
{
    /// <summary>Chapters playable in the web demo: the Demo (WarriorScene) only.</summary>
    public const int DemoChapterCount = 1;

    public const string PlayStoreUrl = "https://play.google.com/store/apps/details?id=com.polymart.GalacticWarrior";

    /// <summary>True in either web demo build (demo chapter only, no purchase).</summary>
    public static bool IsBuild
    {
        get
        {
#if YOUTUBE_PLAYABLES || POLYMART_SITE
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>True in the polymart.be build, where linking to the Play Store is allowed.</summary>
    public static bool IsSite
    {
        get
        {
#if POLYMART_SITE
            return true;
#else
            return false;
#endif
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void GW_OpenUrl(string url);
    [DllImport("__Internal")] private static extern void GW_ReportGameCompleted();
#endif

    /// <summary>
    /// Tells the polymart.be page that a demo run was finished, so it can count it. Does nothing
    /// in the YouTube build, which must not talk to anything outside YouTube.
    /// </summary>
    public static void ReportGameCompleted()
    {
        if (!IsSite)
            return;

#if UNITY_WEBGL && !UNITY_EDITOR
        GW_ReportGameCompleted();
#endif
    }

    /// <summary>
    /// Opens the full game's Play Store page. A browser only allows a new tab during a click,
    /// and Unity handles the click a frame later (and a gamepad press is no click at all), so
    /// the page falls back to opening it in the same tab when the new tab is refused.
    /// </summary>
    public static void OpenFullGame()
    {
        if (!IsSite)
            return;

#if UNITY_WEBGL && !UNITY_EDITOR
        GW_OpenUrl(PlayStoreUrl);
#else
        Application.OpenURL(PlayStoreUrl);
#endif
    }
}

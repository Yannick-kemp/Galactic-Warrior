using UnityEngine;

public static class AudioMute
{
    private const string KEY = "audio_muted";

    public static bool IsMuted => PlayerPrefs.GetInt(KEY, 0) == 1;

    public static void Apply()
    {
        // YouTube's mute button overrides the in-game setting (always "enabled" elsewhere).
        bool silent = IsMuted || !YouTubePlayables.AudioEnabled;
        AudioListener.volume = silent ? 0f : 1f;
        // If you prefer to fully pause audio processing:
        // AudioListener.pause = IsMuted;
    }

    public static void SetMuted(bool muted)
    {
        PlayerPrefs.SetInt(KEY, muted ? 1 : 0);
        PlayerPrefs.Save();
        Apply();
    }

    public static void Toggle() => SetMuted(!IsMuted);
}
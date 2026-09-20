using UnityEngine;

namespace Assets.Scripts.Objects.BossFinisherFx
{
    /// <summary>
    /// The two boss battle tracks and their trigger tuning. Loaded from Resources so nothing has to
    /// be wired in any of the four gameplay scenes.
    /// </summary>
    [CreateAssetMenu(menuName = "Galactic Warrior/Boss Battle Music", fileName = "SO_BossBattleMusic")]
    public class BossBattleMusicSettings : ScriptableObject
    {
        public const string ResourcePath = "SO_BossBattleMusic";

        [Tooltip("Turn the whole feature off without unwiring anything.")]
        public bool enabled = true;

        [Header("Tracks")]
        [Tooltip("Plays when the Warrior steps onto the platform a boss is standing on.")]
        public AudioClip generalBossTrack;

        [Tooltip("Zort is the exception: his track starts as soon as he is on screen, not when the " +
                 "Warrior reaches him.")]
        public AudioClip zortTrack;

        [Header("Triggers")]
        [Tooltip("Seconds between checks. The trigger is not frame-critical and this runs for every " +
                 "active boss, so there is no reason to test it every frame.")]
        [Min(0.02f)] public float checkInterval = 0.1f;

        [Tooltip("Margin in world units added around the screen before Zort counts as visible. " +
                 "Negative values make him come further in before his track starts.")]
        public float zortVisibilityMargin = -0.5f;
    }
}

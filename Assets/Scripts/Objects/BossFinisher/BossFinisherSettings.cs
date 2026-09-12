using UnityEngine;

namespace Assets.Scripts.Objects.BossFinisherFx
{
    /// <summary>
    /// Tuning for the boss finisher. Loaded from Resources so no scene wiring is needed in any of
    /// the four gameplay scenes; if the asset is missing the finisher still runs on these defaults.
    /// </summary>
    /// <summary>One timed spawn in the boss death chain.</summary>
    [System.Serializable]
    public class DeathBurst
    {
        public GameObject prefab;

        [Tooltip("Real seconds after the killing blow.")]
        [Min(0f)] public float delay;

        [Tooltip("How many copies to spawn at this beat.")]
        [Min(1)] public int count = 1;

        [Tooltip("Copies are scattered inside this radius around the boss. 0 stacks them on it.")]
        [Min(0f)] public float radius;

        [Min(0.01f)] public float scale = 1f;
    }

    [CreateAssetMenu(menuName = "Galactic Warrior/Boss Finisher Settings", fileName = "SO_BossFinisher")]
    public class BossFinisherSettings : ScriptableObject
    {
        /// <summary>Name looked up under any Resources folder. Keep in sync with the asset name.</summary>
        public const string ResourcePath = "SO_BossFinisher";

        [Header("Trigger")]
        [Tooltip("The finisher arms when the boss first drops to this share of its health.")]
        [Range(0.05f, 0.9f)] public float brinkHealthFraction = 0.25f;

        [Tooltip("Turn the whole feature off without unwiring anything.")]
        public bool enabled = true;

        [Header("Zoom")]
        [Tooltip("Orthographic size held during the finisher. The camera normally sits at 5, so " +
                 "smaller is a tighter shot.")]
        [Min(0.5f)] public float zoomOrthographicSize = 2.6f;

        [Min(0f)] public float zoomInDuration = 0.28f;
        [Min(0f)] public float zoomOutDuration = 0.45f;

        [Tooltip("How long the tight shot holds with no further hits. Every new hit extends it.")]
        [Min(0f)] public float holdDuration = 1.1f;

        [Tooltip("0 frames the Warrior, 1 frames the boss, 0.5 sits between them.")]
        [Range(0f, 1f)] public float bossFraming = 0.55f;

        [Header("Slow motion")]
        [Range(0.05f, 1f)] public float slowMotionScale = 0.35f;

        [Header("Impact of each hit")]
        [Tooltip("Freeze frame on every hit landed while the finisher is running.")]
        [Min(0f)] public float hitStopDuration = 0.07f;
        [Min(0f)] public float hitShakeAmplitude = 0.18f;
        [Min(0f)] public float shakeDuration = 0.25f;

        [Header("Killing blow")]
        [Tooltip("How long the tight shot holds after the kill. Keep it just past the death chain: " +
                 "GameMgr runs its own boss-death slow motion and then the relic sequence, which " +
                 "want the normal camera back.")]
        [Min(0f)] public float killHoldDuration = 0.9f;

        [Min(0f)] public float killShakeAmplitude = 0.45f;

        [Header("Post processing punch")]
        [Tooltip("Applied through a dedicated runtime Volume layered above the scene's Global " +
                 "Volume. The project's own profile asset is never touched.")]
        [Min(0f)] public float bloomIntensity = 4f;
        [Range(0f, 1f)] public float vignetteIntensity = 0.5f;
        [Range(0f, 1f)] public float chromaticAberration = 0.7f;
        [Range(-100f, 100f)] public float saturation = -25f;
        [Range(-100f, 100f)] public float contrast = 25f;

        [Header("Optional extras")]
        [Tooltip("Spawned once at the boss position when the finisher arms.")]
        public GameObject brinkVfxPrefab;

        [Header("Death spectacle")]
        [Tooltip("White-out on the killing blow. Set the alpha to 0 to disable it.")]
        public Color screenFlashColor = new Color(1f, 1f, 1f, 0.85f);

        [Tooltip("Time for the flash to fade out, in real seconds.")]
        [Min(0f)] public float screenFlashDuration = 0.35f;

        [Tooltip("Chain of bursts fired on the killing blow. One puff reads as a hit; a staggered " +
                 "chain reads as a death. Delays are real seconds, so slow motion does not stretch " +
                 "the choreography.")]
        public DeathBurst[] deathBursts = new DeathBurst[0];

        public AudioClip brinkSfx;
        public AudioClip killSfx;
        [Range(0f, 1f)] public float sfxVolume = 1f;
    }
}

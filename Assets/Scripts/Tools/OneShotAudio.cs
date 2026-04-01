using UnityEngine;

namespace Assets.Scripts.Tools
{
    public static class OneShotAudio
    {
        public static void Play(
            AudioClip clip,
            Vector3 position,
            float volume = 1f,
            Vector2 pitchRange = default,
            float spatialBlend = 0f,   // 0 = 2D, 1 = 3D
            float maxDistance = 20f
        )
        {
            if (clip == null) return;

            var go = new GameObject($"OneShot_{clip.name}");
            go.transform.position = position;

            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = spatialBlend;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.maxDistance = maxDistance;
            src.volume = volume;

            float pitch = 1f;
            if (pitchRange != default && pitchRange.y > 0f)
                pitch = Random.Range(pitchRange.x, pitchRange.y);

            src.pitch = pitch;
            src.PlayOneShot(clip, volume);

            // destroy after sound ends (account for pitch)
            Object.Destroy(go, (clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch))) + 0.1f);
        }
    }
}
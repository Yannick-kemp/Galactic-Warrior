using System.Collections;
using UnityEngine;

/// <summary>
/// Minimal boss-relic award animation: the relic rises a bit from the boss death spot,
/// fades out, then destroys itself. World-space (uses a SpriteRenderer). No VFX/SFX, no HUD.
///
/// Put this on a small prefab that has a SpriteRenderer (the relic sprite). GameMgr instantiates
/// that prefab at the boss death position and waits <see cref="Duration"/> before incrementing
/// the persistent boss-relic counter (which then shows in RelicMemory's CountText).
/// </summary>
[DisallowMultipleComponent]
public class BossRelicRiseAnimation : MonoBehaviour
{
    [SerializeField] private float duration = 0.8f;
    [SerializeField] private float riseHeight = 1.5f;
    [SerializeField] private float startScale = 1f;
    [SerializeField] private float endScale = 0.7f;
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Feedback (optional)")]
    [Tooltip("VFX prefab spawned at the relic position (e.g. the existing pickup VFX).")]
    [SerializeField] private GameObject vfxPrefab;
    [SerializeField, Min(0f)] private float vfxLifetime = 2f;
    [Tooltip("Pickup SFX clip (e.g. the same clip as RelicPickupSfxPlayer).")]
    [SerializeField] private AudioClip sfxClip;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.9f;

    public float Duration => duration;

    private void Start() => StartCoroutine(Run());

    private IEnumerator Run()
    {
        PlayFeedback(transform.position);

        SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
        if (sr == null)
            Debug.LogWarning($"[BossRelicRiseAnimation] No SpriteRenderer on '{name}' → nothing visible. Add a SpriteRenderer with a sprite.", this);
        else if (sr.sprite == null)
            Debug.LogWarning($"[BossRelicRiseAnimation] SpriteRenderer on '{name}' has no Sprite assigned → nothing visible.", this);

        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.up * riseHeight;
        Color baseColor = sr != null ? sr.color : Color.white;

        float t = 0f;
        float dur = Mathf.Max(0.01f, duration);

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = ease != null ? ease.Evaluate(k) : k;

            transform.position = Vector3.LerpUnclamped(startPos, endPos, e);
            float s = Mathf.LerpUnclamped(startScale, endScale, e);
            transform.localScale = new Vector3(s, s, 1f);

            if (sr != null)
            {
                Color c = baseColor;
                c.a = 1f - k;
                sr.color = c;
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    private void PlayFeedback(Vector3 pos)
    {
        if (vfxPrefab != null)
        {
            GameObject vfx = Instantiate(vfxPrefab, pos, Quaternion.identity);
            if (vfxLifetime > 0f)
                Destroy(vfx, vfxLifetime);
        }

        if (sfxClip != null)
            AudioSource.PlayClipAtPoint(sfxClip, pos, sfxVolume);
    }
}

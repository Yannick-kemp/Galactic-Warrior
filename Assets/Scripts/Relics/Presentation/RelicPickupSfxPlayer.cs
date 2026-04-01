using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Events;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RelicManager))]
public sealed class RelicPickupSfxPlayer : MonoBehaviour
{
    [Header("Relic Pickup SFX")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip pickupClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.9f;
    [SerializeField] private Vector2 pitchRange = new(0.96f, 1.04f);
    [SerializeField] private bool playOncePerFrame = true;

    private RelicManager _rm;
    private int _lastFrame = -1;

    private void Awake()
    {
        _rm = GetComponent<RelicManager>();

        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();

            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f; // 2D UI-like sound (set to 1 for 3D)
        }
    }

    private void OnEnable()
    {
        if (_rm != null) _rm.OnRelicCollected += HandleRelicCollected;
    }

    private void OnDisable()
    {
        if (_rm != null) _rm.OnRelicCollected -= HandleRelicCollected;
    }

    private void HandleRelicCollected(RelicCollectedEvent e)
    {
        if (pickupClip == null) return;

        if (playOncePerFrame && _lastFrame == Time.frameCount)
            return;

        _lastFrame = Time.frameCount;

        float oldPitch = sfxSource.pitch;
        sfxSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
        sfxSource.PlayOneShot(pickupClip, volume);
        sfxSource.pitch = oldPitch;
    }
}
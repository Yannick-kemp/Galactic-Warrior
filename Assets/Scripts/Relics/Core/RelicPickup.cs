using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Core;
using UnityEngine;

[DisallowMultipleComponent]
public class RelicPickup : MonoBehaviour
{
    [Header("Pickup Data")]
    [SerializeField] private RelicDefinition definition;
    //[SerializeField] private float armDelay = 0f;

    [Header("Pickup VFX")]
    [SerializeField] private GameObject pickupVfxPrefab;
    [SerializeField] private Vector3 pickupVfxOffset = new(0f, 0.2f, 0f);
    [SerializeField] private bool parentVfxToWarrior = false;
    [SerializeField] private float vfxFallbackLifetime = 2f;

    private bool _armed;
    private bool _consumed;
    private int _hitBoxLayer;
    private Collider2D[] _cols;
    private Rigidbody2D _rb;

    private int _pickupFrame = -1;
    private void Awake()
    {
        _hitBoxLayer = LayerMask.NameToLayer("Hit Box");
        if (_hitBoxLayer == -1)
            Debug.LogError("Layer 'Hit Box' not found.");

        _cols = GetComponents<Collider2D>();
        _rb = GetComponent<Rigidbody2D>();
    }
    private void Start()
    {
        _armed = true;
    }
    private void OnEnable()
    {
        _armed = false;
        _consumed = false;
        CancelInvoke(nameof(Arm));
        //  Invoke(nameof(Arm), armDelay);
    }

    private void Arm() => _armed = true;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_armed || _consumed) return;
        if (other.gameObject.layer != _hitBoxLayer) return;

        var warrior = other.GetComponentInParent<Warrior>();
        if (!warrior) return;

        Collect(warrior);
    }

    /// <summary>
    /// Collects this scene relic into <paramref name="by"/>'s RelicManager (counter increment +
    /// HUD + SFX via RelicCollectedEvent) and plays the pickup VFX, exactly like the Warrior
    /// walk-over pickup. Reusable entry point: also called when a WarriorProjectile (ice bullet)
    /// hits the relic. Does NOT arm or activate the relic -- that stays driven by the player's
    /// click. Idempotent: returns false if already consumed (anti double-collect, on top of the
    /// manager's pickup-instance dedupe).
    /// </summary>
    public bool Collect(Warrior by)
    {
        if (_consumed || by == null) return false;

        // HARD LOCK FIRST
        _consumed = true;
        _armed = false;
        enabled = false;
        CancelInvoke(nameof(Arm));

        // Stop further trigger callbacks immediately
        if (_cols != null)
            for (int i = 0; i < _cols.Length; i++)
                if (_cols[i] != null) _cols[i].enabled = false;

        if (_rb != null) _rb.simulated = false;

        var rm = by.GetComponent<RelicManager>();
        if (rm != null)
            rm.CollectFromPickup(definition, gameObject.GetInstanceID(), transform.position);

        SpawnPickupVfx(by.transform);
        Destroy(gameObject);
        return true;
    }

    private void SpawnPickupVfx(Transform warriorTf)
    {
        if (!pickupVfxPrefab) return;

        Vector3 spawnPos = transform.position + pickupVfxOffset;
        Transform parent = parentVfxToWarrior ? warriorTf : null;
        var vfx = Instantiate(pickupVfxPrefab, spawnPos, Quaternion.identity, parent);

        var ps = vfx.GetComponentInChildren<ParticleSystem>();
        if (ps != null) Destroy(vfx, GetParticleSystemTotalDuration(ps));
        else Destroy(vfx, vfxFallbackLifetime);
    }

    private static float GetParticleSystemTotalDuration(ParticleSystem ps)
    {
        var main = ps.main;
        float startLifetime = main.startLifetime.mode switch
        {
            ParticleSystemCurveMode.Constant => main.startLifetime.constant,
            ParticleSystemCurveMode.TwoConstants => main.startLifetime.constantMax,
            _ => 1f
        };

        return main.duration + startLifetime; // no /2
    }
}
using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the Laser VFX shader properties for dynamic effects with frame-based animation support.
/// Handles targeting, obstacle collision, shield blocking, and dodge-invulnerability.
/// </summary>
public class LaserVFXController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] public Transform firePoint;
    [SerializeField] private Warrior target;
    [SerializeField] private Enemy ownerEnemy;

    [Header("Frame-Based Offset Settings")]
    [SerializeField] public float minYOffset = -0.16f;
    [SerializeField] public float maxYOffset = 0.04f;
    [SerializeField] public float midYOffset = -0.01f;

    [Header("Frame-Based Rotation Settings")]
    [SerializeField] public float midRotation = 0f;
    [SerializeField] public float maxRotation = 9f;
    [SerializeField] public float minRotation = -9f;

    [Header("Child VFX References")]
    [SerializeField] private ParticleSystem startVFXParticle;
    [SerializeField] private ParticleSystem endVFXParticle;
    [SerializeField] private ParticleSystem startBeamFlashParticle;
    [SerializeField] private ParticleSystem endBeamFlashParticle;

    [Header("Shader Noise Settings")]
    [SerializeField, Range(1f, 30f)] private float noiseScale = 5f;
    [SerializeField, Range(0f, 1f)] private float noiseStrength = 0.1f;
    [SerializeField, Range(0f, 3f)] private float noiseSpeed = 0.5f;
    [SerializeField, Range(1f, 50f)] private float noise2Scale = 15f;
    [SerializeField, Range(0f, 0.5f)] private float noise2Strength = 0.05f;
    [SerializeField, Range(1f, 30f)] private float edgeNoiseScale = 10f;
    [SerializeField, Range(0f, 0.5f)] private float edgeNoiseStrength = 0.1f;
    [SerializeField, Range(0f, 20f)] private float flickerSpeed = 3f;
    [SerializeField, Range(0f, 0.3f)] private float flickerStrength = 0.05f;

    [Header("Dynamic Effects")]
    [SerializeField] private NoisePreset currentPreset = NoisePreset.Standard;

    [Header("Interaction Settings")]
    [SerializeField] private bool breakOnShield = true;
    [SerializeField] private float shieldBlockCost = 4f;
    [SerializeField] private bool stunEnemyOnBlock = true;
    [SerializeField] private bool breakOnObstacles = true;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private bool stopBeamAtObstacle = true;
    [SerializeField] private bool disableLaserWhenObstacleHit = false;
    [SerializeField] private float obstacleBackoff = 0.02f;

    [Header("Dodge Ignore")]
    [SerializeField] private bool ignoreWarriorWhileDodging = true;
    [SerializeField] private bool ignoreShieldWhileDodging = true;
    [SerializeField] private float dodgeNoHitBeamLength = 20f;

    private MaterialPropertyBlock propertyBlock;
    private bool _blockedThisActivation = false;
    private bool wasHittingWarrior = false;
    private bool _useExternalDir = false;
    private Vector2 _externalDir = Vector2.right;

    public Vector3 CurrentAimPoint { get; private set; }
    public Vector3 CurrentEndPos { get; private set; }
    public Vector3 CurrentStartPos { get; private set; }
    public bool IsLaserActive => lineRenderer != null && lineRenderer.enabled;

    public enum NoisePreset { Clean, Standard, CracklingLightning, OrganicPlasma, UnstableEnergy, SmoothLaser, PulsingCore, Custom }

    void Start()
    {
        if (ownerEnemy == null) ownerEnemy = GetComponentInParent<Enemy>();
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();

        EnsureTarget();
        DisableLaser();

        // Auto-find particles if missing
        if (startVFXParticle == null) startVFXParticle = transform.Find("StartVFXParticle")?.GetComponent<ParticleSystem>();
        if (endVFXParticle == null) endVFXParticle = transform.Find("EndVFXParticle")?.GetComponent<ParticleSystem>();
        if (startBeamFlashParticle == null) startBeamFlashParticle = transform.Find("StartbeamFlash")?.GetComponent<ParticleSystem>();
        if (endBeamFlashParticle == null) endBeamFlashParticle = transform.Find("EndbeamFlash")?.GetComponent<ParticleSystem>();

        propertyBlock = new MaterialPropertyBlock();
        ApplyPreset(currentPreset);
    }

    void Update() => UpdateNoiseProperties();

    void LateUpdate()
    {
        if (_useExternalDir) return; // M97 drives this manually
        UpdateLaser();
    }

    public void EnableLaser()
    {
        _blockedThisActivation = false;
        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, firePoint.position);

        PlayParticle(startVFXParticle);
        PlayParticle(endVFXParticle);
        PlayParticle(startBeamFlashParticle);
        PlayParticle(endBeamFlashParticle);
    }

    public void DisableLaser()
    {
        if (lineRenderer != null) lineRenderer.enabled = false;

        if (wasHittingWarrior) ownerEnemy?.OnWarriorLeftLaser();

        ownerEnemy?.OnLaserDeactivated();
        wasHittingWarrior = false;

        StopAndClearParticle(startVFXParticle);
        StopAndClearParticle(endVFXParticle);
        StopAndClearParticle(startBeamFlashParticle);
        StopAndClearParticle(endBeamFlashParticle);
    }

    private void PlayParticle(ParticleSystem ps)
    {
        if (ps != null && !ps.isPlaying) ps.Play();
    }

    private void StopAndClearParticle(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Clear(true);
    }

    private void UpdateLaser()
    {
        if (target == null) EnsureTarget();
        if (target == null || firePoint == null || lineRenderer == null || !IsLaserActive) return;

        // Turn off laser if target is dead
        if (target.IsDeadOrDying)
        {
            if (wasHittingWarrior) ownerEnemy?.OnWarriorLeftLaser();
            wasHittingWarrior = false;
            DisableLaser();
            return;
        }

        Vector2 origin = firePoint.position;
        CurrentStartPos = origin;

        Vector2 targetPoint = target.collider2 != null ? (Vector2)target.collider2.bounds.center : (Vector2)target.transform.position;
        CurrentAimPoint = targetPoint;

        Vector2 dir = _useExternalDir ? _externalDir : (targetPoint - origin).normalized;
        if (dir.sqrMagnitude < 0.0001f) return;

        bool warriorDodging = ignoreWarriorWhileDodging && target.IsDodging;

        // Force exit event if they start dodging while hit
        if (warriorDodging && wasHittingWarrior)
        {
            ownerEnemy?.OnWarriorLeftLaser();
            wasHittingWarrior = false;
        }

        int warriorLayer = LayerMask.NameToLayer("Hit Box");
        int shieldLayer = LayerMask.NameToLayer("Shield Laser");
        Collider2D sh = GameMgr.Instance?.WarriorInstance?.shieldHitbox;

        bool shieldActive = breakOnShield && !target.IsDeadOrDying && !warriorDodging &&
                           target.ShieldIsUp && sh != null && sh.enabled && shieldLayer >= 0;

        int mask = 0;
        if (!warriorDodging) mask |= (1 << warriorLayer);
        if (shieldActive && !(warriorDodging && ignoreShieldWhileDodging)) mask |= (1 << shieldLayer);
        if (breakOnObstacles) mask |= obstacleMask.value;

        bool oldQueries = Physics2D.queriesHitTriggers;
        Physics2D.queriesHitTriggers = true;

        float eps = 0.05f;
        Vector2 rayOrigin = origin + dir * eps;
        RaycastHit2D hit = (mask != 0) ? Physics2D.Raycast(rayOrigin, dir, Mathf.Infinity, mask) : default;

        Physics2D.queriesHitTriggers = oldQueries;

        bool hitSomething = hit.collider != null;
        bool hitObstacle = hitSomething && breakOnObstacles && ((obstacleMask.value & (1 << hit.collider.gameObject.layer)) != 0);

        Vector3 endPos;
        if (hitObstacle && stopBeamAtObstacle)
        {
            endPos = hit.point - dir * obstacleBackoff;
            UpdateBeamPositions(origin, endPos);
            if (disableLaserWhenObstacleHit) DisableLaser();
            if (wasHittingWarrior) ownerEnemy?.OnWarriorLeftLaser();
            wasHittingWarrior = false;
            return;
        }

        endPos = hitSomething ? (Vector3)hit.point : (warriorDodging ? (Vector3)(origin + dir * dodgeNoHitBeamLength) : (Vector3)targetPoint);
        UpdateBeamPositions(origin, endPos);

        // Shield Logic
        if (shieldActive)
        {
            if (hit.collider == sh || (hit.collider != null && hit.collider.GetComponentInParent<Warrior>() != null && sh.OverlapPoint(hit.point)))
            {
                TryConsumeShieldAndBreakLaser();
                return;
            }
        }

        // Hitting Warrior Logic
        bool isHittingWarrior = !target.IsDeadOrDying && !warriorDodging && hit.collider != null && hit.collider.GetComponentInParent<Warrior>() != null;

        if (isHittingWarrior && !wasHittingWarrior)
        {
            ownerEnemy?.OnWarriorDetectedInLaser();
            target.ApplyHitReaction(HitKind.Laser, origin, stunSeconds: 0.5f, knockbackVel: 2.5f);
        }
        else if (!isHittingWarrior && wasHittingWarrior)
        {
            ownerEnemy?.OnWarriorLeftLaser();
        }

        wasHittingWarrior = isHittingWarrior;
    }

    private void UpdateBeamPositions(Vector3 start, Vector3 end)
    {
        CurrentEndPos = end;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);

        if (startVFXParticle != null) startVFXParticle.transform.position = start;
        if (startBeamFlashParticle != null) startBeamFlashParticle.transform.position = start;
        if (endVFXParticle != null) endVFXParticle.transform.position = end;
        if (endBeamFlashParticle != null) endBeamFlashParticle.transform.position = end;
    }

    private void TryConsumeShieldAndBreakLaser()
    {
        if (_blockedThisActivation) return;
        _blockedThisActivation = true;
        if (target != null && ownerEnemy != null)
            target.TryBlockEnemyHit(ownerEnemy, blockCost: shieldBlockCost, applyReaction: stunEnemyOnBlock);
    }

    private void EnsureTarget()
    {
        if (target == null && GameMgr.Instance != null)
            target = GameMgr.Instance.WarriorInstance;
    }

    // --- Noise & Presets (Cleaned) ---
    void UpdateNoiseProperties()
    {
        if (lineRenderer == null) return;
        lineRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat("_NoiseScale", noiseScale);
        propertyBlock.SetFloat("_NoiseStrength", noiseStrength);
        propertyBlock.SetFloat("_NoiseSpeed", noiseSpeed);
        propertyBlock.SetFloat("_Noise2Scale", noise2Scale);
        propertyBlock.SetFloat("_Noise2Strength", noise2Strength);
        propertyBlock.SetFloat("_EdgeNoiseScale", edgeNoiseScale);
        propertyBlock.SetFloat("_EdgeNoiseStrength", edgeNoiseStrength);
        propertyBlock.SetFloat("_FlickerSpeed", flickerSpeed);
        propertyBlock.SetFloat("_FlickerStrength", flickerStrength);
        lineRenderer.SetPropertyBlock(propertyBlock);
    }

    public void ApplyPreset(NoisePreset preset)
    {
        currentPreset = preset;
        switch (preset)
        {
            case NoisePreset.Clean: SetNoise(3f, 0.05f, 10f, 0.02f, 8f, 0.03f, 0f, 0f, 0.3f); break;
            case NoisePreset.Standard: SetNoise(5f, 0.1f, 15f, 0.05f, 10f, 0.1f, 0f, 0f, 0.5f); break;
            case NoisePreset.CracklingLightning: SetNoise(8f, 0.15f, 20f, 0.08f, 15f, 0.2f, 5f, 0.1f, 1.5f); break;
            case NoisePreset.OrganicPlasma: SetNoise(4f, 0.12f, 12f, 0.06f, 6f, 0.15f, 2f, 0.04f, 0.8f); break;
            case NoisePreset.UnstableEnergy: SetNoise(10f, 0.25f, 25f, 0.12f, 20f, 0.3f, 8f, 0.15f, 2.0f); break;
            case NoisePreset.SmoothLaser: SetNoise(2f, 0.02f, 5f, 0.01f, 5f, 0.01f, 0f, 0f, 0.1f); break;
            case NoisePreset.PulsingCore: SetNoise(5f, 0.08f, 15f, 0.04f, 8f, 0.05f, 1.5f, 0.2f, 0.5f); break;
        }
        UpdateNoiseProperties();
    }

    private void SetNoise(float ns, float nst, float n2s, float n2st, float ens, float enst, float fs, float fst, float nsp)
    {
        noiseScale = ns; noiseStrength = nst; noise2Scale = n2s; noise2Strength = n2st;
        edgeNoiseScale = ens; edgeNoiseStrength = enst; flickerSpeed = fs; flickerStrength = fst; noiseSpeed = nsp;
    }

    public void SetExternalAimDir(Vector2 dir) { if (dir.sqrMagnitude > 0.0001f) { _externalDir = dir.normalized; _useExternalDir = true; } }
    public void ClearExternalAimDir() => _useExternalDir = false;
    public void TickLaser() => UpdateLaser();
}
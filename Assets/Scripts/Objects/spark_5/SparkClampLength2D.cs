using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

[DisallowMultipleComponent]
public class SparkShieldAwareClamp2D : MonoBehaviour
{
    public enum AnchorMode { Center, Bottom, Top }

    [Header("Spark direction")]
    [SerializeField] private Vector2 localAxis = Vector2.up;   // vertical spark
    [SerializeField] private AnchorMode anchor = AnchorMode.Center;

    [Header("Layers (names in Unity > Tags & Layers)")]
    [SerializeField] private string warriorHitBoxLayerName = "Hit Box";
    [SerializeField] private string shieldLayerName = "Shield Laser";

    [Tooltip("Other things the spark should also stop on (walls/platforms/etc). Optional.")]
    [SerializeField] private LayerMask environmentMask;

    [Header("Cast settings")]
    [SerializeField] private bool hitTriggers = false;
    [SerializeField] private float castThickness = 0.08f; // match visible spark width
    [SerializeField] private float skin = 0.03f;          // prevents micro-overlap/jitter
    [SerializeField] private float minLength = 0f;

    [Header("Max length (never extend)")]
    [Tooltip("0 = auto-detect from Renderer bounds at runtime. Otherwise set fixed max world length.")]
    [SerializeField] private float maxLengthWorld = 0f;

    [Header("Electricity VFX (at contact point)")]
    [SerializeField] private GameObject electricityPrefab;     // drag vfx_Electricity_01 here
    [Tooltip("If true: electricity only shows when shield is up (still shows on hitbox only if shield is up).")]
    [SerializeField] private bool electricityOnlyWhenShieldUp = false;
    [SerializeField] private bool alignElectricityToHitNormal = false;
    [SerializeField] private float electricityStopDelay = 0.05f; // small delay to avoid flicker
    [SerializeField] private float electricityBurstInterval = 0.12f; // 0 = only once, >0 = restart burst interval

    [Header("Damage")]
    [SerializeField] private int damagePerTick = 3;
    [SerializeField] private float damageTickInterval = 0.2f;

    [Header("Shield Cost (optional)")]
    [SerializeField] private float shieldCostPerTick = 2f;
    [SerializeField] private float shieldCostInterval = 0.06f;

    [Header("Damage gating (avoid invisible damage)")]
    [SerializeField] private float damageStartDelay = 0.08f;     // grace after enable
    [SerializeField] private float minLengthToDamage = 0.05f;    // world units
    [SerializeField] private bool requireParticlesEmitting = true;

    // Optional: who is causing the damage (for blood VFX)
    [SerializeField] private Enemy owner;
    [SerializeField] private bool ignoreShieldWhileSprint = true;

    [Header("Warrior Hit Reaction")]
    [Tooltip("Default stun applied to the Warrior on spark contact. Overridable per spawn point via EnemySpawnOverrides.bee.")]
    [SerializeField, Min(0f)] private float sparkStunSeconds = 0.10f;
    [Tooltip("Default knockback velocity applied to the Warrior on spark contact. Overridable per spawn point via EnemySpawnOverrides.bee.")]
    [SerializeField, Min(0f)] private float sparkKnockbackVel = 0f;

    private Warrior _warrior;
    private Renderer _rend;

    private int _warriorLayer;
    private int _shieldLayer;

    private float _baseMaxLen;
    private Vector3 _baseLocalScale;
    private Vector3 _baseLocalPos;

    private bool _initialized;

    // gating
    private float _enabledAt;
    private float _currentLen;
    private ParticleSystem[] _sparkParticles;
    private float _nextDamageTime;

    // electricity instance (kept & moved)
    private GameObject _elecInstance;
    private bool _elecActive;
    private float _elecDisableAt = -1f;
    private float _nextElectricityBurstTime = -1f;

    private void Awake()
    {
        _warrior = GameMgr.Instance?.WarriorInstance;

        _sparkParticles = GetComponentsInChildren<ParticleSystem>(true);
        _rend = GetComponentInChildren<Renderer>(true);

        _warriorLayer = LayerMask.NameToLayer(warriorHitBoxLayerName);
        _shieldLayer = LayerMask.NameToLayer(shieldLayerName);

        _baseLocalScale = transform.localScale;
        _baseLocalPos = transform.localPosition;
    }

    private void OnEnable()
    {
        _enabledAt = Time.time;
        _nextDamageTime = Time.time;

        _wasArmedWarriorHit = false;   // <-- add this

        DisableElectricityImmediate();
        _initialized = false;
    }


    private void OnDisable()
    {
        DisableElectricityImmediate();
    }

    private void LateUpdate()
    {
        EnsureInitialized();
        if (!_initialized) return;

        ApplyClampAndEffects();
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        _baseLocalScale = transform.localScale;
        _baseLocalPos = transform.localPosition;

        if (maxLengthWorld > 0.0001f)
        {
            _baseMaxLen = maxLengthWorld;
            _initialized = true;
            return;
        }

        if (_rend == null) return;

        Vector3 dirW = transform.TransformDirection((Vector3)localAxis.normalized);
        dirW.z = 0f;
        if (dirW.sqrMagnitude < 1e-6f) return;

        Vector3 absDir = new Vector3(Mathf.Abs(dirW.x), Mathf.Abs(dirW.y), 0f);
        Vector3 size = _rend.bounds.size;

        float detected = size.x * absDir.x + size.y * absDir.y; // projection (AABB safe)
        if (detected > 0.001f)
        {
            _baseMaxLen = detected;
            _initialized = true;
        }
    }

    private LayerMask BuildMask()
    {
        _warrior = GameMgr.Instance?.WarriorInstance;

        int mask = environmentMask.value;

        int warriorBit = (_warriorLayer >= 0) ? (1 << _warriorLayer) : 0;
        int shieldBit = (_shieldLayer >= 0) ? (1 << _shieldLayer) : 0;

        bool sprintDodging = (_warrior != null && _warrior.IsDodging);

        // During sprint: spark should ignore warrior (and optionally shield)
        if (sprintDodging)
        {
            if (warriorBit != 0) mask &= ~warriorBit;
            if (ignoreShieldWhileSprint && shieldBit != 0) mask &= ~shieldBit;
            return mask;
        }

        // Normal behavior
        if (warriorBit != 0)
            mask |= warriorBit;

        // Only include shield layer when shield is UP
        if (shieldBit != 0 && _warrior != null && _warrior.ShieldIsUp)
            mask |= shieldBit;

        return mask;
    }
    private bool _wasArmedWarriorHit;
    private void ApplyClampAndEffects()
    {
        _warrior = GameMgr.Instance?.WarriorInstance;

        Vector2 axisL = localAxis.sqrMagnitude < 1e-6f ? Vector2.up : localAxis.normalized;
        Vector2 dirW = ((Vector2)transform.TransformDirection(axisL)).normalized;

        float maxLen = _baseMaxLen;
        float newLen = maxLen;

        LayerMask mask = BuildMask();

        RaycastHit2D CastFrom(Vector2 origin, Vector2 direction, float distance)
        {
            RaycastHit2D h;
            if (castThickness > 0f)
            {
                Vector2 boxSize = new Vector2(castThickness, castThickness);
                h = Physics2D.BoxCast(origin, boxSize, transform.eulerAngles.z, direction, distance, mask);
            }
            else
            {
                h = Physics2D.Raycast(origin, direction, distance, mask);
            }

            if (h.collider != null && (!h.collider.isTrigger || hitTriggers))
                return h;

            return default;
        }

        RaycastHit2D hit = default;
        bool hasHit = false;

        if (anchor == AnchorMode.Center)
        {
            float half = maxLen * 0.5f;
            Vector2 center = transform.position;

            var hitUp = CastFrom(center, dirW, half);
            var hitDown = CastFrom(center, -dirW, half);

            float upDist = (hitUp.collider != null) ? Mathf.Max(0f, hitUp.distance - skin) : half;
            float downDist = (hitDown.collider != null) ? Mathf.Max(0f, hitDown.distance - skin) : half;

            float newHalf = Mathf.Clamp(Mathf.Min(upDist, downDist), minLength * 0.5f, half);
            newLen = newHalf * 2f;

            if (hitUp.collider != null || hitDown.collider != null)
            {
                hasHit = true;
                hit = (upDist <= downDist) ? hitUp : hitDown;
            }

            transform.localPosition = _baseLocalPos;
        }
        else
        {
            float half = maxLen * 0.5f;
            Vector2 basePosW = transform.position;

            if (anchor == AnchorMode.Bottom)
            {
                Vector2 bottom = basePosW - dirW * half;
                hit = CastFrom(bottom, dirW, maxLen);
                hasHit = hit.collider != null;

                float allowed = hasHit ? Mathf.Max(0f, hit.distance - skin) : maxLen;
                newLen = Mathf.Clamp(allowed, minLength, maxLen);

                float delta = (newLen - maxLen) * 0.5f;
                transform.localPosition = _baseLocalPos + (Vector3)(axisL * delta);
            }
            else // Top
            {
                Vector2 top = basePosW + dirW * half;
                hit = CastFrom(top, -dirW, maxLen);
                hasHit = hit.collider != null;

                float allowed = hasHit ? Mathf.Max(0f, hit.distance - skin) : maxLen;
                newLen = Mathf.Clamp(allowed, minLength, maxLen);

                float delta = (maxLen - newLen) * 0.5f;
                transform.localPosition = _baseLocalPos + (Vector3)(axisL * delta);
            }
        }

        // never extend
        newLen = Mathf.Min(newLen, maxLen);

        // IMPORTANT: update current length AFTER final clamp
        _currentLen = newLen;

        // apply scale on main axis
        float t = (maxLen <= 0.0001f) ? 1f : (newLen / maxLen);
        Vector3 s = _baseLocalScale;

        if (Mathf.Abs(axisL.y) >= Mathf.Abs(axisL.x))
            s.y = _baseLocalScale.y * t;
        else
            s.x = _baseLocalScale.x * t;

        transform.localScale = s;

        // classify hit
        bool hitShield = hasHit && hit.collider != null && hit.collider.gameObject.layer == _shieldLayer;
        bool hitWarrior = hasHit && hit.collider != null && hit.collider.gameObject.layer == _warriorLayer;

        // ---------------------------------
        // Electricity: ONLY when spark visible + (hit shield or hitbox)
        // ---------------------------------
        bool sparkVisible = IsSparkVisibleNow();
        bool allowElectric = !electricityOnlyWhenShieldUp || (_warrior != null && _warrior.ShieldIsUp);

        bool shouldShowElectricity =
            allowElectric &&
            sparkVisible &&
            hasHit &&
            (hitShield || hitWarrior);

        if (shouldShowElectricity)
        {
            ShowElectricity(hit.point, hit.normal);
        }
        else
        {
            // If spark isn't visible, electricity must NEVER be visible.
            if (!sparkVisible)
                DisableElectricityImmediate();
            else
                HideElectricityWithDelay();
        }


        // ---------------------------------
        // Damage / Block: ONLY when spark is armed/visible
        // ---------------------------------
        if (_warrior == null || !hasHit)
        {
            _wasArmedWarriorHit = false;     // <-- important reset
            _nextDamageTime = Time.time;
            return;
        }

        bool damageArmed = IsDamageArmed();

        // "armed hit" state (this is what we want to detect transitions for)
        bool armedWarriorHit = damageArmed && hitWarrior;

        // ---- CALL ONCE when it becomes true
        if (armedWarriorHit && !_wasArmedWarriorHit)
        {
            // FIRST FRAME of a valid warrior contact (hitWarrior + damage armed)

            // Spark hit reaction. Defaults come from the serialized fields; a spawn point can
            // override stun / knockback per-instance via EnemySpawnOverrides.bee (read from the
            // owner enemy, set by EnemyMgr at spawn). Falls back to defaults if not spawned with
            // overrides (e.g. hand-placed) or owner is missing.
            float stun = sparkStunSeconds;
            float knock = sparkKnockbackVel;

            EnemySpawnOverrides ov = owner != null ? owner.ActiveSpawnOverrides : null;
            if (ov != null)
            {
                if (ov.overrideSparkStun) stun = ov.sparkStunSeconds;
                if (ov.overrideSparkKnockback) knock = ov.sparkKnockbackVel;
            }

            _warrior.ApplyHitReaction(HitKind.Spark, hit.point, stunSeconds: stun, knockbackVel: knock);
        }

        _wasArmedWarriorHit = armedWarriorHit;

        // If not armed yet, do nothing (no invisible damage and no reaction spam)
        if (!damageArmed)
        {
            _nextDamageTime = Time.time;
            return;
        }

        // Shield blocks damage
        if (hitShield && _warrior.ShieldIsUp)
        {
            _wasArmedWarriorHit = false;     // <-- because we are NOT "hitting warrior" anymore
            _warrior.TryAbsorbSqueeze(shieldCostPerTick, shieldCostInterval);
            _nextDamageTime = Time.time;
            return;
        }

        // Hitbox takes damage
        if (hitWarrior)
        {
            if (Time.time >= _nextDamageTime)
            {
                _warrior.TakeDamage(damagePerTick);

                if (owner != null)
                    _warrior.SpawnBloodshedEffectFromEnemy(owner);

                _nextDamageTime = Time.time + damageTickInterval;
            }
        }
        else
        {
            _wasArmedWarriorHit = false;     // <-- reset when not on hitbox
            _nextDamageTime = Time.time;
        }

    }

    private void ShowElectricity(Vector2 pos, Vector2 normal)
    {
        if (_elecInstance == null && electricityPrefab != null)
            _elecInstance = Instantiate(electricityPrefab);

        if (_elecInstance == null) return;

        _elecInstance.transform.position = pos;

        if (alignElectricityToHitNormal && normal.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            _elecInstance.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
        else
        {
            _elecInstance.transform.rotation = Quaternion.identity;
        }

        if (!_elecInstance.activeSelf)
            _elecInstance.SetActive(true);

        _elecActive = true;

        // Burst frequency control
        if (_nextElectricityBurstTime < 0f)
        {
            RestartAllParticles(_elecInstance);
            _nextElectricityBurstTime = (electricityBurstInterval > 0f)
                ? Time.time + electricityBurstInterval
                : float.PositiveInfinity;
        }
        else if (electricityBurstInterval > 0f && Time.time >= _nextElectricityBurstTime)
        {
            RestartAllParticles(_elecInstance);
            _nextElectricityBurstTime = Time.time + electricityBurstInterval;
        }

        _elecDisableAt = -1f;
    }

    private void HideElectricityWithDelay()
    {
        if (_elecInstance == null || !_elecActive) return;

        if (_elecDisableAt < 0f)
            _elecDisableAt = Time.time + electricityStopDelay;

        if (Time.time >= _elecDisableAt)
            DisableElectricityImmediate();
    }

    private void DisableElectricityImmediate()
    {
        _elecDisableAt = -1f;
        _elecActive = false;
        _nextElectricityBurstTime = -1f;

        if (_elecInstance != null)
            _elecInstance.SetActive(false);
    }

    private bool IsDamageArmed()
    {
        if (Time.time < _enabledAt + damageStartDelay)
            return false;

        if (_currentLen < minLengthToDamage)
            return false;

        if (requireParticlesEmitting && _sparkParticles != null && _sparkParticles.Length > 0)
        {
            foreach (var ps in _sparkParticles)
            {
                if (ps == null) continue;
                if (ps.particleCount > 0) return true;
            }
            return false;
        }

        return true;
    }

    private bool IsSparkVisibleNow()
    {
        if (_currentLen < minLengthToDamage)
            return false;

        if (Time.time < _enabledAt + damageStartDelay)
            return false;

        if (requireParticlesEmitting && _sparkParticles != null && _sparkParticles.Length > 0)
        {
            foreach (var ps in _sparkParticles)
            {
                if (ps == null) continue;
                if (ps.particleCount > 0) return true;
            }
            return false;
        }

        return true;
    }

    private static void RestartAllParticles(GameObject go)
    {
        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }
    }
}

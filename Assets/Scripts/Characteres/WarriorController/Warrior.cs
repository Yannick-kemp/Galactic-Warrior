using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Relics.Events;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {
        #region Singleton / Constants

        public static Warrior Instance { get; private set; }
        public const float CONTACT_Y_TOLERANCE = 0.11f;

        #endregion

        #region Enums

        public enum HitFxPoint
        {
            ContactOnEnemy,
            FistSocket,
            KickSocket
        }

        public enum AttackAnimMode
        {
            Attack1,
            Attack2,
            Attack3,
        }

        #endregion

        #region Prefabs / Visuals

        [Header("Prefabs")]
        [SerializeField] private GameObject slashPrefab;
        [SerializeField] private GameObject novaPrefab;
        [SerializeField] private GameObject twirl2Prefab;

        [Header("Hit FX (Attack1 Animation Event)")]
        [SerializeField] private GameObject hitExplosionPrefab;
        [SerializeField] private float hitExplosionScale = 0.25f;
        [SerializeField] private float hitExplosionDestroyAfter = 0.6f;

        [Header("Hit FX Determinism")]
        [SerializeField] private bool hitFxUseWorldSpace = true;
        [SerializeField] private uint hitFxFixedSeed = 12345;

        [Header("Hit FX Spawn Points")]
        [SerializeField] private Transform fistHitSocket;
        [SerializeField] private Transform kickHitSocket;
        [SerializeField] private Vector2 fistFxOffset = Vector2.zero;
        [SerializeField] private Vector2 kickFxOffset = Vector2.zero;

        [Header("Echo Settings")]
        [SerializeField] private Color runEchoColor = new Color(1f, 1f, 1f, 0.3f);
        [SerializeField] private Color jumpEchoColor = new Color(1f, 1f, 0f, 0.4f);
        [SerializeField] private Color attackEchoColor = new Color(1f, 0f, 0f, 0.5f);

        [Header("Blood Effects")]
        [SerializeField] private GameObject bloodshedPrefab;
        [SerializeField] private float bloodEffectCooldown = 0.5f;

        #endregion

        #region Attack SettingsDama

        [Header("Layers")]
        [SerializeField] private LayerMask enemyLayer;

        [Header("Attack Settings")]
        [SerializeField] private float attackRadius = 1.5f;
        [SerializeField] private Vector2 attackCenterOffset = new Vector2(0.5f, 0f);

        [Header("Attack Damage/Knockback")]
        [SerializeField] private int attack1Damage = 10;
        [SerializeField] private float attack1KnockbackForce = 0.35f;

        [SerializeField] private float attack2Duration = 5f; // legacy support
        private bool attack2Enabled = false;                 // legacy support

        [SerializeField] private AttackAnimMode attackMode = AttackAnimMode.Attack1;
        [SerializeField] private string attack1Trigger = "attack_animation_1";
        [SerializeField] private string attack2Trigger = "attack_animation_2";

        [Header("Attack2 Cooldown")]
        [SerializeField] private float attack2Cooldown = 6f;
        [SerializeField] private bool fallbackToAttack1IfAttack2OnCooldown = true;

        #endregion

        #region Shield

        [Header("Shield VFX")]
        [SerializeField] private GameObject shieldPrefab;
        [SerializeField] private Transform shieldSocket;
        [SerializeField] private Vector3 shieldLocalOffset = new Vector3(0f, 0.6f, 0f);
        [SerializeField] private Vector3 shieldLocalScale = Vector3.one;
        [SerializeField] private bool shieldSimulationLocalSpace = true;

        [SerializeField] private string shieldSortingLayerName = "Default";
        [SerializeField] private int shieldOrderInLayer = 50;

        [Header("Shield Gameplay")]
        [SerializeField] private bool shieldGameplayEnabled = true;
        [SerializeField] private float shieldMaxDurability = 100f;
        [SerializeField] private float shieldBlockCost = 10f;
        [SerializeField] private float shieldStompCost = 20f;
        [SerializeField] private float shieldBreakCooldown = 2.0f;
        [SerializeField] private float shieldMinBlockInterval = 0.06f;

        [Header("Shield Block Reaction (Enemy)")]
        [SerializeField] private float blockKnockback = 0.25f;
        [SerializeField] private float blockStunSeconds = 0.15f;

        [Header("Shield Stomp (Landing on Enemy)")]
        [SerializeField] private int stompDamage = 25;
        [SerializeField] private float stompKnockback = 0.9f;
        [SerializeField] private float stompStunSeconds = 0.20f;

        [SerializeField] public Collider2D shieldHitbox;

        [Header("Shield Relic Use (UI Only)")]
        [SerializeField] private float shieldRelicDefaultCooldown = 8f;
        [SerializeField] private bool shieldRelicRefreshIfAlreadyActive = true;

        #region Low Health Blink

        [Header("Low Health Blink")]
        [SerializeField] private float lowHealthThreshold = 0.25f;
        [SerializeField] private float blinkSpeed = 8f;
        [SerializeField] private float blinkMinAlpha = 0.25f;
        [SerializeField] private float blinkMaxAlpha = 1f;

        private bool _lowHealthBlinkActive;
        private Coroutine _lowHealthBlinkRoutine;
        private SpriteRenderer[] _renderers;

        #endregion

        private float _nextShieldRelicReadyTime = -999f;

        public bool IsShieldRelicReady => Time.time >= _nextShieldRelicReadyTime;
        public float ShieldRelicCooldownRemaining => Mathf.Max(0f, _nextShieldRelicReadyTime - Time.time);

        private float _shieldDurability;
        private bool _shieldBroken;
        private float _lastBlockTime;
        private Coroutine _shieldRecoverRoutine;
        private GameObject _shieldInstance;
        private ParticleSystem[] _shieldParticles;
        private ParticleSystemRenderer[] _shieldRenderers;
        private Coroutine _shieldDisableRoutine;

        public bool ShieldIsUp =>
            shieldGameplayEnabled &&
            !_shieldBroken &&
            _shieldInstance != null &&
            _shieldInstance.activeSelf;

        public float ShieldDurability01 =>
            shieldMaxDurability <= 0 ? 0 : Mathf.Clamp01(_shieldDurability / shieldMaxDurability);

        #endregion

        #region Hard Action Lock

        private bool _platformStoneRepulseActive;

        public bool IsPlatformStoneRepulseActive => _platformStoneRepulseActive;

        private bool IsHardActionLocked =>
            _platformStoneRepulseActive ||
            _frozenByHivernox ||
            _hivernoxHitLockRoutine != null ||
            _deathStarted ||
            CanDie ||
            IsDeadOrDying;

        #endregion

        #region Bounce / Collision Anti-Loop

        [Header("Bounce Landing Away (Anti-Double-Landing)")]
        [SerializeField] private float landAwayDistance = 1.2f;
        [SerializeField] private float ignoreEnemyCollisionTime = 0.9f;
        [SerializeField] private float clearancePad = 0.03f;

        private float _requiredClearanceX;
        private Enemy _lastBouncedEnemy;
        private bool _postBounceActive;
        private float _postBounceStartTime;

        private int _morvexTopContactFrames;
        private Enemy _morvexTopContactEnemy;

        #endregion

        #region UI Input Guard

        [Header("UI Input Guard")]
        [SerializeField] private bool blockWorldInputWhenPointerOverUI = true;
        [SerializeField] private float uiInputGuardDuration = 0.12f;
        private float _uiInputBlockUntil = -999f;

        #endregion

        #region Sprint Relic (Arm -> Move -> Dodge)

        [Header("Sprint Relic")]
        [SerializeField] private Color sprintEchoColor = new Color(0.35f, 0.95f, 1f, 0.55f);
        [SerializeField] private float sprintMinDuration = 0.05f;
        [SerializeField] private float sprintMinMultiplier = 1.01f;
        [SerializeField] private float sprintIgnoreRefreshInterval = 0.15f;
        [SerializeField] private bool consumeSprintStackInsideWarrior = true;

        #region Max Jump height

        [Header("Max Jump height")]
        [SerializeField] private float maxJump = 6f;

        #endregion

        [SerializeField] private Collider2D warriorMeteorBodyHitbox;
        [SerializeField] private Collider2D warriorMeteorShieldHitbox;

        private readonly List<Collider2D> _warriorCollidersDuringSprint = new List<Collider2D>();
        private int _sprintArmFrame = -1;

        private bool _sprintArmed;
        private bool _sprintActive;

        private string _sprintRelicId;
        private bool _sprintConsumeOnUse;
        private float _sprintDuration;
        private float _sprintSpeedMultiplier;
        private float _sprintCooldown;

        private float _nextSprintReadyTime = -999f;
        private float _speedBeforeSprint;
        private float _nextSprintIgnoreRefreshTime;

        private Coroutine _sprintRoutine;
        private readonly HashSet<Collider2D> _ignoredEnemyCollidersDuringSprint = new HashSet<Collider2D>();

        public bool IsDodging => _sprintActive;
        public bool IsSprintArmed => _sprintArmed;
        public float SprintCooldownRemaining => Mathf.Max(0f, _nextSprintReadyTime - Time.time);

        #endregion

        #region Runtime State

        protected override bool AllowEdgeExitWhenTargetOutside => true;

        protected override float PlatformSafeMargin => 0.12f;

        private bool _attack2ArmedByRelic = false;
        private bool _attack2WindowStarted = false;
        private float _armedAttack2Duration = 0f;
        private float _nextAttack2ReadyTime = -999f;

        public bool IsAttack2Ready => Time.time >= _nextAttack2ReadyTime;
        public float Attack2CooldownRemaining => Mathf.Max(0f, _nextAttack2ReadyTime - Time.time);

        public bool IsPowerComboArmed => _attack2ArmedByRelic && !_attack2CooldownStarted;

        private bool IsRelicAttack2Active =>
            _attack2ArmedByRelic &&
            (
                !_attack2CooldownStarted ||
                Time.time < _nextAttack2ReadyTime
            );

        private bool _attack2CooldownStarted = false;

        public bool CanDie { get; set; }
        public bool IsFallingEdge { get; set; }
        public bool IsFallingPlfExit { get; set; }
        public bool IsFallingHitEnemy { get; set; }
        public bool IsFallingGrazesEdge { get; set; }
        public bool IsCollindindWhenAttacking { get; set; }
        public bool CanAttackWarrior { get; set; } = false;

        public bool _blockAction;
        public GalaticfFileSys.TimerManager.Timer ValidateTimer { get; set; }

        public override bool CanJump => collider2.bounds.max.y < InputMgr.Instance.TouchedVector.y;

        private bool IsFalling =>
            IsFallingEdge || IsFallingPlfExit || IsFallingHitEnemy || IsFallingGrazesEdge;

        private float _lastBloodSpawnTime = -999f;
        private int _cmp = 0;

        private CharacterEchoGenerator _echoGenerator;
        private PlayerEventHub _hub;

        private HashSet<Enemy> _enemiesInRangeLastFrame = new HashSet<Enemy>();
        private bool _attack1HitEventConsumed = false;

        private float _lastSlashTime = 0f;
        private const float SLASH_COOLDOWN = 0.2f;
        private const int MAX_SLASH_EFFECTS = 5;
        private List<GameObject> _activeSlashEffects = new List<GameObject>();

        private bool _blockedByEnemyContact;
        private Enemy _blockingEnemy;

        [SerializeField] private float idleGuardAfterAttackRequest = 0.12f;
        private float _idleGuardUntil = -999f;

        #endregion

        [Header("SFX")]
        [SerializeField] private AudioClip attack1HitClip;
        [SerializeField, Range(0f, 1f)] private float attack1HitVolume = 0.9f;
        [SerializeField] private Vector2 attack1HitPitchRange = new Vector2(0.96f, 1.04f);
        [SerializeField] private bool attack1HitPlayOncePerFrame = true;

        private AudioSource _sfxSource;
        private int _lastAttack1HitSfxFrame = -1;

        [Header("Jump SFX")]
        [SerializeField] private AudioClip jumpClip;
        [SerializeField, Range(0f, 1f)] private float jumpVolume = 0.85f;
        [SerializeField] private Vector2 jumpPitchRange = new Vector2(0.96f, 1.04f);
        [SerializeField] private bool jumpPlayOncePerFrame = true;

        private int _lastJumpSfxFrame = -1;

        [Header("Attack1 Miss SFX")]
        [SerializeField] private AudioClip attack1MissClip;
        [SerializeField, Range(0f, 1f)] private float attack1MissVolume = 0.85f;
        [SerializeField] private Vector2 attack1MissPitchRange = new Vector2(0.96f, 1.04f);
        [SerializeField] private bool attack1MissPlayOncePerFrame = true;

        private int _lastAttack1MissSfxFrame = -1;

        [Header("Attack2 SFX")]
        [SerializeField] private AudioClip attack2Clip;
        [SerializeField, Range(0f, 1f)] private float attack2Volume = 0.9f;
        [SerializeField] private Vector2 attack2PitchRange = new Vector2(0.96f, 1.04f);
        [SerializeField] private bool attack2PlayOncePerFrame = true;

        private int _lastAttack2SfxFrame = -1;

        #region Unity Lifecycle

        private void Awake()
        {
#if UNITY_ANDROID
            Time.fixedDeltaTime = 0.02f;
            Application.targetFrameRate = 60;
#endif
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            CanAttackWarrior = true;
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _hub = GetComponent<PlayerEventHub>();
        }

        protected override void Start()
        {
            base.Start();

            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            _allowViewportDeathTime = Time.time + 0.8f;

            GameMgr.Instance?.RegisterHero(this);
            CanMove = true;

            _echoGenerator = GetComponent<CharacterEchoGenerator>();
            _echoGenerator?.SetEchoActive(false);

            SetupShield();
            _shieldDurability = shieldMaxDurability;

            CacheWarriorCollidersForSprint();

            if (warriorMeteorShieldHitbox != null)
                warriorMeteorShieldHitbox.enabled = false;

            if (warriorMeteorBodyHitbox != null)
                warriorMeteorBodyHitbox.enabled = true;

            AwakeAttack3VisualDefaults();
        }

        protected void Update()
        {
            RefreshRelicAttack2State();

            CheckIfStopRunDisplay();
            HandleInput();
            HandleFallingAndDeath();

            CheckWorldYDeathFallback();
            CheckEnemiesLeavingRange();
            UpdateEcho();
            UpdateLowHealthBlink();

            _activeSlashEffects.RemoveAll(slash => slash == null);

            if (!IsHardActionLocked &&
                _attack3Casting &&
                _iceBallShotPending &&
                InputMgr.Instance != null &&
                InputMgr.Instance.IsScreenTouched())
            {
                _pendingIceBallAimWorld = InputMgr.Instance.TouchedVector;
                ApplyAttack3OrbitAim(_pendingIceBallAimWorld);
            }

            EnsureDefaultWarriorSpriteVisibleWhenNotCasting();
        }

        private void FixedUpdate()
        {
            RememberZalaytyImpactPrePhysicsPosition();

            ApplyDestinationPlatformAntiTunnelDuringPhysicsFall();

            ApplyMovingPlatformIdleStick();

            ApplyActiveZalaytyBodyImpactAbsorber();

            // Guaranteed-separation backstop: runs every frame on the final resolved
            // position (after anti-tunnel + absorber). Self-defers while a bounce is
            // already active, so it must run before the post-bounce early-return below.
           EnforceEnemyOverlapRecovery();

            if (!_postBounceActive)
                return;

            if (Time.time - _postBounceStartTime > ignoreEnemyCollisionTime)
            {
                EndPostBounce();
                return;
            }

            if (activesJumpCoroutine != null)
                return;

            if (CountGroundPoints() <= 0)
                return;

            if (_lastBouncedEnemy == null || _lastBouncedEnemy.NormalCollider == null)
            {
                EndPostBounce();
                return;
            }

            float enemyX = _lastBouncedEnemy.NormalCollider.bounds.center.x;
            float myX = collider2.bounds.center.x;

            if (Mathf.Abs(myX - enemyX) >= _requiredClearanceX)
                EndPostBounce();
        }

        [SerializeField] private float platformSafeMargin = 1f;

        private Vector3 _lastSafePosition;

        public Vector3 LastSafePosition
        {
            get => _lastSafePosition;
            set => _lastSafePosition = value;
        }

        private void LateUpdate()
        {
            if (_deathStarted)
                return;

            if (CurrentplatForm == null || collider2 == null)
                return;

            LastSafePlatform = CurrentplatForm;

            if (CurrentplatForm is Assets.Scripts.Platforms.MovingVerticalPlatform movingVerticalPlatform)
            {
                _lastSafePosition = movingVerticalPlatform.GetSafeRespawnPositionFor(
                    this,
                    transform.position.x
                );

                return;
            }

            if (CurrentplatForm is Assets.Scripts.Platforms.MovingHorizontalPlatform movingHorizontalPlatform)
            {
                _lastSafePosition = movingHorizontalPlatform.GetSafeRespawnPositionFor(
                    this,
                    transform.position.x
                );

                return;
            }

            if (CurrentplatForm is global::RotatingPlatform rotatingPlatform)
            {
                _lastSafePosition = rotatingPlatform.GetSafeRespawnPositionFor(
                    this,
                    transform.position.x
                );

                return;
            }

            if (CurrentplatForm.platformCollider == null)
                return;

            Bounds pb = CurrentplatForm.platformCollider.bounds;

            float safeY = pb.max.y + collider2.bounds.extents.y + 0.02f;

            float minX = pb.min.x + platformSafeMargin;
            float maxX = pb.max.x - platformSafeMargin;

            float clampedX = minX <= maxX
                ? Mathf.Clamp(transform.position.x, minX, maxX)
                : pb.center.x;

            _lastSafePosition = new Vector3(clampedX, safeY, transform.position.z);
        }

        #endregion

        #region Hit Reaction

        [Header("Zalayty Different-Platform Impact Absorption")]
        [SerializeField] private bool enableZalaytyDifferentPlatformImpactAbsorption = true;

        [SerializeField, Min(0f)] private float zalaytyDifferentPlatformImpactMinSpeed = 3.0f;
        [SerializeField, Min(0f)] private float zalaytyDifferentPlatformImpactMaxSpeed = 13.0f;
        [SerializeField, Min(0.01f)] private float zalaytyDifferentPlatformImpactXLockSeconds = 0.12f;
        [SerializeField, Min(0f)] private float zalaytyGroundedImpactMaxSnapBackX = 0.80f;
        [SerializeField, Min(0f)] private float zalaytyAirborneImpactMaxSnapBackX = 4.00f;
        [SerializeField, Min(0.01f)] private float zalaytyDifferentPlatformImpactAbsorbCooldown = 0.16f;
        [SerializeField] private bool cancelWarriorControlledJumpOnAirborneZalaytyImpact = true;
        [SerializeField] private bool cancelUpwardVelocityOnAirborneZalaytyImpact = true;

        private float _nextAllowedZalaytyDifferentPlatformImpactAbsorbTime = -999f;

        private Vector2 _zalaytyImpactPrePhysicsPosition;
        private float _zalaytyImpactPrePhysicsPositionTime = -999f;
        private bool _hasZalaytyImpactPrePhysicsPosition;

        private bool _zalaytyBodyImpactAbsorbActive;
        private float _zalaytyBodyImpactAnchorX;
        private float _zalaytyBodyImpactAbsorbUntil = -999f;
        private bool _zalaytyBodyImpactAirborne;

        public void ApplyHitReaction(HitKind kind, Vector2 fromWorldPos, float stunSeconds, float knockbackVel)
        {
            if (_sprintActive) return;
            if (Time.time < _stunImmuneUntil) return;
            if (Time.time - _lastHitReactTime < hitReactMinInterval) return;

            _lastHitReactTime = Time.time;

            if (kind == HitKind.Spark)
            {
                ApplySparkReaction(fromWorldPos, stunSeconds);
                return;
            }

            float dir = Mathf.Sign(transform.position.x - fromWorldPos.x);
            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();
            StartHitStun(stunSeconds, knockbackX: knockbackVel * dir);
        }

        private void ApplySparkReaction(Vector2 fromWorldPos, float stunSeconds)
        {
            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();
            WaitAnimationDisplay();
            IsFallingGrazesEdge = false;

            Vector2 knockbackDirection = (Vector2)transform.position - fromWorldPos;
            if (knockbackDirection.sqrMagnitude < 0.0001f)
                knockbackDirection = Vector2.right;

            knockbackDirection = new Vector2(Mathf.Sign(knockbackDirection.x), sparkUpBias).normalized;

            rigidbody2?.AddForce(knockbackDirection * sparkImpulse, ForceMode2D.Impulse);
            StartHitStun(stunSeconds, knockbackX: 0f);
        }

        private void StartHitStun(float seconds, float knockbackX)
        {
            if (_hitReactRoutine != null)
                StopCoroutine(_hitReactRoutine);

            _hitReactRoutine = StartCoroutine(HitStunRoutine(seconds, knockbackX));
        }

        private IEnumerator HitStunRoutine(float seconds, float knockbackX)
        {
            CanMove = false;
            CanAttackWarrior = false;

            if (Mathf.Abs(knockbackX) > 0.001f)
            {
                Vector2 v = rigidbody2.linearVelocity;
                v.x = knockbackX;
                rigidbody2.linearVelocity = v;
            }

            yield return new WaitForSeconds(seconds);

            CanMove = true;
            CanAttackWarrior = true;

            _stunImmuneUntil = Time.time + stunRecoveryImmunity;

            _hitReactRoutine = null;
        }

        private void RememberZalaytyImpactPrePhysicsPosition()
        {
            if (_zalaytyBodyImpactAbsorbActive)
                return;

            Vector2 p = rigidbody2 != null
                ? rigidbody2.position
                : (Vector2)transform.position;

            _zalaytyImpactPrePhysicsPosition = p;
            _zalaytyImpactPrePhysicsPositionTime = Time.time;
            _hasZalaytyImpactPrePhysicsPosition = true;
        }

        public bool IsAirborneForZalaytyBodyImpactAbsorption()
        {
            if (collider2 == null)
                return false;

            bool noGroundPoints = CountGroundPoints() == 0;
            bool controlledJump = activesJumpCoroutine != null || IsJumping;
            bool fallingState = IsFalling;
            bool verticalMotionWithoutPlatform =
                CurrentplatForm == null &&
                rigidbody2 != null &&
                Mathf.Abs(rigidbody2.linearVelocity.y) > 0.05f;

            return noGroundPoints || controlledJump || fallingState || verticalMotionWithoutPlatform;
        }

        public bool TryAbsorbZalaytyDifferentPlatformImpact(
            global::ZalaytyMonster zalayty,
            Vector2 impactFromWorldPos,
            Vector2 incomingVelocity,
            float incomingSpeed)
        {
            if (!enableZalaytyDifferentPlatformImpactAbsorption)
                return false;

            if (zalayty == null)
                return false;

            if (IsDeadOrDying || _deathStarted || CanDie)
                return false;

            if (_sprintActive || _reviveInvulnerable)
                return false;

            incomingSpeed = Mathf.Abs(incomingSpeed);
            if (incomingSpeed < zalaytyDifferentPlatformImpactMinSpeed)
                return false;

            bool airborne = IsAirborneForZalaytyBodyImpactAbsorption();

            if (Time.time < _nextAllowedZalaytyDifferentPlatformImpactAbsorbTime)
            {
                if (_zalaytyBodyImpactAbsorbActive)
                    ApplyZalaytyBodyImpactAbsorberCorrection();

                return true;
            }

            float anchorX = SelectZalaytyBodyImpactAnchorX(airborne);

            _nextAllowedZalaytyDifferentPlatformImpactAbsorbTime =
                Time.time + Mathf.Max(0.01f, zalaytyDifferentPlatformImpactAbsorbCooldown);

            _zalaytyBodyImpactAbsorbActive = true;
            _zalaytyBodyImpactAnchorX = anchorX;
            _zalaytyBodyImpactAirborne = airborne;
            _zalaytyBodyImpactAbsorbUntil =
                Time.time + Mathf.Max(0.01f, zalaytyDifferentPlatformImpactXLockSeconds);

            StopMoveTowardCoroutine();

            if (airborne && cancelWarriorControlledJumpOnAirborneZalaytyImpact)
            {
                StopJumpTowardCoroutine();
                DescendentPhase = true;
                JumpAnimationDisplay();
            }

            ApplyZalaytyBodyImpactAbsorberCorrection();
            return true;
        }

        private float SelectZalaytyBodyImpactAnchorX(bool airborne)
        {
            float currentX = rigidbody2 != null
                ? rigidbody2.position.x
                : transform.position.x;

            if (!_hasZalaytyImpactPrePhysicsPosition)
                return currentX;

            float age = Time.time - _zalaytyImpactPrePhysicsPositionTime;
            if (age > 0.25f)
                return currentX;

            float previousX = _zalaytyImpactPrePhysicsPosition.x;
            float delta = currentX - previousX;

            float maxSnap = airborne
                ? zalaytyAirborneImpactMaxSnapBackX
                : zalaytyGroundedImpactMaxSnapBackX;

            if (maxSnap <= 0f || Mathf.Abs(delta) <= maxSnap)
                return previousX;

            return currentX - Mathf.Sign(delta) * maxSnap;
        }

        private void ApplyActiveZalaytyBodyImpactAbsorber()
        {
            if (!_zalaytyBodyImpactAbsorbActive)
                return;

            if (Time.time > _zalaytyBodyImpactAbsorbUntil)
            {
                _zalaytyBodyImpactAbsorbActive = false;
                return;
            }

            ApplyZalaytyBodyImpactAbsorberCorrection();
        }

        private void ApplyZalaytyBodyImpactAbsorberCorrection()
        {
            if (rigidbody2 == null)
            {
                Vector3 p = transform.position;
                p.x = _zalaytyBodyImpactAnchorX;
                transform.position = p;
                Physics2D.SyncTransforms();
                return;
            }

            Vector2 p2 = rigidbody2.position;
            p2.x = _zalaytyBodyImpactAnchorX;
            rigidbody2.position = p2;

            Vector2 v = rigidbody2.linearVelocity;
            v.x = 0f;

            if (_zalaytyBodyImpactAirborne &&
                cancelUpwardVelocityOnAirborneZalaytyImpact &&
                v.y > 0f)
            {
                v.y = 0f;
            }

            rigidbody2.linearVelocity = v;
            rigidbody2.angularVelocity = 0f;

            Physics2D.SyncTransforms();
        }

        #endregion

        private void CacheWarriorCollidersForSprint()
        {
            _warriorCollidersDuringSprint.Clear();

            Collider2D[] cols = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null && !_warriorCollidersDuringSprint.Contains(cols[i]))
                    _warriorCollidersDuringSprint.Add(cols[i]);
            }

            if (collider2 != null && !_warriorCollidersDuringSprint.Contains(collider2))
                _warriorCollidersDuringSprint.Add(collider2);
        }

        private void SetIgnoreWithAllWarriorColliders(Collider2D enemyCol, bool ignore)
        {
            if (enemyCol == null)
                return;

            if (_warriorCollidersDuringSprint.Count == 0)
                CacheWarriorCollidersForSprint();

            for (int i = 0; i < _warriorCollidersDuringSprint.Count; i++)
            {
                Collider2D wcol = _warriorCollidersDuringSprint[i];
                if (wcol == null)
                    continue;

                Physics2D.IgnoreCollision(wcol, enemyCol, ignore);
            }
        }

        public override void TakeDamage(float damage)
        {
            if (IsDeadOrDying) return;
            if (_sprintActive) return;
            if (_reviveInvulnerable) return;
            if (_deathStarted) return;

            base.TakeDamage(damage);

            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            UpdateLowHealthBlink();

            if (currentHealth <= 0f)
                StartDeath();
        }

        #region blicking methode

        private void UpdateLowHealthBlink()
        {
            if (_deathStarted)
            {
                StopLowHealthBlink();
                return;
            }

            bool shouldBlink = Health01 < lowHealthThreshold;

            if (shouldBlink && !_lowHealthBlinkActive)
            {
                _lowHealthBlinkRoutine = StartCoroutine(LowHealthBlinkRoutine());
                _lowHealthBlinkActive = true;
            }
            else if (!shouldBlink && _lowHealthBlinkActive)
            {
                StopLowHealthBlink();
            }
        }

        private IEnumerator LowHealthBlinkRoutine()
        {
            while (true)
            {
                float t = Mathf.PingPong(Time.time * blinkSpeed, 1f);
                float alpha = Mathf.Lerp(blinkMinAlpha, blinkMaxAlpha, t);

                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] == null)
                        continue;

                    Color c = _renderers[i].color;
                    c.a = alpha;
                    _renderers[i].color = c;
                }

                yield return null;
            }
        }

        private void StopLowHealthBlink()
        {
            if (_lowHealthBlinkRoutine != null)
                StopCoroutine(_lowHealthBlinkRoutine);

            _lowHealthBlinkRoutine = null;
            _lowHealthBlinkActive = false;

            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] == null)
                        continue;

                    Color c = _renderers[i].color;
                    c.a = 1f;
                    _renderers[i].color = c;
                }
            }
        }

        #endregion

        private void OnDisable()
        {
            ForceStopSprint();
            CancelIceBallCastVisualState(restoreMovementAfterCancel: false);
        }

        private void OnDestroy()
        {
            ForceStopSprint();
            CancelIceBallCastVisualState(restoreMovementAfterCancel: false);
        }

        private bool IsSprintBlockingShieldUse()
        {
            return _sprintActive || _sprintArmed;
        }

        private bool IsShieldBlockingSprintUse()
        {
            return ShieldIsUp;
        }

        [SerializeField] private float stunRecoveryImmunity = 0.25f;
        private float _stunImmuneUntil = -999f;

        private void ApplyMovingPlatformIdleStick()
        {
            if (_isMoving || _isJumping)
                return;

            if (activesJumpCoroutine != null || activesMoveCoroutine != null)
                return;

            if (CurrentplatForm is not Assets.Scripts.Platforms.MovingVerticalPlatform mvp)
                return;

            if (!mvp.IsMovingDownNow)
                return;

            float surfaceY = GetMovingPlateSurfaceY();
            if (surfaceY == float.MinValue)
                return;

            if (rigidbody2 == null)
                return;

            Vector2 pos = rigidbody2.position;

            if (pos.y <= surfaceY + 0.001f)
                return;

            // Seat with MovePosition (not a direct position write + SyncTransforms). A teleport
            // here would reset interpolation on the same body the lift carries with MovePosition,
            // producing a one-frame render pop while descending. The lift's CarryRegisteredRiders
            // runs later this frame (DefaultExecutionOrder 50) and is authoritative; this is only
            // a consistent safety net if the warrior is briefly not yet a registered rider.
            pos.y = surfaceY;
            rigidbody2.MovePosition(pos);

            Vector2 v = rigidbody2.linearVelocity;
            if (v.y < 0f)
                v.y = 0f;

            rigidbody2.linearVelocity = v;
        }
    }
}
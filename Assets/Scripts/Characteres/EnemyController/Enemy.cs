using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using Assets.Scripts.Relics.Events;
using Assets.Scripts.Services;
using Assets.Scripts.Tools;
using Assets.Scripts.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    [RequireComponent(typeof(EnemyRangeService))]
    public class Enemy : CharacterController, IStepable, IAttacker
    {
        [Header("Attack Configuration")]
        [SerializeField] public float Range = 3f;
        [SerializeField] protected float attackCooldown = 1.5f;
        [SerializeField] protected int attackDamage = 10;

        [Header("Target & Movement")]
        public Transform target;
        public Transform groundCheckPoint;

        [Header("Colliders")]
        public BoxCollider2D TriggerColliderLeft;
        public BoxCollider2D TriggerColliderRight;
        public BoxCollider2D NormalCollider;

        [Header("Services")]
        [SerializeField] public EnemyRangeService EnemyRangeService;

        [Header("Health Bar Configuration")]
        [SerializeField] protected WorldSpaceHealthBar worldHealthBar;
        [SerializeField] protected GameObject healthBarPrefab;
        [SerializeField] protected bool autoCreateHealthBar = true;
        [SerializeField] protected Vector3 healthBarOffset = new Vector3(0, 1.5f, 0);

        [Header("Hit Effects")]
        [SerializeField] private float blinkDuration = 0.08f;
        [SerializeField] private int blinkCount = 3;

        [Header("Death Effects")]
        [SerializeField] private float deathDelay = 1.5f;
        [SerializeField] private bool hideOnDeath = true;

        [Header("Dissolve Death")]
        [SerializeField] private bool useDissolveOnDeath = true;
        [SerializeField] private Material dissolveMaterial;
        [SerializeField] private float dissolveDuration = 0.8f;

        private static readonly int DissolveAmountID = Shader.PropertyToID("_DissovleAmount");
        private static readonly int MainTexID = Shader.PropertyToID("_MainTex");

        private Material runtimeDissolveMat;
        private Color originalColor;
        private Coroutine blinkCoroutine;

        public float stepBackDistance = 0.5f;
        public float rayLength = 0.5f;
        public float xEdge;
        protected bool OddValue;
        protected bool StopMovingWhenWarriorDie = false;

        [Header("Death SFX")]
        [SerializeField] private AudioClip deathSfxClip;
        [SerializeField, Range(0f, 1f)] private float deathSfxVolume = 1f;
        [SerializeField] private Vector2 deathSfxPitchRange = new Vector2(0.95f, 1.05f);
        [SerializeField, Range(0f, 1f)] private float deathSfxSpatialBlend = 0f;
        [SerializeField] private float deathSfxMaxDistance = 20f;

        private bool _deathSfxPlayed;

        [Header("Stun")]
        [SerializeField] private bool canBeStunned = true;
        [SerializeField] protected float meleeHitDistance = 0.65f;

        [Header("Void Death Fallback")]
        [SerializeField] private bool useWorldYDeathFallback = true;
        [SerializeField] private float worldDeathY = -30f;

        [Header("Ground Bound (terrestrial enemies only)")]
        [Tooltip("ON = this enemy is permanently seated on the top surface of its current " +
                 "platform and can never become airborne (gravity, body collisions, external " +
                 "forces). Horizontal patrol and horizontal knockback are preserved. Set true in " +
                 "Start() by terrestrial enemies (Raka, CrawlingMonster, P39). Flying / jumping " +
                 "enemies must leave this false.")]
        [SerializeField] protected bool groundBound = false;

        // Last platform this ground-bound enemy was seated on. Cached so we can still detect when
        // the platform is genuinely gone (destroyed/disabled) and release the Y lock so normal
        // falling/death still work.
        private Collider2D _groundBoundPlatformCollider;

        private const float GroundBoundEdgeReleaseSkin = 0.1f;

        // Y-lock state. While a ground-bound enemy rests on a static platform we add a
        // FreezePositionY constraint so gravity / collisions / external forces cannot move it
        // vertically, while X stays fully free for patrol and knockback. We never touch X, so the
        // movement system is never fought. The base constraints are captured once and restored
        // when the lock is released (moving platform, platform gone, or death).
        [Tooltip("The support collider bottom must be within this distance of the platform top " +
                 "before the Y axis is frozen, so the enemy is never locked mid-air while still " +
                 "settling onto its platform.")]
        [SerializeField, Min(0f)] private float groundBoundSeatTolerance = 0.06f;

        private RigidbodyConstraints2D _groundBoundBaseConstraints;
        private bool _groundBoundConstraintsCaptured;
        private bool _groundBoundYLocked;

        private bool _isStunned;
        private Coroutine _stunRoutine;
        public bool IsStunned => _isStunned;

        public bool IsGroundedOnPlatform => CurrentplatForm != null;
        public Transform Transform => transform;
        public Animator Animator => animator;
        public AudioSource AudioSource => GetComponent<AudioSource>();
        public string Name => gameObject.name;

        public bool IsAttacked { get; set; } = false;

        [SerializeField] public string targetAnimationName = "AttackAnimation";

        [Header("Identity")]
        [SerializeField] private EnemyType enemyType;
        public EnemyType EnemyType => enemyType;

        [Header("Boss")]
        [SerializeField] private bool isBoss = false;
        [SerializeField] private string bossDisplayName = "";

        public bool IsBoss => isBoss;
        public string BossDisplayName => string.IsNullOrWhiteSpace(bossDisplayName) ? gameObject.name : bossDisplayName;

        /// <summary>Read-only access to current health (used by projectiles for half-health hits).</summary>
        public float CurrentHealth => currentHealth;

        /// <summary>
        /// True once an IceBulletProjectile has landed its first (half-health) hit on this enemy.
        /// The next IceBulletProjectile hit then executes the enemy outright. Bosses and Arachnee
        /// never use this mechanic. See <see cref="MarkIceBulletHit"/>.
        /// </summary>
        private bool _iceBulletMarked;
        public bool IsIceBulletMarked => _iceBulletMarked;
        public void MarkIceBulletHit() => _iceBulletMarked = true;

        public bool CountsForLevelClear
        {
            get
            {
                return enemyType != EnemyType.Bee
                    && enemyType != EnemyType.BeeEretic
                    && enemyType != EnemyType.Wraith;
            }
        }

        private AnimatorStateInfo stateInfo;
        private int lastFrameIndex = -1;
        protected AnimationClip currentClip;

        [SerializeField] public int totalFramesInAnimation = 16;
        public int frameIndex = -1;

        protected bool _deathStarted;
        private bool _isDead;

        public bool IsDeadOrDying => _deathStarted || _isDead;

        public static readonly List<Enemy> ActiveEnemies = new List<Enemy>();

        [SerializeField] protected float disableAttackWhenHitSeconds = 0.2f;
        protected float _attackDisabledUntil = -999f;

        public bool IsAttackTemporarilyDisabled => Time.time < _attackDisabledUntil;

        public virtual bool HardAnchorToMovingPlatforms => true;

        // Enemies are scripted movers: their MovePosition must never shove the Warrior via the
        // implicit velocity Unity derives from it. See CharacterController.RequestCoreMovePosition.
        protected override bool NeutralizeImplicitMoveVelocity => true;

        [SerializeField] protected EnemyHitReactionProfile hitReaction;
        public EnemyHitReactionProfile HitReaction => hitReaction;

        private EnemySpawnOverrides _spawnOverrides;
        protected EnemySpawnOverrides SpawnOverrides => _spawnOverrides;

        /// <summary>Read-only access to the spawn-point overrides stored on this enemy (set by
        /// EnemyMgr at spawn). Lets owned sub-components (e.g. a Bee's spark) read per-spawn-point
        /// values without going through ApplySpawnOverridesNow. Null if spawned without overrides.</summary>
        public EnemySpawnOverrides ActiveSpawnOverrides => _spawnOverrides;

        [Header("Moving Platform Patrol")]
        [SerializeField] protected float patrolEdgeArriveThreshold = 0.12f;

        protected bool _hasCommittedPatrolEdge;
        protected float _committedPatrolEdgeX;
        protected Collider2D _committedPatrolPlatform;

        [Header("Warrior Body Momentum Guard")]
        [Tooltip("ON = Warrior body contact cannot physically shove / drag this enemy through Rigidbody2D solver momentum. Scripted movement such as patrol, chase, jumps, SmoothStepBack, stun, death, and platform carry is preserved.")]
        [SerializeField] private bool preventWarriorBodyMomentumPush = true;

        [Tooltip("How strongly the guard restores unwanted body-push drift. 1 = cancel all excess drift, lower values soften the correction.")]
        [SerializeField, Range(0f, 1f)] private float warriorBodyPushRestoreStrength = 1f;

        [Tooltip("Small drift allowed during a Warrior body contact before correction starts. Keep this slightly above your physics skin.")]
        [SerializeField, Min(0f)] private float maxAllowedBodyPushDistance = 0.03f;

        [Tooltip("How long a Warrior body contact is considered active after the latest collision callback.")]
        [SerializeField, Min(0.02f)] private float warriorBodyPushContactMemory = 0.10f;

        [Tooltip("Maximum position correction applied in one physics callback. Prevents visible teleports if something extreme happens.")]
        [SerializeField, Min(0.01f)] private float warriorBodyPushMaxCorrectionPerStep = 0.20f;

        [Tooltip("ON = cancel horizontal shove/drag caused by Warrior body contact.")]
        [SerializeField] private bool preventWarriorBodyHorizontalPush = true;

        [Tooltip("ON = cancel unwanted vertical lifting caused by Warrior body contact. Automatically skipped on moving/rotating platforms and while jumping.")]
        [SerializeField] private bool preventWarriorBodyVerticalLift = true;

        [Tooltip("Extra vertical tolerance before treating upward drift as Warrior lifting the enemy.")]
        [SerializeField, Min(0f)] private float warriorBodyLiftAllowance = 0.04f;

        [Tooltip("ON = clears enemy Rigidbody2D velocity along the Warrior push direction while body contact is active.")]
        [SerializeField] private bool cancelWarriorBodyPushVelocity = true;

        private Vector2 _warriorBodyPushPhysicsStepStart;
        private bool _hasWarriorBodyPushPhysicsStepStart;

        private Vector2 _warriorBodyPushSafePosition;
        private bool _hasWarriorBodyPushSafePosition;

        private float _lastWarriorBodyContactTime = -999f;
        private int _lastWarriorBodyContactFrame = -999;
        private Warrior _lastWarriorBodyContactWarrior;
        private Vector2 _lastWarriorBodyPushAxis = Vector2.right;
        private float _ignoreWarriorBodyPushCorrectionUntil = -999f;
        private int _ignoreWarriorBodyPushCorrectionFrame = -999;

        public EnemySpawnPoint OwnerSpawnPoint { get; private set; }

        public void SetEnemyType(EnemyType type)
        {
            enemyType = type;
        }

        public void SetBoss(bool value, string displayName = null)
        {
            isBoss = value;

            if (!string.IsNullOrWhiteSpace(displayName))
                bossDisplayName = displayName;
        }

        public void SetOwnerSpawnPoint(EnemySpawnPoint owner)
        {
            OwnerSpawnPoint = owner;
        }

        public virtual void DisableAttackTemporarily(float seconds = -1f)
        {
            float d = (seconds > 0f) ? seconds : disableAttackWhenHitSeconds;
            _attackDisabledUntil = Mathf.Max(_attackDisabledUntil, Time.time + d);
        }

        protected virtual void FixedUpdate()
        {
            CaptureWarriorBodyPushPhysicsStepStart();

            if (groundCheckPoint != null)
            {
                RaycastHit2D hit = Physics2D.Raycast(
                    groundCheckPoint.position,
                    Vector2.down,
                    rayLength,
                    PlatformLayer
                );

                if (hit.collider != null)
                {
                    var platform = hit.collider.GetComponent<PlatFormPlfColliderTrigger>();
                    if (platform != null)
                        CurrentplatForm = platform;
                }
                else
                {
                    if (CurrentplatForm != null)
                        CurrentplatForm = null;
                }
            }

            PreventUnwantedWarriorBodyPush(null);
            RefreshWarriorBodyPushSafePosition();

            // Keep ground-bound enemies glued to their platform by freezing the Rigidbody2D Y axis
            // while resting on a static platform, so gravity / external forces cannot lift them.
            ClampGroundBoundToSurface();
        }

        protected override void Start()
        {
            base.Start();

            OddValue = initDirection();
            currentClip = GetAnimationClip(targetAnimationName);

            if (spriteRenderer != null)
                originalColor = spriteRenderer.color;
            else
                Debug.LogError($"{gameObject.name}: No SpriteRenderer found!");

            if (target == null)
            {
                GameObject warrior = GameObject.Find("Warrior");
                if (warrior != null)
                    target = warrior.transform;
            }

            if (EnemyRangeService == null)
            {
                EnemyRangeService = GetComponent<EnemyRangeService>();

                if (EnemyRangeService == null)
                    EnemyRangeService = gameObject.AddComponent<EnemyRangeService>();
            }

            if (EnemyRangeService != null)
            {
                EnemyRangeService.Initialize(this);
                ConfigureAttack();
            }

            InitializeHealthBar();
            ApplySpawnOverridesNow();
        }

        protected virtual void InitializeHealthBar()
        {
            if (worldHealthBar == null)
                worldHealthBar = GetComponentInChildren<WorldSpaceHealthBar>();

            if (worldHealthBar == null)
            {
                if (autoCreateHealthBar)
                {
                    worldHealthBar = CreateHealthBar();
                }
                else
                {
                    Debug.LogWarning($"{gameObject.name}: No health bar assigned and autoCreateHealthBar is false.");
                    return;
                }
            }

            if (worldHealthBar != null)
            {
                worldHealthBar.SetTarget(this.transform);
                worldHealthBar.SetOffset(healthBarOffset);
                worldHealthBar.ForceUpdate(currentHealth, maxHealth);
            }
        }

        protected virtual WorldSpaceHealthBar CreateHealthBar()
        {
            WorldSpaceHealthBar healthBar;

            if (healthBarPrefab != null)
            {
                healthBar = HealthBarFactory.CreateHealthBarFromPrefab(healthBarPrefab, transform, healthBarOffset);
                Debug.Log($"{gameObject.name}: Health bar created from prefab");
            }
            else
            {
                healthBar = HealthBarFactory.CreateHealthBar(transform, healthBarOffset);
                Debug.Log($"{gameObject.name}: Default health bar created");
            }

            return healthBar;
        }

        protected virtual void Update()
        {
            CheckWorldYDeathFallback();

            if (UsesCommittedPatrolEdge)
                CommitPatrolEdgeForMovingVerticalPlatform();

            if (groundCheckPoint != null)
            {
                RaycastHit2D hit = Physics2D.Raycast(
                    groundCheckPoint.position,
                    Vector2.down,
                    rayLength,
                    PlatformLayer
                );

                if (!hit.collider && CurrentplatForm != null)
                {
                    StopMoveTowardCoroutine();

                    Vector3 safePos = transform.position;
                    safePos.x = ClampToCurrentPlatform(safePos.x);
                    transform.position = safePos;

                    if (CurrentplatForm is MovingVerticalPlatform)
                        if (UsesCommittedPatrolEdge)
                            CommitPatrolEdgeForMovingVerticalPlatform();
                        else
                            xEdge = GetOppositeEdgeX();

                    if (IsGroundedOnPlatform)
                        StickToPlatform();
                }
            }

            if (animator == null) return;

            stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            if (stateInfo.IsName(targetAnimationName))
            {
                frameIndex = GetCurrentFrameIndex();

                if (frameIndex != lastFrameIndex && frameIndex >= 0)
                    lastFrameIndex = frameIndex;
            }
        }

        /// <summary>
        /// Keeps a terrestrial enemy (<see cref="groundBound"/> = true) glued to its platform
        /// surface by freezing the Rigidbody2D Y axis while it rests on a static platform. With Y
        /// frozen, gravity, collision impulses and external forces (AddForce, Warrior body contact)
        /// cannot move it vertically, so it can never become airborne -- while X stays completely
        /// free, so patrol and horizontal knockback (SmoothStepBack) are untouched. We never write
        /// position or velocity here, so the movement system is never fought (that was the cause of
        /// the earlier freeze / lost-knockback regressions).
        ///
        /// The Y lock is released so normal physics resumes when: the enemy is on a moving/rotating
        /// platform (its carry owns Y), its platform is gone (destroyed/disabled or it has drifted
        /// off the span -> it must be able to fall and trigger the void-death fallback), or it dies.
        /// It is only engaged once the enemy is actually resting on the surface, never mid-air.
        ///
        /// Driven once per physics step from FixedUpdate.
        /// </summary>
        protected void ClampGroundBoundToSurface()
        {
            if (!groundBound || rigidbody2 == null) return;

            if (!_groundBoundConstraintsCaptured)
            {
                _groundBoundBaseConstraints = rigidbody2.constraints;
                _groundBoundConstraintsCaptured = true;
            }

            if (IsDeadOrDying)
            {
                SetGroundBoundYLock(false);
                return;
            }

            // Cache the live platform so we can still tell when it is genuinely gone.
            if (CurrentplatForm != null && CurrentplatForm.platformCollider != null)
                _groundBoundPlatformCollider = CurrentplatForm.platformCollider;

            Collider2D platformCol = _groundBoundPlatformCollider;

            // Platform genuinely gone -> release so the enemy can fall / die normally.
            if (platformCol == null || !platformCol.enabled || !platformCol.gameObject.activeInHierarchy)
            {
                _groundBoundPlatformCollider = null;
                SetGroundBoundYLock(false);
                return;
            }

            // Moving / rotating platforms own the vertical carry -> never freeze Y there.
            if (IsOnMovingOrRotatingPlatform())
            {
                SetGroundBoundYLock(false);
                return;
            }

            Collider2D support = (NormalCollider != null && NormalCollider.enabled)
                ? NormalCollider
                : collider2;

            if (support == null) return;

            Bounds pb = platformCol.bounds;

            // No live ground contact and no longer horizontally over the cached platform ->
            // the enemy has truly left it; release so it falls instead of hanging in mid-air.
            if (CurrentplatForm == null)
            {
                float cx = support.bounds.center.x;
                if (cx < pb.min.x - GroundBoundEdgeReleaseSkin ||
                    cx > pb.max.x + GroundBoundEdgeReleaseSkin)
                {
                    _groundBoundPlatformCollider = null;
                    SetGroundBoundYLock(false);
                    return;
                }
            }

            // Only engage the lock once the support collider is actually resting on the surface, so
            // we never freeze the enemy mid-air (e.g. while it is still settling after spawn). Once
            // locked, forces can no longer lift it, so it stays resting and stays locked.
            bool restingOnSurface = support.bounds.min.y <= pb.max.y + groundBoundSeatTolerance;
            if (restingOnSurface)
                SetGroundBoundYLock(true);
        }

        private void SetGroundBoundYLock(bool locked)
        {
            if (rigidbody2 == null) return;
            if (locked == _groundBoundYLocked) return;

            _groundBoundYLocked = locked;

            if (locked)
                rigidbody2.constraints = _groundBoundBaseConstraints | RigidbodyConstraints2D.FreezePositionY;
            else if (_groundBoundConstraintsCaptured)
                rigidbody2.constraints = _groundBoundBaseConstraints;
        }

        protected void ClampEnemyToPlatformTop()
        {
            if (CurrentplatForm == null) return;
            if (collider2 == null) return;

            Bounds pb = CurrentplatForm.platformCollider.bounds;
            Bounds eb = collider2.bounds;

            Vector3 pos = transform.position;
            pos.y = pb.max.y + eb.extents.y;

            transform.position = pos;
        }

        protected virtual void ConfigureAttack()
        {
            if (EnemyRangeService != null)
            {
                EnemyRangeService.SetAttackRange(Range);
                EnemyRangeService.SetAttackCooldown(attackCooldown);
                EnemyRangeService.SetAttackDamage(attackDamage);
            }
        }

        public virtual void PlayAttackAnimation()
        {
            AttackAnimationDisplay();
        }

        public override void StopMoveTowardCoroutine()
        {
            base.StopMoveTowardCoroutine();
        }

        public void OnRangeExecuted(Transform target, int damage)
        {
        }

        public virtual void OnAttackPerformed(IAttacker attacker, Transform attackedTarget)
        {
            if (!CanStartAttackNow())
                return;

            Transform t = attackedTarget != null ? attackedTarget : target;

            if (IsWarriorInFront(t))
                AttackAnimationDisplay();
        }

        public virtual bool TakeDamageAndReturnKilled(float damage)
        {
            if (_isDead) return false;
            if (_deathStarted) return false;
            if (damage <= 0f) return false;

            currentHealth -= damage;
            currentHealth = Mathf.Max(0, currentHealth);

            UpdateHealthBarDisplay();

            if (blinkCoroutine != null)
                StopCoroutine(blinkCoroutine);

            blinkCoroutine = StartCoroutine(BlinkOnHit());

            bool killed = currentHealth <= 0f;

            OnDamaged(damage, killed);

            if (killed)
            {
                _isDead = true;
                OnDeath();
                return true;
            }

            return false;
        }

        public override void TakeDamage(float damage)
        {
            TakeDamageAndReturnKilled(damage);
        }

        protected virtual void UpdateHealthBarDisplay()
        {
            if (worldHealthBar != null)
                worldHealthBar.UpdateHealth(currentHealth, maxHealth);
        }

        /// <summary>Show/hide the world-space health bar. Used by bosses that briefly vanish
        /// (e.g. Zort's blinks/teleports) so the bar disappears with the sprite. Null-safe.</summary>
        protected void SetHealthBarVisible(bool visible)
        {
            if (worldHealthBar != null)
                worldHealthBar.SetVisibility(visible);
        }

        protected virtual void OnDeath()
        {
            if (_deathStarted) return;

            _deathStarted = true;

            Debug.Log($"[Enemy] OnDeath started: {name} | boss={IsBoss}");

            OwnerSpawnPoint?.NotifyEnemyDefeated(this);
            EnemyMgr.Instance?.OnEnemyDeathStarted(this);

            if (!_deathSfxPlayed)
            {
                _deathSfxPlayed = true;
                OneShotAudio.Play(
                    deathSfxClip,
                    transform.position,
                    deathSfxVolume,
                    deathSfxPitchRange,
                    deathSfxSpatialBlend,
                    deathSfxMaxDistance
                );
            }

            if (worldHealthBar != null)
                worldHealthBar.SetVisibility(false);

            var w = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (w != null)
            {
                var hub = w.GetComponent<PlayerEventHub>();
                if (hub != null)
                {
                    hub.RaiseKill(new KillEvent
                    {
                        killer = w.gameObject,
                        victim = gameObject
                    });
                }
            }

            StartCoroutine(DeathSequence());
        }

        private IEnumerator DeathSequence()
        {
            if (rigidbody2 != null)
            {
                rigidbody2.linearVelocity = Vector2.zero;
                rigidbody2.simulated = false;
            }

            if (NormalCollider != null) NormalCollider.enabled = false;
            if (TriggerColliderLeft != null) TriggerColliderLeft.enabled = false;
            if (TriggerColliderRight != null) TriggerColliderRight.enabled = false;

            if (animator != null)
                animator.enabled = false;

            if (useDissolveOnDeath && spriteRenderer != null && dissolveMaterial != null)
            {
                yield return StartCoroutine(PlayDissolve());
            }
            else
            {
                if (hideOnDeath && spriteRenderer != null)
                    spriteRenderer.enabled = false;
            }

            yield return new WaitForSeconds(deathDelay);

            EnemyMgr.Instance?.OnEnemyDestroyed(this);
            Destroy(gameObject);
        }

        private IEnumerator PlayDissolve()
        {
            if (spriteRenderer == null || dissolveMaterial == null)
                yield break;

            if (runtimeDissolveMat == null)
                runtimeDissolveMat = new Material(dissolveMaterial);

            if (spriteRenderer.sprite != null)
                runtimeDissolveMat.SetTexture(MainTexID, spriteRenderer.sprite.texture);

            spriteRenderer.material = runtimeDissolveMat;
            runtimeDissolveMat.SetFloat(DissolveAmountID, 0f);

            float t = 0f;
            while (t < dissolveDuration)
            {
                t += Time.deltaTime;
                float v = Mathf.Clamp01(t / dissolveDuration);
                runtimeDissolveMat.SetFloat(DissolveAmountID, v);
                yield return null;
            }

            runtimeDissolveMat.SetFloat(DissolveAmountID, 1f);

            if (hideOnDeath)
                spriteRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (blinkCoroutine != null)
                StopCoroutine(blinkCoroutine);

            if (worldHealthBar != null && worldHealthBar.gameObject != null)
                Destroy(worldHealthBar.gameObject);

            if (runtimeDissolveMat != null)
            {
                Destroy(runtimeDissolveMat);
                runtimeDissolveMat = null;
            }
        }

        private IEnumerator BlinkOnHit()
        {
            if (spriteRenderer == null)
            {
                Debug.LogError($"{gameObject.name}: SpriteRenderer is null in BlinkOnHit!");
                yield break;
            }

            for (int i = 0; i < blinkCount; i++)
            {
                spriteRenderer.color = Color.black;
                yield return new WaitForSeconds(blinkDuration);

                spriteRenderer.color = Color.white;
                yield return new WaitForSeconds(blinkDuration);
            }

            spriteRenderer.color = Color.white;
            blinkCoroutine = null;
        }

        private bool initDirection()
        {
            int val = Random.Range(1, 10);
            return val % 2 == 0;
        }

        float GetOppositeEdgeX()
        {
            if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
                return transform.position.x;

            Bounds platformBounds = CurrentplatForm.platformCollider.bounds;
            float distanceToLeftEdge = Mathf.Abs(groundCheckPoint.position.x - platformBounds.min.x);
            float distanceToRightEdge = Mathf.Abs(groundCheckPoint.position.x - platformBounds.max.x);

            if (distanceToRightEdge < distanceToLeftEdge)
                return platformBounds.min.x;
            else
                return platformBounds.max.x;
        }

        #region Warrior Body Momentum Guard

        private void CaptureWarriorBodyPushPhysicsStepStart()
        {
            _warriorBodyPushPhysicsStepStart = GetEnemyPhysicsPosition();
            _hasWarriorBodyPushPhysicsStepStart = true;
        }

        private void RefreshWarriorBodyPushSafePosition()
        {
            if (!preventWarriorBodyMomentumPush)
                return;

            if (HasRecentWarriorBodyContact())
                return;

            _warriorBodyPushSafePosition = GetEnemyPhysicsPosition();
            _hasWarriorBodyPushSafePosition = true;
            _lastWarriorBodyContactWarrior = null;
        }

        protected virtual void OnCollisionEnter2D(Collision2D collision)
        {
            TryRegisterWarriorBodyMomentumContact(collision);
        }

        protected virtual void OnCollisionStay2D(Collision2D collision)
        {
            TryRegisterWarriorBodyMomentumContact(collision);
        }

        protected virtual void OnCollisionExit2D(Collision2D collision)
        {
            Warrior warrior = GetWarriorFromCollision(collision);

            if (warrior == null)
                return;

            if (_lastWarriorBodyContactWarrior == warrior)
            {
                _lastWarriorBodyContactTime = Time.time;
                _lastWarriorBodyContactFrame = Time.frameCount;
            }
        }

        private void TryRegisterWarriorBodyMomentumContact(Collision2D collision)
        {
            if (!preventWarriorBodyMomentumPush)
                return;

            Warrior warrior = GetWarriorFromCollision(collision);

            if (warrior == null)
                return;

            Collider2D warriorCollider = collision != null ? collision.collider : null;

            if (!IsWarriorBodyMomentumCollider(warrior, warriorCollider))
                return;

            _lastWarriorBodyContactWarrior = warrior;
            _lastWarriorBodyContactTime = Time.time;
            _lastWarriorBodyContactFrame = Time.frameCount;
            _lastWarriorBodyPushAxis = GetWarriorBodyPushAxis(warrior);

            if (!_hasWarriorBodyPushSafePosition)
            {
                _warriorBodyPushSafePosition = _hasWarriorBodyPushPhysicsStepStart
                    ? _warriorBodyPushPhysicsStepStart
                    : GetEnemyPhysicsPosition();

                _hasWarriorBodyPushSafePosition = true;
            }

            PreventUnwantedWarriorBodyPush(collision);
        }

        private Warrior GetWarriorFromCollision(Collision2D collision)
        {
            if (collision == null || collision.collider == null)
                return null;

            return collision.collider.GetComponentInParent<Warrior>();
        }

        private bool IsWarriorBodyMomentumCollider(Warrior warrior, Collider2D warriorCollider)
        {
            if (warrior == null || warriorCollider == null)
                return false;

            if (warriorCollider.isTrigger)
                return false;

            int shieldLaserLayer = LayerMask.NameToLayer("Shield Laser");

            if (shieldLaserLayer >= 0 && warriorCollider.gameObject.layer == shieldLaserLayer)
                return false;

            return true;
        }

        private Vector2 GetEnemyPhysicsPosition()
        {
            if (rigidbody2 != null)
                return rigidbody2.position;

            return transform.position;
        }

        private Vector2 GetEnemyBodyCenter()
        {
            Collider2D body = NormalCollider != null && NormalCollider.enabled
                ? NormalCollider
                : collider2;

            if (body != null)
                return body.bounds.center;

            return transform.position;
        }

        private Vector2 GetWarriorBodyCenter(Warrior warrior)
        {
            if (warrior != null && warrior.collider2 != null)
                return warrior.collider2.bounds.center;

            return warrior != null ? (Vector2)warrior.transform.position : Vector2.zero;
        }

        private Vector2 GetWarriorBodyPushAxis(Warrior warrior)
        {
            Vector2 myCenter = GetEnemyBodyCenter();
            Vector2 warriorCenter = GetWarriorBodyCenter(warrior);

            float dx = myCenter.x - warriorCenter.x;

            if (Mathf.Abs(dx) < 0.001f)
                dx = transform.position.x - warrior.transform.position.x;

            if (Mathf.Abs(dx) < 0.001f)
                dx = transform.localScale.x >= 0f ? 1f : -1f;

            return new Vector2(Mathf.Sign(dx), 0f);
        }

        private bool HasRecentWarriorBodyContact()
        {
            if (_lastWarriorBodyContactWarrior == null)
                return false;

            if (Time.frameCount == _lastWarriorBodyContactFrame)
                return true;

            return Time.time - _lastWarriorBodyContactTime <= warriorBodyPushContactMemory;
        }

        private bool IsIgnoringWarriorBodyPushPositionCorrection()
        {
            if (Time.frameCount <= _ignoreWarriorBodyPushCorrectionFrame)
                return true;

            return Time.time <= _ignoreWarriorBodyPushCorrectionUntil;
        }

        /// <summary>
        /// Call this from intentional scripted displacement, for example custom recoil,
        /// custom dash, boss reposition, or child-specific movement that should not be
        /// interpreted as Warrior body momentum.
        /// </summary>
        protected void MarkIntentionalEnemyDisplacement(float protectSeconds = 0.08f)
        {
            _ignoreWarriorBodyPushCorrectionUntil =
                Mathf.Max(_ignoreWarriorBodyPushCorrectionUntil, Time.time + protectSeconds);

            _ignoreWarriorBodyPushCorrectionFrame =
                Mathf.Max(_ignoreWarriorBodyPushCorrectionFrame, Time.frameCount + 1);
        }

        private bool CanCorrectWarriorBodyPushPosition()
        {
            if (!preventWarriorBodyMomentumPush)
                return false;

            if (!HasRecentWarriorBodyContact())
                return false;

            if (_deathStarted || _isDead || currentHealth <= 0f)
                return false;

            if (IsIgnoringWarriorBodyPushPositionCorrection())
                return false;

            // Do not fight active scripted movement. We still cancel Rigidbody2D push
            // velocity below, but we do not teleport/rollback while AI or pathfinding
            // is actively moving the enemy.
            if (activesMoveCoroutine != null)
                return false;

            if (activesJumpCoroutine != null || _isJumping)
                return false;

            return true;
        }

        private void PreventUnwantedWarriorBodyPush(Collision2D collision)
        {
            if (!preventWarriorBodyMomentumPush)
                return;

            if (!HasRecentWarriorBodyContact())
                return;

            CancelWarriorBodyMomentumVelocity();

            if (!CanCorrectWarriorBodyPushPosition())
                return;

            Vector2 currentPosition = GetEnemyPhysicsPosition();

            Vector2 referencePosition = _hasWarriorBodyPushPhysicsStepStart
                ? _warriorBodyPushPhysicsStepStart
                : (_hasWarriorBodyPushSafePosition ? _warriorBodyPushSafePosition : currentPosition);

            Vector2 delta = currentPosition - referencePosition;
            Vector2 correction = Vector2.zero;

            if (preventWarriorBodyHorizontalPush)
            {
                float pushSign = Mathf.Sign(_lastWarriorBodyPushAxis.x);

                if (Mathf.Abs(pushSign) < 0.001f)
                    pushSign = 1f;

                float pushedAwayAmount = delta.x * pushSign;

                if (pushedAwayAmount > maxAllowedBodyPushDistance)
                {
                    float excess = pushedAwayAmount - maxAllowedBodyPushDistance;
                    float amount = Mathf.Min(
                        excess * warriorBodyPushRestoreStrength,
                        warriorBodyPushMaxCorrectionPerStep
                    );

                    correction.x = -pushSign * amount;
                }
            }

            if (ShouldCorrectWarriorBodyVerticalLift(delta))
            {
                float allowedLift = maxAllowedBodyPushDistance + warriorBodyLiftAllowance;
                float liftedAmount = delta.y - allowedLift;

                if (liftedAmount > 0f)
                {
                    float amount = Mathf.Min(
                        liftedAmount * warriorBodyPushRestoreStrength,
                        warriorBodyPushMaxCorrectionPerStep
                    );

                    correction.y = -amount;
                }
            }

            if (correction.sqrMagnitude <= 0.0000001f)
                return;

            Vector2 correctedPosition = currentPosition + correction;
            correctedPosition = ClampWarriorBodyPushCorrectionToPlatform(correctedPosition, currentPosition);

            ApplyWarriorBodyPushCorrectedPosition(correctedPosition);
        }

        private bool ShouldCorrectWarriorBodyVerticalLift(Vector2 delta)
        {
            if (!preventWarriorBodyVerticalLift)
                return false;

            if (delta.y <= maxAllowedBodyPushDistance + warriorBodyLiftAllowance)
                return false;

            if (activesJumpCoroutine != null || _isJumping)
                return false;

            // Moving/rotating platforms own vertical seating/carry. Do not fight them.
            if (IsOnMovingOrRotatingPlatform())
                return false;

            if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
                return false;

            return true;
        }

        private bool IsOnMovingOrRotatingPlatform()
        {
            if (CurrentplatForm == null)
                return false;

            string typeName = CurrentplatForm.GetType().Name;

            return typeName == "MovingVerticalPlatform"
                || typeName == "MovingHorizontalPlatform"
                || typeName == "RotatingPlatform";
        }

        private Vector2 ClampWarriorBodyPushCorrectionToPlatform(Vector2 correctedPosition, Vector2 currentPosition)
        {
            if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
                return correctedPosition;

            Bounds pb = CurrentplatForm.platformCollider.bounds;

            correctedPosition.x = Mathf.Clamp(correctedPosition.x, pb.min.x, pb.max.x);

            // Never correct downward through the platform surface.
            Collider2D body = NormalCollider != null && NormalCollider.enabled
                ? NormalCollider
                : collider2;

            if (body != null)
            {
                float bottomOffsetFromBody = body.bounds.min.y - transform.position.y;
                float minY = pb.max.y + 0.01f - bottomOffsetFromBody;

                if (correctedPosition.y < minY)
                    correctedPosition.y = Mathf.Min(currentPosition.y, minY);
            }

            return correctedPosition;
        }

        private void ApplyWarriorBodyPushCorrectedPosition(Vector2 correctedPosition)
        {
            if (rigidbody2 != null)
            {
                rigidbody2.position = correctedPosition;
                rigidbody2.angularVelocity = 0f;
                rigidbody2.WakeUp();
            }
            else
            {
                Vector3 p = transform.position;
                p.x = correctedPosition.x;
                p.y = correctedPosition.y;
                transform.position = p;
            }
        }

        private void CancelWarriorBodyMomentumVelocity()
        {
            if (!cancelWarriorBodyPushVelocity)
                return;

            if (rigidbody2 == null)
                return;

            Vector2 v = rigidbody2.linearVelocity;

            if (preventWarriorBodyHorizontalPush)
            {
                float pushSign = Mathf.Sign(_lastWarriorBodyPushAxis.x);

                if (Mathf.Abs(pushSign) > 0.001f)
                {
                    float velocityAwayFromWarrior = v.x * pushSign;

                    if (velocityAwayFromWarrior > 0f)
                        v.x -= velocityAwayFromWarrior * pushSign;
                }
            }

            if (preventWarriorBodyVerticalLift && !IsOnMovingOrRotatingPlatform())
            {
                if (v.y > 0f && !_isJumping && activesJumpCoroutine == null)
                    v.y = 0f;
            }

            rigidbody2.linearVelocity = v;
            rigidbody2.angularVelocity = 0f;
        }

        #endregion

        #region Trigger Colliders for Warrior Overlap Resolution
        protected virtual void OnTriggerEnter2D(Collider2D collision)
        {
            if (collision.gameObject.name == "Warrior")
            {
                if (CurrentplatForm != null)
                {
                    var w = GameMgr.Instance.WarriorInstance;
                    if (w != null &&
                        !w.collider2.IsTouching(CurrentplatForm?.platformCollider) &&
                        w.activesJumpCoroutine != null)
                    {
                        if (!w.DescendentPhase)
                            Physics2D.IgnoreCollision(w.collider2, NormalCollider, true);
                    }
                }
            }
        }

        protected virtual void OnTriggerExit2D(Collider2D collision)
        {
            if (collision.gameObject.name == "Warrior")
            {
                var w = GameMgr.Instance.WarriorInstance;
                if (w == null) return;

                w.CanMove = true;
                Physics2D.IgnoreCollision(w.collider2, NormalCollider, false);
            }
        }

        protected virtual void OnTriggerStay2D(Collider2D collision)
        {
            if (collision.gameObject.name == "Warrior")
            {
                var w = GameMgr.Instance.WarriorInstance;
                if (w == null) return;

                if (w.activesJumpCoroutine == null && !w.DescendentPhase)
                {
                    bool tmin = w.GoRight &&
                                collision.bounds.max.x > NormalCollider.bounds.min.x &&
                                collision.bounds.max.x.GetDistanceXAxis(NormalCollider.bounds.min.x) >= 0.2f;

                    bool tmax = w.GoLeft &&
                                collision.bounds.min.x < NormalCollider.bounds.max.x &&
                                collision.bounds.min.x.GetDistanceXAxis(NormalCollider.bounds.max.x) >= 0.2f;

                    if (tmin)
                    {
                        w.CanMove = false;
                        w.ResolveOverlap(collision, TriggerColliderLeft);
                    }
                    else if (tmax)
                    {
                        w.CanMove = false;
                        w.ResolveOverlap(collision, TriggerColliderRight);
                    }
                }
            }
        }
        #endregion

        public virtual IEnumerator SmoothStepBack(bool positif)
        {
            var platformSnapshot = CurrentplatForm; // snapshot au début

            if (!CanStepBack(positif))
                yield break;

            Vector3 startPos = transform.position;
            Vector3 targetPos = startPos;

            if (positif)
                targetPos.x += stepBackDistance;
            else
                targetPos.x -= stepBackDistance;

            float clampedX = ClampToCurrentPlatform(targetPos.x);

            if (Mathf.Abs(clampedX - startPos.x) < 0.05f)
            {
                Debug.Log($"{gameObject.name}: Cannot step back - at platform edge!");
                yield break;
            }

            targetPos.x = clampedX;

            float elapsed = 0f;
            float duration = 0.1f;

            while (elapsed < duration)
            {
                if (this == null) yield break;

                Vector3 newPos = Vector3.Lerp(startPos, targetPos, elapsed / duration);
                newPos.x = ClampToCurrentPlatform(newPos.x);
                newPos = ResolveStepBackPositionOnMovingVerticalPlatform(newPos);
                MoveStepBackBody(newPos);

                elapsed += Time.fixedDeltaTime; //<- cohérent avec FixedUpdate
                yield return new WaitForFixedUpdate(); //<- synchronisé avec le lift
            }

            targetPos.x = ClampToCurrentPlatform(targetPos.x);
            targetPos = ResolveStepBackPositionOnMovingVerticalPlatform(targetPos);
            MoveStepBackBody(targetPos);
        }

        private void MoveStepBackBody(Vector3 position)
        {
            // SmoothStepBack is an intentional gameplay reaction. The Warrior body
            // momentum guard must never rollback this movement.
            MarkIntentionalEnemyDisplacement(0.12f);

            if (rigidbody2 != null)
                rigidbody2.MovePosition(position);
            else
                transform.position = position;
        }

        private Vector3 ResolveStepBackPositionOnMovingVerticalPlatform(Vector3 desiredPosition)
        {
            if (CurrentplatForm is not MovingVerticalPlatform movingPlatform)
                return desiredPosition;

            if (movingPlatform.platformCollider == null)
                return desiredPosition;

            Collider2D support = NormalCollider != null && NormalCollider.enabled
                ? NormalCollider
                : collider2;

            if (support == null)
                return desiredPosition;

            Bounds platformBounds = movingPlatform.platformCollider.bounds;
            Bounds supportBounds = support.bounds;

            bool horizontallyOverLift =
                supportBounds.max.x > platformBounds.min.x + 0.03f &&
                supportBounds.min.x < platformBounds.max.x - 0.03f;

            if (!horizontallyOverLift)
                return desiredPosition;

            // Keep Y seated on the lift during hit step-back. The old implementation
            // wrote transform.position with a fixed start Y; when the lift moved upward
            // during the 0.1s step-back, that fixed Y pushed the enemy down into/through
            // the platform. Preserve only the horizontal knockback and let the lift own Y.
            float bottomOffsetFromTransform = supportBounds.min.y - transform.position.y;
            desiredPosition.y = platformBounds.max.y + 0.02f - bottomOffsetFromTransform;

            return desiredPosition;
        }

        public virtual bool CanStepBack(bool positif)
        {
            if (CurrentplatForm == null)
                return false;

            float targetX = transform.position.x + (positif ? stepBackDistance : -stepBackDistance);
            return CanMoveToPosition(targetX);
        }

        public virtual float StepRightMaxamize() => NormalCollider.bounds.max.x;
        public virtual float StepLeftMaxamize() => NormalCollider.bounds.min.x;

        float tolerance = 1f;

        public bool IsGroundPointCloserToEdge()
        {
            return Mathf.Abs(groundCheckPoint.position.x - xEdge) < tolerance;
        }

        public virtual void OnWarriorDetectedInLaser() { }
        public virtual void OnWarriorLeftLaser() { }
        public virtual void EnableRunningMovementAfterAttack() { }
        public virtual void OnLaserDeactivated() { }

        #region Animation Methods
        int GetCurrentFrameIndex()
        {
            if (currentClip != null)
            {
                float normalizedTime = stateInfo.normalizedTime % 1f;
                float clipLength = currentClip.length;
                float frameRate = currentClip.frameRate;
                int totalFrames = Mathf.CeilToInt(clipLength * frameRate);
                int frameIndex = Mathf.FloorToInt(normalizedTime * totalFrames);
                return Mathf.Clamp(frameIndex, 0, totalFrames - 1);
            }
            else
            {
                return GetCurrentFrameIndexManual();
            }
        }

        int GetCurrentFrameIndexManual()
        {
            float normalizedTime = stateInfo.normalizedTime % 1f;
            int frameIndex = Mathf.FloorToInt(normalizedTime * totalFramesInAnimation);
            return Mathf.Clamp(frameIndex, 0, totalFramesInAnimation - 1);
        }

        AnimationClip GetAnimationClip(string clipName)
        {
            if (animator == null) return null;

            RuntimeAnimatorController ac = animator.runtimeAnimatorController;
            if (ac == null) return null;

            AnimationClip[] clips = ac.animationClips;

            foreach (AnimationClip clip in clips)
            {
                if (clip.name == clipName)
                    return clip;
            }

            return null;
        }
        #endregion

        protected float GetTargetCenterX(Transform t)
        {
            if (t == null) return float.NaN;

            var cols = t.GetComponentsInChildren<Collider2D>();
            if (cols != null && cols.Length > 0)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null && cols[i].enabled && !cols[i].isTrigger)
                        return cols[i].bounds.center.x;
                }

                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null && cols[i].enabled)
                        return cols[i].bounds.center.x;
                }
            }

            return t.position.x;
        }

        protected float GetMyCenterX()
        {
            if (NormalCollider != null && NormalCollider.enabled)
                return NormalCollider.bounds.center.x;

            var c = GetComponent<Collider2D>();
            if (c != null) return c.bounds.center.x;

            return transform.position.x;
        }

        protected void RefreshFacingFlags()
        {
            if (Front != null && Back != null)
            {
                leftFacing = Front.position.x < Back.position.x;
                rightFacing = Front.position.x > Back.position.x;
            }
            else
            {
                rightFacing = transform.localScale.x >= 0f;
                leftFacing = !rightFacing;
            }
        }

        protected bool IsWarriorInFront(Transform t, float frontEpsilon = 0.02f)
        {
            if (t == null) return false;

            RefreshFacingFlags();

            float myX = GetMyCenterX();
            float tx = GetTargetCenterX(t);
            if (float.IsNaN(tx)) return false;

            if (leftFacing) return tx <= myX + frontEpsilon;
            if (rightFacing) return tx >= myX - frontEpsilon;

            return (transform.localScale.x >= 0f)
                ? (tx >= myX - frontEpsilon)
                : (tx <= myX + frontEpsilon);
        }

        protected bool IsWarriorBehind(Transform t, float epsilon = 0.02f)
        {
            if (t == null) return false;

            RefreshFacingFlags();

            float myX = GetMyCenterX();
            float tx = GetTargetCenterX(t);
            if (float.IsNaN(tx)) return false;

            if (leftFacing) return tx > myX + epsilon;
            if (rightFacing) return tx < myX - epsilon;

            return (transform.localScale.x >= 0f)
                ? (tx < myX - epsilon)
                : (tx > myX + epsilon);
        }

        public bool IsWarriorCloseEnoughToHit(Warrior warrior)
        {
            if (warrior == null) return false;
            if (NormalCollider == null || warrior.collider2 == null) return false;

            float myX = NormalCollider.bounds.center.x;
            float warriorX = warrior.collider2.bounds.center.x;
            float dist = Mathf.Abs(warriorX - myX);

            return dist <= meleeHitDistance;
        }

        protected void Flip()
        {
            Vector3 scale = transform.localScale;
            scale.x *= -1;
            transform.localScale = scale;

            if (Front != null)
            {
                Vector3 frontScale = Front.localScale;
                frontScale.x *= -1;
                Front.localScale = frontScale;
            }

            if (Back != null)
            {
                Vector3 backScale = Back.localScale;
                backScale.x *= -1;
                Back.localScale = backScale;
            }
        }

        public void ApplyStun(float seconds)
        {
            if (!canBeStunned) return;
            if (seconds <= 0f) return;
            if (currentHealth <= 0) return;
            if (IsDeadOrDying) return;

            DisableAttackTemporarily(seconds);

            if (_stunRoutine != null)
                StopCoroutine(_stunRoutine);

            _stunRoutine = StartCoroutine(StunRoutine(seconds));
        }

        private IEnumerator StunRoutine(float seconds)
        {
            _isStunned = true;
            CanMove = false;
            IsAttacked = false;

            StopMoveTowardCoroutine();

            if (rigidbody2 != null)
            {
                rigidbody2.linearVelocity = Vector2.zero;
                rigidbody2.angularVelocity = 0f;
            }

            OnStunStarted();

            yield return new WaitForSeconds(seconds);

            if (this != null && !IsDeadOrDying)
            {
                _isStunned = false;
                CanMove = true;
                OnStunEnded();
            }

            _stunRoutine = null;
        }
        protected virtual void OnDamaged(float damage, bool killed)
        {
        }

        protected virtual void OnEnable()
        {
            if (!ActiveEnemies.Contains(this))
                ActiveEnemies.Add(this);
        }

        protected virtual void OnDisable()
        {
            ActiveEnemies.Remove(this);
        }

        public virtual float ComputeStepBackDistance(float incoming)
        {
            if (hitReaction == null) return incoming;

            float mul = hitReaction.stepBackMultiplier;

            if (Animator != null && Animator.GetCurrentAnimatorStateInfo(0).IsTag("Attack"))
                mul *= hitReaction.whileAttackingMultiplier;

            return Mathf.Clamp(incoming * mul, hitReaction.min, hitReaction.max);
        }

        public override void OnDrawGizmos()
        {
            base.OnDrawGizmos();
            Gizmos.color = Color.red;

            if (groundCheckPoint != null)
                Gizmos.DrawLine(groundCheckPoint.position, groundCheckPoint.position + Vector3.down * rayLength);
        }

        protected bool CanDealDamageNow()
        {
            if (currentHealth <= 0f) return false;
            if (_deathStarted) return false;
            if (_isStunned) return false;
            if (IsAttackTemporarilyDisabled) return false;
            return true;
        }

        public void ResetCombatState(float healthPercent = 1f)
        {
            if (_deathStarted) return;

            StopMovingWhenWarriorDie = false;

            float newHealth = maxHealth * healthPercent;
            currentHealth = Mathf.Clamp(newHealth, 1f, maxHealth);

            base.StopMoveTowardCoroutine();

            if (rigidbody2 != null)
            {
                rigidbody2.simulated = true;
                rigidbody2.linearVelocity = Vector2.zero;
                rigidbody2.angularVelocity = 0f;
            }

            _isStunned = false;
            _isDead = false;
            _deathStarted = false;
            _iceBulletMarked = false;

            DisableAttackTemporarily(1.5f);

            if (NormalCollider != null) NormalCollider.enabled = true;
            if (TriggerColliderLeft != null) TriggerColliderLeft.enabled = true;
            if (TriggerColliderRight != null) TriggerColliderRight.enabled = true;

            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = true;
                spriteRenderer.color = Color.white;
            }

            if (worldHealthBar != null)
            {
                worldHealthBar.SetVisibility(true);
                worldHealthBar.ForceUpdate(currentHealth, maxHealth);
            }

            if (animator != null)
                animator.enabled = true;

            UpdateHealthBarDisplay();
        }

        protected virtual void StickToPlatform()
        {
            if (CurrentplatForm == null) return;
            if (rigidbody2 == null) return;

            Vector2 v = rigidbody2.linearVelocity;
            v.y = 0f;
            rigidbody2.linearVelocity = v;
        }

        public virtual void SetSpawnOverrides(EnemySpawnOverrides overrides)
        {
            _spawnOverrides = overrides;

            // Apply the boss flag immediately (not in ApplySpawnOverridesNow): EnemyMgr reads
            // IsBoss right after SetSpawnOverrides but before Start, and some enemies (Bee) never
            // call base.Start. This keeps boss registration / display correct.
            if (overrides != null && overrides.overrideIsBoss)
                SetBoss(overrides.isBoss);
        }

        protected void ApplySpawnOverridesNow()
        {
            if (_spawnOverrides == null)
                return;

            if (_spawnOverrides.overrideSpeed)
                Speed = _spawnOverrides.speed;

            if (_spawnOverrides.overrideAttackRange)
                Range = _spawnOverrides.attackRange;

            if (_spawnOverrides.overrideAttackCooldown)
                attackCooldown = _spawnOverrides.attackCooldown;

            if (_spawnOverrides.overrideAttackDamage)
                attackDamage = _spawnOverrides.attackDamage;

            if (_spawnOverrides.overrideMaxHealth)
            {
                maxHealth = Mathf.Max(1f, _spawnOverrides.maxHealth);
                currentHealth = maxHealth;
            }

            ApplySpecificSpawnOverrides();

            ConfigureAttack();
            UpdateHealthBarDisplay();
        }

        protected virtual void ApplySpecificSpawnOverrides()
        {
        }


        [SerializeField] protected bool useGroundCheckPointForPatrolEdge = true;
        [SerializeField] protected float patrolEdgeAheadProbeDistance = 0.55f;
        [SerializeField] protected float patrolEdgeRayExtraLength = 0.20f;
        [SerializeField] protected float patrolEdgeBoundsSkin = 0.02f;



        // Simple patrol enemies use this.
        // Path-driven enemies like Zalayty override this to false.
        protected virtual bool UsesCommittedPatrolEdge => true;

        protected void CommitPatrolEdgeForMovingVerticalPlatform()
        {
            if (!UsesCommittedPatrolEdge)
            {
                _committedPatrolPlatform = null;
                _hasCommittedPatrolEdge = false;
                return;
            }

            if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
            {
                _committedPatrolPlatform = null;
                _hasCommittedPatrolEdge = false;
                return;
            }

            Collider2D platformCol = CurrentplatForm.platformCollider;
            Bounds pb = platformCol.bounds;

            float leftEdge = pb.min.x;
            float rightEdge = pb.max.x;

            if (_committedPatrolPlatform != platformCol)
            {
                _committedPatrolPlatform = platformCol;
                _hasCommittedPatrolEdge = false;
            }

            if (!_hasCommittedPatrolEdge)
            {
                bool xEdgeIsLeft = Mathf.Abs(xEdge - leftEdge) < 0.01f;
                bool xEdgeIsRight = Mathf.Abs(xEdge - rightEdge) < 0.01f;

                if (xEdgeIsLeft)
                    _committedPatrolEdgeX = leftEdge;
                else if (xEdgeIsRight)
                    _committedPatrolEdgeX = rightEdge;
                else
                    _committedPatrolEdgeX = OddValue ? rightEdge : leftEdge;

                _hasCommittedPatrolEdge = true;
            }

            xEdge = _committedPatrolEdgeX;

            // Important: do this for every platform type, not only MovingVerticalPlatform.
            // This is what flips the patrol target when the enemy reaches an edge.
            if (HasReachedCommittedPatrolEdge(pb))
            {
                bool committedLeft = Mathf.Abs(_committedPatrolEdgeX - leftEdge) < 0.01f;

                _committedPatrolEdgeX = committedLeft ? rightEdge : leftEdge;
                xEdge = _committedPatrolEdgeX;

                if (activesMoveCoroutine != null)
                    StopMoveTowardCoroutine();
            }
        }



        protected bool HasReachedCommittedPatrolEdge(Bounds pb)
        {
            bool targetLeft = Mathf.Abs(_committedPatrolEdgeX - pb.min.x) < 0.01f;

            if (useGroundCheckPointForPatrolEdge &&
                groundCheckPoint != null &&
                CurrentplatForm != null &&
                CurrentplatForm.platformCollider != null)
            {
                float direction = targetLeft ? -1f : 1f;

                float probeDistance = Mathf.Max(
                    patrolEdgeAheadProbeDistance,
                    PlatformSafeMargin + patrolEdgeArriveThreshold + 0.05f
                );

                Vector2 aheadOrigin =
                    (Vector2)groundCheckPoint.position +
                    Vector2.right * direction * probeDistance;

                RaycastHit2D hitAhead = Physics2D.Raycast(
                    aheadOrigin,
                    Vector2.down,
                    rayLength + patrolEdgeRayExtraLength,
                    PlatformLayer
                );

                bool hitCurrentPlatform = IsRayHitCurrentPlatform(hitAhead);

                bool probeIsPastPlatformBounds = targetLeft
                    ? aheadOrigin.x <= pb.min.x + patrolEdgeBoundsSkin
                    : aheadOrigin.x >= pb.max.x - patrolEdgeBoundsSkin;

                if (probeIsPastPlatformBounds || !hitCurrentPlatform)
                    return true;
            }

            Collider2D support = null;

            if (NormalCollider != null && NormalCollider.enabled)
                support = NormalCollider;
            else if (collider2 != null && collider2.enabled)
                support = collider2;

            if (support == null)
                return false;

            Bounds eb = support.bounds;

            if (targetLeft)
                return Mathf.Abs(eb.min.x - pb.min.x) <= patrolEdgeArriveThreshold;

            return Mathf.Abs(eb.max.x - pb.max.x) <= patrolEdgeArriveThreshold;
        }

        private bool IsRayHitCurrentPlatform(RaycastHit2D hit)
        {
            if (hit.collider == null)
                return false;

            if (CurrentplatForm == null)
                return false;

            if (hit.collider == CurrentplatForm.platformCollider)
                return true;

            PlatFormPlfColliderTrigger platform =
                hit.collider.GetComponentInParent<PlatFormPlfColliderTrigger>();

            return platform == CurrentplatForm;
        }
        public void ForceDeath()
        {
            if (_deathStarted || _isDead) return;

            currentHealth = 0f;
            UpdateHealthBarDisplay();

            _isDead = true;
            OnDeath();
        }

        public void ForceDeathImmediate()
        {
            if (_deathStarted || _isDead) return;

            _deathStarted = true;
            _isDead = true;
            currentHealth = 0f;

            OwnerSpawnPoint?.NotifyEnemyDefeated(this);
            EnemyMgr.Instance?.OnEnemyDeathStarted(this);

            if (worldHealthBar != null)
                worldHealthBar.SetVisibility(false);

            if (NormalCollider != null) NormalCollider.enabled = false;
            if (TriggerColliderLeft != null) TriggerColliderLeft.enabled = false;
            if (TriggerColliderRight != null) TriggerColliderRight.enabled = false;

            if (rigidbody2 != null)
            {
                rigidbody2.linearVelocity = Vector2.zero;
                rigidbody2.simulated = false;
            }

            EnemyMgr.Instance?.OnEnemyDestroyed(this);
            Destroy(gameObject);
        }
        protected void CheckWorldYDeathFallback()
        {
            if (!useWorldYDeathFallback) return;
            if (_deathStarted || _isDead) return;
            if (collider2 == null) return;

            if (collider2.bounds.max.y < worldDeathY)
            {
                Debug.Log($"[Enemy] {name} fell below world death Y");
                ForceDeathImmediate();
            }
        }

        [Header("Warrior Top Ping-Pong")]
        [SerializeField] private bool participatesInWarriorTopPingPongEscape = true;

        public virtual bool CanCauseWarriorTopPingPongTrap =>
            participatesInWarriorTopPingPongEscape &&
            !IsDeadOrDying &&
            currentHealth > 0f;

        public virtual Collider2D WarriorTopPingPongCollider =>
            NormalCollider != null ? NormalCollider : collider2;

        public virtual void OnWarriorTopPingPongTrapBroken(Warrior warrior)
        {
            // Optional extension point.
            // Most enemies do nothing.
            // A special enemy can override this to step aside, stop attacking, etc.
        }

        protected bool CanStartAttackNow()
        {
            if (currentHealth <= 0f) return false;
            if (_deathStarted) return false;
            if (_isDead) return false;
            if (_isStunned) return false;
            if (IsAttackTemporarilyDisabled) return false;
            return true;
        }
        protected virtual void OnStunStarted()
        {
            if (animator != null && animator.enabled)
                WaitAnimationDisplay();
        }

        protected virtual void OnStunEnded()
        {
        }
    }


}
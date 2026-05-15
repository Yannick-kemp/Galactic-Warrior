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

        public bool CountsForLevelClear
        {
            get
            {
                return enemyType != EnemyType.Bee
                    && enemyType != EnemyType.BeeEretic;
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

        [SerializeField] protected EnemyHitReactionProfile hitReaction;
        public EnemyHitReactionProfile HitReaction => hitReaction;

        private EnemySpawnOverrides _spawnOverrides;
        protected EnemySpawnOverrides SpawnOverrides => _spawnOverrides;

        [Header("Moving Platform Patrol")]
        [SerializeField] protected float patrolEdgeArriveThreshold = 0.12f;

        protected bool _hasCommittedPatrolEdge;
        protected float _committedPatrolEdgeX;
        protected Collider2D _committedPatrolPlatform;

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
            if (groundCheckPoint == null) return;

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
            if (IsWarriorInFront(target))
                AttackAnimationDisplay();
        }

        public bool TakeDamageAndReturnKilled(float damage)
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

        public IEnumerator SmoothStepBack(bool positif)
        {
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

                elapsed += Time.deltaTime;
                yield return null;
            }

            targetPos.x = ClampToCurrentPlatform(targetPos.x);
            targetPos = ResolveStepBackPositionOnMovingVerticalPlatform(targetPos);
            MoveStepBackBody(targetPos);
        }

        private void MoveStepBackBody(Vector3 position)
        {
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

            if (_stunRoutine != null)
                StopCoroutine(_stunRoutine);

            _stunRoutine = StartCoroutine(StunRoutine(seconds));
        }

        private IEnumerator StunRoutine(float seconds)
        {
            _isStunned = true;
            CanMove = false;

            StopMoveTowardCoroutine();

            if (rigidbody2 != null)
                rigidbody2.linearVelocity = Vector2.zero;

            yield return new WaitForSeconds(seconds);

            if (this != null)
            {
                _isStunned = false;
                CanMove = true;
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
    }


}
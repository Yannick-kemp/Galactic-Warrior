using System.Collections;
using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Hivernox boss AI.
    /// Inherits from your existing Enemy class, so it keeps health bar, death flow,
    /// platform clamping, stun support, knockback, EnemyMgr notification, and boss flags.
    /// </summary>
    public class HivernoxBoss : Enemy
    {
        [Header("Hivernox State")]
        [SerializeField] private HivernoxState state = HivernoxState.Idle;
        [SerializeField] private bool activateOnStart = false;
        [SerializeField] private bool debugStateChanges = false;

        [Header("Detection")]
        [SerializeField] private float detectionRange = 9f;
        [SerializeField] private bool requireSamePlatformForIceAttack = false;

        [Header("Ice Projectile Attack")]
        [SerializeField] private GameObject iceProjectilePrefab;
        [SerializeField] private Transform projectileOrigin;
        [SerializeField] private float iceAttackRange = 8f;
        [SerializeField] private float minIceAttackDistance = 1.25f;
        [SerializeField] private float iceAttackCooldown = 2.2f;
        [SerializeField] private float iceAttackAnimationSeconds = 0.85f;
        [SerializeField] private float autoFireProjectileDelay = 0.28f;
        [SerializeField] private bool autoFireIfNoAnimationEvent = true;
        [SerializeField] private bool aimProjectileAtWarrior = true;
        [SerializeField] private bool horizontalProjectileOnly = true;
        [SerializeField] private float projectileSpeed = 8f;
        [SerializeField] private float projectileLifetime = 4f;
        [SerializeField] private float warriorFreezeSeconds = 2.2f;
        [SerializeField] private float iceProjectileDamage = 0f;
        [SerializeField] private LayerMask projectileObstacleMask;

        [Header("Ice Projectile Visual Alignment")]
        [Tooltip("ON = projectileOrigin marks the BACK/TAIL of the visual projectile. OFF = projectileOrigin marks the prefab pivot/center.")]
        [SerializeField] private bool alignProjectileTailToOrigin = true;

        [Tooltip("Distance from the prefab pivot/center to the BACK/TAIL of the projectile visual. Increase this until the bolt tail sits exactly on projectileOrigin.")]
        [SerializeField, Min(0f)] private float projectilePivotToTailDistance = 0.55f;

        [Tooltip("Draws the tail-to-center offset in Scene view when the projectile is fired.")]
        [SerializeField] private bool debugProjectileAlignment = false;

        [Header("Freeze Finisher")]
        [SerializeField] private float moveToFrozenWarriorSpeed = 4.2f;
        [SerializeField] private float finisherRange = 1.15f;
        [SerializeField] private float maxFinisherChaseSeconds = 2.2f;
        [SerializeField] private int iceBreakerDamage = 30;
        [SerializeField] private float iceBreakerKnockbackVelocity = 5.5f;
        [SerializeField] private float iceBreakerStunSeconds = 0.25f;
        [SerializeField] private float iceBreakerAnimationSeconds = 0.95f;
        [SerializeField] private float autoIceBreakerHitDelay = 0.38f;
        [SerializeField] private bool autoResolveIceBreakerIfNoAnimationEvent = false;

        [Header("Counterattack / Hand Smash")]
        [SerializeField] private bool enableCounterOnDirectHit = true;
        [SerializeField] private float counterReactionRange = 1.85f;
        [SerializeField] private float counterCooldown = 1.1f;
        [SerializeField] private int handSmashDamage = 16;
        [SerializeField] private float handSmashHitRange = 1.9f;
        [SerializeField] private float handSmashKnockbackVelocity = 6.5f;
        [SerializeField] private float handSmashStunSeconds = 0.22f;
        [SerializeField] private float handSmashAnimationSeconds = 0.72f;
        [SerializeField] private float autoHandSmashHitDelay = 0.25f;
        [SerializeField] private bool autoResolveHandSmashIfNoAnimationEvent = false;
        [SerializeField] private float dashReturnDamage = 5f;

        [Header("Retreat / Cooldown")]
        [SerializeField] private Transform[] safeRetreatPoints;
        [SerializeField] private float retreatSpeed = 4f;
        [SerializeField] private float retreatArriveDistance = 0.12f;
        [SerializeField] private float maxRetreatSeconds = 1.4f;
        [SerializeField] private float recoveryCooldownSeconds = 0.9f;

        [Header("Animation")]
        [Tooltip("Optional. Leave empty if your Animator does not have an int parameter for boss state.")]
        [SerializeField] private string stateIntParameter = "";

        [Header("Impact FX")]
        [SerializeField] private GameObject iceBreakerHitFxPrefab;
        [SerializeField] private GameObject handSmashHitFxPrefab;

        private Coroutine _actionRoutine;
        private bool _bossActivated;
        private bool _projectileFiredThisAttack;
        private bool _iceBreakerHitResolved;
        private bool _handSmashHitResolved;
        private float _nextIceAttackTime = -999f;
        private float _nextCounterTime = -999f;
        private Vector3 _homePosition;
        private Warrior _finisherTarget;

        public HivernoxState State => state;
        public bool IsBossActivated => _bossActivated;

        protected override void Start()
        {
            totalFramesInAnimation = 18;
            base.Start();

            SetEnemyType(EnemyType.Hivernox);
            SetBoss(true, "Hivernox");

            _homePosition = transform.position;
            CanMove = true;

            if (activateOnStart)
                ActivateBoss();
            else
                SetState(HivernoxState.Idle);
        }

        protected override void Update()
        {
            if (StopMovingWhenWarriorDie)
                return;

            base.Update();

            if (IsDeadOrDying || state == HivernoxState.Dead)
                return;

            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null || warrior.IsDeadOrDying)
                return;

            if (!_bossActivated)
            {
                if (GetHorizontalDistanceTo(warrior.transform) <= detectionRange)
                    ActivateBoss();
                else
                    return;
            }

            if (_actionRoutine != null)
                return;

            if (IsStunned)
            {
                StopMoveTowardCoroutine();
                WaitAnimationDisplay();
                return;
            }

            if (warrior.IsFrozenByHivernox)
            {
                BeginFreezeFinisher(warrior);
                return;
            }

            SetState(HivernoxState.DetectWarrior);
            FaceWarrior();

            float distance = GetHorizontalDistanceTo(warrior.transform);
            bool samePlatformOk = !requireSamePlatformForIceAttack || IsWarriorOnSamePlatform(warrior);

            if (samePlatformOk &&
                distance <= iceAttackRange &&
                distance >= minIceAttackDistance &&
                Time.time >= _nextIceAttackTime)
            {
                BeginIceAttack();
                return;
            }

            WaitAnimationDisplay();
        }

        public void ActivateBoss()
        {
            if (_bossActivated || IsDeadOrDying)
                return;

            _bossActivated = true;
            SetState(HivernoxState.DetectWarrior);
        }

        private void BeginIceAttack()
        {
            StartExclusiveRoutine(IceAttackRoutine());
        }

        private IEnumerator IceAttackRoutine()
        {
            SetState(HivernoxState.IceAttack);
            StopMoveTowardCoroutine();
            FaceWarrior();
            CanMove = false;

            _projectileFiredThisAttack = false;
            // Uses your existing CharacterController animation-display method.
            // This sets isAttacking = true and clears isAttacking2/isAttacking3/etc.
            AttackAnimationDisplay();

            if (autoFireIfNoAnimationEvent)
            {
                yield return new WaitForSeconds(autoFireProjectileDelay);

                if (!_projectileFiredThisAttack)
                    FireIceProjectile();

                float remaining = Mathf.Max(0f, iceAttackAnimationSeconds - autoFireProjectileDelay);
                yield return new WaitForSeconds(remaining);
            }
            else
            {
                yield return new WaitForSeconds(iceAttackAnimationSeconds);
            }

            WaitAnimationDisplay();
            CanMove = true;
            _nextIceAttackTime = Time.time + iceAttackCooldown;

            _actionRoutine = null;

            if (state != HivernoxState.Dead)
                StartExclusiveRoutine(CooldownRoutine());
        }

        /// <summary>
        /// Put this animation event on the exact frame where the ice/light projectile must leave the hand.
        /// </summary>
        public void AE_FireIceProjectile()
        {
            FireIceProjectile();
        }

        public void FireIceProjectile()
        {
            if (_projectileFiredThisAttack)
                return;

            if (iceProjectilePrefab == null || projectileOrigin == null)
                return;

            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null)
                return;

            FaceWarrior();

            Vector2 direction = GetProjectileDirection(warrior);
            if (direction.sqrMagnitude < 0.001f)
                direction = GetFacingDirection();

            Vector3 spawnPosition = GetProjectileSpawnPosition(direction);

            GameObject projectileObject = Instantiate(
                iceProjectilePrefab,
                spawnPosition,
                Quaternion.identity);

            PrepareProjectileObject(projectileObject, direction);

            HivernoxIceProjectile projectile = projectileObject.GetComponent<HivernoxIceProjectile>();
            if (projectile == null)
                projectile = projectileObject.AddComponent<HivernoxIceProjectile>();

            projectile.Initialize(
                owner: this,
                direction: direction,
                speed: projectileSpeed,
                freezeSeconds: warriorFreezeSeconds,
                projectileDamage: iceProjectileDamage,
                lifetime: projectileLifetime,
                obstacleMask: projectileObstacleMask);

            IgnoreOwnerCollisions(projectileObject);
            _projectileFiredThisAttack = true;
        }

        private Vector2 GetProjectileDirection(Warrior warrior)
        {
            Vector2 direction;

            if (aimProjectileAtWarrior && warrior != null)
            {
                Vector2 targetPoint = warrior.collider2 != null
                    ? (Vector2)warrior.collider2.bounds.center
                    : (Vector2)warrior.transform.position;

                direction = targetPoint - (Vector2)projectileOrigin.position;
            }
            else
            {
                direction = GetFacingDirection();
            }

            if (horizontalProjectileOnly)
                direction.y = 0f;

            if (direction.sqrMagnitude < 0.001f)
                direction = GetFacingDirection();

            return direction.normalized;
        }

        private Vector3 GetProjectileSpawnPosition(Vector2 direction)
        {
            if (projectileOrigin == null)
                return transform.position;

            Vector2 safeDirection = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : GetFacingDirection();

            Vector3 originPosition = projectileOrigin.position;

            // Your FX_LightningBolt_Bullet_Hivernox pivot is in the middle of the bolt.
            // If we spawn the pivot directly on projectileOrigin, the bolt center sits on the hand.
            // Move the pivot forward so the BACK/TAIL of the bolt starts exactly at projectileOrigin.
            if (!alignProjectileTailToOrigin || projectilePivotToTailDistance <= 0f)
                return originPosition;

            Vector3 spawnPosition = originPosition + (Vector3)(safeDirection * projectilePivotToTailDistance);

            if (debugProjectileAlignment)
            {
                Debug.DrawLine(originPosition, spawnPosition, Color.cyan, 1.25f);
                Debug.DrawRay(originPosition, safeDirection * 0.25f, Color.yellow, 1.25f);
            }

            return spawnPosition;
        }

        private void PrepareProjectileObject(GameObject projectileObject, Vector2 direction)
        {
            if (projectileObject == null)
                return;

            projectileObject.transform.right = direction;

            Rigidbody2D rb = projectileObject.GetComponent<Rigidbody2D>();
            if (rb == null)
                rb = projectileObject.AddComponent<Rigidbody2D>();

            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            Collider2D col = projectileObject.GetComponent<Collider2D>();
            if (col == null)
            {
                CircleCollider2D circle = projectileObject.AddComponent<CircleCollider2D>();
                circle.radius = 0.18f;
                circle.isTrigger = true;
            }
            else
            {
                col.isTrigger = true;
            }
        }

        private void IgnoreOwnerCollisions(GameObject projectileObject)
        {
            if (projectileObject == null)
                return;

            Collider2D[] projectileColliders = projectileObject.GetComponentsInChildren<Collider2D>(true);
            Collider2D[] ownerColliders = GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < projectileColliders.Length; i++)
            {
                if (projectileColliders[i] == null)
                    continue;

                for (int j = 0; j < ownerColliders.Length; j++)
                {
                    if (ownerColliders[j] == null)
                        continue;

                    Physics2D.IgnoreCollision(projectileColliders[i], ownerColliders[j], true);
                }
            }
        }

        public void NotifyWarriorFrozen(Warrior warrior)
        {
            if (warrior == null || IsDeadOrDying)
                return;

            if (!_bossActivated)
                ActivateBoss();

            BeginFreezeFinisher(warrior);
        }

        private void BeginFreezeFinisher(Warrior warrior)
        {
            if (warrior == null || !warrior.IsFrozenByHivernox)
                return;

            _finisherTarget = warrior;
            StartExclusiveRoutine(FreezeFinisherRoutine(warrior));
        }

        private IEnumerator FreezeFinisherRoutine(Warrior warrior)
        {
            SetState(HivernoxState.FreezeWarrior);
            StopMoveTowardCoroutine();
            CanMove = false;
            FaceWarrior();

            yield return new WaitForSeconds(0.08f);

            SetState(HivernoxState.MoveToWarrior);

            float chaseTimer = 0f;
            while (warrior != null &&
                   warrior.IsFrozenByHivernox &&
                   GetHorizontalDistanceTo(warrior.transform) > finisherRange &&
                   chaseTimer < maxFinisherChaseSeconds)
            {
                FaceWarrior();
                RunAnimationDisplay();

                float targetX = warrior.transform.position.x;
                float nextX = Mathf.MoveTowards(
                    transform.position.x,
                    targetX,
                    moveToFrozenWarriorSpeed * Time.deltaTime);

                nextX = ClampToCurrentPlatform(nextX);
                transform.position = new Vector3(nextX, transform.position.y, transform.position.z);

                chaseTimer += Time.deltaTime;
                yield return null;
            }

            StopMoveTowardCoroutine();
            WaitAnimationDisplay();

            if (warrior == null || !warrior.IsFrozenByHivernox)
            {
                _actionRoutine = null;
                StartExclusiveRoutine(RetreatRoutine());
                yield break;
            }

            yield return StartCoroutine(IceBreakerAttackRoutine(warrior));
        }

        private IEnumerator IceBreakerAttackRoutine(Warrior warrior)
        {
            SetState(HivernoxState.IceBreakerAttack);
            FaceWarrior();
            CanMove = false;

            _iceBreakerHitResolved = false;
            // Uses your existing CharacterController animation-display method.
            // This sets isAttacking2 = true.
            AttackAnimation2Display();

            if (autoResolveIceBreakerIfNoAnimationEvent)
            {
                yield return new WaitForSeconds(autoIceBreakerHitDelay);

                if (!_iceBreakerHitResolved)
                    AE_IceBreakerHit();

                float remaining = Mathf.Max(0f, iceBreakerAnimationSeconds - autoIceBreakerHitDelay);
                yield return new WaitForSeconds(remaining);
            }
            else
            {
                yield return new WaitForSeconds(iceBreakerAnimationSeconds);
            }

            WaitAnimationDisplay();
            CanMove = true;

            _actionRoutine = null;
            StartExclusiveRoutine(RetreatRoutine());
        }

        /// <summary>
        /// Put this animation event on the frame where Hivernox breaks the ice around the Warrior.
        /// </summary>
        public void AE_IceBreakerHit()
        {
            if (_iceBreakerHitResolved)
                return;

            _iceBreakerHitResolved = true;

            Warrior warrior = _finisherTarget != null
                ? _finisherTarget
                : (GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null);

            if (warrior == null)
                return;

            FaceWarrior();

            warrior.BreakHivernoxFreeze();
            warrior.TryReceiveHivernoxDamage(
                source: this,
                damage: iceBreakerDamage,
                canBeBlockedByShield: false,
                stunSeconds: iceBreakerStunSeconds,
                knockbackVelocity: iceBreakerKnockbackVelocity);

            SpawnFx(iceBreakerHitFxPrefab, warrior.transform.position);
        }

        protected override void OnDamaged(float damage, bool killed)
        {
            base.OnDamaged(damage, killed);

            if (killed || IsDeadOrDying || !enableCounterOnDirectHit)
                return;

            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null || warrior.IsDeadOrDying)
                return;

            float distance = GetHorizontalDistanceTo(warrior.transform);
            if (distance > counterReactionRange)
                return;

            if (!IsWarriorOnSamePlatform(warrior))
                return;

            if (dashReturnDamage > 0f && warrior.IsDodging)
                warrior.ApplyHivernoxDashReturnDamage(dashReturnDamage, transform.position);

            if (Time.time < _nextCounterTime)
                return;

            if (state == HivernoxState.FreezeWarrior ||
                state == HivernoxState.MoveToWarrior ||
                state == HivernoxState.IceBreakerAttack ||
                state == HivernoxState.Retreat ||
                state == HivernoxState.Dead)
                return;

            BeginCounterAttack();
        }

        private void BeginCounterAttack()
        {
            _nextCounterTime = Time.time + counterCooldown;
            StartExclusiveRoutine(CounterAttackRoutine());
        }

        private IEnumerator CounterAttackRoutine()
        {
            SetState(HivernoxState.CounterAttack);
            StopMoveTowardCoroutine();
            FaceWarrior();
            CanMove = false;

            yield return null;

            SetState(HivernoxState.HandSmash);
            _handSmashHitResolved = false;
            // Uses your existing CharacterController animation-display method.
            // This sets isAttacking3 = true.
            AttackAnimation3Display();

            if (autoResolveHandSmashIfNoAnimationEvent)
            {
                yield return new WaitForSeconds(autoHandSmashHitDelay);

                if (!_handSmashHitResolved)
                    AE_HandSmashHit();

                float remaining = Mathf.Max(0f, handSmashAnimationSeconds - autoHandSmashHitDelay);
                yield return new WaitForSeconds(remaining);
            }
            else
            {
                yield return new WaitForSeconds(handSmashAnimationSeconds);
            }

            AE_EndHandSmash();
        }

        /// <summary>
        /// Put this animation event on the exact hand-impact frame.
        /// </summary>
        public void AE_HandSmashHit()
        {
            if (_handSmashHitResolved)
                return;

            _handSmashHitResolved = true;

            if (!CanDealDamageNow())
                return;

            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null || warrior.IsDeadOrDying)
                return;

            if (GetHorizontalDistanceTo(warrior.transform) > handSmashHitRange)
                return;

            if (!IsWarriorOnSamePlatform(warrior))
                return;

            if (!IsWarriorInFront(warrior.transform))
                return;

            warrior.TryReceiveHivernoxDamage(
                source: this,
                damage: handSmashDamage,
                canBeBlockedByShield: true,
                stunSeconds: handSmashStunSeconds,
                knockbackVelocity: handSmashKnockbackVelocity);

            SpawnFx(handSmashHitFxPrefab, warrior.transform.position);
        }

        public void AE_EndHandSmash()
        {
            WaitAnimationDisplay();
            CanMove = true;

            _actionRoutine = null;
            StartExclusiveRoutine(CooldownRoutine());
        }

        public void AE_EndIceBreakerAttack()
        {
            WaitAnimationDisplay();
        }

        private IEnumerator RetreatRoutine()
        {
            SetState(HivernoxState.Retreat);
            CanMove = false;

            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            Vector3 retreatTarget = GetBestRetreatPosition(warrior);

            float timer = 0f;
            while (Mathf.Abs(transform.position.x - retreatTarget.x) > retreatArriveDistance &&
                   timer < maxRetreatSeconds)
            {
                RunAnimationDisplay();
                FaceAwayFrom(warrior);

                float nextX = Mathf.MoveTowards(
                    transform.position.x,
                    retreatTarget.x,
                    retreatSpeed * Time.deltaTime);

                nextX = ClampToCurrentPlatform(nextX);
                transform.position = new Vector3(nextX, transform.position.y, transform.position.z);

                timer += Time.deltaTime;
                yield return null;
            }

            WaitAnimationDisplay();
            CanMove = true;

            _actionRoutine = null;
            StartExclusiveRoutine(CooldownRoutine());
        }

        private IEnumerator CooldownRoutine()
        {
            SetState(HivernoxState.Cooldown);
            StopMoveTowardCoroutine();
            WaitAnimationDisplay();
            CanMove = false;

            yield return new WaitForSeconds(recoveryCooldownSeconds);

            CanMove = true;
            _finisherTarget = null;
            SetState(HivernoxState.DetectWarrior);
            _actionRoutine = null;
        }

        private Vector3 GetBestRetreatPosition(Warrior warrior)
        {
            if (safeRetreatPoints != null && safeRetreatPoints.Length > 0)
            {
                Transform best = null;
                float bestScore = float.MinValue;

                for (int i = 0; i < safeRetreatPoints.Length; i++)
                {
                    Transform candidate = safeRetreatPoints[i];
                    if (candidate == null)
                        continue;

                    float score = warrior != null
                        ? Mathf.Abs(candidate.position.x - warrior.transform.position.x)
                        : Mathf.Abs(candidate.position.x - transform.position.x);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best != null)
                    return new Vector3(ClampToCurrentPlatform(best.position.x), transform.position.y, transform.position.z);
            }

            if (CurrentplatForm != null && CurrentplatForm.platformCollider != null)
            {
                Bounds platformBounds = CurrentplatForm.platformCollider.bounds;
                float leftX = platformBounds.min.x + 0.7f;
                float rightX = platformBounds.max.x - 0.7f;

                if (warrior != null)
                {
                    float warriorX = warrior.transform.position.x;
                    float leftScore = Mathf.Abs(leftX - warriorX);
                    float rightScore = Mathf.Abs(rightX - warriorX);
                    return new Vector3(leftScore > rightScore ? leftX : rightX, transform.position.y, transform.position.z);
                }
            }

            return new Vector3(ClampToCurrentPlatform(_homePosition.x), transform.position.y, transform.position.z);
        }

        private void StartExclusiveRoutine(IEnumerator routine)
        {
            StopActionRoutine();
            _actionRoutine = StartCoroutine(routine);
        }

        private void StopActionRoutine()
        {
            if (_actionRoutine != null)
            {
                StopCoroutine(_actionRoutine);
                _actionRoutine = null;
            }

            WaitAnimationDisplay();
        }

        private void SetState(HivernoxState nextState)
        {
            if (state == nextState)
                return;

            state = nextState;

            if (debugStateChanges)
                Debug.Log($"[Hivernox] State => {state}", this);

            if (animator != null && !string.IsNullOrWhiteSpace(stateIntParameter))
                animator.SetInteger(stateIntParameter, (int)state);
        }

        private void FaceWarrior()
        {
            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null)
                return;

            FlipCharacter(warrior.transform.position.x);
            RefreshFacingFlags();
        }

        private void FaceAwayFrom(Warrior warrior)
        {
            if (warrior == null)
                return;

            float targetX = transform.position.x < warrior.transform.position.x
                ? transform.position.x - 1f
                : transform.position.x + 1f;

            FlipCharacter(targetX);
            RefreshFacingFlags();
        }

        private Vector2 GetFacingDirection()
        {
            RefreshFacingFlags();
            return rightFacing ? Vector2.right : Vector2.left;
        }

        private float GetHorizontalDistanceTo(Transform other)
        {
            if (other == null)
                return float.MaxValue;

            return Mathf.Abs(GetTargetCenterX(other) - GetMyCenterX());
        }

        private bool IsWarriorOnSamePlatform(Warrior warrior)
        {
            if (warrior == null)
                return false;

            if (CurrentplatForm == null || warrior.CurrentplatForm == null)
                return true;

            return CurrentplatForm == warrior.CurrentplatForm;
        }

        private void SpawnFx(GameObject prefab, Vector3 position)
        {
            if (prefab == null)
                return;

            GameObject fx = Instantiate(prefab, position, Quaternion.identity);
            Destroy(fx, 2f);
        }

        protected override void OnDeath()
        {
            StopActionRoutine();
            SetState(HivernoxState.Dead);
            base.OnDeath();
        }
    }
}

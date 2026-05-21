using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using System.Collections;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Arachnee is a normal countable enemy.
    /// Behavior:
    /// - Patrols only inside the current platform bounds.
    /// - When Warrior enters range and cooldown is ready, runs to the midpoint,
    ///   then performs an explosive jump toward Warrior.
    /// - If a platform blocks the jump line, only that platform collider is ignored,
    ///   and the collision is always restored.
    /// </summary>
    [RequireComponent(typeof(Assets.Scripts.Services.EnemyRangeService))]
    public class ArachneeMonster : Enemy
    {
        [Header("Arachnee Patrol")]
        [SerializeField, Min(0f)] private float idleDurationMin = 0.6f;
        [SerializeField, Min(0f)] private float idleDurationMax = 1.2f;
        [SerializeField, Min(0f)] private float patrolEdgePadding = 0.25f;
        [SerializeField, Min(0.01f)] private float patrolTargetReachTolerance = 0.12f;

        [Header("Arachnee Attack")]
        [SerializeField, Min(0f)] private float attackRange = 6f;
        [SerializeField, Min(0f)] private float arachneeAttackCooldown = 2f;
        [SerializeField, Min(0)] private int explosionDamage = 20;

        [SerializeField, Min(0.01f)] private float runHalfDistanceStopTolerance = 0.08f;
        [SerializeField, Min(0f)] private float jumpHeight = 4f;
        [SerializeField, Min(0.01f)] private float jumpDuration = 0.55f;

        [SerializeField] private GameObject explosionPrefab;
        [SerializeField] private bool destroySelfAfterExplosion = true;
        [SerializeField, Min(0f)] private float explosionDestroyDelay = 0.1f;

        [SerializeField] private LayerMask platformObstacleMask;
        [SerializeField] private float obstacleRayHeightOffset = 0.4f;
        [SerializeField, Min(0.05f)] private float platformClearSafetyTimeout = 1.5f;

        [Header("Arachnee Obstacle Probe")]
        [SerializeField, Min(0.02f)] private float obstacleProbeHeight = 0.12f;
        [SerializeField, Range(0.1f, 1f)] private float obstacleProbeWidthMultiplier = 0.65f;

        [Header("Arachnee Safety")]
        [SerializeField, Min(0.5f)] private float attackOverallSafetyTimeout = 4f;

        private Coroutine patrolRoutine;
        private Coroutine attackRoutine;
        private Coroutine selfDestructRoutine;

        private bool attackJumpActive;
        private bool attackResolved;

        private Collider2D ignoredBlockingPlatform;
        private bool ignoredBlockingPlatformWasOverlapped;
        private float ignoredBlockingPlatformStartedAt = -999f;

        private static readonly Collider2D[] ObstacleHitsBuffer = new Collider2D[16];

        protected override void Start()
        {
            // Scene-placed Arachnees should still have the correct type even if the
            // inspector field was left on another enum value.
            SetEnemyType(EnemyType.Arachnee);

            // Mirror Arachnee inspector defaults into the base Enemy fields before
            // Enemy.Start() configures EnemyRangeService and applies common spawn overrides.
            Range = attackRange;
            attackCooldown = arachneeAttackCooldown;
            attackDamage = explosionDamage;

            base.Start();

            // If EnemySpawnOverrides changed common values during base.Start(),
            // keep Arachnee's local fields in sync for inspector/debug readability.
            SyncLocalAttackFieldsFromBase();

            StartPatrolRoutineIfNeeded();
        }

        protected override void ConfigureAttack()
        {
            Range = Mathf.Max(0f, Range);
            attackCooldown = Mathf.Max(0f, attackCooldown);
            attackDamage = Mathf.Max(0, attackDamage);

            if (EnemyRangeService != null)
            {
                EnemyRangeService.SetAttackRange(Range);
                EnemyRangeService.SetAttackCooldown(attackCooldown);
                EnemyRangeService.SetAttackDamage(attackDamage);
            }
        }

        protected override void Update()
        {
            base.Update();

            RestoreIgnoredPlatformCollisionWhenSafe();

            if (CanCheckForArachneeAttack())
                TryStartAttackFromRangeService();
        }

        protected override void OnCollisionEnter2D(Collision2D collision)
        {
            base.OnCollisionEnter2D(collision);
            TryResolveWarriorJumpHit(collision);
        }

        protected override void OnCollisionStay2D(Collision2D collision)
        {
            base.OnCollisionStay2D(collision);
            TryResolveWarriorJumpHit(collision);
        }

        protected override void OnDeath()
        {
            StopPatrolRoutine();

            if (attackRoutine != null)
            {
                StopCoroutine(attackRoutine);
                attackRoutine = null;
            }

            RestoreIgnoredPlatformCollisionIfNeeded();

            base.OnDeath();
        }

        private void OnDisable()
        {
            RestoreIgnoredPlatformCollisionIfNeeded();
        }

        private void OnDestroy()
        {
            RestoreIgnoredPlatformCollisionIfNeeded();
        }

        private void OnValidate()
        {
            if (idleDurationMax < idleDurationMin)
                idleDurationMax = idleDurationMin;

            attackRange = Mathf.Max(0f, attackRange);
            arachneeAttackCooldown = Mathf.Max(0f, arachneeAttackCooldown);
            explosionDamage = Mathf.Max(0, explosionDamage);
            runHalfDistanceStopTolerance = Mathf.Max(0.01f, runHalfDistanceStopTolerance);
            jumpDuration = Mathf.Max(0.01f, jumpDuration);
            platformClearSafetyTimeout = Mathf.Max(0.05f, platformClearSafetyTimeout);
            attackOverallSafetyTimeout = Mathf.Max(0.5f, attackOverallSafetyTimeout);
        }

        private void SyncLocalAttackFieldsFromBase()
        {
            attackRange = Range;
            arachneeAttackCooldown = attackCooldown;
            explosionDamage = attackDamage;
        }

        private bool CanCheckForArachneeAttack()
        {
            if (!isActiveAndEnabled) return false;
            if (IsDeadOrDying) return false;
            if (IsStunned) return false;
            if (IsAttackTemporarilyDisabled) return false;
            if (attackRoutine != null) return false;
            if (selfDestructRoutine != null) return false;
            if (target == null) return false;
            if (EnemyRangeService == null) return false;

            // Important: EnemyRangeService.TryAction returns true when Warrior is
            // still in range but cooldown is not ready. This explicit check prevents
            // false attack starts.
            if (!EnemyRangeService.CanPerformRangeDetection()) return false;

            return true;
        }

        private bool CanStartAttackStateOnly()
        {
            if (!isActiveAndEnabled) return false;
            if (IsDeadOrDying) return false;
            if (IsStunned) return false;
            if (IsAttackTemporarilyDisabled) return false;
            if (attackRoutine != null) return false;
            if (selfDestructRoutine != null) return false;
            if (target == null) return false;

            return true;
        }

        private void TryStartAttackFromRangeService()
        {
            bool cooldownConsumedAndAttackApproved = false;

            EnemyRangeService.TryAction(
                target,
                Range,
                (attacker, attackedTarget) =>
                {
                    cooldownConsumedAndAttackApproved = true;
                }
            );

            if (!cooldownConsumedAndAttackApproved)
                return;

            if (!CanStartAttackStateOnly())
                return;

            StartArachneeAttack();
        }

        private void StartArachneeAttack()
        {
            StopPatrolRoutine();
            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();

            attackRoutine = StartCoroutine(ArachneeAttackRoutine());
        }

        private IEnumerator ArachneeAttackRoutine()
        {
            attackResolved = false;
            attackJumpActive = false;

            Vector2 attackStartPosition = GetColliderCenter();
            Vector2 warriorPositionAtAttackStart = target != null
                ? (Vector2)target.position
                : attackStartPosition;

            Collider2D blockingPlatform = DetectBlockingPlatform(warriorPositionAtAttackStart);

            float midpointX = (attackStartPosition.x + warriorPositionAtAttackStart.x) * 0.5f;

            RunAnimationDisplay();

            if (Mathf.Abs(midpointX - transform.position.x) > runHalfDistanceStopTolerance)
            {
                activesMoveCoroutine = MoveTowardPostionAction(midpointX);
                StartCoroutine(activesMoveCoroutine);

                while (activesMoveCoroutine != null &&
                       !attackResolved &&
                       !IsDeadOrDying &&
                       !IsStunned)
                {
                    if (Mathf.Abs(midpointX - transform.position.x) <= runHalfDistanceStopTolerance)
                    {
                        StopMoveTowardCoroutine();
                        break;
                    }

                    yield return null;
                }
            }

            StopMoveTowardCoroutine();

            if (attackResolved || IsDeadOrDying || IsStunned)
            {
                FinishAttackWithoutExplosion();
                yield break;
            }

            Vector2 warriorJumpTarget = target != null
                ? (Vector2)target.position
                : warriorPositionAtAttackStart;

            if (blockingPlatform != null)
                IgnoreBlockingPlatformTemporarily(blockingPlatform);

            JumpAnimationDisplay();
            attackJumpActive = true;

            activesJumpCoroutine = JumpTowardPositionAction(
                warriorJumpTarget,
                jumpHeight,
                jumpDuration,
                collider2
            );

            StartCoroutine(activesJumpCoroutine);

            float jumpStartedAt = Time.time;
            float timeout = Mathf.Max(attackOverallSafetyTimeout, jumpDuration + platformClearSafetyTimeout + 0.5f);

            while (!attackResolved &&
                   !IsDeadOrDying &&
                   activesJumpCoroutine != null)
            {
                RestoreIgnoredPlatformCollisionWhenSafe();

                if (Time.time - jumpStartedAt >= timeout)
                {
                    StopJumpTowardCoroutine();
                    break;
                }

                yield return null;
            }

            attackJumpActive = false;

            if (!attackResolved && !IsDeadOrDying)
            {
                ResolveExplosion(
                    GetLandingExplosionPoint(),
                    null,
                    damageWarrior: false
                );
            }

            FinishAttackAfterResolution();
        }

        private void FinishAttackWithoutExplosion()
        {
            attackJumpActive = false;
            attackResolved = false;

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();
            RestoreIgnoredPlatformCollisionIfNeeded();

            attackRoutine = null;

            if (!IsDeadOrDying && selfDestructRoutine == null)
                StartPatrolRoutineIfNeeded();
        }

        private void FinishAttackAfterResolution()
        {
            attackJumpActive = false;

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();
            RestoreIgnoredPlatformCollisionIfNeeded();

            attackRoutine = null;

            if (!destroySelfAfterExplosion && !IsDeadOrDying)
            {
                attackResolved = false;
                StartPatrolRoutineIfNeeded();
            }
        }

        private void StartPatrolRoutineIfNeeded()
        {
            if (!isActiveAndEnabled) return;
            if (patrolRoutine != null) return;
            if (attackRoutine != null) return;
            if (IsDeadOrDying) return;

            patrolRoutine = StartCoroutine(PatrolRoutine());
        }

        private void StopPatrolRoutine()
        {
            if (patrolRoutine != null)
            {
                StopCoroutine(patrolRoutine);
                patrolRoutine = null;
            }
        }

        private IEnumerator PatrolRoutine()
        {
            while (isActiveAndEnabled && !IsDeadOrDying)
            {
                if (attackRoutine != null || IsStunned)
                {
                    yield return null;
                    continue;
                }

                WaitAnimationDisplay();

                float idleDuration = Random.Range(idleDurationMin, idleDurationMax);
                if (idleDuration > 0f)
                    yield return new WaitForSeconds(idleDuration);

                if (attackRoutine != null || IsDeadOrDying || IsStunned)
                    continue;

                if (!TryGetPatrolTargetX(out float targetX))
                {
                    yield return null;
                    continue;
                }

                WalkAnimationDisplay();

                activesMoveCoroutine = MoveTowardPostionAction(targetX);
                StartCoroutine(activesMoveCoroutine);

                while (activesMoveCoroutine != null &&
                       attackRoutine == null &&
                       !IsDeadOrDying &&
                       !IsStunned)
                {
                    if (Mathf.Abs(transform.position.x - targetX) <= patrolTargetReachTolerance)
                    {
                        StopMoveTowardCoroutine();
                        break;
                    }

                    yield return null;
                }

                StopMoveTowardCoroutine();
                yield return null;
            }

            patrolRoutine = null;
        }

        private bool TryGetPatrolTargetX(out float targetX)
        {
            targetX = transform.position.x;

            if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
                return false;

            Collider2D myCollider = GetMainCollider();
            if (myCollider == null)
                return false;

            Bounds platformBounds = CurrentplatForm.platformCollider.bounds;
            Bounds myBounds = myCollider.bounds;

            float minX = platformBounds.min.x + myBounds.extents.x + patrolEdgePadding;
            float maxX = platformBounds.max.x - myBounds.extents.x - patrolEdgePadding;

            if (maxX < minX)
                return false;

            float currentX = transform.position.x;
            float leftDistance = Mathf.Abs(currentX - minX);
            float rightDistance = Mathf.Abs(currentX - maxX);

            // Prefer the side farther away to avoid choosing a tiny move repeatedly.
            if (leftDistance > rightDistance)
                targetX = Random.Range(minX, Mathf.Max(minX, currentX - patrolTargetReachTolerance));
            else
                targetX = Random.Range(Mathf.Min(maxX, currentX + patrolTargetReachTolerance), maxX);

            targetX = Mathf.Clamp(targetX, minX, maxX);
            return Mathf.Abs(targetX - currentX) > patrolTargetReachTolerance;
        }

        private Collider2D DetectBlockingPlatform(Vector2 warriorWorldPosition)
        {
            Collider2D myCollider = GetMainCollider();
            if (myCollider == null)
                return null;

            Bounds myBounds = myCollider.bounds;

            Vector2 origin = new Vector2(
                myBounds.center.x,
                myBounds.center.y + obstacleRayHeightOffset
            );

            Vector2 destination = new Vector2(
                warriorWorldPosition.x,
                warriorWorldPosition.y + obstacleRayHeightOffset
            );

            Vector2 direction = destination - origin;
            float distance = direction.magnitude;

            if (distance <= 0.05f)
                return null;

            int mask = platformObstacleMask.value != 0
                ? platformObstacleMask.value
                : PlatformLayer.value;

            if (mask == 0)
                return null;

            float probeWidth = Mathf.Max(0.05f, myBounds.size.x * obstacleProbeWidthMultiplier);
            Vector2 probeSize = new Vector2(probeWidth, obstacleProbeHeight);

            RaycastHit2D[] hits = Physics2D.BoxCastAll(
                origin,
                probeSize,
                0f,
                direction.normalized,
                distance,
                mask
            );

            if (hits == null || hits.Length == 0)
                return null;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hitCollider = hits[i].collider;
                if (hitCollider == null) continue;
                if (hitCollider.isTrigger) continue;
                if (IsOwnCollider(hitCollider)) continue;
                if (IsCurrentPlatformCollider(hitCollider)) continue;

                return hitCollider;
            }

            return null;
        }

        private void IgnoreBlockingPlatformTemporarily(Collider2D platformCollider)
        {
            if (platformCollider == null)
                return;

            RestoreIgnoredPlatformCollisionIfNeeded();

            Collider2D myCollider = GetMainCollider();
            if (myCollider == null)
                return;

            ignoredBlockingPlatform = platformCollider;
            ignoredBlockingPlatformWasOverlapped = myCollider.bounds.Intersects(platformCollider.bounds);
            ignoredBlockingPlatformStartedAt = Time.time;

            Physics2D.IgnoreCollision(myCollider, ignoredBlockingPlatform, true);
        }

        private void RestoreIgnoredPlatformCollisionWhenSafe()
        {
            if (ignoredBlockingPlatform == null)
                return;

            Collider2D myCollider = GetMainCollider();
            if (myCollider == null || !myCollider.enabled || !ignoredBlockingPlatform.enabled)
            {
                RestoreIgnoredPlatformCollisionIfNeeded();
                return;
            }

            bool overlapsNow = myCollider.bounds.Intersects(ignoredBlockingPlatform.bounds);
            if (overlapsNow)
                ignoredBlockingPlatformWasOverlapped = true;

            bool clearedAfterOverlap =
                ignoredBlockingPlatformWasOverlapped &&
                !overlapsNow;

            bool safetyTimeout =
                Time.time - ignoredBlockingPlatformStartedAt >= platformClearSafetyTimeout;

            if (clearedAfterOverlap || safetyTimeout)
                RestoreIgnoredPlatformCollisionIfNeeded();
        }

        private void RestoreIgnoredPlatformCollisionIfNeeded()
        {
            if (ignoredBlockingPlatform == null)
                return;

            Collider2D myCollider = GetMainCollider();
            if (myCollider != null)
                Physics2D.IgnoreCollision(myCollider, ignoredBlockingPlatform, false);

            ignoredBlockingPlatform = null;
            ignoredBlockingPlatformWasOverlapped = false;
            ignoredBlockingPlatformStartedAt = -999f;
        }

        private void TryResolveWarriorJumpHit(Collision2D collision)
        {
            if (!attackJumpActive) return;
            if (attackResolved) return;
            if (IsDeadOrDying) return;
            if (collision == null || collision.collider == null) return;

            if (!TryGetDirectWarrior(collision.collider, out Warrior warrior))
                return;

            Vector2 point = GetCollisionPoint(collision);
            ResolveExplosion(point, warrior, damageWarrior: true);
        }

        private bool TryGetDirectWarrior(Collider2D other, out Warrior warrior)
        {
            warrior = null;

            if (other == null)
                return false;

            int shieldLayer = LayerMask.NameToLayer("Shield Laser");
            if (shieldLayer >= 0 && other.gameObject.layer == shieldLayer)
                return false;

            warrior = other.GetComponentInParent<Warrior>();
            if (warrior == null)
                return false;

            if (warrior.IsDeadOrDying)
                return false;

            return true;
        }

        private void ResolveExplosion(Vector2 explosionPoint, Warrior warrior, bool damageWarrior)
        {
            if (attackResolved)
                return;

            attackResolved = true;
            attackJumpActive = false;

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();
            RestoreIgnoredPlatformCollisionIfNeeded();

            SpawnExplosion(explosionPoint);

            if (damageWarrior && warrior != null)
                warrior.TakeDamage(attackDamage);

            if (destroySelfAfterExplosion && selfDestructRoutine == null && !IsDeadOrDying)
                selfDestructRoutine = StartCoroutine(SelfDestructAfterExplosionDelay());
            else if (!destroySelfAfterExplosion && !IsDeadOrDying)
                WaitAnimationDisplay();
        }

        private IEnumerator SelfDestructAfterExplosionDelay()
        {
            if (explosionDestroyDelay > 0f)
                yield return new WaitForSeconds(explosionDestroyDelay);

            selfDestructRoutine = null;

            if (!IsDeadOrDying)
                ForceDeath();
        }

        private void SpawnExplosion(Vector2 worldPoint)
        {
            if (explosionPrefab == null)
                return;

            Instantiate(explosionPrefab, worldPoint, Quaternion.identity);
        }

        private Vector2 GetCollisionPoint(Collision2D collision)
        {
            if (collision != null && collision.contactCount > 0)
                return collision.GetContact(0).point;

            return GetLandingExplosionPoint();
        }

        private Vector2 GetLandingExplosionPoint()
        {
            Collider2D myCollider = GetMainCollider();
            if (myCollider != null)
            {
                Bounds b = myCollider.bounds;
                return new Vector2(b.center.x, b.min.y);
            }

            return transform.position;
        }

        private Vector2 GetColliderCenter()
        {
            Collider2D myCollider = GetMainCollider();
            return myCollider != null ? (Vector2)myCollider.bounds.center : (Vector2)transform.position;
        }

        private Collider2D GetMainCollider()
        {
            if (collider2 != null && collider2.enabled)
                return collider2;

            if (NormalCollider != null && NormalCollider.enabled)
                return NormalCollider;

            return collider2 != null ? collider2 : NormalCollider;
        }

        private bool IsOwnCollider(Collider2D other)
        {
            if (other == null)
                return false;

            if (collider2 != null && other == collider2)
                return true;

            if (NormalCollider != null && other == NormalCollider)
                return true;

            return other.transform.IsChildOf(transform);
        }

        private bool IsCurrentPlatformCollider(Collider2D other)
        {
            return CurrentplatForm != null &&
                   CurrentplatForm.platformCollider != null &&
                   other == CurrentplatForm.platformCollider;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Collider2D myCollider = GetMainCollider();
            Vector3 center = myCollider != null ? myCollider.bounds.center : transform.position;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(center, attackRange);
        }
#endif
    }
}

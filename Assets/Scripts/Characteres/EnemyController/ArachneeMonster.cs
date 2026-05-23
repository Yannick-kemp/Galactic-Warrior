using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Arachnee is a normal countable enemy.
    /// Behavior:
    /// - Patrols only inside the current platform bounds.
    /// - When Warrior enters range and cooldown is ready, runs to the midpoint,
    ///   then performs an explosive jump toward Warrior.
    /// - During the attack jump, every platform body collider on this Arachnee's
    ///   jump path can be ignored per-instance and restored safely.
    /// </summary>
    [RequireComponent(typeof(Assets.Scripts.Services.EnemyRangeService))]
    public class ArachneeMonster : Enemy
    {
        private enum AttackPlatformRelation
        {
            Unknown = 0,
            SamePlatform = 1,
            WarriorBelow = 2,
            WarriorAbove = 3
        }

        private sealed class IgnoredAttackPlatformRecord
        {
            public Collider2D arachneeCollider;
            public Collider2D platformCollider;
            public bool wasOverlapped;
            public float startedAt;
        }

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

        [Header("Arachnee Platform Ignore")]
        [Tooltip("Layer used to detect platform body colliders that can block Arachnee's attack jump. If empty, Arachnee falls back to PlatformLayer.")]
        [SerializeField] private LayerMask platformObstacleMask;

        [Tooltip("Vertical tolerance used to decide if Warrior's platform is above, below, or the same height as Arachnee's current platform.")]
        [SerializeField, Min(0f)] private float platformRelationVerticalTolerance = 0.12f;

        [Tooltip("How many segments are used to probe the parabolic attack path before the jump starts.")]
        [SerializeField, Range(4, 40)] private int attackPathProbeSegments = 18;

        [Tooltip("Safety restore time for each ignored platform if Arachnee never cleanly exits that collider.")]
        [SerializeField, Min(0.05f)] private float platformClearSafetyTimeout = 1.5f;

        [Header("Arachnee Obstacle Probe")]
        [Tooltip("Minimum probe height used while scanning the attack path. The real probe also uses part of Arachnee's collider height so body-blocking platforms are not missed.")]
        [SerializeField, Min(0.02f)] private float obstacleProbeHeight = 0.12f;

        [Tooltip("Width multiplier applied to Arachnee's main collider while scanning the attack path.")]
        [SerializeField, Range(0.1f, 1f)] private float obstacleProbeWidthMultiplier = 0.65f;

        [Header("Arachnee Safety")]
        [SerializeField, Min(0.5f)] private float attackOverallSafetyTimeout = 4f;

        private Coroutine patrolRoutine;
        private Coroutine attackRoutine;
        private Coroutine selfDestructRoutine;

        private bool attackJumpActive;
        private bool attackResolved;

        private readonly List<IgnoredAttackPlatformRecord> ignoredAttackPlatforms =
            new List<IgnoredAttackPlatformRecord>();

        private readonly HashSet<int> ignoredAttackPlatformIds = new HashSet<int>();

        private readonly List<Collider2D> attackPathPlatformCandidates =
            new List<Collider2D>();

        private readonly HashSet<int> attackPathPlatformCandidateIds = new HashSet<int>();

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

            RestoreAllIgnoredAttackPlatformCollisions();

            base.OnDeath();
        }

        private void OnDisable()
        {
            RestoreAllIgnoredAttackPlatformCollisions();
        }

        private void OnDestroy()
        {
            RestoreAllIgnoredAttackPlatformCollisions();
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
            platformRelationVerticalTolerance = Mathf.Max(0f, platformRelationVerticalTolerance);
            platformClearSafetyTimeout = Mathf.Max(0.05f, platformClearSafetyTimeout);
            attackOverallSafetyTimeout = Mathf.Max(0.5f, attackOverallSafetyTimeout);
            attackPathProbeSegments = Mathf.Clamp(attackPathProbeSegments, 4, 40);
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
            RestoreAllIgnoredAttackPlatformCollisions();

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

            PrepareAttackPlatformIgnoresForJump(warriorJumpTarget);

            JumpAnimationDisplay();
            attackJumpActive = true;

            activesJumpCoroutine = ArachneeAttackJumpTowardPositionAction(
                warriorJumpTarget,
                jumpHeight,
                jumpDuration
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

        /// <summary>
        /// Private attack-only jump.
        /// The base CharacterController.JumpTowardPositionAction contains destination-platform
        /// top-landing anti-tunneling that intentionally restores/solidifies platforms.
        /// Arachnee's attack jump needs the opposite behavior for detected path blockers,
        /// so this attack-only jump keeps the same parabolic motion but lets the
        /// per-instance IgnoreCollision list control what can stop Arachnee.
        /// </summary>
        private IEnumerator ArachneeAttackJumpTowardPositionAction(Vector2 targetPosition, float height, float duration)
        {
            if (_isJumping)
                yield break;

            _isJumping = true;
            targetReached = false;
            DescendentPhase = false;

            Vector2 startPosition = rigidbody2 != null ? rigidbody2.position : (Vector2)transform.position;
            float elapsedTime = 0f;
            float previousY = startPosition.y;

            FlipCharacter(targetPosition.x);

            WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();

            while (!targetReached && elapsedTime < duration)
            {
                float stepTime = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : Time.deltaTime;
                elapsedTime += stepTime;

                float t = duration > 0f
                    ? Mathf.Clamp01(elapsedTime / duration)
                    : 1f;

                Vector2 desiredPosition = Vector2.Lerp(startPosition, targetPosition, t);
                desiredPosition.y += height * Mathf.Sin(Mathf.PI * t);

                MoveCharacterTo(desiredPosition);
                Physics2D.SyncTransforms();

                DescendentPhase = desiredPosition.y < previousY;
                previousY = desiredPosition.y;

                RestoreIgnoredPlatformCollisionWhenSafe();

                yield return waitForFixedUpdate;
            }

            _isJumping = false;
            DescendentPhase = false;
            activesJumpCoroutine = null;
        }

        private void FinishAttackWithoutExplosion()
        {
            attackJumpActive = false;
            attackResolved = false;

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();
            RestoreAllIgnoredAttackPlatformCollisions();

            attackRoutine = null;

            if (!IsDeadOrDying && selfDestructRoutine == null)
                StartPatrolRoutineIfNeeded();
        }

        private void FinishAttackAfterResolution()
        {
            attackJumpActive = false;

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();
            RestoreAllIgnoredAttackPlatformCollisions();

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

        private void PrepareAttackPlatformIgnoresForJump(Vector2 jumpTargetWorldPosition)
        {
            RestoreAllIgnoredAttackPlatformCollisions();

            DetectAttackPathPlatforms(jumpTargetWorldPosition, attackPathPlatformCandidates);

            for (int i = 0; i < attackPathPlatformCandidates.Count; i++)
                IgnoreAttackPlatformTemporarily(attackPathPlatformCandidates[i]);

            Physics2D.SyncTransforms();
        }

        private void DetectAttackPathPlatforms(Vector2 jumpTargetWorldPosition, List<Collider2D> result)
        {
            result.Clear();
            attackPathPlatformCandidateIds.Clear();

            Collider2D myCollider = GetMainCollider();
            if (myCollider == null)
                return;

            int mask = GetPlatformObstacleMaskValue();
            if (mask == 0)
                return;

            Collider2D sourcePlatform = GetCurrentPlatformBodyCollider();
            Collider2D warriorPlatform = GetWarriorPlatformBodyCollider();
            AttackPlatformRelation relation = GetAttackPlatformRelation(jumpTargetWorldPosition, sourcePlatform, warriorPlatform);

            // Case 1: Warrior is below Arachnee. The source platform must be ignored
            // because it is part of the downward attack path.
            if (relation == AttackPlatformRelation.WarriorBelow && sourcePlatform != null)
                TryAddAttackPathPlatformCandidate(sourcePlatform, relation, sourcePlatform, warriorPlatform, result);

            Bounds myBounds = myCollider.bounds;

            Vector2 startTransformPosition = rigidbody2 != null
                ? rigidbody2.position
                : (Vector2)transform.position;

            Vector2 centerOffset = (Vector2)myBounds.center - startTransformPosition;

            float probeWidth = Mathf.Max(0.05f, myBounds.size.x * obstacleProbeWidthMultiplier);
            float probeHeight = Mathf.Max(obstacleProbeHeight, myBounds.size.y * 0.8f);
            Vector2 probeSize = new Vector2(probeWidth, probeHeight);

            int segments = Mathf.Max(4, attackPathProbeSegments);
            Vector2 previousCenter = SampleAttackJumpColliderCenter(
                startTransformPosition,
                jumpTargetWorldPosition,
                centerOffset,
                0f
            );

            CollectOverlappedAttackPathPlatforms(previousCenter, probeSize, mask, relation, sourcePlatform, warriorPlatform, result);

            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector2 currentCenter = SampleAttackJumpColliderCenter(
                    startTransformPosition,
                    jumpTargetWorldPosition,
                    centerOffset,
                    t
                );

                CollectOverlappedAttackPathPlatforms(currentCenter, probeSize, mask, relation, sourcePlatform, warriorPlatform, result);
                CollectCastAttackPathPlatforms(previousCenter, currentCenter, probeSize, mask, relation, sourcePlatform, warriorPlatform, result);

                previousCenter = currentCenter;
            }
        }

        private int GetPlatformObstacleMaskValue()
        {
            if (platformObstacleMask.value != 0)
                return platformObstacleMask.value;

            return PlatformLayer.value;
        }

        private Vector2 SampleAttackJumpColliderCenter(
            Vector2 startTransformPosition,
            Vector2 targetTransformPosition,
            Vector2 colliderCenterOffset,
            float t)
        {
            t = Mathf.Clamp01(t);

            Vector2 position = Vector2.Lerp(startTransformPosition, targetTransformPosition, t);
            position.y += jumpHeight * Mathf.Sin(Mathf.PI * t);

            return position + colliderCenterOffset;
        }

        private void CollectOverlappedAttackPathPlatforms(
            Vector2 center,
            Vector2 probeSize,
            int mask,
            AttackPlatformRelation relation,
            Collider2D sourcePlatform,
            Collider2D warriorPlatform,
            List<Collider2D> result)
        {
            Collider2D[] hits = Physics2D.OverlapBoxAll(center, probeSize, 0f, mask);
            if (hits == null)
                return;

            for (int i = 0; i < hits.Length; i++)
                TryAddAttackPathPlatformCandidate(hits[i], relation, sourcePlatform, warriorPlatform, result);
        }

        private void CollectCastAttackPathPlatforms(
            Vector2 fromCenter,
            Vector2 toCenter,
            Vector2 probeSize,
            int mask,
            AttackPlatformRelation relation,
            Collider2D sourcePlatform,
            Collider2D warriorPlatform,
            List<Collider2D> result)
        {
            Vector2 direction = toCenter - fromCenter;
            float distance = direction.magnitude;

            if (distance <= 0.001f)
                return;

            RaycastHit2D[] hits = Physics2D.BoxCastAll(
                fromCenter,
                probeSize,
                0f,
                direction.normalized,
                distance,
                mask
            );

            if (hits == null)
                return;

            for (int i = 0; i < hits.Length; i++)
                TryAddAttackPathPlatformCandidate(hits[i].collider, relation, sourcePlatform, warriorPlatform, result);
        }

        private void TryAddAttackPathPlatformCandidate(
            Collider2D rawCollider,
            AttackPlatformRelation relation,
            Collider2D sourcePlatform,
            Collider2D warriorPlatform,
            List<Collider2D> result)
        {
            Collider2D platformCollider = NormalizePlatformBodyCollider(rawCollider);
            if (platformCollider == null)
                return;

            if (!ShouldIgnorePlatformForAttackPath(platformCollider, relation, sourcePlatform, warriorPlatform))
                return;

            int id = platformCollider.GetInstanceID();
            if (!attackPathPlatformCandidateIds.Add(id))
                return;

            result.Add(platformCollider);
        }

        private Collider2D NormalizePlatformBodyCollider(Collider2D rawCollider)
        {
            if (rawCollider == null)
                return null;

            if (IsOwnCollider(rawCollider))
                return null;

            // Never ignore trigger colliders. If the trigger belongs to a platform,
            // normalize to the real platform body collider instead.
            if (rawCollider.isTrigger)
            {
                PlatFormColliderTrigger triggerPlatform = rawCollider.GetComponentInParent<PlatFormColliderTrigger>();
                if (triggerPlatform == null || triggerPlatform.platformCollider == null)
                    return null;

                rawCollider = triggerPlatform.platformCollider;
            }

            if (rawCollider == null || rawCollider.isTrigger || !rawCollider.enabled)
                return null;

            if (IsOwnCollider(rawCollider))
                return null;

            PlatFormColliderTrigger platform = rawCollider.GetComponentInParent<PlatFormColliderTrigger>();
            if (platform != null && platform.platformCollider != null)
                rawCollider = platform.platformCollider;

            if (rawCollider == null || rawCollider.isTrigger || !rawCollider.enabled)
                return null;

            if (IsOwnCollider(rawCollider))
                return null;

            return rawCollider;
        }

        private bool ShouldIgnorePlatformForAttackPath(
            Collider2D platformCollider,
            AttackPlatformRelation relation,
            Collider2D sourcePlatform,
            Collider2D warriorPlatform)
        {
            if (platformCollider == null)
                return false;

            if (platformCollider.isTrigger)
                return false;

            if (IsOwnCollider(platformCollider))
                return false;

            bool isSourcePlatform = sourcePlatform != null && platformCollider == sourcePlatform;

            // Case 3: same platform. Never ignore the platform both are standing on.
            // Case 2: Warrior above. Also do not ignore Arachnee's current platform.
            // Case 1: Warrior below. The source platform is deliberately included.
            if (isSourcePlatform)
                return relation == AttackPlatformRelation.WarriorBelow;

            switch (relation)
            {
                case AttackPlatformRelation.SamePlatform:
                    return IsPlatformAboveArachneeSource(platformCollider, sourcePlatform);

                case AttackPlatformRelation.WarriorAbove:
                    return IsPlatformAboveArachneeSource(platformCollider, sourcePlatform);

                case AttackPlatformRelation.WarriorBelow:
                    // The path probe already proved this platform intersects the attack path.
                    // Keep all intermediate/body-blocking platforms, including Warrior's platform.
                    return true;

                default:
                    // Unknown platform relation: keep the fix conservative.
                    // Ignore only path blockers that are not the current platform.
                    return !IsCurrentPlatformCollider(platformCollider);
            }
        }

        private bool IsPlatformAboveArachneeSource(Collider2D platformCollider, Collider2D sourcePlatform)
        {
            if (platformCollider == null)
                return false;

            if (sourcePlatform != null)
            {
                Bounds sourceBounds = sourcePlatform.bounds;
                Bounds platformBounds = platformCollider.bounds;

                return platformBounds.center.y > sourceBounds.center.y + platformRelationVerticalTolerance ||
                       platformBounds.min.y > sourceBounds.max.y - platformRelationVerticalTolerance;
            }

            Collider2D myCollider = GetMainCollider();
            float referenceY = myCollider != null ? myCollider.bounds.center.y : transform.position.y;

            return platformCollider.bounds.center.y > referenceY + platformRelationVerticalTolerance;
        }

        private AttackPlatformRelation GetAttackPlatformRelation(
            Vector2 warriorWorldPosition,
            Collider2D sourcePlatform,
            Collider2D warriorPlatform)
        {
            if (sourcePlatform != null && warriorPlatform != null)
            {
                if (sourcePlatform == warriorPlatform)
                    return AttackPlatformRelation.SamePlatform;

                float platformDeltaY = warriorPlatform.bounds.center.y - sourcePlatform.bounds.center.y;

                if (platformDeltaY > platformRelationVerticalTolerance)
                    return AttackPlatformRelation.WarriorAbove;

                if (platformDeltaY < -platformRelationVerticalTolerance)
                    return AttackPlatformRelation.WarriorBelow;

                return AttackPlatformRelation.Unknown;
            }

            Vector2 myCenter = GetColliderCenter();
            float deltaY = warriorWorldPosition.y - myCenter.y;

            if (deltaY > platformRelationVerticalTolerance)
                return AttackPlatformRelation.WarriorAbove;

            if (deltaY < -platformRelationVerticalTolerance)
                return AttackPlatformRelation.WarriorBelow;

            return AttackPlatformRelation.Unknown;
        }

        private Collider2D GetCurrentPlatformBodyCollider()
        {
            if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
                return null;

            return CurrentplatForm.platformCollider;
        }

        private Collider2D GetWarriorPlatformBodyCollider()
        {
            Warrior warrior = target != null ? target.GetComponentInParent<Warrior>() : null;
            if (warrior == null || warrior.CurrentplatForm == null || warrior.CurrentplatForm.platformCollider == null)
                return null;

            return warrior.CurrentplatForm.platformCollider;
        }

        private void IgnoreAttackPlatformTemporarily(Collider2D platformCollider)
        {
            platformCollider = NormalizePlatformBodyCollider(platformCollider);
            if (platformCollider == null)
                return;

            Collider2D myCollider = GetMainCollider();
            if (myCollider == null || !myCollider.enabled)
                return;

            if (myCollider == platformCollider)
                return;

            int id = platformCollider.GetInstanceID();
            if (!ignoredAttackPlatformIds.Add(id))
                return;

            ignoredAttackPlatforms.Add(new IgnoredAttackPlatformRecord
            {
                arachneeCollider = myCollider,
                platformCollider = platformCollider,
                wasOverlapped = myCollider.bounds.Intersects(platformCollider.bounds),
                startedAt = Time.time
            });

            Physics2D.IgnoreCollision(myCollider, platformCollider, true);
        }

        private void RestoreIgnoredPlatformCollisionWhenSafe()
        {
            if (ignoredAttackPlatforms.Count == 0)
                return;

            for (int i = ignoredAttackPlatforms.Count - 1; i >= 0; i--)
            {
                IgnoredAttackPlatformRecord record = ignoredAttackPlatforms[i];

                if (record == null)
                {
                    ignoredAttackPlatforms.RemoveAt(i);
                    continue;
                }

                bool shouldRestore = !attackJumpActive;

                if (!shouldRestore)
                {
                    if (record.arachneeCollider == null || record.platformCollider == null)
                    {
                        shouldRestore = true;
                    }
                    else if (!record.arachneeCollider.enabled || !record.platformCollider.enabled)
                    {
                        shouldRestore = true;
                    }
                    else
                    {
                        bool overlapsNow = record.arachneeCollider.bounds.Intersects(record.platformCollider.bounds);
                        if (overlapsNow)
                            record.wasOverlapped = true;

                        bool clearedAfterOverlap = record.wasOverlapped && !overlapsNow;
                        bool safetyTimeout = Time.time - record.startedAt >= platformClearSafetyTimeout;

                        shouldRestore = clearedAfterOverlap || safetyTimeout;
                    }
                }

                if (shouldRestore)
                    RestoreIgnoredAttackPlatformAt(i);
            }
        }

        private void RestoreAllIgnoredAttackPlatformCollisions()
        {
            for (int i = ignoredAttackPlatforms.Count - 1; i >= 0; i--)
                RestoreIgnoredAttackPlatformAt(i);

            ignoredAttackPlatforms.Clear();
            ignoredAttackPlatformIds.Clear();
        }

        private void RestoreIgnoredAttackPlatformAt(int index)
        {
            if (index < 0 || index >= ignoredAttackPlatforms.Count)
                return;

            IgnoredAttackPlatformRecord record = ignoredAttackPlatforms[index];

            if (record != null)
            {
                if (record.arachneeCollider != null && record.platformCollider != null)
                    Physics2D.IgnoreCollision(record.arachneeCollider, record.platformCollider, false);

                if (record.platformCollider != null)
                    ignoredAttackPlatformIds.Remove(record.platformCollider.GetInstanceID());
            }

            ignoredAttackPlatforms.RemoveAt(index);
        }

        private void TryResolveWarriorJumpHit(Collision2D collision)
        {
            if (!attackJumpActive) return;
            if (attackResolved) return;
            if (IsDeadOrDying) return;
            if (collision == null || collision.collider == null) return;

            if (!TryGetWarriorHitOrShieldCollider(collision.collider, out Warrior warrior, out bool directShieldContact))
                return;

            Vector2 point = GetCollisionPoint(collision);

            // Shield rule:
            // If Arachnee hits the Shield Laser layer, or Warrior's shield is up while
            // Arachnee reaches the Hit Box layer, the explosion is resolved but Warrior
            // takes no damage.
            bool blockedByShield =
                directShieldContact ||
                IsWarriorShieldBlockingArachnee(warrior, collision);

            ResolveExplosion(point, warrior, damageWarrior: !blockedByShield);
        }

        private bool TryGetWarriorHitOrShieldCollider(
            Collider2D other,
            out Warrior warrior,
            out bool directShieldContact)
        {
            warrior = null;
            directShieldContact = false;

            if (other == null)
                return false;

            int warriorLayer = LayerMask.NameToLayer("Hit Box");
            int shieldLayer = LayerMask.NameToLayer("Shield Laser");

            bool isWarriorHitBoxLayer =
                warriorLayer >= 0 &&
                other.gameObject.layer == warriorLayer;

            bool isShieldLayer =
                shieldLayer >= 0 &&
                other.gameObject.layer == shieldLayer;

            // Arachnee damage/block resolution must only be driven by Warrior's
            // gameplay hitbox or the active shield collider. This prevents random
            // Warrior child colliders from triggering the explosion.
            if (!isWarriorHitBoxLayer && !isShieldLayer)
                return false;

            warrior = other.GetComponentInParent<Warrior>();
            if (warrior == null)
                return false;

            if (warrior.IsDeadOrDying)
                return false;

            directShieldContact = isShieldLayer;
            return true;
        }

        private bool IsWarriorShieldBlockingArachnee(Warrior warrior, Collision2D collision)
        {
            if (warrior == null)
                return false;

            if (!warrior.ShieldIsUp)
                return false;

            int shieldLayer = LayerMask.NameToLayer("Shield Laser");

            Collider2D otherCollider = collision != null ? collision.collider : null;
            if (otherCollider != null &&
                shieldLayer >= 0 &&
                otherCollider.gameObject.layer == shieldLayer)
            {
                return true;
            }

            Collider2D arachneeCollider = null;
            if (collision != null && collision.otherCollider != null && IsOwnCollider(collision.otherCollider))
                arachneeCollider = collision.otherCollider;

            if (arachneeCollider == null)
                arachneeCollider = GetMainCollider();

            // If the shield collider is configured and currently touches Arachnee,
            // this is a definite shield block.
            if (warrior.shieldHitbox != null &&
                warrior.shieldHitbox.enabled &&
                arachneeCollider != null)
            {
                bool shieldColliderUsesExpectedLayer =
                    shieldLayer < 0 ||
                    warrior.shieldHitbox.gameObject.layer == shieldLayer;

                if (shieldColliderUsesExpectedLayer &&
                    (warrior.shieldHitbox.IsTouching(arachneeCollider) ||
                     warrior.shieldHitbox.bounds.Intersects(arachneeCollider.bounds)))
                {
                    return true;
                }
            }

            // Final rule requested for Arachnee: when Warrior's shield is up,
            // Arachnee cannot damage him even if Unity reports the collision through
            // the Hit Box layer first during the same physics step.
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
            RestoreAllIgnoredAttackPlatformCollisions();

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

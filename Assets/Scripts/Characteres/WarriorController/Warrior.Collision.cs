using Assets.Scripts.Characteres.EnemyContoller;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {

        #region Edge-Fall Destination Platform Anti-Tunneling

        [Header("Edge Fall Destination Platform Anti-Tunneling")]
        [SerializeField] private bool enableEdgeFallDestinationAntiTunnel = true;

        [Tooltip("Predicts the next physics step while Warrior is falling from an edge. This catches the destination platform even when CurrentplatForm is still the source platform.")]
        [SerializeField, Min(0.5f)] private float edgeFallAntiTunnelPredictionMultiplier = 1.35f;

        [Tooltip("Minimum downward speed before the physics-fall anti-tunnel check runs.")]
        [SerializeField, Min(0f)] private float edgeFallAntiTunnelMinDownSpeed = 0.01f;

        [Tooltip("Small extra distance added to the next-step physics fall prediction. Keep small: the exact previous/current sweep is also checked.")]
        [SerializeField, Min(0f)] private float physicsFallAntiTunnelExtraLookAhead = 0.03f;

        private Vector2 _lastPhysicsFallAntiTunnelPosition;
        private bool _hasLastPhysicsFallAntiTunnelPosition;

        #endregion

        #region Collision / Bounce / Contact Blocking

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var enemy = collision.collider.GetComponentInParent<Enemy>();

            if (enemy != null && TryStopPlatformStoneRepulseOnEnemyContact(enemy))
                return;

            if (_sprintActive && enemy != null && collision.collider != null)
            {
                if (_ignoredEnemyCollidersDuringSprint.Add(collision.collider))
                    SetIgnoreWithAllWarriorColliders(collision.collider, true);

                if (collision.otherCollider != null)
                    Physics2D.IgnoreCollision(collision.otherCollider, collision.collider, true);

                return;
            }

            if (enemy != null)
            {
                if (DescendentPhase && CountGroundPoints() == 0)
                {
                    if (ShieldIsUp) DoShieldStomp(enemy);
                    BounceAndLandAway(enemy);
                    return;
                }

                if ((IsFallingEdge || IsFallingPlfExit) && WarriorOverlay(enemy))
                {
                    BounceAndLandAway(enemy, plfExitMode: true);
                    IsFallingEdge = false;
                    IsFallingPlfExit = false;
                    IsFallingHitEnemy = false;
                    return;
                }

                if (CountGroundPoints() == 0 && OnCollisionHitBottom(collision))
                {
                    IsFallingHitEnemy = true;
                    StopJumpTowardCoroutine();
                    WaitAnimationDisplay();

                }

                if (!_postBounceActive && !IsFalling && activesJumpCoroutine == null)
                {
                    StopRunningOnEnemyContact(enemy);
                }

                return;
            }

            if (collision.gameObject.layer == LayerMask.NameToLayer("PlatformLayer"))
                _cmp = 0;
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            var enemy = collision.collider.GetComponentInParent<Enemy>();

            if (enemy != null && TryStopPlatformStoneRepulseOnEnemyContact(enemy))
                return;

            if (_sprintActive && collision.collider != null)
            {
                if (enemy != null && collider2 != null)
                {
                    if (_ignoredEnemyCollidersDuringSprint.Add(collision.collider))
                        Physics2D.IgnoreCollision(collider2, collision.collider, true);
                    return;
                }
            }

            // ── Morvex-top stuck guard ────────────────────────────────────────────────
            // Morvex flies with gravityScale = 0 and moves via transform.position, so
            // the warrior can land on its NormalCollider top and get stuck there if none
            // of the normal DescendentPhase / IsFallingEdge conditions were set. We allow
            // at most 2 consecutive physics frames of contact before forcing a bounce.
            if (enemy != null && enemy is MorvexMonster && WarriorSitsOnEnemyTop(enemy))
            {
                if (_morvexTopContactEnemy != enemy)
                {
                    _morvexTopContactEnemy = enemy;
                    _morvexTopContactFrames = 0;
                }

                _morvexTopContactFrames++;

                if (_morvexTopContactFrames >= 2)
                {
                    _morvexTopContactFrames = 0;
                    _morvexTopContactEnemy = null;
                    BounceAndLandAway(enemy);
                    return;
                }

                // Frame 1: not yet at threshold — do nothing else this frame.
                return;
            }
            else if (_morvexTopContactEnemy != null && _morvexTopContactEnemy == enemy)
            {
                // Contact has shifted away from yMax — reset counter.
                _morvexTopContactFrames = 0;
                _morvexTopContactEnemy = null;
            }
            // ─────────────────────────────────────────────────────────────────────────

            if (enemy == null || enemy.CurrentplatForm == null) return;

            if (!_postBounceActive && !IsFalling && activesJumpCoroutine == null)
            {
                StopRunningOnEnemyContact(enemy);
                if (_blockedByEnemyContact) return;
            }

            if (_postBounceActive) return;

            if (WarriorOverlay(enemy) && CountGroundPoints() == 0)
                BounceAndLandAway(enemy);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            var enemy = collision.collider.GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                _blockAction = false;
                ClearEnemyContactBlock(enemy);

                // ── Morvex-top counter reset ──────────────────────────────────────────
                if (enemy == _morvexTopContactEnemy)
                {
                    _morvexTopContactFrames = 0;
                    _morvexTopContactEnemy = null;
                }
                // ─────────────────────────────────────────────────────────────────────

                return;
            }

            if (collision.gameObject.layer == LayerMask.NameToLayer("PlatformLayer"))
                _cmp = 0;
        }

        private void BounceAndLandAway(Enemy enemy, bool plfExitMode = false)
        {
            if (enemy == null) return;
            if (_postBounceActive) return;

            _blockAction = true;
            _postBounceActive = true;
            _postBounceStartTime = Time.time;
            _lastBouncedEnemy = enemy;

            if (enemy.NormalCollider != null)
                _requiredClearanceX = enemy.NormalCollider.bounds.extents.x + collider2.bounds.extents.x + clearancePad;
            else
                _requiredClearanceX = collider2.bounds.extents.x + 0.2f;

            IgnoreAllEnemyColliders(enemy, true);

            Vector2 landing = CalculateLandingAwayFromEnemy(enemy, plfExitMode);
            TriggerJump(landing);
        }

        private void EndPostBounce()
        {
            if (_lastBouncedEnemy != null)
                IgnoreAllEnemyColliders(_lastBouncedEnemy, false);

            _postBounceActive = false;
            _lastBouncedEnemy = null;
            _blockAction = false;
        }

        private Vector2 CalculateLandingAwayFromEnemy(Enemy enemy, bool plfExitMode)
        {
            float enemyX = enemy.NormalCollider != null ? enemy.NormalCollider.bounds.center.x : enemy.transform.position.x;
            float dir = (transform.position.x < enemyX) ? -1f : 1f;

            float desiredSeparation = _requiredClearanceX + Mathf.Max(0f, landAwayDistance);
            if (plfExitMode) desiredSeparation += 0.08f;

            float candidate1 = enemyX + dir * desiredSeparation;
            float candidate2 = enemyX - dir * desiredSeparation;

            var platform = enemy.CurrentplatForm != null ? enemy.CurrentplatForm : CurrentplatForm;

            float y = transform.position.y;
            float minX = float.NegativeInfinity;
            float maxX = float.PositiveInfinity;

            if (platform != null && platform.platformCollider != null)
            {
                float platformTop = platform.platformCollider.bounds.max.y;
                y = platformTop + collider2.bounds.extents.y * 0.95f;

                minX = platform.platformCollider.bounds.min.x + collider2.bounds.extents.x;
                maxX = platform.platformCollider.bounds.max.x - collider2.bounds.extents.x;
            }

            float c1 = Mathf.Clamp(candidate1, minX, maxX);
            float c2 = Mathf.Clamp(candidate2, minX, maxX);

            bool c1Ok = Mathf.Abs(c1 - enemyX) >= _requiredClearanceX;
            bool c2Ok = Mathf.Abs(c2 - enemyX) >= _requiredClearanceX;

            float myX = collider2.bounds.center.x;

            if (c1Ok && c2Ok)
                return new Vector2(Mathf.Abs(c1 - myX) <= Mathf.Abs(c2 - myX) ? c1 : c2, y);

            if (c1Ok) return new Vector2(c1, y);
            if (c2Ok) return new Vector2(c2, y);

            float closest = Mathf.Abs(c1 - myX) <= Mathf.Abs(c2 - myX) ? c1 : c2;
            return new Vector2(closest, y);
        }

        public void CheckIfAvoidCollider(bool enableCollision)
        {
            var touching = new List<Collider2D>();

            var filter = new ContactFilter2D
            {
                useTriggers = true,
                useLayerMask = true
            };
            filter.SetLayerMask(LayerMask.GetMask("Enemy"));

            int count = Physics2D.GetContacts(collider2, filter, touching);
            if (count <= 0) return;

            foreach (Collider2D c in touching)
            {
                if (!c.isTrigger) continue;

                var enemyNormalCollider = touching.FirstOrDefault(x => x.isTrigger == false);
                if (enemyNormalCollider == null) continue;

                ResolveOverlap(collider2, enemyNormalCollider);
                Physics2D.IgnoreCollision(collider2, enemyNormalCollider, enableCollision);
            }
        }

        public void ResolveOverlap(Collider2D colA, Collider2D colB, bool resolveXAxisOnly = true)
        {
            Vector3 separation = (colA.transform.position - colB.transform.position).normalized * 0.1f;

            if (resolveXAxisOnly) separation.y = 0;
            else separation.x = 0;

            colA.transform.position += separation;
        }



        private void TriggerJump(Vector2 targetPosition, float height = 2.2f, float duration = 0.6f)
        {
            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();

            MarkJumpStarted(); // <--- ADD THIS

            JumpAnimationDisplay();
            activesJumpCoroutine = JumpTowardPositionAction(targetPosition, height, duration);
            StartCoroutine(activesJumpCoroutine);
        }

        private void IgnoreAllEnemyColliders(Enemy enemy, bool ignore)
        {
            if (enemy == null) return;

            var cols = enemy.GetComponentsInChildren<Collider2D>(true);
            foreach (var c in cols)
            {
                if (c == null) continue;
                Physics2D.IgnoreCollision(collider2, c, ignore);
            }
        }

        private bool WarriorOverlay(Enemy enemy)
        {
            float warriorBottom = collider2.bounds.min.y;
            float enemyTop = enemy.NormalCollider.bounds.max.y;
            return warriorBottom >= enemyTop - 0.01f;
        }

        /// <summary>
        /// Returns true when the warrior's collider bottom is within CONTACT_Y_TOLERANCE
        /// of the enemy's NormalCollider top — i.e. he is resting on top of it.
        /// </summary>
        private bool WarriorSitsOnEnemyTop(Enemy enemy)
        {
            if (enemy?.NormalCollider == null || collider2 == null) return false;

            float warriorBottom = collider2.bounds.min.y;
            float enemyTop = enemy.NormalCollider.bounds.max.y;

            return Mathf.Abs(warriorBottom - enemyTop) <= CONTACT_Y_TOLERANCE;
        }

        private bool IsSamePlatformAs(Enemy e)
        {
            if (e == null) return false;

            var myPlf = CurrentplatForm != null ? CurrentplatForm.platformCollider : null;
            var enPlf = e.CurrentplatForm != null ? e.CurrentplatForm.platformCollider : null;

            if (myPlf == null || enPlf == null)
                return true;

            return myPlf == enPlf;
        }

        private void StopRunningOnEnemyContact(Enemy enemy)
        {
            if (enemy == null) return;
            if (!IsSamePlatformAs(enemy)) return;
            if (CountGroundPoints() <= 0) return;

            bool wasMoving = activesMoveCoroutine != null || Mathf.Abs(rigidbody2.linearVelocity.x) > 0.15f;
            if (!wasMoving) return;

            _blockedByEnemyContact = true;
            _blockingEnemy = enemy;

            StopMoveTowardCoroutine();

            if (rigidbody2 != null)
            {
                var v = rigidbody2.linearVelocity;
                v.x = 0f;
                rigidbody2.linearVelocity = v;
            }

            if (CountGroundPoints() <= 1)
                ShowLosingBalance();
            else
                WaitAnimationDisplay();
        }

        private void ClearEnemyContactBlock(Enemy enemy)
        {
            if (!_blockedByEnemyContact) return;

            if (_blockingEnemy == null || enemy == null || enemy == _blockingEnemy)
            {
                _blockedByEnemyContact = false;
                _blockingEnemy = null;
            }
        }

        private bool OnCollisionHitBottom(Collision2D collision)
        {
            Vector2 avg = Vector2.zero;
            foreach (ContactPoint2D contact in collision.contacts)
                avg += contact.normal;

            avg /= collision.contactCount;
            return avg.y < -0.7f;
        }

        // ── Physics-driven fall anti-tunneling ─────────────────────────────────────────
        // This is the missing case for PerformWarriorEdgeFall().
        // After an edge fall, Warrior is not moved by JumpTowardPositionAction anymore;
        // he falls by Rigidbody2D velocity/gravity. The destination platform can be
        // different from the source platform, so CurrentplatForm is deliberately ignored.
        private void ApplyDestinationPlatformAntiTunnelDuringPhysicsFall()
        {
            if (!enableEdgeFallDestinationAntiTunnel)
            {
                _hasLastPhysicsFallAntiTunnelPosition = false;
                return;
            }

            if (collider2 == null || rigidbody2 == null || PlatformLayer.value == 0)
            {
                _hasLastPhysicsFallAntiTunnelPosition = false;
                return;
            }

            if (CanDie || _deathStarted)
            {
                _hasLastPhysicsFallAntiTunnelPosition = false;
                return;
            }

            Vector2 currentPosition = rigidbody2.position;

            if (!_hasLastPhysicsFallAntiTunnelPosition)
            {
                _lastPhysicsFallAntiTunnelPosition = currentPosition;
                _hasLastPhysicsFallAntiTunnelPosition = true;
            }

            // JumpTowardPositionAction already has its own destination-platform sweep.
            // This method is only for real Rigidbody2D / gravity-driven falling.
            if (activesJumpCoroutine != null)
            {
                _lastPhysicsFallAntiTunnelPosition = currentPosition;
                return;
            }

            Vector2 velocity = rigidbody2.linearVelocity;

            bool physicallyDescending = velocity.y < -edgeFallAntiTunnelMinDownSpeed;
            bool fallStateKnown =
                IsFallingEdge ||
                IsFallingPlfExit ||
                IsFallingGrazesEdge ||
                CountGroundPoints() == 0;

            // Robust rule:
            // If Warrior is descending fast enough, run the sweep even if a ground point
            // is stale for one frame. The destination-platform crossing test will decide
            // whether a landing is valid.
            if (!physicallyDescending && !fallStateKnown)
            {
                _lastPhysicsFallAntiTunnelPosition = currentPosition;
                return;
            }

            Vector2 resolvedLandingPosition;
            PlatFormColliderTrigger destinationPlatform;

            // 1) Previous physics position -> current physics position.
            // This catches cases where Unity already moved Warrior through the destination
            // platform during the last physics simulation step because that platform was
            // still ignored by trigger-first logic.
            if (currentPosition.y < _lastPhysicsFallAntiTunnelPosition.y - 0.0001f &&
                TryResolveDestinationPlatformTopLanding(
                    _lastPhysicsFallAntiTunnelPosition,
                    currentPosition,
                    out resolvedLandingPosition,
                    out destinationPlatform,
                    ignoreCurrentPlatform: true))
            {
                ResolvePredictedPhysicsFallLanding(resolvedLandingPosition, destinationPlatform);
                _lastPhysicsFallAntiTunnelPosition = resolvedLandingPosition;
                return;
            }

            if (!physicallyDescending)
            {
                _lastPhysicsFallAntiTunnelPosition = currentPosition;
                return;
            }

            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : Time.deltaTime;

            // 2) Current physics position -> next predicted physics position.
            // Use the actual integration formula instead of a very large multiplier:
            // pNext = p + v*dt + 0.5*g*dt^2. Then add a tiny downward cushion.
            Vector2 gravity = Physics2D.gravity * rigidbody2.gravityScale;
            Vector2 predictedDelta = velocity * dt + 0.5f * gravity * dt * dt;

            if (physicsFallAntiTunnelExtraLookAhead > 0f)
                predictedDelta += Vector2.down * physicsFallAntiTunnelExtraLookAhead;

            // Keep your existing tuning as an optional extra guard, but apply it only to
            // the predicted delta, not to the previous/current correction.
            if (edgeFallAntiTunnelPredictionMultiplier > 1f)
                predictedDelta *= edgeFallAntiTunnelPredictionMultiplier;

            Vector2 predictedNextPosition = currentPosition + predictedDelta;

            if (predictedNextPosition.y < currentPosition.y - 0.0001f &&
                TryResolveDestinationPlatformTopLanding(
                    currentPosition,
                    predictedNextPosition,
                    out resolvedLandingPosition,
                    out destinationPlatform,
                    ignoreCurrentPlatform: true))
            {
                ResolvePredictedPhysicsFallLanding(resolvedLandingPosition, destinationPlatform);
                _lastPhysicsFallAntiTunnelPosition = resolvedLandingPosition;
                return;
            }

            _lastPhysicsFallAntiTunnelPosition = currentPosition;
        }

        private void ResolvePredictedPhysicsFallLanding(Vector2 landingPosition, PlatFormColliderTrigger destinationPlatform)
        {
            MoveCharacterTo(landingPosition);
            CompletePredictedTopLanding(destinationPlatform);

            IsFallingEdge = false;
            IsFallingPlfExit = false;
            IsFallingGrazesEdge = false;
            IsFallingHitEnemy = false;
            CanMove = true;
            _blockAction = false;

            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();

            if (rigidbody2 != null)
            {
                Vector2 v = rigidbody2.linearVelocity;
                if (v.y < 0f)
                    v.y = 0f;
                rigidbody2.linearVelocity = v;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────

        #endregion
    }
}

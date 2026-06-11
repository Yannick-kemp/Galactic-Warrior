using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Assets.Scripts.Platforms
{
    [DefaultExecutionOrder(50)]
    public class MovingVerticalPlatform : PlatFormPlfColliderTrigger
    {
        [Header("Relative Limits (Offset from Start)")]
        [SerializeField] private float relativeMinY = -2f;
        [SerializeField] private float relativeMaxY = 5f;
        [SerializeField] private float moveSpeed = 2f;
        [SerializeField] private bool startsMovingUp = true;

        [Header("Lift Carry")]
        [Tooltip("When true, every CharacterController standing on the top surface is carried by the platform delta.")]
        [SerializeField] private bool carryCharactersLikeLift = true;

        [Tooltip("Keeps the rider bottom exactly on the platform top while the platform moves up or down.")]
        [SerializeField] private bool keepRidersSeatedOnSurface = true;

        [Tooltip("Small vertical offset between the platform top and the rider bottom.")]
        [SerializeField, Min(0f)] private float riderSurfaceOffset = 0.02f;

        [Tooltip("Maximum gap allowed between rider bottom and platform top before the rider is considered detached.")]
        [SerializeField, Min(0f)] private float maxRideGap = 0.18f;

        [Tooltip("Maximum amount the rider bottom may sink into the platform top and still be considered a rider.")]
        [SerializeField, Min(0f)] private float maxSinkIntoTop = 0.08f;

        [Tooltip("Horizontal overlap skin used to know if the rider is still above the lift surface.")]
        [SerializeField, Min(0f)] private float horizontalRideSkin = 0.03f;

        [Tooltip("If the character is going upward faster than this, the lift releases him instead of gluing him to the top.")]
        [SerializeField, Min(0f)] private float jumpOffVelocity = 0.12f;

        [Tooltip("Vertical contact normal threshold used to recognize top-surface contacts.")]
        [SerializeField, Range(0f, 1f)] private float topContactNormalThreshold = 0.45f;

        [Header("Respawn On Lift")]
        [Tooltip("Extra surface offset used only when a character is respawned directly onto this lift.")]
        [SerializeField, Min(0f)] private float respawnSurfaceOffset = 0.04f;

        [Tooltip("Horizontal skin used when clamping the respawn X inside the lift top bounds.")]
        [SerializeField, Min(0f)] private float respawnHorizontalSkin = 0.08f;

        [Tooltip("Small fixed-update grace period that keeps the respawned character registered as a lift rider.")]
        [SerializeField, Min(0f)] private float respawnSeatGraceSeconds = 0.25f;

        [Header("System")]
        [SerializeField] private string respawnId;

        private float _worldMinY;
        private float _worldMaxY;
        private bool _isMovingUp;
        private Rigidbody2D _platformBody;
        private Vector2 _lastLiftDelta;

        private readonly HashSet<CharacterController> _riders = new HashSet<CharacterController>();
        private readonly List<CharacterController> _ridersToRemove = new List<CharacterController>();
        private readonly Dictionary<int, Coroutine> _exitValidationCoroutines = new Dictionary<int, Coroutine>();
        private readonly Dictionary<int, Coroutine> _respawnSeatCoroutines = new Dictionary<int, Coroutine>();

        public string RespawnId => respawnId;
        public bool IsMovingUpNow => _isMovingUp;
        public bool IsMovingDownNow => !_isMovingUp;
        public Vector2 LastLiftDelta => _lastLiftDelta;

        /// <summary>
        /// True while this lift is actively carrying the given character as a registered rider.
        /// This is the authoritative "grounded on the lift" signal: it stays valid even while the
        /// platform descends and the rider's foot ground points momentarily float a hair above the
        /// lift top (riderSurfaceOffset), where CountGroundPoints() would otherwise read 0.
        /// </summary>
        public bool IsCarrying(CharacterController character)
        {
            return character != null && _riders.Contains(character);
        }

        private void Awake()
        {
#if UNITY_ANDROID
            Time.fixedDeltaTime = 0.01667f;
            Application.targetFrameRate = 60;
#endif
        }


        protected override void Start()
        {
            base.Start();

            _platformBody = GetComponent<Rigidbody2D>();

            if (_platformBody != null)
            {
                // Match the proven MovingHorizontalPlatform setup: a Kinematic body moved
                // with MovePosition under Interpolate. A directly-assigned Rigidbody2D.position
                // is a non-interpolated teleport, which renders the platform one physics step
                // ahead of an Interpolated rider carried with MovePosition. That offset is the
                // micro-separation / jitter, most visible while descending.
                _platformBody.bodyType = RigidbodyType2D.Kinematic;
                _platformBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                _platformBody.gravityScale = 0f;
                _platformBody.interpolation = RigidbodyInterpolation2D.Interpolate;
                _platformBody.constraints |= RigidbodyConstraints2D.FreezeRotation;
            }

            float startY = transform.position.y;
            _worldMinY = startY + relativeMinY;
            _worldMaxY = startY + relativeMaxY;

            if (_worldMinY > _worldMaxY)
            {
                float temp = _worldMinY;
                _worldMinY = _worldMaxY;
                _worldMaxY = temp;
            }

            _isMovingUp = startsMovingUp;

            if (string.IsNullOrEmpty(respawnId))
                respawnId = $"VP_{name}_{transform.position.x:F1}_{_worldMinY:F1}";
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            if (!PlatformMotionEnabled)
            {
                _lastLiftDelta = Vector2.zero;

                // Motion is paused only. Keep rider validation and surface seating alive.
                if (carryCharactersLikeLift)
                    CarryRegisteredRiders(_lastLiftDelta);

                return;
            }

            // Compute this step's vertical delta but do NOT move the body yet.
            Vector2 current = transform.position;
            float targetY = _isMovingUp ? _worldMaxY : _worldMinY;
            float newY = Mathf.MoveTowards(current.y, targetY, moveSpeed * Time.fixedDeltaTime);
            Vector2 next = new Vector2(current.x, newY);
            _lastLiftDelta = next - current;

            // Carry the riders FIRST, using the predicted delta, then commit the platform
            // body. Both the platform and its riders are moved with MovePosition in the same
            // physics step, so their interpolated render poses stay locked together and no
            // micro-gap can open between the rider's feet and the platform top.
            if (carryCharactersLikeLift)
                CarryRegisteredRiders(_lastLiftDelta);

            if (_lastLiftDelta.sqrMagnitude > 0.0000001f)
                MovePlatformTo(next);

            if (Mathf.Abs(newY - targetY) < 0.001f)
                _isMovingUp = !_isMovingUp;
        }

        private void MovePlatformTo(Vector2 position)
        {
            // MovePosition (not a direct position/transform assignment) so the Interpolated
            // kinematic body renders in lockstep with the riders, which are also carried with
            // MovePosition. No mid-step transform write or SyncTransforms: those defeat
            // interpolation and reintroduce the jitter.
            if (_platformBody != null)
                _platformBody.MovePosition(position);
            else
                transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        protected override void OnCollisionEnter2D(Collision2D collision)
        {
            //var character = collision.collider.GetComponentInParent<CharacterController>();
            //if (character is M97Monster)

            //{
            //    int t = 0;


            //}
            base.OnCollisionEnter2D(collision);
            //var character = collision.collider.GetComponentInParent<CharacterController>();
            //if (character is not ZalaytyMonster)

            //{
            //    return;


            //}
            TryRegisterRiderFromCollision(collision, snapImmediately: true);
        }

        protected override void OnCollisionStay2D(Collision2D collision)
        {
            base.OnCollisionStay2D(collision);
            //var character = collision.collider.GetComponentInParent<CharacterController>();
            //if (character is not ZalaytyMonster)

            //{
            //    return;


            //}
            TryRegisterRiderFromCollision(collision, snapImmediately: false);
        }

        protected override void OnCollisionExit2D(Collision2D collision)
        {
            base.OnCollisionExit2D(collision);


            //CharacterController character = collision.collider.GetComponentInParent<CharacterController>();
            //if (character == null)
            //    return;
            //StartExitValidation(character);
        }

        private void TryRegisterRiderFromCollision(Collision2D collision, bool snapImmediately)
        {
            if (!carryCharactersLikeLift || platformCollider == null)
                return;

            CharacterController character = collision.collider.GetComponentInParent<CharacterController>();
            if (character == null)
                return;

            if (IsPlatformIgnoredByCharacter(character))
                return;

            if (!IsTopSurfaceContact(collision, character))
                return;

            // If the enemy is intentionally a child of this platform,
            // do not also add it to the lift rider list.
            // Otherwise it would be moved twice: once by the parent transform,
            // and once by CarryRegisteredRiders.
            if (IsAlreadyMovedByThisPlatformHierarchy(character))
            {
                character.CurrentplatForm = this;
                StopDownwardVelocity(character);

                if (character is Warrior hierarchyWarrior)
                    ConfirmWarriorLiftLandingState(hierarchyWarrior);

                return;
            }

            AddRider(character);
            character.CurrentplatForm = this;
            ConfirmRiderTopSupport(character);

            if (character is Warrior warrior)
            {
                ConfirmWarriorLiftLandingState(warrior);
            }
            else if (character is ZalaytyMonster zalayty)
            {
                ConfirmZalaytyTopSupport(zalayty);
            }

            StopDownwardVelocity(character);

            if (snapImmediately && keepRidersSeatedOnSurface)
                MoveRiderToSurface(character, Vector2.zero);
        }
        private void ConfirmWarriorLiftLandingState(Warrior warrior)
        {
            if (warrior == null)
                return;

            // The lift already confirmed a real top-surface contact.
            // So do not use CountGroundPoints() as the main landing proof here.
            warrior.StopJumpTowardCoroutine();

            warrior.CanMove = true;
            warrior.CanAttackWarrior = true;

            warrior.IsFallingEdge = false;
            warrior.IsFallingPlfExit = false;
            warrior.IsFallingHitEnemy = false;
            warrior.IsFallingGrazesEdge = false;

            // Confirmed top-surface landing on the lift: clear the descent flag too. Otherwise it
            // stays stale-true through the platform's downward leg (where CountGroundPoints() reads
            // 0) and an enemy touch would wrongly trigger the "descending onto enemy" bounce path.
            warrior.DescendentPhase = false;

            warrior._blockAction = false;
            warrior.LastSafePlatform = this;
            warrior.LastSafePosition =
                GetSafeRespawnPositionFor(warrior, warrior.transform.position.x);

            bool warriorIsMoving =
                warrior.activesMoveCoroutine != null ||
                (warrior.rigidbody2 != null &&
                 Mathf.Abs(warrior.rigidbody2.linearVelocity.x) > 0.05f);

            bool warriorIsAttacking =
                warrior.animator != null &&
                (
                    warrior.animator.GetBool("isAttacking") ||
                    warrior.animator.GetBool("isAttacking2") ||
                    warrior.animator.GetBool("isAttacking3")
                );

            bool warriorIsDying =
                warrior.animator != null &&
                warrior.animator.GetBool("isDying");

            bool warriorIsLosingControl =
                warrior.animator != null &&
                warrior.animator.GetBool("IsLosingCtrl");

            if (warriorIsAttacking || warriorIsDying || warriorIsLosingControl)
                return;

            if (warriorIsMoving)
                warrior.RunAnimationDisplay();
            else
                warrior.WaitAnimationDisplay();
        }


        public Vector3 GetSafeRespawnPositionFor(CharacterController character, float preferredWorldX)
        {
            Physics2D.SyncTransforms();

            if (platformCollider == null)
                return transform.position;

            Collider2D support = GetStandingCollider(character);
            Bounds platformBounds = platformCollider.bounds;

            float halfWidth = support != null ? support.bounds.extents.x : 0.4f;
            float halfHeight = support != null ? support.bounds.extents.y : 0.8f;

            float minX = platformBounds.min.x + halfWidth + respawnHorizontalSkin;
            float maxX = platformBounds.max.x - halfWidth - respawnHorizontalSkin;

            float safeX = minX <= maxX
                ? Mathf.Clamp(preferredWorldX, minX, maxX)
                : platformBounds.center.x;

            float safeY =
                platformBounds.max.y +
                halfHeight +
                Mathf.Max(riderSurfaceOffset, respawnSurfaceOffset);

            float safeZ = character != null
                ? character.transform.position.z
                : transform.position.z;

            return new Vector3(safeX, safeY, safeZ);
        }

        public void RespawnRiderOnLift(CharacterController character, float preferredWorldX)
        {
            if (character == null || platformCollider == null)
                return;

            Physics2D.SyncTransforms();

            Vector3 safePosition = GetSafeRespawnPositionFor(character, preferredWorldX);

            SetPlatformCollisionForCharacter(character, ignore: false);

            if (character is Warrior warrior)
            {
                warrior.StopMoveTowardCoroutine();
                warrior.StopJumpTowardCoroutine();

                warrior.CanMove = true;
                warrior.CanAttackWarrior = true;

                warrior.IsFallingEdge = false;
                warrior.IsFallingPlfExit = false;
                warrior.IsFallingHitEnemy = false;
                warrior.IsFallingGrazesEdge = false;
                warrior.DescendentPhase = false;

                warrior._blockAction = false;
                warrior.LastSafePlatform = this;
                warrior.LastSafePosition = safePosition;
            }
            else if (character is ZalaytyMonster zalayty)
            {
                zalayty.StopMoveTowardCoroutine();
                zalayty.StopJumpTowardCoroutine();
                ConfirmZalaytyTopSupport(zalayty);
            }

            if (character.rigidbody2 != null)
            {
                character.rigidbody2.simulated = true;
                character.rigidbody2.linearVelocity = Vector2.zero;
                character.rigidbody2.angularVelocity = 0f;
                character.rigidbody2.constraints = RigidbodyConstraints2D.FreezeRotation;
                character.rigidbody2.position = new Vector2(safePosition.x, safePosition.y);
                character.transform.position = safePosition;
                character.rigidbody2.WakeUp();
            }
            else
            {
                character.transform.position = safePosition;
            }

            Physics2D.SyncTransforms();

            AddRider(character);
            character.CurrentplatForm = this;
            ConfirmRiderTopSupport(character);

            StopDownwardVelocity(character);

            if (keepRidersSeatedOnSurface)
                MoveRiderToSurface(character, Vector2.zero);

            StartRespawnSeatGrace(character);
        }

        private void StartRespawnSeatGrace(CharacterController character)
        {
            if (character == null)
                return;

            int id = character.GetInstanceID();

            if (_respawnSeatCoroutines.TryGetValue(id, out Coroutine oldRoutine) && oldRoutine != null)
                StopCoroutine(oldRoutine);

            _respawnSeatCoroutines[id] = StartCoroutine(RespawnSeatGraceRoutine(character, id));
        }

        private IEnumerator RespawnSeatGraceRoutine(CharacterController character, int id)
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();
            float endTime = Time.time + respawnSeatGraceSeconds;

            while (character != null &&
                   platformCollider != null &&
                   Time.time < endTime)
            {
                SetPlatformCollisionForCharacter(character, ignore: false);

                AddRider(character);
                character.CurrentplatForm = this;
                ConfirmRiderTopSupport(character);

                StopDownwardVelocity(character);

                if (keepRidersSeatedOnSurface)
                    MoveRiderToSurface(character, Vector2.zero);

                if (character is Warrior warrior)
                {
                    warrior.CanMove = true;
                    warrior.CanAttackWarrior = true;
                    warrior.IsFallingEdge = false;
                    warrior.IsFallingPlfExit = false;
                    warrior.IsFallingHitEnemy = false;
                    warrior.IsFallingGrazesEdge = false;
                    warrior.DescendentPhase = false;
                    warrior._blockAction = false;
                    warrior.LastSafePlatform = this;
                    warrior.LastSafePosition = GetSafeRespawnPositionFor(warrior, warrior.transform.position.x);
                }
                else if (character is ZalaytyMonster zalayty)
                {
                    ConfirmZalaytyTopSupport(zalayty);
                }

                yield return wait;
            }

            _respawnSeatCoroutines.Remove(id);
        }

        private void SetPlatformCollisionForCharacter(CharacterController character, bool ignore)
        {
            if (character == null || platformCollider == null)
                return;

            Collider2D[] colliders = character.GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D col = colliders[i];

                if (col == null || col.isTrigger)
                    continue;

                Physics2D.IgnoreCollision(platformCollider, col, ignore);
            }
        }

        private void AddRider(CharacterController character)
        {
            if (character == null)
                return;

            _riders.Add(character);

            int id = character.GetInstanceID();

            if (_exitValidationCoroutines.TryGetValue(id, out Coroutine routine) && routine != null)
                StopCoroutine(routine);

            _exitValidationCoroutines.Remove(id);
        }

        private void ConfirmRiderTopSupport(CharacterController character)
        {
            if (character is ZalaytyMonster zalayty)
                ConfirmZalaytyTopSupport(zalayty);
        }

        private void ConfirmZalaytyTopSupport(ZalaytyMonster zalayty)
        {
            if (zalayty == null)
                return;

            // This is important: ZalaytyMonster.SetJumping(false) only changes
            // isOnEdgePlatform in the current file. It does not clear the internal
            // _isJumping flag or the active jump coroutine. NotifyMovingPlatformTopSupport()
            // is the Zalayty-specific landing confirmation that clears those states.
            zalayty.NotifyMovingPlatformTopSupport(this);
        }

        private void CarryRegisteredRiders(Vector2 liftDelta)
        {


            if (_riders.Count == 0)
                return;

            _ridersToRemove.Clear();

            foreach (CharacterController rider in _riders)
            {
                if (!CanContinueRiding(rider))
                {
                    _ridersToRemove.Add(rider);
                    continue;
                }

                rider.CurrentplatForm = this;
                ConfirmRiderTopSupport(rider);

                // Zalayty's same-platform chase uses independent MovePosition inside a
                // WaitForFixedUpdate coroutine. For active Zalayty movement, do not move
                // him from the platform FixedUpdate; expose this lift delta and let
                // MoveZalaytyBody() add it to his own requested X movement. This avoids
                // both run-in-place and double vertical carry.
                if (rider is ZalaytyMonster movingZalayty &&
                    movingZalayty.ShouldApplyVerticalMovingPlatformCarryInsideOwnMove())
                {
                    continue;
                }

                Vector2 finalDelta = liftDelta;

                if (keepRidersSeatedOnSurface)
                    finalDelta = GetSurfaceCorrectedDelta(rider, liftDelta);

                MoveRiderByDelta(rider, finalDelta);

                if (rider is Warrior warrior)
                {
                    warrior.LastSafePlatform = this;
                    warrior.LastSafePosition = GetSafeRespawnPositionFor(warrior, warrior.transform.position.x);
                }
                else if (rider is ZalaytyMonster zalayty)
                {
                    ConfirmZalaytyTopSupport(zalayty);
                }
            }

            for (int i = 0; i < _ridersToRemove.Count; i++)
                RemoveRider(_ridersToRemove[i], clearPlatformIfDetached: true);
        }

        private bool CanContinueRiding(CharacterController rider)
        {
            if (rider == null || !rider.gameObject.activeInHierarchy)
                return false;

            if (platformCollider == null)
                return false;

            Collider2D support = GetStandingCollider(rider);
            if (support == null || !support.enabled)
                return false;

            if (IsPlatformIgnoredByCharacter(rider))
            {
                Debug.Log("[LiftDetach] platform ignored for " + rider.name);
                return false;
            }


            if (rider.rigidbody2 != null && rider.rigidbody2.linearVelocity.y > jumpOffVelocity)
            {
                Debug.Log("[LiftDetach] upward velocity too high: " + rider.rigidbody2.linearVelocity.y);
                return false;
            }

            if (!IsHorizontallyOverPlatform(support))
            {
                Debug.Log("[LiftDetach] not horizontally over platform for " + rider.name);
                return false;
            }

            bool bottomCloseToTop = IsBottomCloseToPlatformTop(support);
            if (!bottomCloseToTop)
            {
                Debug.Log("[LiftDetach] bottom not close to platform top");
                return false;
            }

            // Do not glue/cancel an active controlled jump that starts from the lift.
            // Landing confirmation is done by OnCollisionEnter2D / OnCollisionStay2D.
            if (rider is ZalaytyMonster && rider.activesJumpCoroutine != null)
            {
                Debug.Log("[LiftDetach] Zalayty active jump coroutine for " + rider.name);
                return false;
            }

            // If Zalayty is physically seated on the lift, confirm the landing before
            // checking IsJumping. Otherwise a stale _isJumping value makes him detach
            // immediately and he cannot chase/move on the elevator.
            ConfirmRiderTopSupport(rider);

            if (rider.IsJumping)
            {
                Debug.Log("[LiftDetach] IsJumping true for " + rider.name);
                return false;
            }

            return true;
        }

        private bool IsTopSurfaceContact(Collision2D collision, CharacterController character)
        {
            Collider2D support = GetStandingCollider(character);
            if (support == null || platformCollider == null)
                return false;

            if (!IsHorizontallyOverPlatform(support))
                return false;

            bool bottomIsNearTop = IsBottomCloseToPlatformTop(support);
            bool centerIsAboveTop = support.bounds.center.y >= platformCollider.bounds.max.y;

            if (bottomIsNearTop && centerIsAboveTop)
                return true;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);

                if (Mathf.Abs(contact.normal.y) < topContactNormalThreshold)
                    continue;

                if (centerIsAboveTop)
                    return true;
            }

            return false;
        }

        private bool IsHorizontallyOverPlatform(Collider2D support)
        {
            if (support == null || platformCollider == null)
                return false;

            Bounds pb = platformCollider.bounds;
            Bounds cb = support.bounds;

            return cb.max.x > pb.min.x + horizontalRideSkin &&
                   cb.min.x < pb.max.x - horizontalRideSkin;
        }

        private bool IsBottomCloseToPlatformTop(Collider2D support)
        {
            if (support == null || platformCollider == null)
                return false;

            float desiredBottom = platformCollider.bounds.max.y + riderSurfaceOffset;
            float bottomDelta = support.bounds.min.y - desiredBottom;

            return bottomDelta >= -maxSinkIntoTop && bottomDelta <= maxRideGap;
        }

        private Vector2 GetSurfaceCorrectedDelta(CharacterController rider, Vector2 liftDelta)
        {
            Collider2D support = GetStandingCollider(rider);
            if (support == null || platformCollider == null)
                return liftDelta;

            // The platform body has not been committed yet this physics step (it is moved
            // via MovePosition after the riders are carried), so its collider bounds still
            // hold the pre-step top. Predict the post-step top by adding the lift delta, and
            // seat the rider bottom exactly onto it. This makes the seat deterministic for
            // ascending, descending, direction changes and the paused/zero-delta case.
            float predictedTop = platformCollider.bounds.max.y + liftDelta.y;
            float bottomAfterLift = support.bounds.min.y + liftDelta.y;
            float desiredBottom = predictedTop + riderSurfaceOffset;
            float correctionY = desiredBottom - bottomAfterLift;

            return new Vector2(liftDelta.x, liftDelta.y + correctionY);
        }

        public bool TryGetVerticalCarryDeltaForCurrentFixedStep(CharacterController rider, out Vector2 carryDelta)
        {
            carryDelta = Vector2.zero;

            if (!carryCharactersLikeLift || rider == null || platformCollider == null)
                return false;

            if (!_riders.Contains(rider))
                return false;

            if (!CanContinueRiding(rider))
                return false;

            Vector2 finalDelta = keepRidersSeatedOnSurface
                ? GetSurfaceCorrectedDelta(rider, _lastLiftDelta)
                : _lastLiftDelta;

            // This is a vertical lift. Never expose horizontal movement as rider carry.
            carryDelta = new Vector2(0f, finalDelta.y);
            return Mathf.Abs(carryDelta.y) > 0.000001f;
        }

        private void MoveRiderToSurface(CharacterController rider, Vector2 extraDelta)
        {
            Vector2 delta = GetSurfaceCorrectedDelta(rider, extraDelta);
            MoveRiderByDelta(rider, delta);
        }

        private void MoveRiderByDelta(CharacterController rider, Vector2 delta)
        {
            if (rider == null || Mathf.Abs(delta.y) <= 0.000001f)
                return;

            // This is a vertical lift. Never apply/write rider X here.
            // Writing Rigidbody2D.position from the lift can cancel Warrior input
            // because Rigidbody2D.position may still contain the old X while
            // CharacterController already requested a new MovePosition X.
            Vector2 liftOnlyDelta = new Vector2(0f, delta.y);

            // Zalayty may have a special independent move request. Keep it first.
            // ApplyMovingPlatformMergedIndependentMove routes through MovePosition; do not
            // SyncTransforms afterwards, otherwise the rider is snapped to its target and
            // loses interpolation against the platform (the jitter source).
            if (rider is ZalaytyMonster zalayty &&
                zalayty.TryGetIndependentMoveRequestForCurrentFixedStep(out Vector2 zalaytyRequestedPosition))
            {
                Vector2 mergedPosition = zalaytyRequestedPosition + liftOnlyDelta;
                zalayty.ApplyMovingPlatformMergedIndependentMove(mergedPosition);
                return;
            }

            // Generic CharacterController merge: Warrior, M97, and other enemies.
            // Preserve the character's requested X and add only the lift Y. This also routes
            // through MovePosition (RequestCoreMovePosition), so no SyncTransforms here.
            if (rider.TryGetCoreMoveRequestForRecentStep(out Vector2 requestedPosition))
            {
                Vector2 mergedPosition = requestedPosition + liftOnlyDelta;
                rider.ApplyMovingPlatformMergedCoreMove(mergedPosition);
                return;
            }

            // No controller movement requested this step: lift the rider vertically only,
            // with MovePosition so it interpolates in lockstep with the platform body.
            if (rider.rigidbody2 != null)
            {
                Vector2 target = rider.rigidbody2.position;
                target.y += liftOnlyDelta.y;
                rider.rigidbody2.MovePosition(target);
            }
            else
            {
                Vector3 position = rider.transform.position;
                position.y += liftOnlyDelta.y;
                rider.transform.position = position;
            }
        }

        private void StopDownwardVelocity(CharacterController rider)
        {
            if (rider == null || rider.rigidbody2 == null)
                return;

            Vector2 velocity = rider.rigidbody2.linearVelocity;

            if (velocity.y < 0f)
            {
                velocity.y = 0f;
                rider.rigidbody2.linearVelocity = velocity;
            }
        }

        private bool IsPlatformIgnoredByCharacter(CharacterController character)
        {
            if (character == null || platformCollider == null)
                return false;

            Collider2D[] colliders = character.GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D col = colliders[i];

                if (col == null || col.isTrigger)
                    continue;

                if (Physics2D.GetIgnoreCollision(platformCollider, col))
                    return true;
            }

            return false;
        }

        private void StartExitValidation(CharacterController character)
        {
            if (character == null)
                return;

            int id = character.GetInstanceID();

            if (_exitValidationCoroutines.TryGetValue(id, out Coroutine routine) && routine != null)
                StopCoroutine(routine);

            _exitValidationCoroutines[id] = StartCoroutine(ValidateExitAfterPhysics(character, id));
        }

        private IEnumerator ValidateExitAfterPhysics(CharacterController character, int id)
        {
            yield return new WaitForFixedUpdate();

            _exitValidationCoroutines.Remove(id);

            if (character == null)
                yield break;

            if (!CanContinueRiding(character))
                RemoveRider(character, clearPlatformIfDetached: true);
        }

        private void RemoveRider(CharacterController character, bool clearPlatformIfDetached)
        {
            if (character == null)
                return;

            _riders.Remove(character);

            int id = character.GetInstanceID();

            if (_exitValidationCoroutines.TryGetValue(id, out Coroutine exitRoutine) && exitRoutine != null)
                StopCoroutine(exitRoutine);

            _exitValidationCoroutines.Remove(id);

            if (_respawnSeatCoroutines.TryGetValue(id, out Coroutine respawnRoutine) && respawnRoutine != null)
                StopCoroutine(respawnRoutine);

            _respawnSeatCoroutines.Remove(id);

            if (clearPlatformIfDetached && character.CurrentplatForm == this)
            {
                Collider2D support = GetStandingCollider(character);

                bool stillGeometricallyOnTop =
                    support != null &&
                    !IsPlatformIgnoredByCharacter(character) &&
                    IsHorizontallyOverPlatform(support) &&
                    IsBottomCloseToPlatformTop(support);

                if (!stillGeometricallyOnTop)
                    character.CurrentplatForm = null;
            }
        }

        private void OnDisable()
        {
            foreach (KeyValuePair<int, Coroutine> pair in _exitValidationCoroutines)
            {
                if (pair.Value != null)
                    StopCoroutine(pair.Value);
            }

            foreach (KeyValuePair<int, Coroutine> pair in _respawnSeatCoroutines)
            {
                if (pair.Value != null)
                    StopCoroutine(pair.Value);
            }

            _exitValidationCoroutines.Clear();
            _respawnSeatCoroutines.Clear();
            _riders.Clear();
            _ridersToRemove.Clear();
        }

        public Vector3 GetSurfacePosition()
        {
            Physics2D.SyncTransforms();

            if (platformCollider == null)
                return transform.position;

            return new Vector3(
                platformCollider.bounds.center.x,
                platformCollider.bounds.max.y,
                transform.position.z
            );
        }
        private bool IsAlreadyMovedByThisPlatformHierarchy(CharacterController character)
        {
            return character != null &&
                   character.transform != null &&
                   character.transform.IsChildOf(transform);
        }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Vector3 center = transform.position;

            Vector3 top = new Vector3(center.x, center.y + relativeMaxY, center.z);
            Vector3 bottom = new Vector3(center.x, center.y + relativeMinY, center.z);

            Gizmos.DrawLine(top, bottom);
            Gizmos.DrawCube(top, new Vector3(1f, 0.1f, 0.1f));
            Gizmos.DrawCube(bottom, new Vector3(1f, 0.1f, 0.1f));
        }
    }
}
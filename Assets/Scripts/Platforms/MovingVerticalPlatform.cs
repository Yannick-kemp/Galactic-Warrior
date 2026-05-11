using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Platforms
{
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

        protected override void Start()
        {
            base.Start();

            _platformBody = GetComponent<Rigidbody2D>();

            if (_platformBody != null)
            {
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

            _lastLiftDelta = MovePlatformStep();

            if (carryCharactersLikeLift)
                CarryRegisteredRiders(_lastLiftDelta);
        }

        private Vector2 MovePlatformStep()
        {
            Vector2 current = transform.position;
            float targetY = _isMovingUp ? _worldMaxY : _worldMinY;
            float newY = Mathf.MoveTowards(current.y, targetY, moveSpeed * Time.fixedDeltaTime);
            Vector2 next = new Vector2(current.x, newY);
            Vector2 delta = next - current;

            if (delta.sqrMagnitude > 0.0000001f)
                MovePlatformTo(next);

            if (Mathf.Abs(newY - targetY) < 0.001f)
                _isMovingUp = !_isMovingUp;

            return delta;
        }

        private void MovePlatformTo(Vector2 position)
        {
            if (_platformBody != null)
                _platformBody.position = position;

            transform.position = new Vector3(position.x, position.y, transform.position.z);
            Physics2D.SyncTransforms();
        }

        protected override void OnCollisionEnter2D(Collision2D collision)
        {
            base.OnCollisionEnter2D(collision);
            TryRegisterRiderFromCollision(collision, snapImmediately: true);
        }

        protected override void OnCollisionStay2D(Collision2D collision)
        {
            base.OnCollisionStay2D(collision);
            TryRegisterRiderFromCollision(collision, snapImmediately: false);
        }

        protected override void OnCollisionExit2D(Collision2D collision)
        {
            base.OnCollisionExit2D(collision);

            CharacterController character = collision.collider.GetComponentInParent<CharacterController>();
            if (character == null)
                return;

            StartExitValidation(character);
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

            AddRider(character);
            character.CurrentplatForm = this;

            if (character is Warrior warrior)
            {
                warrior.CanMove = true;
                warrior.CanAttackWarrior = true;
                warrior.IsFallingEdge = false;
                warrior.IsFallingPlfExit = false;
                warrior.IsFallingHitEnemy = false;
                warrior.IsFallingGrazesEdge = false;
                warrior._blockAction = false;
                warrior.LastSafePlatform = this;
                warrior.LastSafePosition = GetSafeRespawnPositionFor(warrior, warrior.transform.position.x);
            }
            else if (character is ZalaytyMonster zalayty)
            {
                zalayty.SetJumping(false);
            }

            StopDownwardVelocity(character);

            if (snapImmediately && keepRidersSeatedOnSurface)
                MoveRiderToSurface(character, Vector2.zero);
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

                warrior._blockAction = false;
                warrior.LastSafePlatform = this;
                warrior.LastSafePosition = safePosition;
            }
            else if (character is ZalaytyMonster zalayty)
            {
                zalayty.StopMoveTowardCoroutine();
                zalayty.StopJumpTowardCoroutine();
                zalayty.SetJumping(false);
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
                    warrior._blockAction = false;
                    warrior.LastSafePlatform = this;
                    warrior.LastSafePosition = GetSafeRespawnPositionFor(warrior, warrior.transform.position.x);
                }
                else if (character is ZalaytyMonster zalayty)
                {
                    zalayty.SetJumping(false);
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

                Vector2 finalDelta = liftDelta;

                if (keepRidersSeatedOnSurface)
                    finalDelta = GetSurfaceCorrectedDelta(rider, liftDelta);

                MoveRiderByDelta(rider, finalDelta);
                rider.CurrentplatForm = this;

                if (rider is Warrior warrior)
                {
                    warrior.LastSafePlatform = this;
                    warrior.LastSafePosition = GetSafeRespawnPositionFor(warrior, warrior.transform.position.x);
                }
                else if (rider is ZalaytyMonster zalayty)
                {
                    zalayty.SetJumping(false);
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

            if (IsPlatformIgnoredByCharacter(rider)) {
                Debug.Log("[LiftDetach] platform ignored for " + rider.name);
                return false;
            }


            if (rider.IsJumping) {
                Debug.Log("[LiftDetach] IsJumping true for " + rider.name);
                return false;
            }


            if (rider.rigidbody2 != null && rider.rigidbody2.linearVelocity.y > jumpOffVelocity) {
                Debug.Log("[LiftDetach] upward velocity too high: " + rider.rigidbody2.linearVelocity.y);
                return false; 
            }


            if (!IsHorizontallyOverPlatform(support)) {

                    Debug.Log("[LiftDetach] not horizontally over platform for " + rider.name);
                return false;
            }

            if (!IsBottomCloseToPlatformTop(support))
                Debug.Log("[LiftDetach] bottom not close to platform top");
            return IsBottomCloseToPlatformTop(support);
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

            float bottomAfterLift = support.bounds.min.y + liftDelta.y;
            float desiredBottom = platformCollider.bounds.max.y + riderSurfaceOffset;
            float correctionY = desiredBottom - bottomAfterLift;

            return new Vector2(liftDelta.x, liftDelta.y + correctionY);
        }

        private void MoveRiderToSurface(CharacterController rider, Vector2 extraDelta)
        {
            Vector2 delta = GetSurfaceCorrectedDelta(rider, extraDelta);
            MoveRiderByDelta(rider, delta);
        }

        private void MoveRiderByDelta(CharacterController rider, Vector2 delta)
        {
            if (rider == null || delta.sqrMagnitude <= 0.0000001f)
                return;

            // Zalayty has his own horizontal AI movement. When the lift executes after
            // Zalayty's MovePosition in the same fixed step, Rigidbody2D.position still
            // contains the old X. Using old position + lift delta would cancel his X move.
            // Merge the lift delta into Zalayty's pending movement target instead.
            if (rider is ZalaytyMonster zalayty &&
                zalayty.TryGetIndependentMoveRequestForCurrentFixedStep(out Vector2 requestedPosition))
            {
                Vector2 mergedPosition = requestedPosition + delta;
                zalayty.ApplyMovingPlatformMergedIndependentMove(mergedPosition);
                Physics2D.SyncTransforms();
                return;
            }

            if (rider.rigidbody2 != null)
            {
                Vector2 target = rider.rigidbody2.position + delta;
                rider.rigidbody2.position = target;
            }
            else
            {
                rider.transform.position += (Vector3)delta;
            }

            Physics2D.SyncTransforms();
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
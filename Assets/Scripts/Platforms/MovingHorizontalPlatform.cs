using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Platforms
{
    /// <summary>
    /// Horizontal moving platform that behaves like a lift/carrier.
    ///
    /// Important design rule:
    /// Riders are tracked by the ROOT CharacterController when possible, not by the exact
    /// child collider / Rigidbody2D that generated the collision callback. This makes the
    /// platform stable for Warrior, ZalaytyMonster, and other CharacterController-based enemies
    /// that use child colliders.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovingHorizontalPlatform : PlatFormPlfColliderTrigger
    {
        [Header("Center X Range (local offsets from start)")]
        [Tooltip("Left offset from the START position of the platform center.")]
        public float xMin = -2f;

        [Tooltip("Right offset from the START position of the platform center.")]
        public float xMax = 2f;

        [Header("Motion")]
        [Min(0.01f)] public float speed = 2.5f;
        [Min(0f)] public float waitAtLeft = 0.25f;
        [Min(0f)] public float waitAtRight = 0.25f;
        [Min(0.0001f)] public float arriveEpsilon = 0.01f;

        [Header("Passengers / Lift Carry")]
        [Tooltip("If true, every Rigidbody2D really standing on the top surface is carried. If false, only CharacterController passengers are carried.")]
        [SerializeField] private bool carryAllStandingRigidbodies = true;

        [Tooltip("How many FixedUpdate steps we wait before removing a passenger after OnCollisionExit2D. This avoids one-frame child-collider contact noise.")]
        [Min(0)] public int exitGraceFixedSteps = 2;

        [Tooltip("Absolute contact normal Y must be above this to count as top support. Absolute is used because Unity can report the opposite sign depending on the callback owner.")]
        [Range(0.1f, 0.95f)] public float topNormalMin = 0.55f;

        [Tooltip("Contact point must be near the platform top to count as standing on the top surface.")]
        [Min(0f)] public float topPointBuffer = 0.08f;

        [Tooltip("Extra vertical tolerance used for top-support fallback checks.")]
        [Min(0f)] public float supportFallbackTopTolerance = 0.14f;

        [Tooltip("Horizontal side tolerance used when checking whether the passenger is still really above the top surface.")]
        [Min(0f)] public float supportFallbackSideTolerance = 0.04f;

        [Tooltip("When the platform is moving, use this smaller side tolerance so a rider near the edge is still carried like an elevator instead of being dropped because of one-frame edge overlap noise.")]
        [Min(0f)] public float movingSupportFallbackSideTolerance = 0.01f;

        [Tooltip("Required overlap ratio between passenger width and platform width. This prevents the platform from pulling a character back after an edge fall.")]
        [Range(0.01f, 0.75f)] public float minRiderWidthOverlapRatio = 0.12f;

        [Tooltip("Softer overlap ratio used only while this platform is moving horizontally. This prevents Zalayty from losing lift-carry at the platform edge.")]
        [Range(0.001f, 0.75f)] public float movingMinRiderWidthOverlapRatio = 0.02f;

        [Tooltip("Very short support memory used while the platform is moving. It prevents child-collider/contact noise from removing a passenger at the edge, but it will not keep carrying a character that has really walked/fallen away.")]
        [Min(0f)] public float movingTopSupportMemoryTime = 0.08f;

        [Tooltip("Small side grace used with movingTopSupportMemoryTime. A recently supported passenger may be carried while almost exactly on the edge, but not after fully leaving the platform.")]
        [Min(0f)] public float movingEdgeSideGrace = 0.06f;

        [Header("Passengers / Small Surface Stabilization")]
        [Tooltip("Keeps a passenger seated on the top surface with a tiny vertical correction. This runs only while the passenger is still supported by the top surface; it is not an edge clamp and it does not catch falling characters.")]
        [SerializeField] private bool keepRidersSeatedOnTop = true;

        [Tooltip("Small vertical gap kept between passenger bottom and platform top.")]
        [Min(0f)] public float seatOnTopOffset = 0.015f;

        [Tooltip("Maximum vertical correction per FixedUpdate. Keep this small so the platform cannot pull a falling/jumping character back onto the edge.")]
        [Min(0f)] public float maxSeatCorrectionPerFixedStep = 0.06f;

        [Header("Respawn / Spawn Safety")]
        [Tooltip("Stable identifier used by GameMgr to find this moving platform again after Warrior death/retry.")]
        [SerializeField] private string respawnId;

        [Tooltip("Vertical space kept between Warrior bottom and the platform top when respawning on this moving platform.")]
        [SerializeField, Min(0f)] private float respawnSeatOffset = 0.05f;

        [Tooltip("Horizontal safety margin from the platform edges when choosing a respawn X on this moving platform.")]
        [SerializeField, Min(0f)] private float respawnHorizontalSkin = 0.08f;

        [Tooltip("Tiny extra clearance to avoid immediate re-overlap with the platform collider after Rigidbody2D.position is changed.")]
        [SerializeField, Min(0f)] private float respawnVerticalClearance = 0.01f;

        [Tooltip("Immediately registers the respawned Warrior as a platform passenger so the next FixedUpdate carries him with the platform.")]
        [SerializeField] private bool registerRespawnedWarriorAsPassenger = true;

        public string RespawnId
        {
            get
            {
                EnsureRespawnId();
                return respawnId;
            }
        }

        [Header("Zalayty / Independent Movement Merge")]
        [Tooltip("When true, if Zalayty requested his own MovePosition in this FixedUpdate, the platform adds its horizontal delta on top of that requested position instead of overwriting it.")]
        [SerializeField] private bool mergeZalaytyIndependentMoveWithPlatformDelta = true;

        private sealed class Passenger
        {
            public int id;
            public CharacterController character;
            public Rigidbody2D body;
            public Collider2D[] colliders;
            public float lastTopSupportTime;
        }

        private Rigidbody2D _rb;
        private Vector2 _leftPos;
        private Vector2 _rightPos;
        private Vector2 _startPos;

        private bool _goingRight = true;
        private float _waitTimer;

        private Vector2 _lastPlatformDelta;
        private float _lastPlatformDeltaFixedTime = -999f;

        private readonly Dictionary<int, Passenger> _passengers = new Dictionary<int, Passenger>();
        private readonly HashSet<int> _pendingRemove = new HashSet<int>();
        private readonly List<int> _passengersToRemove = new List<int>();

        protected override void Start()
        {
            base.Start();

            _rb = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            _startPos = transform.position;
            BuildPathPositions();
            EnsureRespawnId();
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            if (_rb == null)
                return;

            Vector2 current = _rb.position;
            Vector2 next = current;

            if (_waitTimer > 0f)
            {
                _waitTimer -= Time.fixedDeltaTime;

                RememberPlatformDelta(Vector2.zero);

                // Even while the platform waits, keep passengers lightly seated.
                // This prevents a one-frame vertical gap from making Zalayty think he is airborne.
                CarryPassengers(Vector2.zero);
                return;
            }

            Vector2 target = _goingRight ? _rightPos : _leftPos;
            next = Vector2.MoveTowards(current, target, speed * Time.fixedDeltaTime);

            bool arrived = Vector2.Distance(next, target) <= arriveEpsilon;
            if (arrived)
                next = target;

            Vector2 platformDelta = next - current;

            RememberPlatformDelta(platformDelta);

            // Apply the same horizontal delta to every valid passenger. This is not parenting
            // and it does not freeze the passenger's own movement. For Zalayty, if his AI also
            // requested a MovePosition during this same physics step, we merge both requests.
            CarryPassengers(platformDelta);

            _rb.MovePosition(next);

            if (arrived)
            {
                if (_goingRight)
                {
                    _waitTimer = waitAtRight;
                    _goingRight = false;
                }
                else
                {
                    _waitTimer = waitAtLeft;
                    _goingRight = true;
                }
            }
        }

        protected override void OnCollisionEnter2D(Collision2D collision)
        {
            base.OnCollisionEnter2D(collision);
            TryAddPassenger(collision);
        }

        protected override void OnCollisionStay2D(Collision2D collision)
        {
            base.OnCollisionStay2D(collision);
            TryAddPassenger(collision);
        }

        protected override void OnCollisionExit2D(Collision2D collision)
        {
            base.OnCollisionExit2D(collision);

            Passenger passenger;
            if (!TryBuildPassengerFromCollision(collision, out passenger))
                return;

            if (!_pendingRemove.Contains(passenger.id))
            {
                _pendingRemove.Add(passenger.id);
                StartCoroutine(RemovePassengerIfReallyLeft(passenger.id));
            }
        }

        private void RememberPlatformDelta(Vector2 platformDelta)
        {
            _lastPlatformDelta = new Vector2(platformDelta.x, 0f);
            _lastPlatformDeltaFixedTime = Time.fixedTime;
        }

        public bool TryGetHorizontalCarryDeltaForCurrentFixedStep(CharacterController passenger, out Vector2 carryDelta)
        {
            carryDelta = Vector2.zero;

            if (passenger == null)
                return false;

            if (Mathf.Abs(_lastPlatformDeltaFixedTime - Time.fixedTime) > 0.0001f)
                return false;

            if (!_passengers.ContainsKey(passenger.GetInstanceID()))
                return false;

            carryDelta = _lastPlatformDelta;
            return Mathf.Abs(carryDelta.x) > 0.000001f;
        }

        private void TryAddPassenger(Collision2D collision)
        {
            if (collision == null || platformCollider == null)
                return;

            Passenger passenger;
            if (!TryBuildPassengerFromCollision(collision, out passenger))
                return;

            if (!carryAllStandingRigidbodies && passenger.character == null)
                return;

            // Only real top-surface contact becomes a passenger.
            // Side hits, underside hits, ignored/pass-through states, jumps and edge falls are rejected.
            if (!CollisionHasTopSupport(collision, passenger))
                return;

            CacheOrRefreshPassenger(passenger);
            passenger.lastTopSupportTime = Time.time;

            // Important for Zalayty landing on a moving horizontal platform:
            // Notify BEFORE CanCarryPassengerNow(). CanCarryPassengerNow rejects active
            // jumps, but a top-surface collision with this platform is exactly the event
            // that should finalize Zalayty's independent jump and make him grounded again.
            if (passenger.character is ZalaytyMonster zalaytyBeforeCarryCheck)
                zalaytyBeforeCarryCheck.NotifyMovingPlatformTopSupport(this);

            if (!CanCarryPassengerNow(passenger))
                return;

            if (passenger.character != null && passenger.character.CurrentplatForm == null)
                passenger.character.CurrentplatForm = this;

            passenger.lastTopSupportTime = Time.time;
            _passengers[passenger.id] = passenger;
            _pendingRemove.Remove(passenger.id);

            if (passenger.character is ZalaytyMonster zalayty)
                zalayty.NotifyMovingPlatformTopSupport(this);
        }

        private bool CollisionHasTopSupport(Collision2D collision, Passenger passenger)
        {
            if (collision == null || collision.contactCount <= 0 || platformCollider == null)
                return false;

            Collider2D hitCollider = collision.collider;
            if (hitCollider == null || hitCollider.isTrigger)
                return false;

            if (Physics2D.GetIgnoreCollision(platformCollider, hitCollider))
                return false;

            Bounds platformBounds = platformCollider.bounds;
            Bounds hitBounds = hitCollider.bounds;

            // The collider that generated the contact must at least be above the platform center.
            // This rejects underside and most side contacts early.
            if (hitBounds.center.y < platformBounds.center.y)
                return false;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);

                bool verticalContact = Mathf.Abs(contact.normal.y) >= topNormalMin;
                bool contactNearTop =
                    contact.point.y >= platformBounds.max.y - topPointBuffer &&
                    contact.point.y <= platformBounds.max.y + supportFallbackTopTolerance;

                if (verticalContact && contactNearTop)
                    return true;
            }

            // Fallback for noisy normals and child-collider setups:
            // check the root character's non-trigger colliders, not only the exact collider
            // that produced this collision callback.
            return IsPassengerSupportedByTopSurface(passenger, Vector2.zero, Vector2.zero);
        }

        private IEnumerator RemovePassengerIfReallyLeft(int passengerId)
        {
            for (int i = 0; i < exitGraceFixedSteps; i++)
                yield return new WaitForFixedUpdate();

            _pendingRemove.Remove(passengerId);

            Passenger passenger;
            if (!_passengers.TryGetValue(passengerId, out passenger))
                yield break;

            RefreshPassengerColliders(passenger);

            if (!CanCarryPassengerNow(passenger))
                RemovePassenger(passengerId, clearCurrentPlatform: true);
        }

        private void CarryPassengers(Vector2 platformDelta)
        {
            _passengersToRemove.Clear();

            foreach (KeyValuePair<int, Passenger> pair in _passengers)
            {
                Passenger passenger = pair.Value;

                if (!CanCarryPassengerNow(passenger))
                {
                    _passengersToRemove.Add(pair.Key);
                    continue;
                }

                MovePassengerWithPlatform(passenger, platformDelta);
            }

            for (int i = 0; i < _passengersToRemove.Count; i++)
                RemovePassenger(_passengersToRemove[i], clearCurrentPlatform: true);

            _passengersToRemove.Clear();
        }

        private void MovePassengerWithPlatform(Passenger passenger, Vector2 platformDelta)
        {
            if (passenger == null || passenger.body == null)
                return;

            CharacterController character = passenger.character;
            Rigidbody2D body = passenger.body;

            if (character != null && character.CurrentplatForm == null)
                character.CurrentplatForm = this;

            Vector2 platformCarry = new Vector2(platformDelta.x, 0f);
            Vector2 targetPosition = body.position + platformCarry;
            Vector2 passengerDeltaForSupportCheck = platformCarry;

            ZalaytyMonster zalayty = character as ZalaytyMonster;
            if (zalayty != null)
            {
                zalayty.NotifyMovingPlatformTopSupport(this);

                // Zalayty's independent chase runs from a WaitForFixedUpdate coroutine.
                // That coroutine can run after this platform FixedUpdate, so a direct
                // body.MovePosition here may be overwritten by Zalayty's own MovePosition.
                // When Zalayty is actively chasing, let Zalayty pull this platform delta
                // through TryGetHorizontalCarryDeltaForCurrentFixedStep() and add it inside
                // MoveZalaytyBody(). Idle/non-moving Zalayty is still carried here normally.
                if (mergeZalaytyIndependentMoveWithPlatformDelta &&
                    zalayty.ShouldApplyHorizontalMovingPlatformCarryInsideOwnMove())
                {
                    return;
                }
            }

            // Must be initialized because C# definite-assignment analysis does not
            // know that this value is only used when mergeWithZalaytyMove is true.
            Vector2 zalaytyRequestedPosition = body.position;

            bool mergeWithZalaytyMove =
                mergeZalaytyIndependentMoveWithPlatformDelta &&
                zalayty != null &&
                zalayty.TryGetIndependentMoveRequestForCurrentFixedStep(out zalaytyRequestedPosition);

            if (mergeWithZalaytyMove)
            {
                // Zalayty is actively chasing/moving this FixedUpdate.
                // Do NOT replace his AI movement with the platform movement.
                // Instead: final position = Zalayty requested position + platform horizontal delta.
                targetPosition = zalaytyRequestedPosition + platformCarry;
                passengerDeltaForSupportCheck = targetPosition - body.position;

                // If Zalayty's own move intentionally leaves the platform top, stop carrying him.
                // This prevents an edge clamp while still allowing normal same-platform chase.
                if (!IsPassengerSupportedByTopSurface(passenger, passengerDeltaForSupportCheck, platformDelta))
                {
                    RemovePassenger(passenger.id, clearCurrentPlatform: true);
                    return;
                }
            }

            if (keepRidersSeatedOnTop &&
                TryGetSmallSeatCorrectionY(passenger, passengerDeltaForSupportCheck, platformDelta, out float correctionY))
            {
                targetPosition.y += correctionY;
            }

            Vector2 finalDelta = targetPosition - body.position;
            if (finalDelta.sqrMagnitude <= 0.0000001f)
                return;

            if (mergeWithZalaytyMove)
                zalayty.ApplyMovingPlatformMergedIndependentMove(targetPosition);
            else
                body.MovePosition(targetPosition);
        }

        private bool CanCarryPassengerNow(Passenger passenger)
        {
            if (passenger == null || passenger.body == null || platformCollider == null)
                return false;

            RefreshPassengerColliders(passenger);

            CharacterController character = passenger.character;
            if (character != null)
            {
                // If another platform owns the character, this platform must not pull it.
                if (character.CurrentplatForm != null && character.CurrentplatForm != this)
                    return false;

                // Never carry active jump / edge fall / repulse / pass-through states.
                if (character.IsJumping)
                    return false;

                Warrior warrior = character as Warrior;
                if (warrior != null)
                {
                    if (warrior.IsFallingEdge ||
                        warrior.IsFallingPlfExit ||
                        warrior.IsFallingHitEnemy ||
                        warrior.IsFallingGrazesEdge)
                        return false;
                }

                ZalaytyMonster zalayty = character as ZalaytyMonster;
                if (zalayty != null && zalayty.IsJumping)
                    return false;
            }

            if (!HasAtLeastOneNonIgnoredBodyCollider(passenger))
                return false;

            Vector2 supportPlatformDelta = GetCurrentSupportPredictionDelta();
            bool supported = IsPassengerSupportedByTopSurface(passenger, Vector2.zero, supportPlatformDelta);

            if (supported)
            {
                passenger.lastTopSupportTime = Time.time;
                return true;
            }

            return CanUseMovingTopSupportMemory(passenger, supportPlatformDelta);
        }

        private Vector2 GetCurrentSupportPredictionDelta()
        {
            if (Mathf.Abs(_lastPlatformDeltaFixedTime - Time.fixedTime) <= 0.0001f)
                return _lastPlatformDelta;

            return Vector2.zero;
        }

        private bool CanUseMovingTopSupportMemory(Passenger passenger, Vector2 platformPredictedDelta)
        {
            if (passenger == null || platformCollider == null)
                return false;

            if (Mathf.Abs(platformPredictedDelta.x) <= 0.000001f)
                return false;

            if (Time.time > passenger.lastTopSupportTime + movingTopSupportMemoryTime)
                return false;

            Collider2D standing = null;
            if (passenger.character != null)
                standing = GetStandingCollider(passenger.character);

            if (standing == null)
            {
                Collider2D[] colliders = passenger.colliders;
                if (colliders != null)
                {
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        Collider2D col = colliders[i];
                        if (col == null || col.isTrigger || !col.enabled || !col.gameObject.activeInHierarchy)
                            continue;

                        standing = col;
                        break;
                    }
                }
            }

            if (standing == null || standing.isTrigger || !standing.enabled || !standing.gameObject.activeInHierarchy)
                return false;

            if (Physics2D.GetIgnoreCollision(platformCollider, standing))
                return false;

            Bounds platformBounds = platformCollider.bounds;
            platformBounds.center += (Vector3)platformPredictedDelta;

            Bounds characterBounds = standing.bounds;

            bool passengerAbovePlatform = characterBounds.center.y >= platformBounds.center.y;
            if (!passengerAbovePlatform)
                return false;

            float overlapX = Mathf.Min(characterBounds.max.x, platformBounds.max.x) -
                             Mathf.Max(characterBounds.min.x, platformBounds.min.x);

            if (overlapX < -movingEdgeSideGrace)
                return false;

            float bottomAboveTop = characterBounds.min.y - platformBounds.max.y;
            bool closeToTop =
                bottomAboveTop >= -supportFallbackTopTolerance &&
                bottomAboveTop <= supportFallbackTopTolerance + seatOnTopOffset + maxSeatCorrectionPerFixedStep;

            return closeToTop;
        }

        private bool IsPassengerSupportedByTopSurface(Passenger passenger, Vector2 passengerPredictedDelta, Vector2 platformPredictedDelta)
        {
            if (passenger == null || platformCollider == null)
                return false;

            RefreshPassengerColliders(passenger);

            Collider2D preferredStandingCollider = null;
            if (passenger.character != null)
                preferredStandingCollider = GetStandingCollider(passenger.character);

            if (IsSupportedByTopSurface(preferredStandingCollider, passengerPredictedDelta, platformPredictedDelta))
                return true;

            Collider2D[] colliders = passenger.colliders;
            if (colliders == null)
                return false;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D col = colliders[i];
                if (col == preferredStandingCollider)
                    continue;

                if (IsSupportedByTopSurface(col, passengerPredictedDelta, platformPredictedDelta))
                    return true;
            }

            return false;
        }

        private bool IsSupportedByTopSurface(Collider2D col, Vector2 passengerPredictedDelta, Vector2 platformPredictedDelta)
        {
            if (col == null || col.isTrigger || !col.enabled || !col.gameObject.activeInHierarchy || platformCollider == null)
                return false;

            if (Physics2D.GetIgnoreCollision(platformCollider, col))
                return false;

            Bounds platformBounds = platformCollider.bounds;
            platformBounds.center += (Vector3)platformPredictedDelta;

            Bounds characterBounds = col.bounds;
            characterBounds.center += (Vector3)passengerPredictedDelta;

            bool passengerAbovePlatform = characterBounds.center.y >= platformBounds.center.y;
            if (!passengerAbovePlatform)
                return false;

            float overlapX = Mathf.Min(characterBounds.max.x, platformBounds.max.x) -
                             Mathf.Max(characterBounds.min.x, platformBounds.min.x);

            if (overlapX <= 0f)
                return false;

            bool movingSupportCheck = Mathf.Abs(platformPredictedDelta.x) > 0.000001f;
            float sideTolerance = movingSupportCheck ? movingSupportFallbackSideTolerance : supportFallbackSideTolerance;
            float overlapRatio = movingSupportCheck ? movingMinRiderWidthOverlapRatio : minRiderWidthOverlapRatio;

            float requiredOverlap = Mathf.Max(
                sideTolerance,
                Mathf.Min(characterBounds.size.x, platformBounds.size.x) * overlapRatio
            );

            bool enoughHorizontalOverlap = overlapX >= requiredOverlap;

            bool closeToTop =
                characterBounds.min.y >= platformBounds.max.y - supportFallbackTopTolerance &&
                characterBounds.min.y <= platformBounds.max.y + supportFallbackTopTolerance;

            return enoughHorizontalOverlap && closeToTop;
        }

        private bool HasAtLeastOneNonIgnoredBodyCollider(Passenger passenger)
        {
            if (passenger == null || platformCollider == null)
                return false;

            RefreshPassengerColliders(passenger);

            Collider2D[] colliders = passenger.colliders;
            if (colliders == null)
                return false;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D col = colliders[i];
                if (col == null || col.isTrigger || !col.enabled || !col.gameObject.activeInHierarchy)
                    continue;

                if (!Physics2D.GetIgnoreCollision(platformCollider, col))
                    return true;
            }

            return false;
        }

        private bool TryGetSmallSeatCorrectionY(Passenger passenger, Vector2 passengerPredictedDelta, Vector2 platformPredictedDelta, out float correctionY)
        {
            correctionY = 0f;

            if (passenger == null || passenger.body == null || platformCollider == null)
                return false;

            RefreshPassengerColliders(passenger);

            Collider2D standing = null;

            if (passenger.character != null)
                standing = GetStandingCollider(passenger.character);

            if (!IsSupportedByTopSurface(standing, passengerPredictedDelta, platformPredictedDelta))
            {
                standing = null;

                Collider2D[] colliders = passenger.colliders;
                if (colliders != null)
                {
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        Collider2D col = colliders[i];
                        if (IsSupportedByTopSurface(col, passengerPredictedDelta, platformPredictedDelta))
                        {
                            standing = col;
                            break;
                        }
                    }
                }
            }

            if (standing == null || standing.isTrigger)
                return false;

            Bounds platformBounds = platformCollider.bounds;
            platformBounds.center += (Vector3)platformPredictedDelta;

            Bounds standingBounds = standing.bounds;
            standingBounds.center += (Vector3)passengerPredictedDelta;

            float predictedBottomY = standingBounds.min.y;
            float wantedBottomY = platformBounds.max.y + seatOnTopOffset;
            float rawCorrection = wantedBottomY - predictedBottomY;

            if (Mathf.Abs(rawCorrection) <= 0.001f)
                return false;

            correctionY = Mathf.Clamp(rawCorrection, -maxSeatCorrectionPerFixedStep, maxSeatCorrectionPerFixedStep);
            return Mathf.Abs(correctionY) > 0.0001f;
        }

        private bool TryBuildPassengerFromCollision(Collision2D collision, out Passenger passenger)
        {
            passenger = null;

            if (collision == null)
                return false;

            Collider2D hitCollider = collision.collider;
            if (hitCollider == null)
                return false;

            return TryBuildPassengerFromCollider(hitCollider, collision.rigidbody, out passenger);
        }

        private bool TryBuildPassengerFromCollider(Collider2D collider, Rigidbody2D fallbackBody, out Passenger passenger)
        {
            passenger = null;

            if (collider == null)
                return false;

            CharacterController character = collider.GetComponentInParent<CharacterController>();
            Rigidbody2D body = null;

            if (character != null)
            {
                body = character.rigidbody2;
                if (body == null)
                    body = character.GetComponent<Rigidbody2D>();
            }

            if (body == null)
                body = fallbackBody;

            if (body == null)
                body = collider.attachedRigidbody;

            if (body == null)
                return false;

            int id = character != null ? character.GetInstanceID() : body.GetInstanceID();

            Passenger existing;
            if (_passengers.TryGetValue(id, out existing))
            {
                existing.character = character != null ? character : existing.character;
                existing.body = body != null ? body : existing.body;
                RefreshPassengerColliders(existing);
                passenger = existing;
                return true;
            }

            passenger = new Passenger
            {
                id = id,
                character = character,
                body = body,
                colliders = GetPassengerColliders(character, body)
            };

            return true;
        }

        private void CacheOrRefreshPassenger(Passenger passenger)
        {
            if (passenger == null)
                return;

            RefreshPassengerColliders(passenger);
            _passengers[passenger.id] = passenger;
        }

        private void RefreshPassengerColliders(Passenger passenger)
        {
            if (passenger == null)
                return;

            passenger.colliders = GetPassengerColliders(passenger.character, passenger.body);
        }

        private Collider2D[] GetPassengerColliders(CharacterController character, Rigidbody2D body)
        {
            if (character != null)
                return character.GetComponentsInChildren<Collider2D>(true);

            if (body != null)
                return body.GetComponentsInChildren<Collider2D>(true);

            return new Collider2D[0];
        }

        private void RemovePassenger(int passengerId, bool clearCurrentPlatform)
        {
            Passenger passenger;
            if (_passengers.TryGetValue(passengerId, out passenger) && clearCurrentPlatform)
            {
                if (passenger.character != null && passenger.character.CurrentplatForm == this)
                    passenger.character.CurrentplatForm = null;
            }

            _passengers.Remove(passengerId);
            _pendingRemove.Remove(passengerId);
        }

        public Vector3 GetSafeRespawnPositionFor(Warrior warrior, float preferredX)
        {
            return GetSafeRespawnPositionFor((CharacterController)warrior, preferredX);
        }

        public Vector3 GetSafeRespawnPositionFor(CharacterController character, float preferredX)
        {
            if (platformCollider == null)
            {
                Vector3 fallback = transform.position;
                if (character != null)
                    fallback.z = character.transform.position.z;
                return fallback;
            }

            Physics2D.SyncTransforms();

            Bounds platformBounds = platformCollider.bounds;
            Collider2D standingCollider = character != null ? GetStandingCollider(character) : null;

            Vector2 halfSize = GetColliderHalfSizeSafe(standingCollider, new Vector2(0.35f, 0.80f));

            float minX = platformBounds.min.x + halfSize.x + respawnHorizontalSkin;
            float maxX = platformBounds.max.x - halfSize.x - respawnHorizontalSkin;

            float safeX = minX <= maxX
                ? Mathf.Clamp(preferredX, minX, maxX)
                : platformBounds.center.x;

            float safeY = platformBounds.max.y + halfSize.y + respawnSeatOffset + respawnVerticalClearance;
            float safeZ = character != null ? character.transform.position.z : transform.position.z;

            return new Vector3(safeX, safeY, safeZ);
        }

        public void RespawnRiderOnLift(Warrior warrior, float preferredX)
        {
            if (warrior == null)
                return;

            RestorePlatformCollisionForCharacter(warrior);

            Vector3 safePosition = GetSafeRespawnPositionFor(warrior, preferredX);

            if (warrior.rigidbody2 != null)
            {
                warrior.rigidbody2.simulated = true;
                warrior.rigidbody2.linearVelocity = Vector2.zero;
                warrior.rigidbody2.angularVelocity = 0f;
                warrior.rigidbody2.constraints = RigidbodyConstraints2D.FreezeRotation;
                warrior.rigidbody2.position = new Vector2(safePosition.x, safePosition.y);
                warrior.rigidbody2.WakeUp();
            }

            warrior.transform.position = safePosition;

            warrior.CurrentplatForm = this;
            warrior.LastSafePlatform = this;
            warrior.LastSafePosition = safePosition;

            warrior.IsFallingPlfExit = false;
            warrior.IsFallingGrazesEdge = false;
            warrior.IsFallingEdge = false;
            warrior.IsFallingHitEnemy = false;
            warrior.CanMove = true;
            warrior.CanAttackWarrior = true;
            warrior._blockAction = false;

            warrior.StopJumpTowardCoroutine();
            warrior.StopMoveTowardCoroutine();
            warrior.WaitAnimationDisplay();

            Physics2D.SyncTransforms();

            if (registerRespawnedWarriorAsPassenger)
                ForceRegisterRespawnedPassenger(warrior);
        }

        public void ForceRegisterRespawnedPassenger(CharacterController character)
        {
            if (character == null || platformCollider == null)
                return;

            RestorePlatformCollisionForCharacter(character);
            Physics2D.SyncTransforms();

            Collider2D standingCollider = GetStandingCollider(character);
            if (standingCollider == null)
                return;

            Passenger passenger;
            if (!TryBuildPassengerFromCollider(standingCollider, character.rigidbody2, out passenger))
                return;

            CacheOrRefreshPassenger(passenger);
            passenger.lastTopSupportTime = Time.time;
            _pendingRemove.Remove(passenger.id);

            character.CurrentplatForm = this;

            Warrior warrior = character as Warrior;
            if (warrior != null)
            {
                warrior.LastSafePlatform = this;
                warrior.LastSafePosition = GetSafeRespawnPositionFor(warrior, warrior.transform.position.x);
            }
        }

        private void RestorePlatformCollisionForCharacter(CharacterController character)
        {
            if (character == null || platformCollider == null)
                return;

            Collider2D[] cols = character.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D col = cols[i];
                if (col == null || col.isTrigger)
                    continue;

                Physics2D.IgnoreCollision(platformCollider, col, false);
            }
        }

        private Vector2 GetColliderHalfSizeSafe(Collider2D col, Vector2 fallback)
        {
            if (col == null)
                return fallback;

            Bounds bounds = col.bounds;
            if (bounds.size.x > 0.0001f && bounds.size.y > 0.0001f)
                return bounds.extents;

            BoxCollider2D box = col as BoxCollider2D;
            if (box != null)
            {
                Vector3 scale = box.transform.lossyScale;
                return new Vector2(
                    Mathf.Abs(box.size.x * scale.x) * 0.5f,
                    Mathf.Abs(box.size.y * scale.y) * 0.5f
                );
            }

            return fallback;
        }

        private void EnsureRespawnId()
        {
            if (!string.IsNullOrWhiteSpace(respawnId))
                return;

            respawnId = GetHierarchyPath(transform);
        }

        private static string GetHierarchyPath(Transform node)
        {
            if (node == null)
                return "MovingHorizontalPlatform";

            string path = node.name;
            Transform parent = node.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private void BuildPathPositions()
        {
            float minOffset = Mathf.Min(xMin, xMax);
            float maxOffset = Mathf.Max(xMin, xMax);

            _leftPos = new Vector2(_startPos.x + minOffset, _startPos.y);
            _rightPos = new Vector2(_startPos.x + maxOffset, _startPos.y);
        }

        private void OnValidate()
        {
            if (xMin > xMax)
                (xMin, xMax) = (xMax, xMin);

            movingMinRiderWidthOverlapRatio = Mathf.Min(movingMinRiderWidthOverlapRatio, minRiderWidthOverlapRatio);
            movingSupportFallbackSideTolerance = Mathf.Min(movingSupportFallbackSideTolerance, supportFallbackSideTolerance);
            EnsureRespawnId();
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 start = Application.isPlaying ? _startPos : (Vector2)transform.position;

            float minOffset = Mathf.Min(xMin, xMax);
            float maxOffset = Mathf.Max(xMin, xMax);

            Vector2 left = new Vector2(start.x + minOffset, start.y);
            Vector2 right = new Vector2(start.x + maxOffset, start.y);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(left, right);
            Gizmos.DrawWireSphere(left, 0.08f);
            Gizmos.DrawWireSphere(right, 0.08f);
        }
    }
}

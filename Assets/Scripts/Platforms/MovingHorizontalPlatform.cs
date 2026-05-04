using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Platforms
{
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

        [Header("Riders / Elevator Carry")]
        [Tooltip("If true, every Rigidbody2D really standing on the top surface is carried. If false, only CharacterController riders are carried.")]
        [SerializeField] private bool carryAllStandingRigidbodies = true;

        [Tooltip("How many FixedUpdate steps we wait before removing a rider after OnCollisionExit2D. This avoids one-frame contact noise.")]
        [Min(0)] public int exitGraceFixedSteps = 2;

        [Tooltip("Absolute contact normal Y must be above this to count as top support. Absolute is used because Unity can report the opposite sign depending on the callback owner.")]
        [Range(0.1f, 0.95f)] public float topNormalMin = 0.55f;

        [Tooltip("Contact point must be near the platform top to count as standing on the top surface.")]
        [Min(0f)] public float topPointBuffer = 0.08f;

        [Tooltip("Extra vertical tolerance used for top-support fallback checks.")]
        [Min(0f)] public float supportFallbackTopTolerance = 0.14f;

        [Tooltip("Horizontal side tolerance used when checking whether the rider is still really above the top surface.")]
        [Min(0f)] public float supportFallbackSideTolerance = 0.04f;

        [Tooltip("Required overlap ratio between rider width and platform width. This prevents the platform from pulling a character back after an edge fall.")]
        [Range(0.01f, 0.75f)] public float minRiderWidthOverlapRatio = 0.12f;

        [Header("Riders / Small Surface Stabilization")]
        [Tooltip("Keeps a rider seated on the top surface with a tiny vertical correction. This runs only while the rider is still supported by the top surface; it is not an edge clamp and it does not catch falling characters.")]
        [SerializeField] private bool keepRidersSeatedOnTop = true;

        [Tooltip("Small vertical gap kept between rider bottom and platform top.")]
        [Min(0f)] public float seatOnTopOffset = 0.015f;

        [Tooltip("Maximum vertical correction per FixedUpdate. Keep this small so the platform cannot pull a falling/jumping character back onto the edge.")]
        [Min(0f)] public float maxSeatCorrectionPerFixedStep = 0.06f;

        private Rigidbody2D _rb;
        private Vector2 _leftPos;
        private Vector2 _rightPos;
        private Vector2 _startPos;

        private bool _goingRight = true;
        private float _waitTimer;

        private readonly HashSet<Rigidbody2D> _riders = new HashSet<Rigidbody2D>();
        private readonly Dictionary<Rigidbody2D, Collider2D[]> _riderCols = new Dictionary<Rigidbody2D, Collider2D[]>();
        private readonly HashSet<Rigidbody2D> _pendingRemove = new HashSet<Rigidbody2D>();
        private readonly List<Rigidbody2D> _ridersToRemove = new List<Rigidbody2D>();

        protected override void Start()
        {
            base.Start();

            _rb = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            _startPos = transform.position;
            BuildPathPositions();
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
                RefreshRidersWithoutMoving();
                return;
            }

            Vector2 target = _goingRight ? _rightPos : _leftPos;
            next = Vector2.MoveTowards(current, target, speed * Time.fixedDeltaTime);

            bool arrived = Vector2.Distance(next, target) <= arriveEpsilon;
            if (arrived)
                next = target;

            Vector2 delta = next - current;

            // Move the platform first. Then apply exactly the same horizontal delta to valid riders.
            // This gives elevator behavior without parenting and without relying on physics friction.
            _rb.MovePosition(next);
            CarryRiders(delta);

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
            TryAddRider(collision);
        }

        protected override void OnCollisionStay2D(Collision2D collision)
        {
            base.OnCollisionStay2D(collision);
            TryAddRider(collision);
        }

        protected override void OnCollisionExit2D(Collision2D collision)
        {
            base.OnCollisionExit2D(collision);

            Rigidbody2D rb = collision.rigidbody;
            if (rb == null)
                return;

            if (!_pendingRemove.Contains(rb))
            {
                _pendingRemove.Add(rb);
                StartCoroutine(RemoveRiderIfReallyLeft(rb));
            }
        }

        private void TryAddRider(Collision2D collision)
        {
            if (collision == null || platformCollider == null)
                return;

            Rigidbody2D rb = collision.rigidbody;
            if (rb == null)
                return;

            CharacterController character = GetCharacterFromRider(rb);
            if (!carryAllStandingRigidbodies && character == null)
                return;

            // Only real top-surface contact becomes a rider.
            // Side hits, underside hits, ignored/pass-through states, jumps and edge falls are rejected.
            if (!CollisionHasTopSupport(collision))
                return;

            CacheRiderColliders(rb);

            if (!CanCarryRiderNow(rb))
                return;

            if (character != null && character.CurrentplatForm == null)
                character.CurrentplatForm = this;

            _riders.Add(rb);
            _pendingRemove.Remove(rb);
        }

        private bool CollisionHasTopSupport(Collision2D collision)
        {
            if (collision == null || collision.contactCount <= 0 || platformCollider == null)
                return false;

            Collider2D riderCollider = collision.collider;
            if (riderCollider == null || riderCollider.isTrigger)
                return false;

            if (Physics2D.GetIgnoreCollision(platformCollider, riderCollider))
                return false;

            Bounds pb = platformCollider.bounds;
            Bounds cb = riderCollider.bounds;

            // Rider must be above the platform center. This rejects underside and most side contacts.
            if (cb.center.y < pb.center.y)
                return false;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D c = collision.GetContact(i);

                bool verticalContact = Mathf.Abs(c.normal.y) >= topNormalMin;
                bool contactNearTop =
                    c.point.y >= pb.max.y - topPointBuffer &&
                    c.point.y <= pb.max.y + supportFallbackTopTolerance;

                if (verticalContact && contactNearTop)
                    return true;
            }

            // Fallback for noisy contact normals while the collider is visibly seated on the top.
            return IsSupportedByTopSurface(riderCollider, Vector2.zero);
        }

        private IEnumerator RemoveRiderIfReallyLeft(Rigidbody2D rb)
        {
            for (int i = 0; i < exitGraceFixedSteps; i++)
                yield return new WaitForFixedUpdate();

            _pendingRemove.Remove(rb);

            if (rb == null || !CanCarryRiderNow(rb))
                RemoveRider(rb);
        }

        private void RefreshRidersWithoutMoving()
        {
            _riders.RemoveWhere(r => r == null);
            _ridersToRemove.Clear();

            foreach (Rigidbody2D rider in _riders)
            {
                if (!CanCarryRiderNow(rider))
                    _ridersToRemove.Add(rider);
            }

            for (int i = 0; i < _ridersToRemove.Count; i++)
                RemoveRider(_ridersToRemove[i]);

            _ridersToRemove.Clear();
        }

        private void CarryRiders(Vector2 platformDelta)
        {
            _riders.RemoveWhere(r => r == null);
            _ridersToRemove.Clear();

            foreach (Rigidbody2D rider in _riders)
            {
                if (!CanCarryRiderNow(rider))
                {
                    _ridersToRemove.Add(rider);
                    continue;
                }

                // Horizontal elevator behavior: only inherit the platform X delta.
                // Gravity/jump/fall remain natural on Y.
                Vector2 carryDelta = new Vector2(platformDelta.x, 0f);
                MoveRiderWithPlatform(rider, carryDelta);
            }

            for (int i = 0; i < _ridersToRemove.Count; i++)
                RemoveRider(_ridersToRemove[i]);

            _ridersToRemove.Clear();
        }

        private void MoveRiderWithPlatform(Rigidbody2D rider, Vector2 carryDelta)
        {
            if (rider == null)
                return;

            CharacterController character = GetCharacterFromRider(rider);
            Rigidbody2D body = character != null && character.rigidbody2 != null
                ? character.rigidbody2
                : rider;

            if (body == null)
                return;

            if (character != null && character.CurrentplatForm == null)
                character.CurrentplatForm = this;

            Vector2 finalDelta = carryDelta;

            if (keepRidersSeatedOnTop && TryGetSmallSeatCorrectionY(character, body, carryDelta, out float correctionY))
                finalDelta.y += correctionY;

            if (finalDelta.sqrMagnitude <= 0.0000001f)
                return;

            body.MovePosition(body.position + finalDelta);
        }

        private bool CanCarryRiderNow(Rigidbody2D rb)
        {
            if (rb == null || platformCollider == null)
                return false;

            CharacterController character = GetCharacterFromRider(rb);

            if (character != null)
            {
                // If another platform owns the character, this platform must not pull it.
                if (character.CurrentplatForm != null && character.CurrentplatForm != this)
                    return false;

                // Never carry active jump / edge fall / repulse / pass-through states.
                if (character.IsJumping)
                    return false;

                if (character is Warrior warrior)
                {
                    if (warrior.IsFallingEdge ||
                        warrior.IsFallingPlfExit ||
                        warrior.IsFallingHitEnemy ||
                        warrior.IsFallingGrazesEdge)
                        return false;
                }
                else if (character is ZalaytyMonster zalayty)
                {
                    if (zalayty.IsJumping)
                        return false;
                }
            }

            if (!HasAtLeastOneNonIgnoredBodyCollider(rb))
                return false;

            return IsStillOnPlatform(rb);
        }

        private bool IsStillOnPlatform(Rigidbody2D rb)
        {
            if (rb == null || platformCollider == null)
                return false;

            CharacterController character = GetCharacterFromRider(rb);
            if (character != null)
            {
                Collider2D standing = GetStandingCollider(character);
                return IsSupportedByTopSurface(standing, Vector2.zero);
            }

            if (!_riderCols.TryGetValue(rb, out Collider2D[] cols) || cols == null || cols.Length == 0)
                cols = rb.GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < cols.Length; i++)
            {
                if (IsSupportedByTopSurface(cols[i], Vector2.zero))
                    return true;
            }

            return false;
        }

        private bool IsSupportedByTopSurface(Collider2D col, Vector2 predictedDelta)
        {
            if (col == null || col.isTrigger || platformCollider == null)
                return false;

            if (Physics2D.GetIgnoreCollision(platformCollider, col))
                return false;

            Bounds pb = platformCollider.bounds;
            Bounds cb = col.bounds;
            cb.center += (Vector3)predictedDelta;

            bool riderAbovePlatform = cb.center.y >= pb.center.y;
            if (!riderAbovePlatform)
                return false;

            float overlapX = Mathf.Min(cb.max.x, pb.max.x) - Mathf.Max(cb.min.x, pb.min.x);
            if (overlapX <= 0f)
                return false;

            float requiredOverlap = Mathf.Max(
                supportFallbackSideTolerance,
                Mathf.Min(cb.size.x, pb.size.x) * minRiderWidthOverlapRatio
            );

            bool enoughHorizontalOverlap = overlapX >= requiredOverlap;

            bool closeToTop =
                cb.min.y >= pb.max.y - supportFallbackTopTolerance &&
                cb.min.y <= pb.max.y + supportFallbackTopTolerance;

            return enoughHorizontalOverlap && closeToTop;
        }

        private bool HasAtLeastOneNonIgnoredBodyCollider(Rigidbody2D rb)
        {
            if (rb == null || platformCollider == null)
                return false;

            if (!_riderCols.TryGetValue(rb, out Collider2D[] cols) || cols == null || cols.Length == 0)
                cols = rb.GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D col = cols[i];
                if (col == null || col.isTrigger || !col.enabled || !col.gameObject.activeInHierarchy)
                    continue;

                if (!Physics2D.GetIgnoreCollision(platformCollider, col))
                    return true;
            }

            return false;
        }

        private bool TryGetSmallSeatCorrectionY(CharacterController character, Rigidbody2D body, Vector2 carryDelta, out float correctionY)
        {
            correctionY = 0f;

            if (platformCollider == null || body == null)
                return false;

            Collider2D standing = null;

            if (character != null)
                standing = GetStandingCollider(character);

            if (standing == null)
            {
                if (!_riderCols.TryGetValue(body, out Collider2D[] cols) || cols == null || cols.Length == 0)
                    cols = body.GetComponentsInChildren<Collider2D>(true);

                for (int i = 0; i < cols.Length; i++)
                {
                    Collider2D c = cols[i];
                    if (IsSupportedByTopSurface(c, carryDelta))
                    {
                        standing = c;
                        break;
                    }
                }
            }

            if (standing == null || standing.isTrigger)
                return false;

            // Guard: do not seat if the rider will not still be supported after horizontal carry.
            // This prevents pulling Warrior/Zalayty back from an intended edge fall.
            if (!IsSupportedByTopSurface(standing, carryDelta))
                return false;

            Bounds pb = platformCollider.bounds;
            Bounds cb = standing.bounds;

            float predictedBottomY = cb.min.y + carryDelta.y;
            float wantedBottomY = pb.max.y + seatOnTopOffset;
            float rawCorrection = wantedBottomY - predictedBottomY;

            if (Mathf.Abs(rawCorrection) <= 0.001f)
                return false;

            correctionY = Mathf.Clamp(rawCorrection, -maxSeatCorrectionPerFixedStep, maxSeatCorrectionPerFixedStep);
            return Mathf.Abs(correctionY) > 0.0001f;
        }

        private void CacheRiderColliders(Rigidbody2D rb)
        {
            if (rb == null)
                return;

            if (!_riderCols.ContainsKey(rb) || _riderCols[rb] == null || _riderCols[rb].Length == 0)
                _riderCols[rb] = rb.GetComponentsInChildren<Collider2D>(true);
        }

        private void RemoveRider(Rigidbody2D rb)
        {
            if (rb == null)
                return;

            _riders.Remove(rb);
            _riderCols.Remove(rb);
            _pendingRemove.Remove(rb);
        }

        private CharacterController GetCharacterFromRider(Rigidbody2D rb)
        {
            if (rb == null)
                return null;

            CharacterController character = rb.GetComponent<CharacterController>();
            if (character != null)
                return character;

            character = rb.GetComponentInParent<CharacterController>();
            if (character != null)
                return character;

            return rb.GetComponentInChildren<CharacterController>();
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
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 start = Application.isPlaying ? _startPos : (Vector2)transform.position;

            float minOffset = Mathf.Min(xMin, xMax);
            float maxOffset = Mathf.Max(xMin, xMax);

            Vector2 l = new Vector2(start.x + minOffset, start.y);
            Vector2 r = new Vector2(start.x + maxOffset, start.y);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(l, r);
            Gizmos.DrawWireSphere(l, 0.08f);
            Gizmos.DrawWireSphere(r, 0.08f);
        }
    }
}

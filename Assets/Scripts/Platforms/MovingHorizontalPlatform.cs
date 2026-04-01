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

        [Header("Riders (anti-jitter)")]
        [Tooltip("How many FixedUpdate steps we wait before removing a rider after OnCollisionExit2D.")]
        [Min(0)] public int exitGraceFixedSteps = 2;

        [Tooltip("Contact normal.y must be above this to count as 'standing on top'.")]
        [Range(0.1f, 0.95f)] public float topNormalMin = 0.55f;

        [Tooltip("Contact point must be near platform top to count as 'standing on top'.")]
        [Min(0f)] public float topPointBuffer = 0.08f;

        private Rigidbody2D _rb;
        private Vector2 _leftPos;
        private Vector2 _rightPos;
        private Vector2 _startPos;

        private bool _goingRight = true;
        private float _waitTimer = 0f;
        private Vector2 _lastPlatformPos;

        private readonly HashSet<Rigidbody2D> _riders = new HashSet<Rigidbody2D>();
        private readonly Dictionary<Rigidbody2D, Collider2D[]> _riderCols = new Dictionary<Rigidbody2D, Collider2D[]>();
        private readonly HashSet<Rigidbody2D> _pendingRemove = new HashSet<Rigidbody2D>();

        protected override void Start()
        {
            base.Start();

            _rb = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            _startPos = transform.position;

            BuildPathPositions();
            _lastPlatformPos = _rb.position;
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (_rb == null) return;

            if (_waitTimer > 0f)
            {
                _waitTimer -= Time.fixedDeltaTime;
                _lastPlatformPos = _rb.position;
                return;
            }

            Vector2 target = _goingRight ? _rightPos : _leftPos;
            Vector2 current = _rb.position;
            Vector2 next = Vector2.MoveTowards(current, target, speed * Time.fixedDeltaTime);

            _rb.MovePosition(next);

            Vector2 delta = next - _lastPlatformPos;
            if (delta != Vector2.zero && _riders.Count > 0)
            {
                _riders.RemoveWhere(r => r == null);

                foreach (var rider in _riders)
                {
                    if (rider == null) continue;
                    if (!IsRiderCarryAllowed(rider)) continue;

                    rider.MovePosition(rider.position + delta);
                }
            }

            _lastPlatformPos = next;

            if (Vector2.Distance(next, target) <= arriveEpsilon)
            {
                _rb.MovePosition(target);
                _lastPlatformPos = target;

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

            var rb = collision.rigidbody;
            if (rb == null) return;

            if (!_pendingRemove.Contains(rb))
            {
                _pendingRemove.Add(rb);
                StartCoroutine(RemoveRiderIfReallyLeft(rb));
            }
        }

        private void TryAddRider(Collision2D collision)
        {
            var rb = collision.rigidbody;
            if (rb == null) return;

            float platformTopY = platformCollider != null ? platformCollider.bounds.max.y : transform.position.y;

            for (int i = 0; i < collision.contactCount; i++)
            {
                var c = collision.GetContact(i);

                if (c.normal.y > topNormalMin && c.point.y >= platformTopY - topPointBuffer)
                {
                    _riders.Add(rb);

                    if (!_riderCols.ContainsKey(rb))
                        _riderCols[rb] = rb.GetComponentsInChildren<Collider2D>(true);

                    _pendingRemove.Remove(rb);
                    return;
                }
            }
        }

        private IEnumerator RemoveRiderIfReallyLeft(Rigidbody2D rb)
        {
            for (int i = 0; i < exitGraceFixedSteps; i++)
                yield return new WaitForFixedUpdate();

            _pendingRemove.Remove(rb);

            if (rb == null)
            {
                _riders.Remove(rb);
                _riderCols.Remove(rb);
                yield break;
            }

            if (IsStillOnPlatform(rb))
                yield break;

            _riders.Remove(rb);
            _riderCols.Remove(rb);
        }

        private bool IsStillOnPlatform(Rigidbody2D rb)
        {
            if (platformCollider == null) return false;

            if (!_riderCols.TryGetValue(rb, out var cols) || cols == null || cols.Length == 0)
                cols = rb.GetComponentsInChildren<Collider2D>(true);

            foreach (var col in cols)
            {
                if (col == null) continue;

                if (Physics2D.GetIgnoreCollision(platformCollider, col))
                    continue;

                if (platformCollider.IsTouching(col))
                    return true;

                Bounds pb = platformCollider.bounds;
                Bounds cb = col.bounds;

                bool horizontallyOver = cb.max.x > pb.min.x + 0.02f && cb.min.x < pb.max.x - 0.02f;
                bool nearTop = cb.min.y >= pb.max.y - 0.12f;

                if (horizontallyOver && nearTop)
                    return true;
            }

            return false;
        }

        private bool IsRiderCarryAllowed(Rigidbody2D rb)
        {
            if (platformCollider == null) return true;

            if (!_riderCols.TryGetValue(rb, out var cols) || cols == null || cols.Length == 0)
                return true;

            foreach (var col in cols)
            {
                if (col == null) continue;
                if (!Physics2D.GetIgnoreCollision(platformCollider, col))
                    return true;
            }

            return false;
        }

        private void BuildPathPositions()
        {
            float minOffset = Mathf.Min(xMin, xMax);
            float maxOffset = Mathf.Max(xMin, xMax);

            _leftPos = new Vector2(_startPos.x + minOffset, _startPos.y);
            _rightPos = new Vector2(_startPos.x + maxOffset, _startPos.y);
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

        private void OnValidate()
        {
            if (xMin > xMax)
                (xMin, xMax) = (xMax, xMin);
        }
    }
}
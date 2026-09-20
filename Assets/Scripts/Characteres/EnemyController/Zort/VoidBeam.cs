using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Sustained tracking beam used by Zort's Void Stare (Phase 3).
    /// Renders a <see cref="LineRenderer"/> from the beam origin toward a target and
    /// re-aims slowly, so the Warrior can dash behind Zort to exploit the turn lag.
    /// The Warrior takes ticking damage while standing inside the beam segment.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class VoidBeam : MonoBehaviour
    {
        private LineRenderer _line;
        private Transform _target;

        private Enemy _owner;
        private int _damage;
        private float _tickInterval = 0.4f;
        private float _maxLength = 12f;
        private float _turnSpeed = 110f;
        private float _width = 0.9f;
        private float _stunSeconds;
        private float _knockbackVelocity;
        private LayerMask _obstacleMask;

        private Vector2 _currentDir = Vector2.right;
        private float _nextTickTime;
        private bool _configured;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
        }

        public void Configure(
            Enemy owner,
            int damage,
            float tickInterval,
            float maxLength,
            float turnSpeed,
            float stunSeconds,
            float knockbackVelocity,
            LayerMask obstacleMask)
        {
            _owner = owner;
            _damage = Mathf.Max(0, damage);
            _tickInterval = Mathf.Max(0.05f, tickInterval);
            _maxLength = Mathf.Max(0.5f, maxLength);
            _turnSpeed = Mathf.Max(1f, turnSpeed);
            _stunSeconds = Mathf.Max(0f, stunSeconds);
            _knockbackVelocity = Mathf.Max(0f, knockbackVelocity);
            _obstacleMask = obstacleMask;
            _configured = true;

            _currentDir = transform.right;
            if (_currentDir.sqrMagnitude < 0.001f)
                _currentDir = Vector2.right;
        }

        /// <summary>Beam widens (e.g. as Zort's HP drops). 1 = base width.</summary>
        public void SetWidth(float widthMultiplier)
        {
            _width = Mathf.Max(0.05f, widthMultiplier);

            if (_line != null)
            {
                _line.startWidth = _width;
                _line.endWidth = _width;
            }
        }

        public void TrackTarget(Transform target)
        {
            _target = target;
        }

        private void Update()
        {
            if (_line == null)
                return;

            Vector2 origin = transform.position;

            // Re-aim slowly toward the target.
            if (_target != null)
            {
                Vector2 desired = ((Vector2)_target.position - origin);
                if (desired.sqrMagnitude > 0.0001f)
                {
                    desired.Normalize();
                    float maxRadians = _turnSpeed * Mathf.Deg2Rad * Time.deltaTime;
                    _currentDir = Vector3.RotateTowards(_currentDir, desired, maxRadians, 0f);
                    _currentDir.Normalize();
                }
            }

            // Stop the beam at the first obstacle within range.
            float length = _maxLength;
            if (_obstacleMask.value != 0)
            {
                RaycastHit2D hit = Physics2D.Raycast(origin, _currentDir, _maxLength, _obstacleMask);
                if (hit.collider != null)
                    length = hit.distance;
            }

            Vector2 endPoint = origin + _currentDir * length;
            _line.SetPosition(0, origin);
            _line.SetPosition(1, endPoint);

            TryDamageWarrior(origin, endPoint);
        }

        private void TryDamageWarrior(Vector2 origin, Vector2 endPoint)
        {
            if (!_configured || _damage <= 0)
                return;

            if (Time.time < _nextTickTime)
                return;

            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null || warrior.IsDeadOrDying || warrior.IsDodging)
                return;

            Collider2D warriorCol = warrior.collider2;
            Vector2 warriorPos = warriorCol != null ? (Vector2)warriorCol.bounds.center : (Vector2)warrior.transform.position;

            // Distance from the Warrior center to the beam segment.
            float dist = DistancePointToSegment(warriorPos, origin, endPoint);
            float hitRadius = _width * 0.5f + (warriorCol != null ? warriorCol.bounds.extents.magnitude * 0.5f : 0.4f);

            if (dist > hitRadius)
                return;

            _nextTickTime = Time.time + _tickInterval;

            if (warrior.ShieldIsUp)
                return;

            warrior.TakeDamage(_damage);
            warrior.ApplyHitReaction(HitKind.Laser, origin, _stunSeconds, _knockbackVelocity);
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f)
                return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 projection = a + ab * t;
            return Vector2.Distance(p, projection);
        }
    }
}

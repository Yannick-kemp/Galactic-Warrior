using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// How a <see cref="VoidProjectile"/> behaves in flight.
    /// <para><b>Aerial</b> — straight-line velocity-held flight (Void Crescent, Rift Barrage's old look).</para>
    /// <para><b>GroundSlide</b> — launches as a gravity arc, then on the first platform contact
    /// switches to a flat horizontal slide toward the Warrior's position at landing time.</para>
    /// </summary>
    public enum ProjectileMode { Aerial, GroundSlide }

    /// <summary>
    /// Runtime projectile fired by <see cref="ZortBoss"/> (Void Crescent + Rift Barrage).
    /// The prefab can be a plain visual/VFX GameObject; ZortBoss adds and initializes
    /// this component automatically, just like HivernoxBoss does with its ice bolt.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class VoidProjectile : MonoBehaviour
    {
        // Gravity applied during the airborne phase of a GroundSlide shot so the fragment
        // arcs out of Zort's hand and falls onto the platform before it begins sliding.
        private const float GroundSlideLaunchGravity = 2.5f;

        private Enemy _owner;
        private Vector2 _direction = Vector2.right;
        private float _speed = 9f;
        private int _damage;
        private float _stunSeconds;
        private float _knockbackVelocity;
        private LayerMask _obstacleMask;
        private GameObject _impactFxPrefab;
        private Rigidbody2D _rb;
        private bool _resolved;

        private ProjectileMode _mode = ProjectileMode.Aerial;
        private bool _sliding;                       // GroundSlide only: true once it has landed
        private Vector2 _slideDir = Vector2.right;   // GroundSlide only: flat slide direction

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        public void Initialize(
            Enemy owner,
            Vector2 direction,
            float speed,
            int damage,
            float stunSeconds,
            float knockbackVelocity,
            float lifetime,
            LayerMask obstacleMask,
            GameObject impactFxPrefab,
            ProjectileMode mode = ProjectileMode.Aerial)
        {
            _owner = owner;
            _direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
            _speed = Mathf.Max(0f, speed);
            _damage = Mathf.Max(0, damage);
            _stunSeconds = Mathf.Max(0f, stunSeconds);
            _knockbackVelocity = Mathf.Max(0f, knockbackVelocity);
            _obstacleMask = obstacleMask;
            _impactFxPrefab = impactFxPrefab;
            _mode = mode;

            if (_rb == null)
                _rb = GetComponent<Rigidbody2D>();

            if (_rb != null)
            {
                _rb.freezeRotation = true;
                _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

                if (_mode == ProjectileMode.GroundSlide)
                {
                    // Launch arc: gravity drops the fragment onto the platform; the flat
                    // slide is started later in BeginGroundSlide() at first terrain contact.
                    _rb.gravityScale = GroundSlideLaunchGravity;
                    _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                }
                else
                {
                    _rb.gravityScale = 0f;
                }

                _rb.linearVelocity = _direction * _speed;
            }

            transform.right = _direction;

            if (lifetime > 0f)
                Destroy(gameObject, lifetime);
        }

        private void FixedUpdate()
        {
            if (_rb == null)
                return;

            if (_mode == ProjectileMode.Aerial)
            {
                // Straight-line flight, exactly as before.
                _rb.linearVelocity = _direction * _speed;
            }
            else if (_sliding)
            {
                // Flat surface slide; Y is frozen so it hugs the ground.
                _rb.linearVelocity = _slideDir * _speed;
            }
            // GroundSlide airborne phase: leave the Rigidbody to gravity so it arcs down.
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Vector2 hitPoint = other != null
                ? other.ClosestPoint(transform.position)
                : (Vector2)transform.position;

            ResolveHit(other, hitPoint);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            Vector2 hitPoint = transform.position;

            if (collision != null && collision.contactCount > 0)
                hitPoint = collision.GetContact(0).point;

            ResolveHit(collision.collider, hitPoint);
        }

        private void ResolveHit(Collider2D other, Vector2 hitPoint)
        {
            if (_resolved || other == null)
                return;

            // Never collide with the boss that fired this.
            if (_owner != null && other.transform.IsChildOf(_owner.transform))
                return;

            Warrior warrior = other.GetComponent<Warrior>() ?? other.GetComponentInParent<Warrior>();
            if (warrior != null)
            {
                _resolved = true;

                if (!warrior.IsDeadOrDying && !warrior.IsDodging)
                {
                    // Shield up: absorbed, no damage. Otherwise apply the standard hit.
                    if (!warrior.ShieldIsUp && _damage > 0)
                    {
                        Vector2 from = _owner != null ? (Vector2)_owner.transform.position : hitPoint;
                        warrior.TakeDamage(_damage);
                        warrior.ApplyHitReaction(HitKind.Projectile, from, _stunSeconds, _knockbackVelocity);
                    }
                }

                SpawnImpactFx(hitPoint);
                Destroy(gameObject);
                return;
            }

            // Pass harmlessly through other enemies.
            Enemy enemy = other.GetComponent<Enemy>() ?? other.GetComponentInParent<Enemy>();
            if (enemy != null)
                return;

            if (IsObstacle(other.gameObject.layer))
            {
                if (_mode == ProjectileMode.GroundSlide)
                {
                    // First terrain contact starts the slide; afterwards terrain is ignored —
                    // only the Warrior or the lifetime timeout ends a sliding projectile.
                    if (!_sliding)
                        BeginGroundSlide();
                    return;
                }

                _resolved = true;
                SpawnImpactFx(hitPoint);
                Destroy(gameObject);
            }
        }

        private void BeginGroundSlide()
        {
            _sliding = true;

            if (_rb != null)
            {
                _rb.gravityScale = 0f;
                _rb.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionY;
            }

            // Resolve the slide direction toward the Warrior's CURRENT position (landing time),
            // not the original fire direction — same live-target lookup used by VoidBeam/VoidWraith.
            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;

            float sign;
            if (warrior != null)
            {
                float dx = warrior.transform.position.x - transform.position.x;
                sign = Mathf.Abs(dx) > 0.001f ? Mathf.Sign(dx) : (_direction.x >= 0f ? 1f : -1f);
            }
            else
            {
                sign = _direction.x >= 0f ? 1f : -1f;
            }

            _slideDir = new Vector2(sign, 0f);
            transform.right = _slideDir;

            if (_rb != null)
                _rb.linearVelocity = _slideDir * _speed;
        }

        private void SpawnImpactFx(Vector2 hitPoint)
        {
            if (_impactFxPrefab == null)
                return;

            GameObject fx = Instantiate(_impactFxPrefab, hitPoint, Quaternion.identity);
            Destroy(fx, 2f);
        }

        private bool IsObstacle(int layer)
        {
            return (_obstacleMask.value & (1 << layer)) != 0;
        }
    }
}

using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// How a <see cref="VoidProjectile"/> behaves in flight.
    /// <para><b>Aerial</b> — straight-line, velocity-held flight.</para>
    /// <para><b>GroundSlide</b> — continuously hunts the platform the Warrior is standing on.
    /// It falls straight down, passing through every platform EXCEPT the Warrior's current one;
    /// on landing it hugs that surface and slides toward the Warrior.
    /// If the Warrior later changes platform (jump, fall, moving platform, elevator, …) the
    /// projectile detaches, falls again, and relocates to the Warrior's new platform.</para>
    /// </summary>
    public enum ProjectileMode { Aerial, GroundSlide }

    /// <summary>
    /// Runtime projectile fired by <see cref="ZortBoss"/> (Void Crescent Slash).
    /// The prefab can be a plain visual/VFX GameObject; ZortBoss adds and initializes
    /// this component automatically, just like HivernoxBoss does with its ice bolt.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class VoidProjectile : MonoBehaviour
    {
        // Gravity applied during the falling/seeking phase of a GroundSlide shot.
        private const float GroundSlideFallGravity = 2.5f;
        // Slack past the target platform's edge before the projectile decides it has run off.
        private const float EdgeMargin = 0.1f;

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
        private bool _sliding;                              // GroundSlide: true while riding a platform
        private PlatFormColliderTrigger _targetPlatform;    // GroundSlide: the platform currently being ridden
        private float _surfaceOffset = 0.18f;               // GroundSlide: half-height used to sit on a surface

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
                    EnterSeek();
                }
                else
                {
                    _rb.gravityScale = 0f;
                    _rb.linearVelocity = _direction * _speed;
                }
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
                _rb.linearVelocity = _direction * _speed;
                return;
            }

            // ── GroundSlide ─────────────────────────────────────────────────────
            // Seeking phase: do nothing — gravity + FreezePositionX make it fall straight down.
            // ResolveHit decides when it has reached the Warrior's current platform.
            if (!_sliding)
                return;

            Warrior warrior = GetWarrior();

            // Continuously re-check: did the Warrior move to a different platform, or did we run
            // off the edge of the one we are on? If so, drop back to a vertical fall and re-seek.
            if (ShouldLeaveCurrentPlatform(warrior))
            {
                EnterSeek();
                return;
            }

            // Slide horizontally toward the Warrior and stay glued to the (possibly moving) surface.
            float vx = 0f;
            if (warrior != null)
            {
                float dx = warrior.transform.position.x - transform.position.x;
                if (Mathf.Abs(dx) > 0.05f)
                {
                    vx = Mathf.Sign(dx) * _speed;
                    transform.right = new Vector2(Mathf.Sign(dx), 0f);
                }
            }

            _rb.linearVelocity = new Vector2(vx, 0f);

            if (_targetPlatform != null && _targetPlatform.platformCollider != null)
            {
                Vector2 p = _rb.position;
                p.y = _targetPlatform.platformCollider.bounds.max.y + _surfaceOffset;
                _rb.position = p;
            }
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
                    // Only the Warrior's CURRENT platform may stop the fall; every other platform is
                    // passed through (the trigger collider never physically blocks).
                    if (!_sliding && IsWarriorCurrentPlatform(other, out PlatFormColliderTrigger plat))
                        BeginGroundSlide(plat);

                    return;
                }

                _resolved = true;
                SpawnImpactFx(hitPoint);
                Destroy(gameObject);
            }
        }

        /// <summary>True when the projectile should abandon the platform it is sliding on.</summary>
        private bool ShouldLeaveCurrentPlatform(Warrior warrior)
        {
            // Warrior airborne (mid-jump/fall): keep sliding until they actually land somewhere,
            // so we don't thrash off and on during the jump arc.
            if (warrior == null)
                return false;

            PlatFormColliderTrigger warriorPlatform = warrior.CurrentplatForm;

            // Warrior is now standing on a different platform → relocate.
            if (warriorPlatform != null && warriorPlatform != _targetPlatform)
                return true;

            // Slid past the edge of the platform we are riding → drop off and re-seek.
            if (_targetPlatform != null && _targetPlatform.platformCollider != null)
            {
                Bounds b = _targetPlatform.platformCollider.bounds;
                float x = transform.position.x;
                if (x < b.min.x - EdgeMargin || x > b.max.x + EdgeMargin)
                    return true;
            }

            return false;
        }

        /// <summary>True only when <paramref name="other"/> belongs to the platform the Warrior is standing on right now.</summary>
        private bool IsWarriorCurrentPlatform(Collider2D other, out PlatFormColliderTrigger plat)
        {
            plat = null;

            Warrior warrior = GetWarrior();
            if (warrior == null)
                return false;

            PlatFormColliderTrigger warriorPlatform = warrior.CurrentplatForm;
            if (warriorPlatform == null)
                return false; // Warrior airborne — keep falling until they are standing on something.

            PlatFormColliderTrigger hitPlatform =
                other.GetComponent<PlatFormColliderTrigger>() ?? other.GetComponentInParent<PlatFormColliderTrigger>();

            if (hitPlatform != null && hitPlatform == warriorPlatform)
            {
                plat = hitPlatform;
                return true;
            }

            return false;
        }

        /// <summary>Enter (or return to) the falling/seeking state so the projectile can relocate.</summary>
        private void EnterSeek()
        {
            _sliding = false;
            _targetPlatform = null;

            if (_rb != null)
            {
                _rb.gravityScale = GroundSlideFallGravity;
                // Pure vertical drop: freeze X so it falls straight down its current column, and
                // clear any leftover slide momentum. Only the Warrior's platform (ResolveHit) lands it.
                _rb.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionX;
                _rb.linearVelocity = Vector2.zero;
            }
        }

        private void BeginGroundSlide(PlatFormColliderTrigger plat)
        {
            _sliding = true;
            _targetPlatform = plat;

            // Sit exactly on the surface using the projectile collider's half-height.
            Collider2D col = GetComponent<Collider2D>();
            _surfaceOffset = col != null ? col.bounds.extents.y : 0.18f;

            if (_rb != null)
            {
                _rb.gravityScale = 0f;
                _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            }

            if (plat != null && plat.platformCollider != null && _rb != null)
            {
                Vector2 p = _rb.position;
                p.y = plat.platformCollider.bounds.max.y + _surfaceOffset;
                _rb.position = p;
            }
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

        private static Warrior GetWarrior()
        {
            return GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
        }
    }
}

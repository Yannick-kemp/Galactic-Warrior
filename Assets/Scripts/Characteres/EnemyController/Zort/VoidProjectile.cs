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

        // GroundSlide — origin-platform targeting (optional). When _hasFixedTarget is true, the shot
        // lands at _targetSurfaceY and slides toward _targetSlideX (the Warrior's surface/position at
        // LAUNCH), bounded by [_targetMinX, _targetMaxX], independent of where the Warrior goes next
        // (e.g. jumps away). When false, GroundSlide keeps its original dynamic hunt of the Warrior's
        // current platform. Aerial ignores all of these.
        private bool _hasFixedTarget;
        private float _targetSurfaceY;
        private float _targetSlideX;
        private float _targetMinX = float.NegativeInfinity;
        private float _targetMaxX = float.PositiveInfinity;

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
            ProjectileMode mode = ProjectileMode.Aerial,
            bool hasGroundSlideTarget = false,
            float targetSurfaceY = 0f,
            float targetSlideX = 0f,
            float targetMinX = float.NegativeInfinity,
            float targetMaxX = float.PositiveInfinity)
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

            // Origin-platform targeting only applies to GroundSlide; ignored otherwise so Aerial and any
            // GroundSlide caller that omits the args keep their exact previous behaviour.
            _hasFixedTarget = hasGroundSlideTarget && mode == ProjectileMode.GroundSlide;
            _targetSurfaceY = targetSurfaceY;
            _targetSlideX = targetSlideX;
            _targetMinX = targetMinX;
            _targetMaxX = targetMaxX;

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

            // Aerial: orient along the travel direction. GroundSlide: never use _direction here —
            // transform.right = Vector2.down would rotate the whole VFX prefab -90°, swinging its
            // offset child systems (Flying/Debris/Trails) around the pivot. Instead, flip it
            // HORIZONTALLY toward the Warrior right away (same transform.right convention the slide
            // phase re-applies each FixedUpdate, so landing simply overwrites with the same logic).
            if (_mode == ProjectileMode.Aerial)
                transform.right = _direction;
            else
                transform.right = new Vector2(GetHorizontalSignTowardWarrior(), 0f);

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
            // Seeking phase: gravity + FreezePositionX make it fall straight down.
            //  • Fixed target : land as soon as it reaches the captured origin surface (Y threshold),
            //                   regardless of the Warrior — robust to a jump during the fall.
            //  • Dynamic      : ResolveHit lands it on the Warrior's CURRENT platform.
            if (!_sliding)
            {
                if (_hasFixedTarget && _rb.position.y <= _targetSurfaceY + _surfaceOffset)
                    BeginFixedGroundSlide();
                return;
            }

            // Fixed-target slide: ride the captured origin surface toward the captured X (where the
            // Warrior was at launch), clamped to the platform's X extent. The Warrior's current
            // position is never consulted here, so a jump after the launch changes nothing.
            if (_hasFixedTarget)
            {
                float targetX = Mathf.Clamp(_targetSlideX, _targetMinX, _targetMaxX);
                float dxFixed = targetX - transform.position.x;
                float vxFixed = 0f;
                if (Mathf.Abs(dxFixed) > 0.05f)
                {
                    vxFixed = Mathf.Sign(dxFixed) * _speed;
                    transform.right = new Vector2(Mathf.Sign(dxFixed), 0f);
                }
                _rb.linearVelocity = new Vector2(vxFixed, 0f);

                Vector2 pFixed = _rb.position;
                pFixed.y = _targetSurfaceY + _surfaceOffset;
                _rb.position = pFixed;
                return;
            }

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
                    // Fixed target: ignore collider-based landing entirely — the fall is resolved by the
                    // Y threshold in FixedUpdate, so a Warrior jump (CurrentplatForm == null) can't make
                    // the shot pass through its captured origin platform.
                    // Dynamic: only the Warrior's CURRENT platform may stop the fall; every other
                    // platform is passed through (the trigger collider never physically blocks).
                    if (!_hasFixedTarget && !_sliding && IsWarriorCurrentPlatform(other, out PlatFormColliderTrigger plat))
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

        /// <summary>Land on the captured ORIGIN surface (fixed-target GroundSlide). Unlike
        /// <see cref="BeginGroundSlide"/> it tracks no PlatFormColliderTrigger — it sits on _targetSurfaceY
        /// and the slide phase (FixedUpdate) drives it toward _targetSlideX within the captured X bounds.</summary>
        private void BeginFixedGroundSlide()
        {
            _sliding = true;
            _targetPlatform = null;

            Collider2D col = GetComponent<Collider2D>();
            _surfaceOffset = col != null ? col.bounds.extents.y : 0.18f;

            if (_rb != null)
            {
                _rb.gravityScale = 0f;
                _rb.constraints = RigidbodyConstraints2D.FreezeRotation;

                Vector2 p = _rb.position;
                p.y = _targetSurfaceY + _surfaceOffset;
                _rb.position = p;
            }
        }

        // Horizontal sign toward the Warrior's current position; falls back to the fire direction's
        // X sign (then right) when no Warrior exists. Shared convention between the spawn orientation
        // and the slide phase, so the two can never fight each other.
        private float GetHorizontalSignTowardWarrior()
        {
            Warrior warrior = GetWarrior();
            if (warrior != null)
            {
                float dx = warrior.transform.position.x - transform.position.x;
                if (Mathf.Abs(dx) > 0.001f)
                    return Mathf.Sign(dx);
            }

            return _direction.x >= 0f ? 1f : -1f;
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

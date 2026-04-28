using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Runtime projectile component for Hivernox ice bullets.
    /// You can add this to the projectile prefab, or HivernoxBoss will add it automatically.
    /// The prefab can be a normal GameObject/VFX object.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class HivernoxIceProjectile : MonoBehaviour
    {
        [SerializeField] private GameObject impactFxPrefab;
        [SerializeField] private bool destroyOnAnyEnemy = false;

        private HivernoxBoss _owner;
        private Vector2 _direction = Vector2.right;
        private float _speed = 8f;
        private float _freezeSeconds = 2f;
        private float _projectileDamage;
        private LayerMask _obstacleMask;
        private Rigidbody2D _rb;
        private bool _resolved;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        public void Initialize(
            HivernoxBoss owner,
            Vector2 direction,
            float speed,
            float freezeSeconds,
            float projectileDamage,
            float lifetime,
            LayerMask obstacleMask)
        {
            _owner = owner;
            _direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
            _speed = Mathf.Max(0f, speed);
            _freezeSeconds = Mathf.Max(0f, freezeSeconds);
            _projectileDamage = Mathf.Max(0f, projectileDamage);
            _obstacleMask = obstacleMask;

            if (_rb == null)
                _rb = GetComponent<Rigidbody2D>();

            if (_rb != null)
            {
                _rb.gravityScale = 0f;
                _rb.freezeRotation = true;
                _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                _rb.linearVelocity = _direction * _speed;
            }

            transform.right = _direction;

            if (lifetime > 0f)
                Destroy(gameObject, lifetime);
        }

        private void FixedUpdate()
        {
            if (_rb != null)
                _rb.linearVelocity = _direction * _speed;
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

            if (_owner != null && other.transform.IsChildOf(_owner.transform))
                return;

            Warrior warrior = other.GetComponent<Warrior>() ?? other.GetComponentInParent<Warrior>();
            if (warrior != null)
            {
                _resolved = true;

                if (_projectileDamage > 0f)
                {
                    warrior.TryReceiveHivernoxDamage(
                        source: _owner,
                        damage: _projectileDamage,
                        canBeBlockedByShield: false,
                        stunSeconds: 0f,
                        knockbackVelocity: 0f);
                }

                bool frozen = warrior.TryFreezeFromHivernox(
                    source: _owner,
                    seconds: _freezeSeconds,
                    hitWorldPosition: hitPoint);

                if (frozen && _owner != null)
                    _owner.NotifyWarriorFrozen(warrior);

                SpawnImpactFx(hitPoint);
                Destroy(gameObject);
                return;
            }

            Enemy enemy = other.GetComponent<Enemy>() ?? other.GetComponentInParent<Enemy>();
            if (enemy != null && !destroyOnAnyEnemy)
                return;

            if (IsObstacle(other.gameObject.layer) || destroyOnAnyEnemy)
            {
                _resolved = true;
                SpawnImpactFx(hitPoint);
                Destroy(gameObject);
            }
        }

        private void SpawnImpactFx(Vector2 hitPoint)
        {
            if (impactFxPrefab == null)
                return;

            GameObject fx = Instantiate(
                impactFxPrefab,
                hitPoint,
                Quaternion.identity);

            Destroy(fx, 2f);
        }

        private bool IsObstacle(int layer)
        {
            return ((_obstacleMask.value & (1 << layer)) != 0);
        }

        private void SpawnImpactFx()
        {
            if (impactFxPrefab == null)
                return;

            GameObject fx = Instantiate(impactFxPrefab, transform.position, Quaternion.identity);
            Destroy(fx, 2f);
        }
    }
}

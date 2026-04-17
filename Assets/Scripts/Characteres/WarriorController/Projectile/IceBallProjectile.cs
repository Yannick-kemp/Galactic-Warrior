using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Events;
using UnityEngine;

namespace Assets.Scripts.Relics.Projectiles
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class IceBallProjectile : MonoBehaviour
    {
        [Header("Impact")]
        [SerializeField] private float knockback = 0.35f;
        [SerializeField] private LayerMask obstacleMask;
        [SerializeField] private bool destroyOnAnyObstacle = true;

        private Warrior _owner;
        private PlayerEventHub _hub;
        private Rigidbody2D _rb;
        private Collider2D[] _selfCols;

        private Vector2 _dir;
        private int _damage;
        private float _stunSeconds;
        private bool _spent;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _selfCols = GetComponentsInChildren<Collider2D>(true);

            if (_rb != null)
            {
                _rb.gravityScale = 0f;
                _rb.freezeRotation = true;
            }
        }

        public void Init(
            Warrior owner,
            Vector2 direction,
            float speed,
            int damage,
            float stunSeconds,
            float lifeTime)
        {
            _owner = owner;
            _hub = owner != null ? owner.GetComponent<PlayerEventHub>() : null;

            _dir = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector2.right;

            _damage = Mathf.Max(1, damage);
            _stunSeconds = Mathf.Max(0f, stunSeconds);

            transform.right = _dir;

            if (_rb != null)
                _rb.linearVelocity = _dir * Mathf.Max(0.1f, speed);

            IgnoreOwnerCollisions();

            Destroy(gameObject, Mathf.Max(0.1f, lifeTime));
        }

        private void IgnoreOwnerCollisions()
        {
            if (_owner == null || _selfCols == null) return;

            var ownerCols = _owner.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < _selfCols.Length; i++)
            {
                if (_selfCols[i] == null) continue;

                for (int j = 0; j < ownerCols.Length; j++)
                {
                    if (ownerCols[j] == null) continue;
                    Physics2D.IgnoreCollision(_selfCols[i], ownerCols[j], true);
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_spent || other == null) return;

            Enemy enemy = other.GetComponent<Enemy>() ?? other.GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                HitEnemy(enemy);
                return;
            }

            if (((1 << other.gameObject.layer) & obstacleMask) != 0)
            {
                if (destroyOnAnyObstacle)
                    SpendAndDestroy();
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_spent || collision == null || collision.collider == null) return;

            Enemy enemy = collision.collider.GetComponent<Enemy>() ?? collision.collider.GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                HitEnemy(enemy);
                return;
            }

            if (((1 << collision.collider.gameObject.layer) & obstacleMask) != 0)
            {
                if (destroyOnAnyObstacle)
                    SpendAndDestroy();
            }
        }

        private void HitEnemy(Enemy enemy)
        {
            if (enemy == null) return;

            _spent = true;

            enemy.DisableAttackTemporarily();

            bool killed = enemy.TakeDamageAndReturnKilled(_damage);

            if (!killed && _stunSeconds > 0f)
                enemy.ApplyStun(_stunSeconds);

            enemy.stepBackDistance = knockback;
            StartCoroutine(_dir.x >= 0f ? enemy.SmoothStepBack(true) : enemy.SmoothStepBack(false));

            if (_hub != null && _owner != null)
            {
                _hub.RaiseHit(new HitEvent
                {
                    attacker = _owner.gameObject,
                    target = enemy.gameObject,
                    damage = _damage,
                    hitPoint = transform.position
                });

                if (killed)
                {
                    _hub.RaiseKill(new KillEvent
                    {
                        killer = _owner.gameObject,
                        victim = enemy.gameObject
                    });
                }
            }

            Destroy(gameObject);
        }

        private void SpendAndDestroy()
        {
            if (_spent) return;
            _spent = true;
            Destroy(gameObject);
        }
    }
}
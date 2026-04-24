using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Events;
using Assets.Scripts.Tools;
using UnityEngine;

namespace Assets.Scripts.Relics.Projectiles
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class IceBulletProjectile : MonoBehaviour
    {
        [Header("Impact")]
        [SerializeField] private float knockback = 0.35f;
        [SerializeField] private LayerMask obstacleMask;
        [SerializeField] private bool destroyOnAnyObstacle = true;

        [Header("Hit FX")]
        [SerializeField] private GameObject hitFxPrefab;          // assign FX_Ice_Hit
        [SerializeField] private float hitFxDestroyAfter = 0.8f;
        [SerializeField] private Vector3 hitFxOffset = Vector3.zero;

        [Header("Orientation")]
        [SerializeField] private bool rotateToTravelDirection = true;

        [Header("Hit SFX")]
        [SerializeField] private AudioClip hitSfxClip; // assign arrow-impact-87260
        [SerializeField, Range(0f, 1f)] private float hitSfxVolume = 0.9f;
        [SerializeField] private Vector2 hitSfxPitchRange = new Vector2(0.98f, 1.02f);
        [SerializeField] private float hitSfxSpatialBlend = 0f;
        [SerializeField] private float hitSfxMaxDistance = 20f;

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

            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
                col.isTrigger = true;
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

            if (rotateToTravelDirection)
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
                HitEnemy(enemy, other);
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
                HitEnemy(enemy, collision.collider);
                return;
            }

            if (((1 << collision.collider.gameObject.layer) & obstacleMask) != 0)
            {
                if (destroyOnAnyObstacle)
                    SpendAndDestroy();
            }
        }

        private void HitEnemy(Enemy enemy, Collider2D hitCollider)
        {
            if (enemy == null) return;

            _spent = true;

            Vector3 hitPoint = transform.position;
            if (hitCollider != null)
                hitPoint = hitCollider.ClosestPoint(transform.position);

            SpawnHitFx(hitPoint);

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
                    hitPoint = hitPoint
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

        private void SpawnHitFx(Vector3 hitPoint)
        {
            Vector3 fxPos = hitPoint + hitFxOffset;

            GameObject fx = null;

            if (hitFxPrefab != null)
            {
                fx = Instantiate(hitFxPrefab, fxPos, Quaternion.identity);

                ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                {
                    systems[i].Play(true);
                }

                Destroy(fx, Mathf.Max(0.1f, hitFxDestroyAfter));
            }

            OneShotAudio.Play(
                hitSfxClip,
                fxPos,
                hitSfxVolume,
                hitSfxPitchRange,
                hitSfxSpatialBlend,
                hitSfxMaxDistance
            );
        }

        private void SpendAndDestroy()
        {
            if (_spent) return;
            _spent = true;
            Destroy(gameObject);
        }
    }
}
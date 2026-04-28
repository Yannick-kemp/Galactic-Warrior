using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class FallingStone : MonoBehaviour
    {
        [Header("Damage")]
        [SerializeField] private float damage = 15f;

        [Header("Life")]
        [SerializeField] private float lifeTime = 8f;
        [SerializeField] private float gravityScale = 1.5f;
        [SerializeField] private float spinSpeed = 240f;

        [Header("Impact")]
        [SerializeField] private GameObject impactVfxPrefab;
        [SerializeField] private AudioClip impactSfx;
        [SerializeField] private float impactSfxVolume = 1f;

        [Header("Platform Miss Repulse")]
        [SerializeField] private bool repulseWarriorWhenStoneHitsHisPlatform = true;
        [SerializeField] private float platformRepulseDistance = 2.25f;
        [SerializeField] private float platformRepulseDuration = 0.18f;
        [SerializeField] private float platformRepulseControlLock = 0.25f;

        private Rigidbody2D rb;
        private bool launched;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
        }

        public void Launch()
        {
            launched = true;
            rb.gravityScale = gravityScale;
            Destroy(gameObject, lifeTime);
        }

        private void Update()
        {
            if (!launched)
                return;

            transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!launched)
                return;

            Warrior warriorHit = collision.collider.GetComponentInParent<Warrior>();

            if (warriorHit != null)
            {
                warriorHit.TakeDamage(damage);
            }
            else
            {
                TryRepulseWarriorIfStoneHitHisPlatform(collision);
            }

            SpawnImpactFeedback();

            Destroy(gameObject);
        }

        private void TryRepulseWarriorIfStoneHitHisPlatform(Collision2D collision)
        {
            if (!repulseWarriorWhenStoneHitsHisPlatform)
                return;

            PlatFormColliderTrigger platform =
                collision.collider.GetComponentInParent<PlatFormColliderTrigger>();

            if (platform == null)
                return;

            Warrior warrior = GameMgr.Instance != null
                ? GameMgr.Instance.WarriorInstance
                : Warrior.Instance;

            if (warrior == null)
                return;

            Vector2 impactPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : (Vector2)transform.position;

            warrior.TryRepulseFromPlatformStoneImpact(
                platform,
                impactPoint,
                platformRepulseDistance,
                platformRepulseDuration,
                platformRepulseControlLock
            );
        }

        private void SpawnImpactFeedback()
        {
            if (impactVfxPrefab != null)
                Instantiate(impactVfxPrefab, transform.position, Quaternion.identity);

            if (impactSfx != null)
                AudioSource.PlayClipAtPoint(impactSfx, transform.position, impactSfxVolume);
        }
    }
}
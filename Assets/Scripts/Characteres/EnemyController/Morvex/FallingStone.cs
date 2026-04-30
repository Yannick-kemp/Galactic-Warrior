using Assets.Scripts.Characteres.WarriorController;
using System.Collections.Generic;
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

        [Header("Shield Block")]
        [Tooltip("When true, a collision/trigger with a collider on Unity layer 'Shield Laser' destroys the stone before it can damage Warrior.")]
        [SerializeField] private bool shieldLaserBlocksStone = true;

        [Tooltip("Extra safety: if the stone reaches Warrior's body collider while Warrior.ShieldIsUp is true, the hit is still blocked.")]
        [SerializeField] private bool shieldUpFallbackBlocksStone = true;

        [Tooltip("Durability cost applied when the shield blocks this falling stone. Set to 0 if the stone should cost nothing to block.")]
        [SerializeField] private float shieldBlockCost = 5f;

        [Header("Sprint Relic Collision Ignore")]
        [Tooltip("When true, Morvex's falling stone ignores Warrior colliders while the sprint relic is active.")]
        [SerializeField] private bool ignoreWarriorCollisionDuringSprint = true;

        [Tooltip("When true, collision with Warrior is restored after sprint ends. Restoration waits until the stone is no longer overlapping Warrior.")]
        [SerializeField] private bool restoreWarriorCollisionAfterSprint = true;

        [Header("Platform Miss Repulse")]
        [SerializeField] private bool repulseWarriorWhenStoneHitsHisPlatform = true;
        [SerializeField] private float platformRepulseDistance = 2.25f;
        [SerializeField] private float platformRepulseDuration = 0.18f;
        [SerializeField] private float platformRepulseControlLock = 0.25f;

        private Rigidbody2D rb;
        private Collider2D[] stoneColliders;

        private bool launched;
        private bool impactResolved;
        private int shieldLaserLayer = -1;

        private bool ignoringWarriorBecauseSprint;
        private readonly List<Collider2D> sprintIgnoredWarriorColliders = new List<Collider2D>();

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;

            stoneColliders = GetComponentsInChildren<Collider2D>(true);

            shieldLaserLayer = LayerMask.NameToLayer("Shield Laser");
            if (shieldLaserBlocksStone && shieldLaserLayer < 0)
            {
                Debug.LogWarning(
                    "[FallingStone] Unity layer 'Shield Laser' does not exist. " +
                    "Create it in Project Settings > Tags and Layers, then assign Warrior shieldHitbox to that layer.",
                    this);
            }
        }

        public void Launch()
        {
            launched = true;
            rb.gravityScale = gravityScale;

            RefreshSprintCollisionIgnore();

            Destroy(gameObject, lifeTime);
        }

        private void Update()
        {
            if (!launched)
                return;

            transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (!launched || impactResolved)
                return;

            RefreshSprintCollisionIgnore();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!launched || impactResolved)
                return;

            // IMPORTANT:
            // Sprint relic means the stone must pass through Warrior.
            // No damage, no impact VFX, no explosion, no Destroy().
            if (TryIgnoreWarriorCollisionBecauseSprint(collision.collider))
                return;

            // IMPORTANT:
            // Check the Shield Laser layer BEFORE GetComponentInParent<Warrior>().
            // The shield hitbox is a child of Warrior, so Warrior could be found
            // through the shield collider and accidentally receive damage.
            if (TryBlockWithShieldLaserCollider(collision.collider))
                return;

            Warrior warriorHit = collision.collider.GetComponentInParent<Warrior>();

            if (warriorHit != null)
            {
                // Safety fallback: if the stone reaches Warrior's body collider in the same frame
                // or the shield collider did not catch it, ShieldIsUp still prevents damage.
                if (TryBlockWithActiveWarriorShield(warriorHit))
                    return;

                warriorHit.TakeDamage(damage);
            }
            else
            {
                TryRepulseWarriorIfStoneHitHisPlatform(collision);
            }

            ResolveImpactAndDestroy();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!launched || impactResolved)
                return;

            // Sprint relic means the stone passes through Warrior trigger colliders too.
            if (TryIgnoreWarriorCollisionBecauseSprint(other))
                return;

            // Supports shieldHitbox configured as Is Trigger.
            TryBlockWithShieldLaserCollider(other);
        }

        private bool TryIgnoreWarriorCollisionBecauseSprint(Collider2D touchedCollider)
        {
            if (!ignoreWarriorCollisionDuringSprint || touchedCollider == null)
                return false;

            Warrior warrior = touchedCollider.GetComponentInParent<Warrior>();
            if (warrior == null)
                return false;

            if (!warrior.IsDodging)
                return false;

            SetIgnoreCollisionWithWarrior(warrior, true);

            // Return true so FallingStone does NOT damage, explode, or destroy itself.
            return true;
        }

        private void RefreshSprintCollisionIgnore()
        {
            if (!ignoreWarriorCollisionDuringSprint)
                return;

            Warrior warrior = GetCurrentWarrior();
            bool shouldIgnore = warrior != null && warrior.IsDodging;

            if (shouldIgnore)
            {
                SetIgnoreCollisionWithWarrior(warrior, true);
                return;
            }

            if (!ignoringWarriorBecauseSprint)
                return;

            if (!restoreWarriorCollisionAfterSprint)
                return;

            // Do not restore while overlapping, otherwise Unity may create a late hit
            // exactly when sprint ends.
            if (IsStillOverlappingIgnoredWarrior())
                return;

            ClearSprintWarriorCollisionIgnore();
        }

        private Warrior GetCurrentWarrior()
        {
            if (GameMgr.Instance != null && GameMgr.Instance.WarriorInstance != null)
                return GameMgr.Instance.WarriorInstance;

            return Warrior.Instance;
        }

        private void SetIgnoreCollisionWithWarrior(Warrior warrior, bool ignore)
        {
            if (warrior == null)
                return;

            Collider2D[] warriorColliders = warrior.GetComponentsInChildren<Collider2D>(true);

            for (int s = 0; s < stoneColliders.Length; s++)
            {
                Collider2D stoneCollider = stoneColliders[s];
                if (stoneCollider == null)
                    continue;

                for (int w = 0; w < warriorColliders.Length; w++)
                {
                    Collider2D warriorCollider = warriorColliders[w];
                    if (warriorCollider == null)
                        continue;

                    Physics2D.IgnoreCollision(stoneCollider, warriorCollider, ignore);

                    if (ignore && !sprintIgnoredWarriorColliders.Contains(warriorCollider))
                        sprintIgnoredWarriorColliders.Add(warriorCollider);
                }
            }

            if (ignore)
                ignoringWarriorBecauseSprint = true;
        }

        private bool IsStillOverlappingIgnoredWarrior()
        {
            for (int s = 0; s < stoneColliders.Length; s++)
            {
                Collider2D stoneCollider = stoneColliders[s];
                if (stoneCollider == null || !stoneCollider.enabled)
                    continue;

                for (int w = 0; w < sprintIgnoredWarriorColliders.Count; w++)
                {
                    Collider2D warriorCollider = sprintIgnoredWarriorColliders[w];
                    if (warriorCollider == null || !warriorCollider.enabled)
                        continue;

                    if (stoneCollider.bounds.Intersects(warriorCollider.bounds))
                        return true;
                }
            }

            return false;
        }

        private void ClearSprintWarriorCollisionIgnore()
        {
            for (int s = 0; s < stoneColliders.Length; s++)
            {
                Collider2D stoneCollider = stoneColliders[s];
                if (stoneCollider == null)
                    continue;

                for (int w = 0; w < sprintIgnoredWarriorColliders.Count; w++)
                {
                    Collider2D warriorCollider = sprintIgnoredWarriorColliders[w];
                    if (warriorCollider == null)
                        continue;

                    Physics2D.IgnoreCollision(stoneCollider, warriorCollider, false);
                }
            }

            sprintIgnoredWarriorColliders.Clear();
            ignoringWarriorBecauseSprint = false;
        }

        private bool TryBlockWithShieldLaserCollider(Collider2D other)
        {
            if (!shieldLaserBlocksStone || other == null)
                return false;

            if (!IsShieldLaserLayer(other.gameObject.layer))
                return false;

            Warrior shieldOwner = other.GetComponentInParent<Warrior>();
            if (shieldOwner != null)
                shieldOwner.TryAbsorbSqueeze(shieldBlockCost);

            ResolveImpactAndDestroy();
            return true;
        }

        private bool TryBlockWithActiveWarriorShield(Warrior warrior)
        {
            if (!shieldUpFallbackBlocksStone || warrior == null)
                return false;

            if (!warrior.ShieldIsUp)
                return false;

            // Keep this protection tied to the requested Shield Laser layer setup.
            if (shieldLaserLayer < 0)
                return false;

            warrior.TryAbsorbSqueeze(shieldBlockCost);
            ResolveImpactAndDestroy();
            return true;
        }

        private bool IsShieldLaserLayer(int layer)
        {
            return shieldLaserLayer >= 0 && layer == shieldLaserLayer;
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

        private void ResolveImpactAndDestroy()
        {
            if (impactResolved)
                return;

            impactResolved = true;
            SpawnImpactFeedback();
            Destroy(gameObject);
        }

        private void SpawnImpactFeedback()
        {
            if (impactVfxPrefab != null)
                Instantiate(impactVfxPrefab, transform.position, Quaternion.identity);

            if (impactSfx != null)
                AudioSource.PlayClipAtPoint(impactSfx, transform.position, impactSfxVolume);
        }

        private void OnDisable()
        {
            ClearSprintWarriorCollisionIgnore();
        }

        private void OnDestroy()
        {
            ClearSprintWarriorCollisionIgnore();
        }
    }
}
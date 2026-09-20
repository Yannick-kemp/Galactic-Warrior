using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Projectiles;
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

        [Header("Carry / Reserve Safety")]
        [Tooltip("When true, this FallingStone can never resolve impact or destroy itself while Morvex is carrying it.")]
        [SerializeField] private bool ignoreAllImpactsWhileCarried = true;

        [Tooltip("When true, a carried visual stone disables its Rigidbody2D simulation while held by Morvex.")]
        [SerializeField] private bool disableRigidbodySimulationWhileCarried = true;

        [Tooltip("When true, a carried visual stone disables all of its Collider2D components while held by Morvex.")]
        [SerializeField] private bool disableCollidersWhileCarried = true;

        [Tooltip("Extra safety if you keep colliders enabled: the carried stone ignores every platform collider until Launch() is called.")]
        [SerializeField] private bool ignorePlatformsWhileCarried = true;

        [Tooltip("When true, platform trigger colliders are also ignored while the stone is carried/unlaunched.")]
        [SerializeField] private bool includePlatformTriggersWhileCarried = true;

        [Header("Platform Miss Repulse")]
        [SerializeField] private bool repulseWarriorWhenStoneHitsHisPlatform = true;
        [SerializeField] private float platformRepulseDistance = 2.25f;
        [SerializeField] private float platformRepulseDuration = 0.18f;
        [SerializeField] private float platformRepulseControlLock = 0.25f;

        [Header("Reserve Ice Bullet Reaction")]
        [Tooltip("When true, a stored reserve stone reacts to Warrior's IceBulletProjectile and asks its StoneReserve to explode all active stones in that reserve.")]
        [SerializeField] private bool reserveCanExplodeFromWarriorIceBullet = true;

        [Tooltip("When true, the ice bullet is destroyed after it triggers the reserve explosion.")]
        [SerializeField] private bool destroyIceBulletWhenReserveExplodes = true;

        private Rigidbody2D rb;
        private Collider2D[] stoneColliders;
        private bool[] originalColliderEnabledStates;
        private bool[] originalColliderTriggerStates;

        private bool originalRigidbodyStateCached;
        private bool originalRigidbodySimulated;
        private RigidbodyType2D originalRigidbodyBodyType;
        private RigidbodyConstraints2D originalRigidbodyConstraints;

        private bool launched;
        private bool impactResolved;
        private bool carriedByMorvex;
        private bool storedInReserve;
        private StoneReserve owningReserve;
        private int shieldLaserLayer = -1;

        public bool IsStoredInReserve => storedInReserve;

        private bool ignoringWarriorBecauseSprint;
        private readonly List<Collider2D> sprintIgnoredWarriorColliders = new List<Collider2D>();

        private bool ignoringPlatformsBecauseCarried;
        private readonly List<Collider2D> carriedIgnoredPlatformColliders = new List<Collider2D>();

        private void Awake()
        {
            CacheOwnPhysicsReferences();

            if (rb != null)
                rb.gravityScale = 0f;

            shieldLaserLayer = LayerMask.NameToLayer("Shield Laser");
            if (shieldLaserBlocksStone && shieldLaserLayer < 0)
            {
                Debug.LogWarning(
                    "[FallingStone] Unity layer 'Shield Laser' does not exist. " +
                    "Create it in Project Settings > Tags and Layers, then assign Warrior shieldHitbox to that layer.",
                    this);
            }
        }

        /// <summary>
        /// Called by StoneReserve for stones that are physically placed in the scene as reserve stock.
        /// A stored reserve stone is visible/countable only: no gravity, no collider contact,
        /// no platform hit, no impact VFX, and no Destroy().
        /// </summary>
        public void SetReserveStorageMode(bool stored)
        {
            SetReserveStorageMode(stored, stored ? owningReserve : null);
        }

        public void SetReserveStorageMode(bool stored, StoneReserve reserveOwner)
        {
            CacheOwnPhysicsReferences();

            storedInReserve = stored;
            owningReserve = stored ? reserveOwner : null;

            if (stored)
            {
                carriedByMorvex = false;
                launched = false;
                impactResolved = false;

                ClearSprintWarriorCollisionIgnore();
                ClearPlatformCollisionIgnoredWhileCarried();
                ConfigureReserveSensorPhysics();
                return;
            }

            RestorePhysicsBeforeLaunchOrReuse();
        }

        /// <summary>
        /// Call this ONLY for the stone object that Morvex visually holds.
        /// This makes the carried object completely safe: no platform hit, no shield hit,
        /// no Warrior hit, no impact VFX, and no Destroy() while it is attached to Morvex.
        /// </summary>
        public void BeginMorvexCarry()
        {
            CacheOwnPhysicsReferences();

            storedInReserve = false;
            owningReserve = null;
            carriedByMorvex = true;
            launched = false;
            impactResolved = false;

            ClearSprintWarriorCollisionIgnore();

            if (rb != null)
            {
                rb.gravityScale = 0f;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;

                if (disableRigidbodySimulationWhileCarried)
                    rb.simulated = false;
            }

            if (disableCollidersWhileCarried)
                SetStoneCollidersEnabled(false);
            else if (ignorePlatformsWhileCarried)
                SetPlatformCollisionIgnoredWhileCarried(true);
        }

        /// <summary>
        /// Use this if the same object must be released instead of only hidden.
        /// Morvex currently instantiates a fresh fallingStonePrefab on release, so the held visual usually stays hidden.
        /// </summary>
        public void EndMorvexCarryWithoutLaunch()
        {
            carriedByMorvex = false;
            owningReserve = null;
            RestorePhysicsBeforeLaunchOrReuse();
            ClearPlatformCollisionIgnoredWhileCarried();
        }

        public void Launch()
        {
            CacheOwnPhysicsReferences();

            storedInReserve = false;
            owningReserve = null;
            carriedByMorvex = false;
            launched = true;
            impactResolved = false;

            RestorePhysicsBeforeLaunchOrReuse();
            ClearPlatformCollisionIgnoredWhileCarried();

            if (rb != null)
            {
                rb.simulated = true;
                rb.gravityScale = gravityScale;
            }

            RefreshSprintCollisionIgnore();

            Destroy(gameObject, lifeTime);
        }

        private void Update()
        {
            if (storedInReserve || carriedByMorvex || !launched)
                return;

            transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (storedInReserve)
            {
                if (rb != null)
                {
                    rb.simulated = true;
                    rb.bodyType = RigidbodyType2D.Kinematic;
                    rb.constraints = RigidbodyConstraints2D.FreezeAll;
                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                    rb.gravityScale = 0f;
                }

                return;
            }

            if (carriedByMorvex)
            {
                if (rb != null)
                {
                    rb.gravityScale = 0f;
                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                }

                if (!disableCollidersWhileCarried && ignorePlatformsWhileCarried)
                    SetPlatformCollisionIgnoredWhileCarried(true);

                return;
            }

            if (!launched || impactResolved)
                return;

            RefreshSprintCollisionIgnore();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (ShouldIgnoreImpactCallbacks())
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
            if (storedInReserve)
            {
                TryTriggerReserveIceBulletExplosion(other);
                return;
            }

            if (ShouldIgnoreImpactCallbacks())
                return;

            // Sprint relic means the stone passes through Warrior trigger colliders too.
            if (TryIgnoreWarriorCollisionBecauseSprint(other))
                return;

            // Supports shieldHitbox configured as Is Trigger.
            TryBlockWithShieldLaserCollider(other);
        }

        private bool ShouldIgnoreImpactCallbacks()
        {
            if (impactResolved)
                return true;

            if (storedInReserve)
                return true;

            if (carriedByMorvex && ignoreAllImpactsWhileCarried)
                return true;

            return !launched;
        }

        private void CacheOwnPhysicsReferences()
        {
            if (rb == null)
                rb = GetComponent<Rigidbody2D>();

            if (rb != null && !originalRigidbodyStateCached)
            {
                originalRigidbodySimulated = rb.simulated;
                originalRigidbodyBodyType = rb.bodyType;
                originalRigidbodyConstraints = rb.constraints;
                originalRigidbodyStateCached = true;
            }

            if (stoneColliders == null || stoneColliders.Length == 0)
            {
                stoneColliders = GetComponentsInChildren<Collider2D>(true);
                originalColliderEnabledStates = new bool[stoneColliders.Length];
                originalColliderTriggerStates = new bool[stoneColliders.Length];

                for (int i = 0; i < stoneColliders.Length; i++)
                {
                    originalColliderEnabledStates[i] = stoneColliders[i] != null && stoneColliders[i].enabled;
                    originalColliderTriggerStates[i] = stoneColliders[i] != null && stoneColliders[i].isTrigger;
                }
            }
        }

        private void ConfigureReserveSensorPhysics()
        {
            CacheOwnPhysicsReferences();

            if (rb != null)
            {
                rb.simulated = true;
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.constraints = RigidbodyConstraints2D.FreezeAll;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.gravityScale = 0f;
            }

            if (stoneColliders == null)
                return;

            for (int i = 0; i < stoneColliders.Length; i++)
            {
                Collider2D stoneCollider = stoneColliders[i];
                if (stoneCollider == null)
                    continue;

                bool shouldBeEnabled = true;
                if (originalColliderEnabledStates != null && i < originalColliderEnabledStates.Length)
                    shouldBeEnabled = originalColliderEnabledStates[i];

                stoneCollider.enabled = shouldBeEnabled;
                if (shouldBeEnabled)
                    stoneCollider.isTrigger = true;
            }
        }

        private void SetStoneCollidersEnabled(bool enabled)
        {
            if (stoneColliders == null)
                return;

            for (int i = 0; i < stoneColliders.Length; i++)
            {
                Collider2D stoneCollider = stoneColliders[i];
                if (stoneCollider == null)
                    continue;

                stoneCollider.enabled = enabled;
            }
        }

        private void RestorePhysicsBeforeLaunchOrReuse()
        {
            CacheOwnPhysicsReferences();

            if (rb != null)
            {
                rb.simulated = originalRigidbodyStateCached ? originalRigidbodySimulated : true;
                if (originalRigidbodyStateCached)
                {
                    rb.bodyType = originalRigidbodyBodyType;
                    rb.constraints = originalRigidbodyConstraints;
                }
            }

            if (stoneColliders == null)
                return;

            for (int i = 0; i < stoneColliders.Length; i++)
            {
                Collider2D stoneCollider = stoneColliders[i];
                if (stoneCollider == null)
                    continue;

                bool shouldBeEnabled = true;

                if (originalColliderEnabledStates != null && i < originalColliderEnabledStates.Length)
                    shouldBeEnabled = originalColliderEnabledStates[i];

                stoneCollider.enabled = shouldBeEnabled;

                if (originalColliderTriggerStates != null && i < originalColliderTriggerStates.Length)
                    stoneCollider.isTrigger = originalColliderTriggerStates[i];
            }
        }

        private void SetPlatformCollisionIgnoredWhileCarried(bool ignore)
        {
            if (!ignore)
            {
                ClearPlatformCollisionIgnoredWhileCarried();
                return;
            }

            CacheOwnPhysicsReferences();

            if (stoneColliders == null || stoneColliders.Length == 0)
                return;

            PlatFormColliderTrigger[] platforms = FindObjectsByType<PlatFormColliderTrigger>(FindObjectsSortMode.None);

            for (int i = 0; i < platforms.Length; i++)
            {
                PlatFormColliderTrigger platform = platforms[i];
                if (platform == null)
                    continue;

                RegisterPlatformColliderIgnoredWhileCarried(platform.platformCollider);

                if (includePlatformTriggersWhileCarried)
                    RegisterPlatformColliderIgnoredWhileCarried(platform.platformTrigger);
            }

            ignoringPlatformsBecauseCarried = carriedIgnoredPlatformColliders.Count > 0;
        }

        private void RegisterPlatformColliderIgnoredWhileCarried(Collider2D platformCollider)
        {
            if (platformCollider == null || stoneColliders == null)
                return;

            for (int i = 0; i < stoneColliders.Length; i++)
            {
                Collider2D stoneCollider = stoneColliders[i];
                if (stoneCollider == null)
                    continue;

                Physics2D.IgnoreCollision(stoneCollider, platformCollider, true);
            }

            if (!carriedIgnoredPlatformColliders.Contains(platformCollider))
                carriedIgnoredPlatformColliders.Add(platformCollider);
        }

        private void ClearPlatformCollisionIgnoredWhileCarried()
        {
            if (!ignoringPlatformsBecauseCarried && carriedIgnoredPlatformColliders.Count == 0)
                return;

            if (stoneColliders != null)
            {
                for (int s = 0; s < stoneColliders.Length; s++)
                {
                    Collider2D stoneCollider = stoneColliders[s];
                    if (stoneCollider == null)
                        continue;

                    for (int p = 0; p < carriedIgnoredPlatformColliders.Count; p++)
                    {
                        Collider2D platformCollider = carriedIgnoredPlatformColliders[p];
                        if (platformCollider == null)
                            continue;

                        Physics2D.IgnoreCollision(stoneCollider, platformCollider, false);
                    }
                }
            }

            carriedIgnoredPlatformColliders.Clear();
            ignoringPlatformsBecauseCarried = false;
        }

        private bool TryTriggerReserveIceBulletExplosion(Collider2D other)
        {
            if (!reserveCanExplodeFromWarriorIceBullet || other == null)
                return false;

            IceBulletProjectile iceBullet = other.GetComponent<IceBulletProjectile>() ?? other.GetComponentInParent<IceBulletProjectile>();
            if (iceBullet == null)
                return false;

            StoneReserve reserve = owningReserve != null ? owningReserve : GetComponentInParent<StoneReserve>();
            if (reserve == null)
                return false;

            reserve.ExplodeAllStonesFromIceBullet(transform.position);

            if (destroyIceBulletWhenReserveExplodes)
                Destroy(iceBullet.gameObject);

            return true;
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

            CacheOwnPhysicsReferences();

            if (stoneColliders == null)
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

            ignoringWarriorBecauseSprint = ignore;
        }

        private bool IsStillOverlappingIgnoredWarrior()
        {
            if (stoneColliders == null)
                return false;

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
            if (stoneColliders == null)
                return;

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

            // Absolute safety: reserve stock and held carry visuals are never allowed to destroy themselves.
            if (storedInReserve)
                return;

            if (carriedByMorvex && ignoreAllImpactsWhileCarried)
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
            ClearPlatformCollisionIgnoredWhileCarried();
        }

        private void OnDestroy()
        {
            ClearSprintWarriorCollisionIgnore();
            ClearPlatformCollisionIgnoredWhileCarried();
        }
    }
}

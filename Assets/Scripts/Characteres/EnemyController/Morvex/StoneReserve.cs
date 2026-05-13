using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    public class StoneReserve : MonoBehaviour
    {
        [Header("Physical FallingStone Stock")]
        [Tooltip("When true, the reserve stock is controlled by real FallingStone objects placed under this reserve in the scene.")]
        [SerializeField] private bool physicalStonesControlAvailability = true;

        [Tooltip("Optional parent that contains the reserve stones. If empty, this StoneReserve transform is used.")]
        [SerializeField] private Transform stoneContainer;

        [Tooltip("Optional explicit list. You can drag FallingStone scene objects here, or simply put FallingStone children under Stone Container / this reserve.")]
        [SerializeField] private List<FallingStone> physicalStones = new List<FallingStone>();

        [Tooltip("When true, child FallingStone objects are automatically detected. This lets the amount increase when you drag a FallingStone prefab instance under the reserve.")]
        [SerializeField] private bool autoDiscoverChildFallingStones = true;

        [Tooltip("When true, inactive child stones remain registered but do not count as available stock until re-enabled.")]
        [SerializeField] private bool includeInactiveChildrenInDiscovery = true;

        [Tooltip("When true, the FallingStone scene objects stored in the reserve have their physics disabled while they are reserve visuals. Keep this ON when the reserve is placed on a platform.")]
        [SerializeField] private bool makeReserveStonesVisualOnly = true;

        [Tooltip("When Morvex takes a stone, the selected FallingStone scene object is disabled. Disabled stones do not count as available stock.")]
        [SerializeField] private bool disableStoneObjectWhenTaken = true;

        [Header("Reserve Explosion")]
        [Tooltip("Explosion VFX spawned on every active stored stone when Warrior's ice bullet hits any stone in this reserve. Assign vfx_Explosion_01 here.")]
        [SerializeField] private GameObject reserveExplosionVfxPrefab;

        [Tooltip("How long each per-stone explosion VFX object lives before it is destroyed. Set to 0 to not auto-destroy it.")]
        [SerializeField] private float reserveExplosionVfxLifetime = 1.5f;

        [Tooltip("Optional small delay between each stone explosion. Use 0 for all stones exploding at exactly the same time. Example: 0.03 gives a very slight chain feel.")]
        [SerializeField] private float perStoneExplosionDelay = 0f;

        [Header("Hidden Relic Rewards")]
        [Tooltip("When true, relic pickups hidden behind this stone pile are revealed only when this reserve becomes empty.")]
        [SerializeField] private bool revealHiddenRelicsWhenEmpty = true;

        [Tooltip("Optional parent containing relic pickups hidden behind the stones. If assigned, children under this transform are auto-registered as hidden relics.")]
        [SerializeField] private Transform hiddenRelicsContainer;

        [Tooltip("Explicit hidden reward objects. Drag your RelicPickup scene objects or a parent object here.")]
        [SerializeField] private List<GameObject> hiddenRelics = new List<GameObject>();

        [Tooltip("When true and Hidden Relics Container is assigned, every child object under it is registered as a hidden reward.")]
        [SerializeField] private bool autoDiscoverHiddenRelicsInContainer = true;

        [Tooltip("When true, hidden relic renderers are disabled on start and restored when all stones are gone.")]
        [SerializeField] private bool hideRelicRenderersUntilReveal = true;

        [Tooltip("When true, hidden relic colliders are disabled until reveal, so Warrior cannot collect the relic through the stones.")]
        [SerializeField] private bool disableRelicCollidersUntilReveal = true;

        [Tooltip("If true and the reserve starts with zero stones, hidden relics are immediately revealed on Start. Keep false if an empty reserve usually means a setup mistake.")]
        [SerializeField] private bool revealHiddenRelicsOnStartIfEmpty = false;

        [Tooltip("Optional VFX spawned on every relic position when it becomes revealed.")]
        [SerializeField] private GameObject hiddenRelicRevealVfxPrefab;

        [SerializeField] private float hiddenRelicRevealVfxLifetime = 1.5f;
        [SerializeField] private Vector3 hiddenRelicRevealVfxOffset = Vector3.zero;

        [Header("Legacy Logical Stock - fallback only")]
        [Tooltip("Used only when Physical Stones Control Availability is false.")]
        [SerializeField] private int stoneCount = 0;

        [Tooltip("Used only when Physical Stones Control Availability is false.")]
        [SerializeField] private bool infiniteStones = false;

        [Header("Positions")]
        [SerializeField] private Transform grabPoint;
        [SerializeField] private Transform approachPoint;
        [SerializeField] private float defaultApproachHeight = 0.8f;

        [Header("Debug")]
        [SerializeField] private int availableStoneAmount;
        [SerializeField] private int registeredPhysicalStoneAmount;

        private readonly List<FallingStone> runtimeDiscoveredStones = new List<FallingStone>();
        private readonly List<FallingStone> reservedStones = new List<FallingStone>();
        private readonly List<FallingStone> hiddenReservedStones = new List<FallingStone>();

        private bool reserveExplosionInProgress;
        private Coroutine reserveExplosionRoutine;
        private readonly List<GameObject> runtimeDiscoveredHiddenRelics = new List<GameObject>();
        private readonly Dictionary<Renderer, bool> hiddenRelicRendererOriginalState = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Collider2D, bool> hiddenRelicColliderOriginalState = new Dictionary<Collider2D, bool>();

        private bool hiddenRelicsInitialized;
        private bool hiddenRelicsRevealed;


        /// <summary>
        /// Backward-compatible name used by existing MorvexMonster logic.
        /// </summary>
        public bool HasStones => HasStoneAvailable();

        /// <summary>
        /// Single-stone alias.
        /// </summary>
        public bool HasStone => HasStoneAvailable();

        public int StoneAmount => GetAvailableStoneAmount();
        public bool UsesPhysicalStock => physicalStonesControlAvailability;

        private Transform EffectiveStoneContainer => stoneContainer != null ? stoneContainer : transform;

        private void Awake()
        {
            RefreshStoneCache();
            RefreshHiddenRelicCache();
            InitializeHiddenRelics();
            RefreshDebugAmounts();
        }

        private void Start()
        {
            if (revealHiddenRelicsOnStartIfEmpty)
                TryRevealHiddenRelicsIfEmpty();
        }

        private void OnValidate()
        {
            stoneCount = Mathf.Max(0, stoneCount);
            defaultApproachHeight = Mathf.Max(0f, defaultApproachHeight);
            perStoneExplosionDelay = Mathf.Max(0f, perStoneExplosionDelay);
            RemoveNullEntries(physicalStones);
            RemoveNullEntries(hiddenRelics);
            hiddenRelicRevealVfxLifetime = Mathf.Max(0f, hiddenRelicRevealVfxLifetime);
            RefreshDebugAmountsEditorSafe();
        }

        private void OnTransformChildrenChanged()
        {
            RefreshStoneCache();
            RefreshHiddenRelicCache();
            if (!hiddenRelicsRevealed)
                ApplyHiddenRelicVisibility(false);
            RefreshDebugAmounts();
        }

        public Vector3 GetGrabWorldPosition()
        {
            if (grabPoint != null)
                return grabPoint.position;

            FallingStone firstAvailableStone = FindFirstAvailablePhysicalStone();
            if (firstAvailableStone != null)
                return firstAvailableStone.transform.position;

            return transform.position;
        }

        public Vector3 GetApproachWorldPosition()
        {
            if (approachPoint != null)
                return approachPoint.position;

            return GetGrabWorldPosition() + Vector3.up * defaultApproachHeight;
        }

        public bool HasStoneAvailable()
        {
            return GetAvailableStoneAmount() > 0;
        }

        public int GetAvailableStoneAmount()
        {
            if (reserveExplosionInProgress)
                return 0;

            if (!physicalStonesControlAvailability)
                return infiniteStones ? int.MaxValue : Mathf.Max(0, stoneCount);

            RefreshStoneCache();

            int count = 0;
            ForEachRegisteredPhysicalStone(stone =>
            {
                if (IsAvailablePhysicalStone(stone))
                    count++;
            });

            availableStoneAmount = count;
            registeredPhysicalStoneAmount = CountRegisteredPhysicalStones();
            return count;
        }

        /// <summary>
        /// Optional helper for multi-Morvex setups. It makes one stock stone unavailable
        /// to other Morvex monsters without decreasing the visible amount yet.
        /// </summary>
        public bool TryReserveStone(bool hideVisualImmediately = false)
        {
            if (!physicalStonesControlAvailability)
                return TryReserveLogicalStone();

            FallingStone stone = FindFirstAvailablePhysicalStone();
            if (stone == null)
                return false;

            reservedStones.Add(stone);

            if (hideVisualImmediately)
            {
                stone.gameObject.SetActive(false);
                if (!hiddenReservedStones.Contains(stone))
                    hiddenReservedStones.Add(stone);
            }

            RefreshDebugAmounts();
            return true;
        }

        public void CancelReservation(bool revealStoneVisual = false)
        {
            if (!physicalStonesControlAvailability)
                return;

            if (revealStoneVisual)
            {
                for (int i = 0; i < reservedStones.Count; i++)
                {
                    FallingStone stone = reservedStones[i];
                    if (stone != null && hiddenReservedStones.Contains(stone))
                        stone.gameObject.SetActive(true);
                }
            }

            reservedStones.Clear();
            hiddenReservedStones.Clear();
            RefreshDebugAmounts();
        }

        /// <summary>
        /// Main API used by Morvex. For physical reserves, this disables exactly one active FallingStone object.
        /// </summary>
        public bool TryTakeStone()
        {
            if (!physicalStonesControlAvailability)
                return TryTakeLogicalStone();

            FallingStone stone = FindFirstAvailablePhysicalStone();
            if (stone == null)
                return false;

            HideTakenPhysicalStone(stone);
            RefreshDebugAmounts();
            TryRevealHiddenRelicsIfEmpty();
            return true;
        }

        /// <summary>
        /// Use after TryReserveStone. If no reservation exists, this safely falls back to TryTakeStone().
        /// </summary>
        public bool TryTakeReservedStone()
        {
            if (!physicalStonesControlAvailability)
                return TryTakeLogicalStone();

            RefreshStoneCache();
            CleanupReservedStones();

            if (reservedStones.Count == 0)
                return TryTakeStone();

            FallingStone stone = reservedStones[0];
            reservedStones.RemoveAt(0);

            if (!IsRegisteredPhysicalStone(stone))
            {
                hiddenReservedStones.Remove(stone);
                RefreshDebugAmounts();
                return false;
            }

            if (!IsPhysicallyPresentAndActive(stone))
            {
                bool wasHiddenByReservation = hiddenReservedStones.Remove(stone);
                RefreshDebugAmounts();
                if (wasHiddenByReservation)
                    TryRevealHiddenRelicsIfEmpty();
                return wasHiddenByReservation;
            }

            hiddenReservedStones.Remove(stone);
            HideTakenPhysicalStone(stone);
            RefreshDebugAmounts();
            TryRevealHiddenRelicsIfEmpty();
            return true;
        }

        /// <summary>
        /// No auto-refill is used. Refill means manually re-enable/add a FallingStone scene object,
        /// or call this method to re-enable one disabled physical stone.
        /// </summary>
        public void RefillStone()
        {
            if (!physicalStonesControlAvailability)
            {
                if (!infiniteStones)
                    stoneCount = Mathf.Max(0, stoneCount) + 1;

                RefreshDebugAmounts();
                TryRevealHiddenRelicsIfEmpty();
                return;
            }

            RefreshStoneCache();

            FallingStone disabledStone = FindFirstRegisteredDisabledPhysicalStone();
            if (disabledStone != null)
            {
                disabledStone.gameObject.SetActive(true);
                PrepareReserveStoneForStorage(disabledStone);
            }

            RefreshDebugAmounts();
        }

        /// <summary>
        /// Kept for compatibility with older calls. This version intentionally does not auto-refill.
        /// </summary>
        public void BeginRefillDelay()
        {
            // Intentionally empty: physical reserves refill only when you add/re-enable a FallingStone.
        }

        /// <summary>
        /// Kept for compatibility with older calls. This version intentionally does not auto-refill.
        /// </summary>
        public void BeginRefillDelay(float delay)
        {
            // Intentionally empty: physical reserves refill only when you add/re-enable a FallingStone.
        }


        /// <summary>
        /// Called by a stored FallingStone when Warrior's IceBulletProjectile touches it.
        /// This consumes every active physical stone in this reserve and gives every stone
        /// its own vfx_Explosion_01 instance. The explosions can be simultaneous or slightly delayed.
        /// </summary>
        public void ExplodeAllStonesFromIceBullet(Vector3 hitPosition)
        {
            ExplodeAllAvailablePhysicalStones(hitPosition);
        }

        /// <summary>
        /// Public helper if another system must destroy the reserve stock.
        /// Each active stored FallingStone spawns its own explosion VFX at its own position.
        /// </summary>
        public void ExplodeAllAvailablePhysicalStones(Vector3 hitPosition)
        {
            if (!physicalStonesControlAvailability)
            {
                if (!infiniteStones)
                    stoneCount = 0;

                RefreshDebugAmounts();
                TryRevealHiddenRelicsIfEmpty();
                return;
            }

            RefreshStoneCache();

            List<FallingStone> stonesToExplode = new List<FallingStone>();
            ForEachRegisteredPhysicalStone(stone =>
            {
                if (stone == null)
                    return;

                if (!stone.gameObject.activeInHierarchy)
                    return;

                if (!stonesToExplode.Contains(stone))
                    stonesToExplode.Add(stone);
            });

            if (stonesToExplode.Count == 0)
                return;

            reservedStones.Clear();
            hiddenReservedStones.Clear();

            // Make the reserve unavailable immediately, even when a slight visual delay is used.
            reserveExplosionInProgress = true;
            availableStoneAmount = 0;

            if (reserveExplosionRoutine != null)
                StopCoroutine(reserveExplosionRoutine);

            reserveExplosionRoutine = StartCoroutine(ExplodePhysicalStonesRoutine(stonesToExplode));
        }

        private IEnumerator ExplodePhysicalStonesRoutine(List<FallingStone> stonesToExplode)
        {
            float delay = Mathf.Max(0f, perStoneExplosionDelay);

            for (int i = 0; i < stonesToExplode.Count; i++)
            {
                FallingStone stone = stonesToExplode[i];
                if (stone != null && stone.gameObject.activeInHierarchy)
                {
                    SpawnReserveExplosionVfx(stone.transform.position);
                    stone.gameObject.SetActive(false);
                }

                if (delay > 0f && i < stonesToExplode.Count - 1)
                    yield return new WaitForSeconds(delay);
            }

            reserveExplosionInProgress = false;
            reserveExplosionRoutine = null;
            RefreshDebugAmounts();
            TryRevealHiddenRelicsIfEmpty();
        }

        private void SpawnReserveExplosionVfx(Vector3 position)
        {
            if (reserveExplosionVfxPrefab == null)
                return;

            GameObject fx = Instantiate(reserveExplosionVfxPrefab, position, Quaternion.identity);

            ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
                systems[i].Play(true);

            if (reserveExplosionVfxLifetime > 0f)
                Destroy(fx, reserveExplosionVfxLifetime);
        }

        private void RefreshHiddenRelicCache()
        {
            RemoveNullEntries(hiddenRelics);
            runtimeDiscoveredHiddenRelics.Clear();

            if (!autoDiscoverHiddenRelicsInContainer || hiddenRelicsContainer == null)
                return;

            for (int i = 0; i < hiddenRelicsContainer.childCount; i++)
            {
                Transform child = hiddenRelicsContainer.GetChild(i);
                if (child == null)
                    continue;

                GameObject rewardObject = child.gameObject;
                if (rewardObject == null || runtimeDiscoveredHiddenRelics.Contains(rewardObject))
                    continue;

                runtimeDiscoveredHiddenRelics.Add(rewardObject);
            }
        }

        private void InitializeHiddenRelics()
        {
            if (hiddenRelicsInitialized)
                return;

            hiddenRelicsInitialized = true;
            RefreshHiddenRelicCache();
            CacheHiddenRelicOriginalStates();

            if (revealHiddenRelicsWhenEmpty)
                ApplyHiddenRelicVisibility(false);
        }

        private void CacheHiddenRelicOriginalStates()
        {
            ForEachHiddenRelicObject(rewardObject =>
            {
                if (rewardObject == null)
                    return;

                Renderer[] renderers = rewardObject.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer != null && !hiddenRelicRendererOriginalState.ContainsKey(renderer))
                        hiddenRelicRendererOriginalState.Add(renderer, renderer.enabled);
                }

                Collider2D[] colliders = rewardObject.GetComponentsInChildren<Collider2D>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider2D col = colliders[i];
                    if (col != null && !hiddenRelicColliderOriginalState.ContainsKey(col))
                        hiddenRelicColliderOriginalState.Add(col, col.enabled);
                }
            });
        }

        private void TryRevealHiddenRelicsIfEmpty()
        {
            if (!revealHiddenRelicsWhenEmpty || hiddenRelicsRevealed)
                return;

            if (!hiddenRelicsInitialized)
                InitializeHiddenRelics();

            if (GetAvailableStoneAmount() > 0)
                return;

            RevealHiddenRelics();
        }

        private void RevealHiddenRelics()
        {
            if (hiddenRelicsRevealed)
                return;

            hiddenRelicsRevealed = true;
            ApplyHiddenRelicVisibility(true);

            ForEachHiddenRelicObject(rewardObject =>
            {
                if (rewardObject == null || hiddenRelicRevealVfxPrefab == null)
                    return;

                GameObject fx = Instantiate(
                    hiddenRelicRevealVfxPrefab,
                    rewardObject.transform.position + hiddenRelicRevealVfxOffset,
                    Quaternion.identity
                );

                ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                    systems[i].Play(true);

                if (hiddenRelicRevealVfxLifetime > 0f)
                    Destroy(fx, hiddenRelicRevealVfxLifetime);
            });
        }

        private void ApplyHiddenRelicVisibility(bool visible)
        {
            ForEachHiddenRelicObject(rewardObject =>
            {
                if (rewardObject == null)
                    return;

                // Keep the GameObject active so RelicPickup can initialize normally.
                // We hide only renderers and pickup colliders.
                if (!rewardObject.activeSelf)
                    rewardObject.SetActive(true);

                if (hideRelicRenderersUntilReveal)
                {
                    Renderer[] renderers = rewardObject.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Renderer renderer = renderers[i];
                        if (renderer == null)
                            continue;

                        if (visible)
                        {
                            bool originalEnabled = true;
                            hiddenRelicRendererOriginalState.TryGetValue(renderer, out originalEnabled);
                            renderer.enabled = originalEnabled;
                        }
                        else
                        {
                            renderer.enabled = false;
                        }
                    }
                }

                if (disableRelicCollidersUntilReveal)
                {
                    Collider2D[] colliders = rewardObject.GetComponentsInChildren<Collider2D>(true);
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        Collider2D col = colliders[i];
                        if (col == null)
                            continue;

                        if (visible)
                        {
                            bool originalEnabled = true;
                            hiddenRelicColliderOriginalState.TryGetValue(col, out originalEnabled);
                            col.enabled = originalEnabled;
                        }
                        else
                        {
                            col.enabled = false;
                        }
                    }
                }
            });
        }

        private void ForEachHiddenRelicObject(System.Action<GameObject> action)
        {
            if (action == null)
                return;

            for (int i = 0; i < hiddenRelics.Count; i++)
            {
                GameObject rewardObject = hiddenRelics[i];
                if (rewardObject != null)
                    action(rewardObject);
            }

            for (int i = 0; i < runtimeDiscoveredHiddenRelics.Count; i++)
            {
                GameObject rewardObject = runtimeDiscoveredHiddenRelics[i];
                if (rewardObject == null || hiddenRelics.Contains(rewardObject))
                    continue;

                action(rewardObject);
            }
        }

        private bool TryReserveLogicalStone()
        {
            // Legacy logical mode has no per-stone reservation object.
            return HasStoneAvailable();
        }

        private bool TryTakeLogicalStone()
        {
            if (!HasStoneAvailable())
                return false;

            if (!infiniteStones)
                stoneCount = Mathf.Max(0, stoneCount - 1);

            RefreshDebugAmounts();
            TryRevealHiddenRelicsIfEmpty();
            return true;
        }

        private void HideTakenPhysicalStone(FallingStone stone)
        {
            if (stone == null)
                return;

            reservedStones.Remove(stone);
            hiddenReservedStones.Remove(stone);

            if (disableStoneObjectWhenTaken)
                stone.gameObject.SetActive(false);
            else
                PrepareReserveStoneForStorage(stone);
        }

        private FallingStone FindFirstAvailablePhysicalStone()
        {
            RefreshStoneCache();
            CleanupReservedStones();

            FallingStone result = null;
            ForEachRegisteredPhysicalStone(stone =>
            {
                if (result != null)
                    return;

                if (IsAvailablePhysicalStone(stone))
                    result = stone;
            });

            return result;
        }

        private FallingStone FindFirstRegisteredDisabledPhysicalStone()
        {
            FallingStone result = null;
            ForEachRegisteredPhysicalStone(stone =>
            {
                if (result != null || stone == null)
                    return;

                if (!stone.gameObject.activeSelf)
                    result = stone;
            });

            return result;
        }

        private bool IsAvailablePhysicalStone(FallingStone stone)
        {
            return IsRegisteredPhysicalStone(stone) &&
                   IsPhysicallyPresentAndActive(stone) &&
                   !reservedStones.Contains(stone);
        }

        private bool IsPhysicallyPresentAndActive(FallingStone stone)
        {
            return stone != null &&
                   stone.gameObject != null &&
                   stone.gameObject.activeInHierarchy;
        }

        private bool IsRegisteredPhysicalStone(FallingStone stone)
        {
            if (stone == null)
                return false;

            bool inExplicitList = physicalStones.Contains(stone);
            bool inRuntimeList = runtimeDiscoveredStones.Contains(stone);

            return inExplicitList || inRuntimeList;
        }

        private void RefreshStoneCache()
        {
            if (!physicalStonesControlAvailability)
                return;

            RemoveNullEntries(physicalStones);
            RemoveNullEntries(runtimeDiscoveredStones);
            CleanupReservedStones();

            if (!autoDiscoverChildFallingStones)
            {
                PrepareRegisteredStonesForStorage();
                return;
            }

            runtimeDiscoveredStones.Clear();

            Transform container = EffectiveStoneContainer;
            if (container == null)
                return;

            FallingStone[] found = container.GetComponentsInChildren<FallingStone>(includeInactiveChildrenInDiscovery);
            for (int i = 0; i < found.Length; i++)
            {
                FallingStone stone = found[i];
                if (stone == null)
                    continue;

                // Avoid accidentally counting a FallingStone that is attached to another StoneReserve below this one.
                StoneReserve owner = stone.GetComponentInParent<StoneReserve>();
                if (owner != null && owner != this)
                    continue;

                if (!runtimeDiscoveredStones.Contains(stone))
                    runtimeDiscoveredStones.Add(stone);
            }

            PrepareRegisteredStonesForStorage();
        }

        private void PrepareRegisteredStonesForStorage()
        {
            if (!makeReserveStonesVisualOnly)
                return;

            ForEachRegisteredPhysicalStone(PrepareReserveStoneForStorage);
        }

        private void PrepareReserveStoneForStorage(FallingStone stone)
        {
            if (stone == null)
                return;

            // This is the key platform-safe behavior. A reserve stock stone may sit visually
            // on top of a platform, but it must not physically touch the platform.
            // Only the launched stone spawned by Morvex is allowed to collide/explode.
            stone.SetReserveStorageMode(true, this);
        }

        private void ForEachRegisteredPhysicalStone(System.Action<FallingStone> action)
        {
            if (action == null)
                return;

            for (int i = 0; i < physicalStones.Count; i++)
            {
                FallingStone stone = physicalStones[i];
                if (stone != null)
                    action(stone);
            }

            for (int i = 0; i < runtimeDiscoveredStones.Count; i++)
            {
                FallingStone stone = runtimeDiscoveredStones[i];
                if (stone == null || physicalStones.Contains(stone))
                    continue;

                action(stone);
            }
        }

        private int CountRegisteredPhysicalStones()
        {
            int count = 0;
            ForEachRegisteredPhysicalStone(stone =>
            {
                if (stone != null)
                    count++;
            });
            return count;
        }

        private void CleanupReservedStones()
        {
            for (int i = reservedStones.Count - 1; i >= 0; i--)
            {
                FallingStone stone = reservedStones[i];
                if (stone == null || !IsRegisteredPhysicalStone(stone))
                {
                    hiddenReservedStones.Remove(stone);
                    reservedStones.RemoveAt(i);
                }
            }
        }

        private void RemoveNullEntries(List<GameObject> list)
        {
            if (list == null)
                return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null)
                    list.RemoveAt(i);
            }
        }

        private void RemoveNullEntries(List<FallingStone> list)
        {
            if (list == null)
                return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null)
                    list.RemoveAt(i);
            }
        }

        private void RefreshDebugAmounts()
        {
            if (reserveExplosionInProgress)
            {
                availableStoneAmount = 0;
                registeredPhysicalStoneAmount = CountRegisteredPhysicalStones();
                return;
            }

            if (!physicalStonesControlAvailability)
            {
                availableStoneAmount = infiniteStones ? int.MaxValue : Mathf.Max(0, stoneCount);
                registeredPhysicalStoneAmount = 0;
                return;
            }

            int available = 0;
            int registered = 0;

            ForEachRegisteredPhysicalStone(stone =>
            {
                if (stone == null)
                    return;

                registered++;
                if (IsAvailablePhysicalStone(stone))
                    available++;
            });

            availableStoneAmount = available;
            registeredPhysicalStoneAmount = registered;
        }

        private void RefreshDebugAmountsEditorSafe()
        {
            registeredPhysicalStoneAmount = physicalStones != null ? physicalStones.Count : 0;
            availableStoneAmount = stoneCount;
        }
    }

}
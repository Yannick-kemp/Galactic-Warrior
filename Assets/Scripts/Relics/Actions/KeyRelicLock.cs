using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Definitions;
using System;
using UnityEngine;
using UnityEngine.Events;

namespace Assets.Scripts.Relics.World
{
    [DisallowMultipleComponent]
    public class KeyRelicLock : MonoBehaviour
    {
        public enum KeyLockMode
        {
            ArmKeyToEnablePlatformMotion,
            ImmediateUnlockObject
           
        }

        public static KeyRelicLock ActivePlatformMotionLock { get; private set; }
        public static event Action<KeyRelicLock> OnActivePlatformMotionLockChanged;

        private const bool ConsumeKeyOnUnlock = true;
        private const bool UnlockOnlyOnce = true;
        private const string DefaultUnlockTriggerName = "Open";
        private const string DefaultUnlockedBoolName = "Unlocked";

        [Header("Mode")]
        [SerializeField] private KeyLockMode mode = KeyLockMode.ImmediateUnlockObject;

        [Header("Key Requirement")]
        [Tooltip("Optional. If empty, the default Key Relic id is used.")]
        [SerializeField] private KeyRelic keyRelic;

        [SerializeField, Min(1)] private int requiredKeys = 1;

        [Header("Platform Motion Target")]
        [Tooltip("Assign the MovingHorizontalPlatform, MovingVerticalPlatform, or RotatingPlatform. If empty, the script searches parent/children automatically.")]
        [SerializeField] private PlatFormColliderTrigger platformMotionTarget;

        [Header("Unlock Result")]
        [Tooltip("Optional object to disable after a normal object unlock. In platform-motion mode, the target platform itself is never disabled.")]
        [SerializeField] private GameObject objectToDisableOnUnlock;

        [Tooltip("Optional object to enable after unlock.")]
        [SerializeField] private GameObject objectToEnableOnUnlock;

        [Header("Animator")]
        [Tooltip("Optional. The script safely uses trigger 'Open' and bool 'Unlocked' only if those parameters exist.")]
        [SerializeField] private Animator animator;

        [Header("Events")]
        public UnityEvent onUnlocked;
        public UnityEvent onMissingKey;
        public UnityEvent onPlatformMotionPromptAvailable;
        public UnityEvent onPlatformMotionPromptClosed;

        /*
         * Hidden legacy fields:
         * Kept only so old prefabs do not lose data unexpectedly.
         * They are no longer shown in the Inspector and no longer control platform colliders.
         */
        [HideInInspector, SerializeField] private string keyRelicIdOverride = KeyRelic.DefaultRelicId;
        [HideInInspector, SerializeField] private Collider2D[] extraCollidersToDisable;

        private bool _unlocked;
        private Warrior _currentWarrior;

        public bool IsPlatformMotionMode => mode == KeyLockMode.ArmKeyToEnablePlatformMotion;
        public Warrior CurrentWarrior => _currentWarrior;

        private void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);

            EnsurePlatformMotionTarget();
        }

        private void OnDisable()
        {
            ClearActivePromptIfThisLockOwnsIt();
            _currentWarrior = null;
        }

        private void OnDestroy()
        {
            ClearActivePromptIfThisLockOwnsIt();
            _currentWarrior = null;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            HandleEnterCollider(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // Important when the Warrior enters without a key, then picks one up while still inside the trigger.
            if (mode == KeyLockMode.ArmKeyToEnablePlatformMotion && ActivePlatformMotionLock != this)
                HandleEnterCollider(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            HandleExitCollider(other);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision.collider != null)
                HandleEnterCollider(collision.collider);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (collision.collider != null)
                HandleExitCollider(collision.collider);
        }

        public bool TryUnlock(Warrior warrior)
        {
            if (warrior == null)
                return false;

            if (_unlocked && UnlockOnlyOnce)
                return true;

            RelicManager relicManager = warrior.GetComponent<RelicManager>();
            if (relicManager == null)
            {
                Debug.LogWarning("[KeyRelicLock] Warrior has no RelicManager.", this);
                return false;
            }

            if (GetOwnedKeyCount(relicManager) < requiredKeys)
            {
                onMissingKey?.Invoke();
                return false;
            }

            if (ConsumeKeyOnUnlock && !ConsumeKeys(relicManager))
            {
                onMissingKey?.Invoke();
                return false;
            }

            CompleteUnlock();
            return true;
        }

        public bool HasPlatformMotionActivationContext(Warrior warrior)
        {
            if (mode != KeyLockMode.ArmKeyToEnablePlatformMotion)
                return false;

            if (warrior == null)
                warrior = _currentWarrior;

            if (warrior == null || _currentWarrior != warrior)
                return false;

            if (_unlocked && UnlockOnlyOnce)
                return false;

            EnsurePlatformMotionTarget();

            if (platformMotionTarget == null)
                return false;

            // Platform-motion relic is useful only while the target is still locked/paused.
            return !platformMotionTarget.PlatformMotionEnabled;
        }

        public bool CanActivateFromUI(Warrior warrior)
        {
            if (!HasPlatformMotionActivationContext(warrior))
                return false;

            RelicManager relicManager = warrior.GetComponent<RelicManager>();
            if (relicManager == null)
                return false;

            return GetOwnedKeyCount(relicManager) >= requiredKeys;
        }

        public bool TryActivateFromUI(Warrior warrior)
        {
            if (!CanActivateFromUI(warrior))
            {
                onMissingKey?.Invoke();
                return false;
            }

            RelicManager relicManager = warrior.GetComponent<RelicManager>();
            if (relicManager == null)
                return false;

            EnsurePlatformMotionTarget();

            if (platformMotionTarget == null)
            {
                Debug.LogWarning("[KeyRelicLock] Cannot activate platform motion: platformMotionTarget is null.", this);
                return false;
            }

            bool previousEnabled = platformMotionTarget.PlatformMotionEnabled;

            // Enable platform motion first. Do not consume the key until the target accepted the value.
            platformMotionTarget.PlatformMotionEnabled = true;

            if (!platformMotionTarget.PlatformMotionEnabled)
            {
                Debug.LogWarning("[KeyRelicLock] Failed to set PlatformMotionEnabled = true. Key was NOT consumed.", platformMotionTarget);
                return false;
            }

            if (ConsumeKeyOnUnlock && !ConsumeKeys(relicManager))
            {
                Debug.LogWarning("[KeyRelicLock] Platform motion was enabled, but key consumption failed. Rolling platform motion back.", this);
                platformMotionTarget.PlatformMotionEnabled = previousEnabled;
                onMissingKey?.Invoke();
                return false;
            }

            CompleteUnlock();
            return true;
        }

        /// <summary>
        /// Called by a separate activation-point trigger. This arms the Key Relic UI for this lock
        /// without requiring the Warrior to touch the target moving/rotating platform.
        /// </summary>
        public bool ArmPlatformMotionFromActivationPoint(Warrior warrior, MonoBehaviour overridePlatformMotionTarget = null)
        {
            if (warrior == null)
                return false;

            if (mode != KeyLockMode.ArmKeyToEnablePlatformMotion)
                return false;

            if (_unlocked && UnlockOnlyOnce)
                return false;

            if (overridePlatformMotionTarget != null)
            {
                PlatFormColliderTrigger resolvedTarget = ResolvePlatformMotionTarget(overridePlatformMotionTarget);
                if (resolvedTarget != null)
                    platformMotionTarget = resolvedTarget;
            }

            BeginPlatformMotionPrompt(warrior);
            return ActivePlatformMotionLock == this && _currentWarrior == warrior;
        }

        /// <summary>
        /// Called by a separate activation-point trigger when Warrior leaves that point.
        /// </summary>
        public void ClosePlatformMotionFromActivationPoint(Warrior warrior)
        {
            if (warrior == null)
                return;

            if (mode != KeyLockMode.ArmKeyToEnablePlatformMotion)
                return;

            if (_currentWarrior != warrior)
                return;

            ClosePlatformMotionPrompt();
        }

        private void HandleEnterCollider(Collider2D other)
        {
            if (other == null)
                return;

            if (_unlocked && UnlockOnlyOnce)
                return;

            Warrior warrior = other.GetComponentInParent<Warrior>();
            if (warrior == null)
                return;

            if (mode == KeyLockMode.ArmKeyToEnablePlatformMotion)
            {
                if (CanOwnColliderArmPlatformMotion())
                    BeginPlatformMotionPrompt(warrior);

                return;
            }

            TryUnlock(warrior);
        }

        private void HandleExitCollider(Collider2D other)
        {
            if (other == null)
                return;

            if (mode != KeyLockMode.ArmKeyToEnablePlatformMotion)
                return;

            if (!CanOwnColliderArmPlatformMotion())
                return;

            Warrior warrior = other.GetComponentInParent<Warrior>();
            if (warrior == null)
                return;

            if (_currentWarrior == warrior)
                ClosePlatformMotionPrompt();
        }

        private void BeginPlatformMotionPrompt(Warrior warrior)
        {
            if (warrior == null)
                return;

            if (_unlocked && UnlockOnlyOnce)
                return;

            EnsurePlatformMotionTarget();

            if (platformMotionTarget == null)
            {
                Debug.LogWarning("[KeyRelicLock] Platform motion mode is enabled, but no platformMotionTarget was found.", this);
                return;
            }

            if (platformMotionTarget.PlatformMotionEnabled)
                return;

            _currentWarrior = warrior;
            SetActivePlatformMotionLock(this, forceNotify: true);
            onPlatformMotionPromptAvailable?.Invoke();

            RelicManager relicManager = warrior.GetComponent<RelicManager>();
            if (relicManager != null && GetOwnedKeyCount(relicManager) < requiredKeys)
                onMissingKey?.Invoke();
        }

        private void ClosePlatformMotionPrompt()
        {
            _currentWarrior = null;
            ClearActivePromptIfThisLockOwnsIt();
            onPlatformMotionPromptClosed?.Invoke();
        }

        private void ClearActivePromptIfThisLockOwnsIt()
        {
            if (ActivePlatformMotionLock == this)
                SetActivePlatformMotionLock(null);
        }

        private int GetOwnedKeyCount(RelicManager relicManager)
        {
            if (relicManager == null)
                return 0;

            if (keyRelic != null)
                return relicManager.GetCount(keyRelic);

            string id = ResolveKeyRelicId();
            return string.IsNullOrEmpty(id) ? 0 : relicManager.GetCountById(id);
        }

        private bool ConsumeKeys(RelicManager relicManager)
        {
            if (relicManager == null)
                return false;

            if (keyRelic != null)
                return relicManager.TryConsume(keyRelic, requiredKeys);

            string id = ResolveKeyRelicId();
            return !string.IsNullOrEmpty(id) && relicManager.TryConsumeById(id, requiredKeys);
        }

        private string ResolveKeyRelicId()
        {
            if (keyRelic != null)
                return !string.IsNullOrEmpty(keyRelic.relicId) ? keyRelic.relicId : keyRelic.name;

            if (!string.IsNullOrEmpty(keyRelicIdOverride))
                return keyRelicIdOverride;

            return KeyRelic.DefaultRelicId;
        }

        private void CompleteUnlock()
        {
            if (_unlocked && UnlockOnlyOnce)
                return;

            _unlocked = true;
            ClosePlatformMotionPrompt();

            PlayUnlockAnimator();

            // Important:
            // Do not disable colliders on moving/rotating platform locks.
            // PlatformMotionEnabled must only control movement/rotation, not collision.
            if (mode == KeyLockMode.ImmediateUnlockObject)
            {
                DisableOwnCollidersIfSafe();
                DisableLegacyExtraCollidersIfSafe();
            }

            if (objectToDisableOnUnlock != null && CanDisableObjectSafely(objectToDisableOnUnlock))
                objectToDisableOnUnlock.SetActive(false);

            if (objectToEnableOnUnlock != null)
                objectToEnableOnUnlock.SetActive(true);

            onUnlocked?.Invoke();
        }

        private void PlayUnlockAnimator()
        {
            if (animator == null)
                return;

            if (HasAnimatorParameter(animator, DefaultUnlockedBoolName, AnimatorControllerParameterType.Bool))
                animator.SetBool(DefaultUnlockedBoolName, true);

            if (HasAnimatorParameter(animator, DefaultUnlockTriggerName, AnimatorControllerParameterType.Trigger))
                animator.SetTrigger(DefaultUnlockTriggerName);
        }

        private void DisableOwnCollidersIfSafe()
        {
            // Never disable platform colliders from this script.
            if (IsAttachedToPlatformObject())
                return;

            Collider2D[] ownColliders = GetComponents<Collider2D>();
            for (int i = 0; i < ownColliders.Length; i++)
            {
                if (ownColliders[i] != null)
                    ownColliders[i].enabled = false;
            }
        }

        private void DisableLegacyExtraCollidersIfSafe()
        {
            if (extraCollidersToDisable == null)
                return;

            for (int i = 0; i < extraCollidersToDisable.Length; i++)
            {
                Collider2D col = extraCollidersToDisable[i];
                if (col == null)
                    continue;

                // Never disable a platform collider through the old legacy array.
                if (col.GetComponentInParent<PlatFormColliderTrigger>() != null)
                    continue;

                col.enabled = false;
            }
        }

        private bool CanDisableObjectSafely(GameObject candidate)
        {
            if (candidate == null)
                return false;

            if (mode != KeyLockMode.ArmKeyToEnablePlatformMotion)
                return true;

            EnsurePlatformMotionTarget();

            if (platformMotionTarget != null && candidate == platformMotionTarget.gameObject)
            {
                Debug.LogWarning("[KeyRelicLock] Refused to disable the platform motion target after unlocking. The platform must stay active.", this);
                return false;
            }

            if (candidate.GetComponent<PlatFormColliderTrigger>() != null)
            {
                Debug.LogWarning("[KeyRelicLock] Refused to disable a platform object after unlocking. The platform must stay active.", this);
                return false;
            }

            return true;
        }

        private bool CanOwnColliderArmPlatformMotion()
        {
            // If this lock is attached to a platform root/child, do not let the platform's own
            // physical colliders arm the key prompt. Use KeyRelicPlatformMotionActivationPoint instead.
            return !IsAttachedToPlatformObject();
        }

        private bool IsAttachedToPlatformObject()
        {
            return GetComponentInParent<PlatFormColliderTrigger>(true) != null ||
                   GetComponentInChildren<PlatFormColliderTrigger>(true) != null;
        }

        private void EnsurePlatformMotionTarget()
        {
            if (platformMotionTarget != null)
                return;

            platformMotionTarget = GetComponent<PlatFormColliderTrigger>();

            if (platformMotionTarget == null)
                platformMotionTarget = GetComponentInParent<PlatFormColliderTrigger>(true);

            if (platformMotionTarget == null)
                platformMotionTarget = GetComponentInChildren<PlatFormColliderTrigger>(true);
        }

        private static PlatFormColliderTrigger ResolvePlatformMotionTarget(MonoBehaviour source)
        {
            if (source == null)
                return null;

            if (source is PlatFormColliderTrigger platform)
                return platform;

            PlatFormColliderTrigger found = source.GetComponent<PlatFormColliderTrigger>();

            if (found == null)
                found = source.GetComponentInParent<PlatFormColliderTrigger>(true);

            if (found == null)
                found = source.GetComponentInChildren<PlatFormColliderTrigger>(true);

            return found;
        }

        private static bool HasAnimatorParameter(
            Animator targetAnimator,
            string parameterName,
            AnimatorControllerParameterType parameterType)
        {
            if (targetAnimator == null || string.IsNullOrEmpty(parameterName))
                return false;

            AnimatorControllerParameter[] parameters = targetAnimator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];

                if (parameter.type == parameterType && parameter.name == parameterName)
                    return true;
            }

            return false;
        }

        private static void SetActivePlatformMotionLock(KeyRelicLock value, bool forceNotify = false)
        {
            if (ActivePlatformMotionLock == value)
            {
                if (forceNotify)
                    OnActivePlatformMotionLockChanged?.Invoke(value);

                return;
            }

            ActivePlatformMotionLock = value;
            OnActivePlatformMotionLockChanged?.Invoke(value);
        }
    }
}

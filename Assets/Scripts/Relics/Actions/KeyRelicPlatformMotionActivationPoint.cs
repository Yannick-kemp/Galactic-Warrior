using Assets.Scripts.Characteres.WarriorController;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Relics.World
{
    /// <summary>
    /// Dedicated scene trigger that arms a KeyRelicLock for platform-motion activation.
    ///
    /// Use this when the activation point is independent from the moving/rotating platform.
    /// The Warrior only needs to reach this trigger. The target platform can be far away.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class KeyRelicPlatformMotionActivationPoint : MonoBehaviour
    {
        [Header("Lock To Arm")]
        [Tooltip("The KeyRelicLock that should become active when Warrior reaches this point. It should be configured with mode = ArmKeyToEnablePlatformMotion.")]
        [SerializeField] private KeyRelicLock keyRelicLock;

        [Header("Remote Platform Target")]
        [Tooltip("Optional override. Assign the far-away MovingHorizontalPlatform / MovingVerticalPlatform / RotatingPlatform component that owns PlatformMotionEnabled.")]
        [SerializeField] private MonoBehaviour platformMotionTarget;

        [Tooltip("If true, this activation point passes platformMotionTarget into the lock when Warrior enters.")]
        [SerializeField] private bool overrideLockPlatformMotionTarget = true;

        [Header("Contact")]
        [Tooltip("Recommended false. Warrior has multiple colliders/layers, so the activation point should accept any collider belonging to Warrior.")]
        [SerializeField] private bool requireWarriorHitBoxLayer = false;

        [SerializeField] private string warriorHitBoxLayerName = "Hit Box";

        [Header("Behavior")]
        [SerializeField] private bool rearmOnTriggerStay = true;
        [SerializeField] private bool closePromptOnExit = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private int _hitBoxLayer = -1;
        private Warrior _currentWarrior;

        // Important:
        // Warrior has multiple colliders. We must not close the prompt when only
        // one child collider exits while another Warrior collider is still inside.
        private readonly HashSet<Collider2D> _warriorCollidersInside = new HashSet<Collider2D>();

        private void Reset()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
                col.isTrigger = true;

            requireWarriorHitBoxLayer = false;
        }

        private void Awake()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null && !col.isTrigger)
            {
                Debug.LogWarning("[KeyRelicPlatformMotionActivationPoint] Collider2D should be Is Trigger = true.", this);
            }

            // This only works if KeyRelicLock is on the same GameObject.
            // If the lock is on another object, assign keyRelicLock manually in the Inspector.
            if (keyRelicLock == null)
                keyRelicLock = GetComponent<KeyRelicLock>();

            if (!string.IsNullOrEmpty(warriorHitBoxLayerName))
                _hitBoxLayer = LayerMask.NameToLayer(warriorHitBoxLayerName);
        }

        private void OnDisable()
        {
            CloseCurrentPrompt();
            _warriorCollidersInside.Clear();
        }

        private void OnDestroy()
        {
            CloseCurrentPrompt();
            _warriorCollidersInside.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            RegisterAndArmFromCollider(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (rearmOnTriggerStay)
                RegisterAndArmFromCollider(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other == null)
                return;

            if (!TryGetWarrior(other, out Warrior warrior))
                return;

            _warriorCollidersInside.Remove(other);

            if (!closePromptOnExit)
                return;

            if (_currentWarrior == null || warrior != _currentWarrior)
                return;

            // Do not close while another Warrior collider is still inside this activation point.
            if (_warriorCollidersInside.Count > 0)
                return;

            CloseCurrentPrompt();
        }

        private void RegisterAndArmFromCollider(Collider2D other)
        {
            if (!TryGetWarrior(other, out Warrior warrior))
                return;

            _warriorCollidersInside.Add(other);

            if (_currentWarrior != null && _currentWarrior != warrior)
                return;

            if (keyRelicLock == null)
            {
                Debug.LogWarning("[KeyRelicPlatformMotionActivationPoint] No KeyRelicLock assigned. Assign it in the Inspector or put KeyRelicLock on the same GameObject.", this);
                return;
            }

            if (overrideLockPlatformMotionTarget && platformMotionTarget == null && debugLogs)
            {
                Debug.LogWarning("[KeyRelicPlatformMotionActivationPoint] overrideLockPlatformMotionTarget is true, but platformMotionTarget is empty. The KeyRelicLock must already have a platformMotionTarget assigned.", this);
            }

            MonoBehaviour targetOverride = overrideLockPlatformMotionTarget ? platformMotionTarget : null;
            bool armed = keyRelicLock.ArmPlatformMotionFromActivationPoint(warrior, targetOverride);

            if (armed)
            {
                _currentWarrior = warrior;

                if (debugLogs)
                    Debug.Log("[KeyRelicPlatformMotionActivationPoint] Key relic platform-motion prompt armed.", this);
            }
            else if (debugLogs)
            {
                Debug.LogWarning("[KeyRelicPlatformMotionActivationPoint] Tried to arm KeyRelicLock, but it refused. Check mode, platformMotionTarget, PlatformMotionEnabled=false, and unlockOnlyOnce.", this);
            }
        }

        private bool TryGetWarrior(Collider2D other, out Warrior warrior)
        {
            warrior = null;

            if (other == null)
                return false;

            warrior = other.GetComponentInParent<Warrior>();
            if (warrior == null)
                return false;

            // Recommended for this activation-point system:
            // accept Warrior layer, Hit Box layer, body collider, child collider, etc.
            if (!requireWarriorHitBoxLayer)
                return true;

            if (_hitBoxLayer < 0)
            {
                Debug.LogWarning("[KeyRelicPlatformMotionActivationPoint] Hit Box layer was not found. Check warriorHitBoxLayerName.", this);
                return false;
            }

            return other.gameObject.layer == _hitBoxLayer;
        }

        private void CloseCurrentPrompt()
        {
            if (_currentWarrior == null || keyRelicLock == null)
            {
                _currentWarrior = null;
                return;
            }

            keyRelicLock.ClosePlatformMotionFromActivationPoint(_currentWarrior);
            _currentWarrior = null;
        }
    }
}
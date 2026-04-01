using System;
using UnityEngine;

namespace Assets.Scripts.Services
{
    /// <summary>
    /// Service d'attaque concret pour Enemy (non-générique pour Unity)
    /// </summary>
    public class EnemyRangeService : MonoBehaviour
    {
        private float attackCooldown = 0.75f;
        private int attackDamage = 10;

        public float AttackCooldown => attackCooldown;
        public int AttackDamage => attackDamage;

        [SerializeField] public bool debugMode = false;

        [Header("Attack Effects")]
        [SerializeField] public GameObject attackEffectPrefab;
        [SerializeField] public Vector3 effectOffset = Vector3.zero;

        private float lastAttackTime = -999f;
        private IAttacker owner;

        public float _range;

        public bool IsInRange { get; private set; }
        public IAttacker Owner => owner;

        /// <summary>
        /// Initialize the attack service with an owner
        /// </summary>
        public void Initialize(IAttacker ownerEnemy)
        {
            owner = ownerEnemy;
        }

        public bool TryAction(
        Transform target,
        float Range,
        Action<IAttacker, Transform> onPerformCallback = null,
        float? threshold = null)
        {
            _range = Range;

            if (target == null || owner == null) return false;

            float distance = Mathf.Abs(owner.Transform.position.x - target.position.x);
            IsInRange = threshold != null ? (threshold.Value < distance && distance <= Range) : (distance <= Range);

            // Only attack if in range AND cooldown ready
            if (!IsInRange) return false;
            if (!CanPerformRangeDetection()) return true; // still in range, just not ready yet

            PerformAction(target, onPerformCallback);
            return true;
        }


        /// <summary>
        /// Check if enough time has passed since last attack
        /// </summary>
        public bool CanPerformRangeDetection()
        {
            return Time.time >= lastAttackTime + attackCooldown;
        }

        private void PerformAction(Transform target, Action<IAttacker, Transform> onRangeCallback)
        {
            lastAttackTime = Time.time;

            if (debugMode)
            {
                Debug.Log($"[AttackService] {owner.Name} attacks {target.name} for {attackDamage} damage!");
            }
            onRangeCallback?.Invoke(owner, target);
        }


        /// <summary>
        /// Get distance to target
        /// </summary>
        public float GetDistanceToTarget(Transform target)
        {
            if (target == null || owner == null) return float.MaxValue;
            return Vector3.Distance(owner.Transform.position, target.position);
        }

        /// <summary>
        /// Force cooldown reset
        /// </summary>
        public void ResetCooldown()
        {
            lastAttackTime = -999f;
        }

        /// <summary>
        /// Get remaining cooldown time
        /// </summary>
        public float GetRemainingCooldown()
        {
            float remaining = (lastAttackTime + attackCooldown) - Time.time;
            return Mathf.Max(0, remaining);
        }

        /// <summary>
        /// Get cooldown progress (0 to 1)
        /// </summary>
        public float GetCooldownProgress()
        {
            if (attackCooldown <= 0) return 1f;
            float elapsed = Time.time - lastAttackTime;
            return Mathf.Clamp01(elapsed / attackCooldown);
        }

        // Runtime configuration setters
        public void SetAttackDamage(int damage) => attackDamage = Mathf.Max(0, damage);
        public void SetAttackRange(float range) => _range = Mathf.Max(0f, range);
        public void SetAttackCooldown(float cooldown) => attackCooldown = Mathf.Max(0f, cooldown);

        /// <summary>
        /// Draw attack range gizmo in editor
        /// </summary>
        public void DrawAttackRangeGizmo()
        {
            if (owner != null)
            {
                Gizmos.color = IsInRange ? Color.red : Color.yellow;
                Gizmos.DrawWireSphere(owner.Transform.position, _range);

                // Draw direction indicator
                if (IsInRange)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(owner.Transform.position, owner.Transform.position + owner.Transform.right * _range * 0.5f);
                }
            }
        }
    }
}
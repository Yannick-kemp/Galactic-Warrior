using System.Collections;
using Assets.Scripts.Characteres.EnemyContoller;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {
        [Header("Hivernox Freeze / Boss Damage")]
        [SerializeField] private bool shieldBlocksHivernoxIceProjectile = false;
        [SerializeField] private string hivernoxFrozenAnimatorBool = "isFrozen";
        [SerializeField] private GameObject hivernoxFreezeVfxPrefab;
        [SerializeField] private Transform hivernoxFreezeVfxSocket;
        [SerializeField] private Vector3 hivernoxFreezeVfxOffset = new Vector3(0f, 0.75f, 0f);
        [SerializeField] private float hivernoxFreezeRecoveryImmunity = 0.25f;
        [SerializeField] private float blockedHivernoxHitStunSeconds = 0.12f;
        [SerializeField] private float blockedHivernoxHitKnockbackVelocity = 3.5f;

        [SerializeField] private WarriorRootIceOverlay warriorIceOverlay;

        private bool _frozenByHivernox;
        private Coroutine _hivernoxFreezeRoutine;
        private Coroutine _hivernoxHitLockRoutine;
        private Coroutine _hivernoxShieldRestoreRoutine;
        private GameObject _hivernoxFreezeVfxInstance;
        private float _hivernoxFreezeImmuneUntil = -999f;

        public bool IsFrozenByHivernox => _frozenByHivernox;

        public bool TryFreezeFromHivernox(HivernoxBoss source, float seconds, Vector2 hitWorldPosition)
        {
            if (seconds <= 0f)
                return false;

            if (IsDeadOrDying || _deathStarted)
                return false;

            if (_sprintActive)
                return false;

            if (Time.time < _hivernoxFreezeImmuneUntil)
                return false;

            if (shieldBlocksHivernoxIceProjectile && ShieldIsUp && TryConsumeShieldForHivernoxBlock())
            {
                ApplyHivernoxHitLock(
                    hitWorldPosition,
                    blockedHivernoxHitStunSeconds,
                    blockedHivernoxHitKnockbackVelocity);
                return false;
            }

            if (_hivernoxFreezeRoutine != null)
                StopCoroutine(_hivernoxFreezeRoutine);

            _hivernoxFreezeRoutine = StartCoroutine(HivernoxFreezeRoutine(seconds));
            return true;
        }

        public void BreakHivernoxFreeze()
        {
            if (!_frozenByHivernox)
                return;

            ReleaseHivernoxFreeze();
            _hivernoxFreezeImmuneUntil = Time.time + hivernoxFreezeRecoveryImmunity;
        }

        public bool TryReceiveHivernoxDamage(
            Enemy source,
            float damage,
            bool canBeBlockedByShield,
            float stunSeconds,
            float knockbackVelocity)
        {
            if (IsDeadOrDying || _deathStarted)
                return false;

            Vector2 fromPosition = source != null ? (Vector2)source.transform.position : (Vector2)transform.position;

            if (canBeBlockedByShield && ShieldIsUp && TryConsumeShieldForHivernoxBlock())
            {
                ApplyHivernoxHitLock(
                    fromPosition,
                    Mathf.Max(stunSeconds, blockedHivernoxHitStunSeconds),
                    Mathf.Max(knockbackVelocity, blockedHivernoxHitKnockbackVelocity));
                return true;
            }

            if (_frozenByHivernox)
                BreakHivernoxFreeze();

            if (damage > 0f)
                TakeDamage(damage);

            if (stunSeconds > 0f || knockbackVelocity > 0f)
                ApplyHivernoxHitLock(fromPosition, stunSeconds, knockbackVelocity);

            return false;
        }

        /// <summary>
        /// Used by Hivernox when Warrior hits him with a dash/dodge-style attack.
        /// This bypasses the sprint damage immunity on purpose, because the design says
        /// dash deals damage but Warrior receives minor return damage.
        /// </summary>
        public void ApplyHivernoxDashReturnDamage(float damage, Vector2 fromWorldPosition)
        {
            if (damage <= 0f)
                return;

            if (IsDeadOrDying || _deathStarted || _reviveInvulnerable)
                return;

            if (_frozenByHivernox)
                BreakHivernoxFreeze();

            base.TakeDamage(damage);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            UpdateLowHealthBlink();

            if (currentHealth <= 0f)
            {
                StartDeath();
                return;
            }

            ApplyHivernoxHitLock(fromWorldPosition, 0.08f, 2.2f);
        }

        private IEnumerator HivernoxFreezeRoutine(float seconds)
        {
            _frozenByHivernox = true;

            ForceCancelCurrentAttackForExternalLock();
            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();
            WaitAnimationDisplay();

            CanMove = false;
            CanAttackWarrior = false;

            if (rigidbody2 != null)
                rigidbody2.linearVelocity = Vector2.zero;

            if (animator != null && !string.IsNullOrWhiteSpace(hivernoxFrozenAnimatorBool))
                animator.SetBool(hivernoxFrozenAnimatorBool, true);

            ShowHivernoxFreezeVfx();

            float timer = 0f;
            while (timer < seconds && _frozenByHivernox && !IsDeadOrDying)
            {
                if (rigidbody2 != null)
                    rigidbody2.linearVelocity = Vector2.zero;

                timer += Time.deltaTime;
                yield return null;
            }

            if (_frozenByHivernox)
            {
                ReleaseHivernoxFreeze();
                _hivernoxFreezeImmuneUntil = Time.time + hivernoxFreezeRecoveryImmunity;
            }

            _hivernoxFreezeRoutine = null;
        }

        private void ReleaseHivernoxFreeze()
        {
            _frozenByHivernox = false;

            if (animator != null && !string.IsNullOrWhiteSpace(hivernoxFrozenAnimatorBool))
                animator.SetBool(hivernoxFrozenAnimatorBool, false);

            HideHivernoxFreezeVfx();

            if (!IsDeadOrDying && !_deathStarted)
            {
                CanMove = true;
                CanAttackWarrior = true;
            }
        }

        private void ShowHivernoxFreezeVfx()
        {
            if (warriorIceOverlay != null)
                warriorIceOverlay.Show();

            if (hivernoxFreezeVfxPrefab == null)
                return;

            if (_hivernoxFreezeVfxInstance != null)
                Destroy(_hivernoxFreezeVfxInstance);

            Transform parent = hivernoxFreezeVfxSocket != null ? hivernoxFreezeVfxSocket : transform;

            _hivernoxFreezeVfxInstance = Instantiate(
                hivernoxFreezeVfxPrefab,
                parent.position + hivernoxFreezeVfxOffset,
                Quaternion.identity,
                parent);
        }

        private void HideHivernoxFreezeVfx()
        {
            if (warriorIceOverlay != null)
                warriorIceOverlay.Hide();

            if (_hivernoxFreezeVfxInstance == null)
                return;

            Destroy(_hivernoxFreezeVfxInstance);
            _hivernoxFreezeVfxInstance = null;
        }

        private bool TryConsumeShieldForHivernoxBlock()
        {
            if (!ShieldIsUp)
                return false;

            if (Time.time - _lastBlockTime < shieldMinBlockInterval)
                return true;

            _lastBlockTime = Time.time;
            _shieldDurability = Mathf.Max(0f, _shieldDurability - shieldBlockCost);

            if (_shieldDurability <= 0f)
                BreakShieldFromHivernoxBlock();

            return true;
        }

        private void BreakShieldFromHivernoxBlock()
        {
            _shieldBroken = true;

            if (_shieldInstance != null)
                _shieldInstance.SetActive(false);

            if (shieldHitbox != null)
                shieldHitbox.enabled = false;

            if (_hivernoxShieldRestoreRoutine != null)
                StopCoroutine(_hivernoxShieldRestoreRoutine);

            _hivernoxShieldRestoreRoutine = StartCoroutine(RestoreShieldAfterHivernoxBreak());
        }

        private IEnumerator RestoreShieldAfterHivernoxBreak()
        {
            yield return new WaitForSeconds(shieldBreakCooldown);

            _shieldDurability = shieldMaxDurability;
            _shieldBroken = false;
            _hivernoxShieldRestoreRoutine = null;
        }

        private void ApplyHivernoxHitLock(Vector2 fromWorldPosition, float seconds, float knockbackVelocity)
        {
            if (IsDeadOrDying || _deathStarted)
                return;

            if (_hivernoxHitLockRoutine != null)
                StopCoroutine(_hivernoxHitLockRoutine);

            _hivernoxHitLockRoutine = StartCoroutine(HivernoxHitLockRoutine(fromWorldPosition, seconds, knockbackVelocity));
        }

        private IEnumerator HivernoxHitLockRoutine(Vector2 fromWorldPosition, float seconds, float knockbackVelocity)
        {
            ForceCancelCurrentAttackForExternalLock();
            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();

            CanMove = false;
            CanAttackWarrior = false;

            if (rigidbody2 != null && knockbackVelocity > 0f)
            {
                float dir = Mathf.Sign(transform.position.x - fromWorldPosition.x);
                if (Mathf.Approximately(dir, 0f))
                    dir = rightFacing ? -1f : 1f;

                Vector2 v = rigidbody2.linearVelocity;
                v.x = dir * knockbackVelocity;
                rigidbody2.linearVelocity = v;
            }

            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);
            else
                yield return null;

            if (!IsDeadOrDying && !_deathStarted && !_frozenByHivernox)
            {
                CanMove = true;
                CanAttackWarrior = true;
            }

            _stunImmuneUntil = Time.time + stunRecoveryImmunity;
            _hivernoxHitLockRoutine = null;
        }
    }
}

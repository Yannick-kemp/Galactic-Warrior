using Assets.Scripts.Characteres.EnemyContoller;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {

        #region Shield Logic

        public void EnableAttack2Temporarily(float duration)
        {
            SetAttackMode(AttackAnimMode.Attack2);
            // ActivateShield(duration);

            if (TimerMgr.Instance == null)
            {
                Debug.LogWarning("TimerMgr.Instance is null. Can't auto-revert Attack2.");
                return;
            }

            TimerMgr.Instance.StartTimer(duration, () =>
            {
                SetAttackMode(AttackAnimMode.Attack1);
            });
        }

        private void SetupShield()
        {
            if (shieldPrefab == null)
            {
                Debug.LogWarning("[Warrior] Shield prefab not assigned.");
                return;
            }

            if (shieldSocket == null)
                shieldSocket = transform;

            _shieldInstance = Instantiate(shieldPrefab, shieldSocket);
            _shieldInstance.name = "ShieldVFX_Instance";
            _shieldInstance.transform.localPosition = shieldLocalOffset;
            _shieldInstance.transform.localRotation = Quaternion.identity;
            _shieldInstance.transform.localScale = shieldLocalScale;

            _shieldParticles = _shieldInstance.GetComponentsInChildren<ParticleSystem>(true);
            _shieldRenderers = _shieldInstance.GetComponentsInChildren<ParticleSystemRenderer>(true);

            if (shieldSimulationLocalSpace && _shieldParticles != null)
            {
                foreach (var ps in _shieldParticles)
                {
                    var main = ps.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                }
            }

            if (_shieldRenderers != null)
            {
                foreach (var r in _shieldRenderers)
                {
                    r.sortingLayerName = shieldSortingLayerName;
                    r.sortingOrder = shieldOrderInLayer;
                }
            }


            EnsureShieldHitboxUsesShieldLaserLayer();

            StopShieldImmediate();
            _shieldInstance.SetActive(false);

            if (shieldHitbox != null)
                shieldHitbox.enabled = false;
        }
        private void EnsureShieldHitboxUsesShieldLaserLayer()
        {
            if (shieldHitbox == null)
                return;

            int shieldLaserLayer = LayerMask.NameToLayer("Shield Laser");
            if (shieldLaserLayer < 0)
            {
                Debug.LogWarning("[Warrior] Unity layer 'Shield Laser' does not exist. Create it in Project Settings > Tags and Layers.", this);
                return;
            }

            shieldHitbox.gameObject.layer = shieldLaserLayer;
        }

        public void ActivateShield(float duration = -1f)
        {
            if (!shieldGameplayEnabled) return;
            if (_shieldBroken) return;

            if (IsSprintBlockingShieldUse()) return;

            if (_shieldInstance == null)
                SetupShield();

            if (_shieldInstance == null) return;

            if (_shieldDisableRoutine != null)
            {
                StopCoroutine(_shieldDisableRoutine);
                _shieldDisableRoutine = null;
            }

            _shieldInstance.SetActive(true);

            EnsureShieldHitboxUsesShieldLaserLayer();

            if (shieldHitbox != null)
                shieldHitbox.enabled = true;

            if (warriorMeteorShieldHitbox != null)
                warriorMeteorShieldHitbox.enabled = true;

            if (_shieldParticles != null)
            {
                foreach (var ps in _shieldParticles)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }
            }

            if (duration > 0f)
                _shieldDisableRoutine = StartCoroutine(DisableShieldAfter(duration));
        }

        public void DeactivateShield()
        {
            if (shieldHitbox != null)
                shieldHitbox.enabled = false;

            if (warriorMeteorShieldHitbox != null)
                warriorMeteorShieldHitbox.enabled = false;

            if (_shieldInstance == null) return;

            StopShieldImmediate();
            _shieldInstance.SetActive(false);
        }

        private IEnumerator DisableShieldAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            DeactivateShield();
            _shieldDisableRoutine = null;
        }

        private void StopShieldImmediate()
        {
            if (_shieldParticles == null) return;

            foreach (var ps in _shieldParticles)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void ConsumeShield(float amount)
        {
            if (!shieldGameplayEnabled) return;

            _shieldDurability -= amount;
            if (_shieldDurability > 0f) return;

            _shieldDurability = 0f;
            BreakShield();
        }

        private void BreakShield()
        {
            if (_shieldBroken) return;

            _shieldBroken = true;
            DeactivateShield();

            if (_shieldRecoverRoutine != null) StopCoroutine(_shieldRecoverRoutine);
            _shieldRecoverRoutine = StartCoroutine(RecoverShieldAfter(shieldBreakCooldown));
        }

        private IEnumerator RecoverShieldAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);

            _shieldBroken = false;
            _shieldDurability = shieldMaxDurability;
            _shieldRecoverRoutine = null;
        }

        private void DoShieldStomp(Enemy enemy)
        {
            if (enemy == null) return;

            ConsumeShield(shieldStompCost);
            bool killed = enemy.TakeDamageAndReturnKilled(stompDamage);

            if (!killed)
            {
                enemy.ApplyStun(stompStunSeconds);

                float dir = (enemy.transform.position.x - transform.position.x) >= 0f ? 1f : -1f;
                enemy.stepBackDistance = stompKnockback;
                StartCoroutine(dir > 0f ? enemy.SmoothStepBack(true) : enemy.SmoothStepBack(false));
            }

            //if (killed)
            //{
            //   var hub = GetComponent<PlayerEventHub>();
            //    if (hub != null)
            //    {
            //        hub.RaiseKill(new KillEvent
            //        {
            //            killer = gameObject,
            //            victim = enemy.gameObject
            //        });
            //    }
            //}
        }

        /// <summary>
        /// Returns TRUE if the hit was BLOCKED (so caller should NOT apply damage).
        /// Front-only, consumes durability, and applies knockback + stun to enemy.
        /// </summary>
        public bool TryBlockEnemyHit(Enemy enemy, float blockCost = -1f, bool applyReaction = true)
        {
            if (!ShieldIsUp) return false;
            if (enemy == null) return false;

            if (Time.time - _lastBlockTime < shieldMinBlockInterval)
                return true;

            _lastBlockTime = Time.time;
            ConsumeShield(blockCost > 0f ? blockCost : shieldBlockCost);

            if (applyReaction)
            {
                enemy.ApplyStun(blockStunSeconds);

                float dir = (enemy.transform.position.x - transform.position.x) >= 0f ? 1f : -1f;
                enemy.stepBackDistance = blockKnockback;
                StartCoroutine(dir > 0f ? enemy.SmoothStepBack(true) : enemy.SmoothStepBack(false));
            }

            return true;
        }

        public bool TryBlockProjectile(Rigidbody2D projRb, Transform projTf, float blockCost = 5f)
        {
            if (!ShieldIsUp) return false;
            if (projRb == null || projTf == null) return false;

            float dx = projTf.position.x - transform.position.x;

            bool incomingFromFront =
                (rightFacing && dx > 0f && projRb.linearVelocity.x < 0f) ||
                (leftFacing && dx < 0f && projRb.linearVelocity.x > 0f);

            if (!incomingFromFront) return false;

            if (Time.time - _lastBlockTime >= shieldMinBlockInterval)
            {
                _lastBlockTime = Time.time;
                ConsumeShield(blockCost);
            }

            Vector2 v = projRb.linearVelocity;
            v.x = -v.x;
            projRb.linearVelocity = v;

            return true;
        }

        public bool TryBlockProjectileKinematic(Transform projectileTf, Vector2 travelDir, float cost = 5f)
        {
            if (!ShieldIsUp) return false;
            if (projectileTf == null) return false;

            float dx = projectileTf.position.x - transform.position.x;

            bool inFront =
                (rightFacing && dx > 0f) ||
                (leftFacing && dx < 0f) ||
                Mathf.Abs(dx) < 0.01f;

            bool movingToward =
                (rightFacing && travelDir.x < 0f) ||
                (leftFacing && travelDir.x > 0f);

            if (!inFront || !movingToward) return false;

            if (Time.time - _lastBlockTime >= shieldMinBlockInterval)
            {
                _lastBlockTime = Time.time;
                ConsumeShield(cost);
            }

            return true;
        }

        // Absorb "squeeze" pressure with shield (cost is applied at most once per interval)
        public bool TryAbsorbSqueeze(float cost, float minInterval = -1f)
        {
            if (!ShieldIsUp) return false;

            float interval = (minInterval > 0f) ? minInterval : shieldMinBlockInterval;
            if (Time.time - _lastBlockTime < interval) return true;

            _lastBlockTime = Time.time;
            ConsumeShield(cost);

            return true;
        }

        public bool TryUseShieldRelic(float duration, float cooldownOverride = -1f)
        {
            if (CanDie) return false;
            if (!shieldGameplayEnabled) return false;
            if (_shieldBroken) return false;
            if (IsSprintBlockingShieldUse()) return false;

            float cd = (cooldownOverride > 0f) ? cooldownOverride : shieldRelicDefaultCooldown;
            if (Time.time < _nextShieldRelicReadyTime) return false;

            _nextShieldRelicReadyTime = Time.time + cd;   // START COOLDOWN

            float dur = Mathf.Max(0.05f, duration);
            ActivateShield(dur);

            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, 0.12f));
            return true;
        }


        #endregion
    }
}
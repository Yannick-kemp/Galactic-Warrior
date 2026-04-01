using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {
        [Header("Meteor")]
        [SerializeField] private float meteorDamageMinInterval = 0.12f;
        [SerializeField] private float meteorShieldBlockCost = 5f;
        [SerializeField] private float meteorHitStunSeconds = 0.08f;
        [SerializeField] private float meteorShieldBlockGrace = 0.06f;

        public bool MeteorShieldIsActive =>
            warriorMeteorShieldHitbox != null &&
            warriorMeteorShieldHitbox.enabled &&
            ShieldIsUp;

        private float _nextMeteorDamageAllowedTime = -999f;
        private float _lastMeteorBlockedTime = -999f;

        public bool TryBlockMeteorHit(float shieldCost = -1f)
        {
            if (!ShieldIsUp) return false;
            if (shieldHitbox == null || !shieldHitbox.enabled) return false;

            _lastMeteorBlockedTime = Time.time;

            float cost = shieldCost > 0f ? shieldCost : meteorShieldBlockCost;

            // avoid draining the shield every single particle callback
            if (Time.time - _lastBlockTime >= shieldMinBlockInterval)
            {
                _lastBlockTime = Time.time;
                ConsumeShield(cost);
            }

            return true;
        }

        public bool TryTakeMeteorHit(float damage, Vector2 fromWorldPos, float stunSeconds = -1f)
        {
            if (damage <= 0f) return false;
            if (_deathStarted || CanDie) return false;
            if (_reviveInvulnerable) return false;

            if (ShieldIsUp && warriorMeteorShieldHitbox != null && warriorMeteorShieldHitbox.enabled)
            {
                TryBlockMeteorHit();
                return false;
            }

            if (Time.time - _lastMeteorBlockedTime <= meteorShieldBlockGrace)
                return false;

            if (Time.time < _nextMeteorDamageAllowedTime)
                return false;

            _nextMeteorDamageAllowedTime = Time.time + meteorDamageMinInterval;

            ApplyMeteorDamageDirect(damage);

            if (!_deathStarted && currentHealth > 0f)
            {
                float stun = stunSeconds >= 0f ? stunSeconds : meteorHitStunSeconds;
                ApplyHitReaction(HitKind.Spark, fromWorldPos, stun, 0f);
            }

            return true;
        }

        private void ApplyMeteorDamageDirect(float damage)
        {
            if (_reviveInvulnerable) return;
            if (_deathStarted) return;

            // bypass sprint-protected Warrior.TakeDamage()
            base.TakeDamage(damage);

            // optional blood effect at warrior position
            SpawnMeteorBloodEffect();

            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            UpdateLowHealthBlink();

            if (currentHealth <= 0f)
                StartDeath();
        }

        private void SpawnMeteorBloodEffect()
        {
            if (bloodshedPrefab == null) return;
            if (!CanSpawnBlood()) return;

            Vector3 pos = collider2 != null ? collider2.bounds.center : transform.position;
            GameObject fx = Instantiate(bloodshedPrefab, pos, Quaternion.identity);
            fx.transform.SetParent(transform);

            Destroy(fx, 0.5f);
            _lastBloodSpawnTime = Time.time;
        }
        public void ResetMeteorHitState(float extraGrace = 0.15f)
        {
            _lastMeteorBlockedTime = -999f;
            _nextMeteorDamageAllowedTime = Time.time + Mathf.Max(0f, extraGrace);
        }

    }
}
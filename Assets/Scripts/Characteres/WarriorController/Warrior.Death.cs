using System;
using System.Collections;
using UnityEngine;
using Assets.Scripts.Characteres.EnemyContoller;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {
        [SerializeField] private float normalGravityScale = 3f;
        public PlatFormColliderTrigger LastSafePlatform { get; set; }
        #region Health / Misc
        private float _allowViewportDeathTime;


        public void Heal(int amount)
        {
            if (amount <= 0) return;
            if (_deathStarted || CanDie) return;

            float before = currentHealth;
            currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);

            if (!Mathf.Approximately(before, currentHealth))
            {
                OnHealthChanged?.Invoke(currentHealth, maxHealth);
                UpdateLowHealthBlink();
                Debug.Log($"[Warrior] Heal +{amount} => {before} -> {currentHealth}/{maxHealth}");
            }
        }



        #endregion

        #region warrior state query (for external callers, e.g. relics)
        [Header("Death (Warrior)")]
        [SerializeField] private float gameOverUiDelay = 0.45f; // petit délai avant UI
        [SerializeField] private bool disablePhysicsOnDeath = true;
        [SerializeField] private bool disableCollidersOnDeath = true;

        [Header("Void Death Fallback")]
        [SerializeField] private bool useWorldYDeathFallback = true;
        [SerializeField] private float worldDeathY = -30f;

        public event Action OnDeathStarted;
        public event Action OnDeathEnded;
        public event Action<float, float> OnHealthChanged; // current, max

        private bool _deathStarted;

        // (optionnel) expose si besoin pour UI
        public bool IsDead => _deathStarted || CanDie;

        public bool IsDeadOrDying => _deathStarted || CanDie || currentHealth <= 0f;

        // Si CharacterController possède currentHealth/maxHealth (comme Enemy),
        // tu peux exposer ça aussi :
        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public float Health01 => maxHealth <= 0 ? 0f : Mathf.Clamp01(currentHealth / maxHealth);

        private void StartDeath()
        {
            if (_deathStarted) return;
            StopLowHealthBlink();
            _deathFinalized = false;
            GameMgr.Instance?.HandleWarriorDead();
            _deathStarted = true;

            CanDie = true;
            CanMove = false;
            CanAttackWarrior = false;

            if (warriorMeteorShieldHitbox != null)
                warriorMeteorShieldHitbox.enabled = false;

            // Stop tout ce qui peut continuer à agir
            ForceCancelCurrentAttack();
            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();

            // Stop sprint + shield proprement
            ForceStopSprint();
            DeactivateShield();

            // Bloque l’input monde (ton guard UI)
            _uiInputBlockUntil = float.PositiveInfinity;

            // Neutralise physique
            if (rigidbody2 != null)
            {
                rigidbody2.linearVelocity = Vector2.zero;
                if (disablePhysicsOnDeath) rigidbody2.simulated = false;
            }

            if (disableCollidersOnDeath)
            {
                if (collider2 != null) collider2.enabled = false;
                if (shieldHitbox != null) shieldHitbox.enabled = false;
            }

            OnDeathStarted?.Invoke();

            // Lancer ton anim existante
            DeathAnimationDisplay();

            // Fallback si tu n’as pas encore d’Animation Event
            StartCoroutine(DeathEndFallback());
        }

        private IEnumerator DeathEndFallback()
        {
            yield return new WaitForSecondsRealtime(gameOverUiDelay);
            EndDeath();
        }

        public void AE_WarriorDeathEnd() // <-- Animation Event à mettre sur la dernière frame du clip mort
        {
            EndDeath();
        }

        private void EndDeath()
        {
            if (!_deathStarted) return;
            if (_deathFinalized) return;
            _deathFinalized = true;

            OnDeathEnded?.Invoke();
            GameMgr.Instance?.HandleWarriorDead();
        }
        #endregion

        #region restart game
        [SerializeField] private float reviveHpPercent = 0.5f;     // 50% HP
        [SerializeField] private float reviveInvulnSeconds = 1.2f; // short invuln
        private bool _reviveInvulnerable;
        private Coroutine _reviveInvulnRoutine;

        public bool TryRevive(float hpPercentOverride = -1f, float invulnOverride = -1f)
        {
            if (!_deathStarted)
                return false;

            float hpPercent = (hpPercentOverride > 0f) ? hpPercentOverride : reviveHpPercent;
            float invuln = (invulnOverride > 0f) ? invulnOverride : reviveInvulnSeconds;

            PrepareForSafeRespawn();

            currentHealth = Mathf.Max(1f, maxHealth * hpPercent);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            UpdateLowHealthBlink();

            _shieldBroken = false;
            _shieldDurability = shieldMaxDurability;
            DeactivateShield();

            WaitAnimationDisplay();

            if (_reviveInvulnRoutine != null)
                StopCoroutine(_reviveInvulnRoutine);

            _reviveInvulnRoutine = StartCoroutine(ReviveInvuln(invuln));

            return true;
        }






        public void PrepareForSafeRespawn()
        {
            IsFallingEdge = false;
            IsFallingPlfExit = false;
            IsFallingHitEnemy = false;
            IsFallingGrazesEdge = false;

            _blockAction = false;
            _blockedByEnemyContact = false;
            _blockingEnemy = null;

            if (warriorMeteorShieldHitbox != null)
                warriorMeteorShieldHitbox.enabled = false;

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();
            ForceCancelCurrentAttack();
            ForceStopSprint();
            DeactivateShield();

            CanMove = true;
            CanAttackWarrior = true;

            if (rigidbody2 != null)
            {
                rigidbody2.simulated = true;
                rigidbody2.linearVelocity = Vector2.zero;
                rigidbody2.angularVelocity = 0f;
                rigidbody2.gravityScale = normalGravityScale;
                rigidbody2.constraints = RigidbodyConstraints2D.FreezeRotation;
            }

            if (collider2 != null) collider2.enabled = true;
            if (shieldHitbox != null) shieldHitbox.enabled = false;

            _deathStarted = false;
            _deathFinalized = false;
            CanDie = false;

            _uiInputBlockUntil = -999f;

            // small post-respawn protection against immediate viewport death
            _allowViewportDeathTime = Time.time + 0.35f;

            if (animator != null)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            StopLowHealthBlink();
            ResetBloodCooldown();
            _cmp = 0;

            WaitAnimationDisplay();
        }

        private IEnumerator ReviveInvuln(float seconds)
        {
            _reviveInvulnerable = true;
            yield return new WaitForSeconds(seconds);
            _reviveInvulnerable = false;
            _reviveInvulnRoutine = null;
        }

        public void IgnoreEnemyCollisionTemporarily(Enemy enemy, float seconds)
        {
            if (enemy == null || seconds <= 0f) return;
            StartCoroutine(IgnoreEnemyCollisionRoutine(enemy, seconds));
        }

        private IEnumerator IgnoreEnemyCollisionRoutine(Enemy enemy, float seconds)
        {
            if (enemy == null) yield break;

            var warriorCols = GetComponentsInChildren<Collider2D>(true);
            var enemyCols = enemy.GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < warriorCols.Length; i++)
            {
                var wcol = warriorCols[i];
                if (wcol == null) continue;

                for (int j = 0; j < enemyCols.Length; j++)
                {
                    var ecol = enemyCols[j];
                    if (ecol == null) continue;

                    Physics2D.IgnoreCollision(wcol, ecol, true);
                }
            }

            yield return new WaitForSeconds(seconds);

            if (this == null || enemy == null) yield break;

            warriorCols = GetComponentsInChildren<Collider2D>(true);
            enemyCols = enemy.GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < warriorCols.Length; i++)
            {
                var wcol = warriorCols[i];
                if (wcol == null) continue;

                for (int j = 0; j < enemyCols.Length; j++)
                {
                    var ecol = enemyCols[j];
                    if (ecol == null) continue;

                    Physics2D.IgnoreCollision(wcol, ecol, false);
                }
            }
        }

        [Header("Out Of View Death")]
        [SerializeField] private bool killWhenBelowViewport = true;
        [SerializeField] private float viewportBottomMargin = 0.15f; // tolérance sous l'écran
        [SerializeField] private Camera deathCheckCamera;
        public void ForceDeath()
        {
            if (_deathStarted || CanDie) return;

            // si tu veux être sûr que la vie est à 0
            currentHealth = 0f;
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            StartDeath();
        }


        private bool _deathFinalized;
        private void CheckOutOfViewportDeath()
        {
            if (!killWhenBelowViewport) return;

            // prevent death right after spawn
            if (Time.time < _allowViewportDeathTime)
                return;

            if (_deathStarted || CanDie) return;

            if (GameMgr.Instance?.IsRestarting == true)
                return;

            Camera cam = deathCheckCamera != null ? deathCheckCamera : Camera.main;
            if (cam == null) return;

            Vector3 worldPos = collider2.bounds.min;
            Vector3 vp = cam.WorldToViewportPoint(worldPos);

            if (vp.z < 0f) return;

            if (vp.y < -viewportBottomMargin)
            {
                Debug.Log("[Warrior] Out of viewport (below screen) -> death");
                ForceDeath();
            }
        }

        private void CheckWorldYDeathFallback()
        {
            if (!useWorldYDeathFallback) return;
            if (Time.time < _allowViewportDeathTime) return;
            if (_deathStarted || CanDie) return;
            if (GameMgr.Instance?.IsRestarting == true) return;
            if (collider2 == null) return;

            if (collider2.bounds.max.y < worldDeathY)
            {
                Debug.Log("[Warrior] Fell below world death Y -> death");
                ForceDeath();
            }
        }

        #endregion
    }
}
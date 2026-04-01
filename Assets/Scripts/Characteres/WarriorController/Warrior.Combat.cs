using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Relics.Events;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {

        #region Hit Reaction

        [Header("Hit Reaction - Spark")]
        [SerializeField] private float sparkImpulse = 1.2f;
        [SerializeField] private float sparkUpBias = 0.15f;

        [SerializeField] private float hitReactMinInterval = 0.12f;
        private float _lastHitReactTime = -999f;
        private Coroutine _hitReactRoutine;

        #endregion

        #region Attack / FX / Damage

        public void AE_Attack1_HitExplosion_Fist() => DoAttack1HitExplosion(HitFxPoint.FistSocket);
        public void AE_Attack1_HitExplosion_Kick() => DoAttack1HitExplosion(HitFxPoint.KickSocket);
        public void AE_Attack1_HitExplosion() => DoAttack1HitExplosion(HitFxPoint.ContactOnEnemy);

        private void DoAttack1HitExplosion(HitFxPoint hitPoint)
        {
            if (attackMode != AttackAnimMode.Attack1) return;

            _attack1HitEventConsumed = true;

            Enemy[] enemiesInRange = GetEnemiesInAttackRange();

            bool anyHit = false;
            int valid = 0;
            Vector2 sumHitPoints = Vector2.zero;

            // If nobody in range at all => MISS
            if (enemiesInRange == null || enemiesInRange.Length == 0)
            {
                PlayAttack1MissSfx();
                return;
            }

            foreach (Enemy enemy in enemiesInRange)
            {
                if (enemy == null) continue;

                // facing filter
                if (leftFacing && enemy.NormalCollider.bounds.center.x > collider2.bounds.center.x) continue;
                if (rightFacing && enemy.NormalCollider.bounds.center.x < collider2.bounds.center.x) continue;

                anyHit = true;
                valid++;

                Vector3 hp3 = GetNovaPosition(enemy);
                sumHitPoints += new Vector2(hp3.x, hp3.y);

                Vector3 hitPos = GetHitFxPosition(enemy, hitPoint);
                SpawnHitExplosion(hitPos);
               
                float KnockBack = enemy switch
                {
                    M97Monster => 0.134f,
                    CrawlingMonster => 0.4f,
                    RakaMonster=> 0.2f,
                    _ => attack1KnockbackForce
                };
                int  damage = enemy switch
                {
                    M97Monster => 6,
                    CrawlingMonster => 8,
                    P39Monster_WithHealthBar => 7,
                    RakaMonster => 4,
                    ZalaytyMonster => 5,
                    HashagarMonster => 2,
                    _ => attack1Damage
                };  
                KnockbackEnemiesInRange(KnockBack, enemy, damage);
            }

            // HIT or MISS
            if (anyHit) PlayAttack1HitSfx();
            else PlayAttack1MissSfx();

            if (valid >= 2)
            {
                Vector2 avgHp = sumHitPoints / valid;
                GetComponent<Assets.Scripts.Scoring.SpectacularActionScorer>()
                    ?.NotifyCrowdHit(valid, avgHp);
            }
        }

        private Vector3 GetHitFxPosition(Enemy enemy, HitFxPoint point)
        {
            switch (point)
            {
                case HitFxPoint.FistSocket:
                    if (fistHitSocket != null)
                    {
                        Vector3 p = fistHitSocket.position;
                        p += (Vector3)fistFxOffset;
                        return p;
                    }
                    break;

                case HitFxPoint.KickSocket:
                    if (kickHitSocket != null)
                    {
                        Vector3 p = kickHitSocket.position;
                        p += (Vector3)kickFxOffset;
                        return p;
                    }
                    break;
            }

            return GetNovaPosition(enemy);
        }

        private void SpawnHitExplosion(Vector3 pos)
        {
            if (hitExplosionPrefab == null) return;

            GameObject fx = Instantiate(hitExplosionPrefab, pos, Quaternion.identity);
            fx.transform.SetPositionAndRotation(pos, Quaternion.identity);
            fx.transform.localScale = Vector3.one * hitExplosionScale;

            var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.useUnscaledTime = false;
                main.playOnAwake = false;
                main.simulationSpace = hitFxUseWorldSpace
                    ? ParticleSystemSimulationSpace.World
                    : ParticleSystemSimulationSpace.Local;

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }

            Destroy(fx, hitExplosionDestroyAfter);
        }

        public void SpawnSlashAt(float rotationZ)
        {
            if (Time.time - _lastSlashTime < SLASH_COOLDOWN) return;
            _lastSlashTime = Time.time;

            if (slashPrefab == null || twirl2Prefab == null)
            {
                Debug.LogError("Missing prefab references in SpawnSlashAt!");
                return;
            }

            if (_activeSlashEffects.Count >= MAX_SLASH_EFFECTS)
            {
                if (_activeSlashEffects[0] != null)
                    Destroy(_activeSlashEffects[0]);

                _activeSlashEffects.RemoveAt(0);
            }

            GameObject slash = Instantiate(
                slashPrefab,
                rigidbody2.position,
                rightFacing ? Quaternion.Euler(-90f, -90f, -90f) : Quaternion.Euler(-90f, 90f, -90f)
            );

            _activeSlashEffects.Add(slash);

            Vector3 warriorCenter = collider2.bounds.center;
            GameObject twirl = Instantiate(twirl2Prefab, warriorCenter, Quaternion.identity);

            if (!rightFacing)
            {
                var ps = twirl.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    if (renderer != null) renderer.flip = new Vector3(1, 0, 0);
                }
            }

            Destroy(twirl, 0.07f);

            Enemy[] enemiesInRange = GetEnemiesInAttackRange();
            if (enemiesInRange.Length > 0)
            {
                // Only play this sound when Attack2 hits at least one enemy
                //if (attackMode == AttackAnimMode.Attack2)
                //    PlayAttack2HitSfx();

                SpawnNovaForCollidingEnemies(enemiesInRange);

                int valid = 0;
                Vector2 sumHp = Vector2.zero;

                foreach (Enemy enemy in enemiesInRange)
                {
                    if (enemy == null) continue;
                    valid++;

                    Vector3 hp3 = GetNovaPosition(enemy);
                    sumHp += new Vector2(hp3.x, hp3.y);

                    KnockbackEnemiesInRange(0.35f, enemy, 10);
                }

                if (valid >= 2)
                {
                    Vector2 avgHp = sumHp / valid;
                    GetComponent<Assets.Scripts.Scoring.SpectacularActionScorer>()
                        ?.NotifyCrowdHit(valid, avgHp);
                }
            }

            StartCoroutine(TrackSlashPosition(slash));
        }

        private IEnumerator TrackSlashPosition(GameObject slash)
        {
            Vector3 startPos = slash.transform.position;
            yield return new WaitForSeconds(0.5f);

            if (slash == null) yield break;

            Destroy(slash);
            _activeSlashEffects.Remove(slash);
        }

        private void SpawnNovaForCollidingEnemies(IEnumerable<Enemy> enemies)
        {
            foreach (Enemy enemy in enemies)
            {
                if (enemy == null) continue;

                Vector3 novaPos = GetNovaPosition(enemy);
                GameObject nova = Instantiate(novaPrefab, novaPos, Quaternion.identity);
                Destroy(nova, 2f);
            }
        }

        private Vector3 GetNovaPosition(Enemy enemy)
        {
            Bounds warriorBounds = collider2.bounds;
            Bounds enemyBounds = enemy.NormalCollider.bounds;

            float contactX = warriorBounds.center.x < enemyBounds.center.x ? warriorBounds.max.x : warriorBounds.min.x;
            float contactY = Mathf.Min(warriorBounds.max.y, enemyBounds.max.y);

            return new Vector3(contactX, contactY, 0f);
        }

        public void KnockbackEnemiesInRange(float knockbackForce, Enemy enemy, int damage)
        {
            if (enemy == null) return;

            enemy.DisableAttackTemporarily();

            Vector2 knockbackDir = (enemy.transform.position - transform.position).normalized;

            enemy.StopMoveTowardCoroutine();
            enemy.stepBackDistance = enemy.ComputeStepBackDistance(knockbackForce);

            bool killed = enemy.TakeDamageAndReturnKilled(damage);

            // Play hit sfx for every damage dealt (Attack2 only)
            if (damage > 0 && attackMode == AttackAnimMode.Attack2)
                PlayAttack2HitSfx();

            if (_hub != null)
            {
                Vector3 hp = GetNovaPosition(enemy);
                _hub.RaiseHit(new HitEvent
                {
                    attacker = gameObject,
                    target = enemy.gameObject,
                    damage = damage,
                    hitPoint = new Vector2(hp.x, hp.y)
                });
            }

            StartCoroutine(knockbackDir.x > 0f ? enemy.SmoothStepBack(true) : enemy.SmoothStepBack(false));
        }

        private void CheckEnemiesLeavingRange()
        {
            Enemy[] current = GetEnemiesInAttackRange();
            var currentSet = new HashSet<Enemy>(current);

            if (currentSet.Count == 0 && _enemiesInRangeLastFrame.Count == 0)
                return;

            foreach (Enemy enemy in _enemiesInRangeLastFrame)
            {
                if (enemy == null) continue;
                if (!currentSet.Contains(enemy))
                    enemy.IsAttacked = false;
            }

            _enemiesInRangeLastFrame = currentSet;
        }

        public bool HasEnemyInAttackRange()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(GetAttackCenter(), attackRadius, enemyLayer);
            return hits.Length > 0;
        }

        private Vector2 GetAttackCenter()
        {
            Vector2 offset = new Vector2(
                rightFacing ? attackCenterOffset.x : -attackCenterOffset.x,
                attackCenterOffset.y
            );

            return (Vector2)transform.position + offset;
        }

        public Enemy[] GetEnemiesInAttackRange()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(GetAttackCenter(), attackRadius, enemyLayer);

            var enemies = new List<Enemy>(hits.Length);
            foreach (Collider2D hit in hits)
            {
                Enemy enemy = hit.GetComponent<Enemy>();
                if (enemy != null) enemies.Add(enemy);
            }

            return enemies.ToArray();
        }

        public bool IsFallingDueToGravity()
        {
            return rigidbody2.linearVelocity.y < 0 && Mathf.Abs(rigidbody2.linearVelocity.x) < 0.1f;
        }

        public void SpawnBloodshedEffectFromEnemy(Enemy enemy)
        {
            if (bloodshedPrefab == null || enemy == null) return;
            if (!CanSpawnBlood()) return;

            Vector3 pos = collider2.bounds.center;
            GameObject fx = Instantiate(bloodshedPrefab, pos, Quaternion.identity);
            fx.transform.SetParent(transform);

            Destroy(fx, 0.5f);
            _lastBloodSpawnTime = Time.time;
        }

        private bool CanSpawnBlood()
        {
            return Time.time >= _lastBloodSpawnTime + bloodEffectCooldown;
        }

        public void ResetBloodCooldown()
        {
            _lastBloodSpawnTime = -999f;
        }

        public void SetAttackMode(AttackAnimMode mode)
        {
            attackMode = mode;
        }

        private void PlayCurrentAttackAnimation()
        {
            if (attackMode == AttackAnimMode.Attack1)
            {
                AttackAnimationDisplay();
                return;
            }

            // Attack2
            //   PlayAttack2Sfx();          // play zoom/whoosh here
            AttackAnimation2Display();
        }
        // Animation Event on the LAST frame of Attack1 clip
        public void AE_EndAttack1()
        {
            if (animator != null)
                animator.SetBool("isAttacking", false);

            ExitAttackToBestState();
        }

        // Animation Event on the LAST frame of Attack2 clip (optional but recommended)
        public void AE_EndAttack2()
        {
            StopAttack2Sfx(); // guarantee end sync
            if (animator != null)
                animator.SetBool("isAttacking2", false);

            ExitAttackToBestState();
        }

        private void ExitAttackToBestState()
        {
            if (CanDie)
            {
                DeathAnimationDisplay();
                return;
            }

            // Air/jump/fall has priority
            bool airborne = (CountGroundPoints() == 0) || (activesJumpCoroutine != null) || IsFalling;
            if (airborne)
            {
                JumpAnimationDisplay();
                return;
            }

            // If currently moving, go run; otherwise wait
            bool moving = (activesMoveCoroutine != null) || Mathf.Abs(rigidbody2.linearVelocity.x) > 0.1f;
            if (moving) RunAnimationDisplay();
            else
                WaitAnimationDisplay();
        }
        //private bool IsAttack1CurrentlyPlaying()
        //{
        //    if (animator == null)
        //        return false;

        //    // Strongest signal: Attack1 bool still active
        //    if (HasBoolParam("isAttacking") && animator.GetBool("isAttacking"))
        //        return true;

        //    // If Attack2 bool is active, don't treat current attack state as Attack1
        //    bool a2Bool = HasBoolParam("isAttacking2") && animator.GetBool("isAttacking2");
        //    if (a2Bool)
        //        return false;

        //    // Fallback for transition frames (bools may lag one frame)
        //    var s = animator.GetCurrentAnimatorStateInfo(0);
        //    var n = animator.GetNextAnimatorStateInfo(0);
        //    var t = s.IsTag("Attack");
        //    var f = n.IsTag("Attack");


        //    return s.IsTag("Attack") || n.IsTag("Attack");
        //}

        #endregion

        #region UI Attack Entrypoints / Relic Attack2

        public void RequestPrimaryAttackFromUIButton()
        {
            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, 0.15f));
            if (CanDie) return;
            if (!CanMove || !CanAttackWarrior) return;
            if (activesJumpCoroutine != null || IsFalling || IsFallingGrazesEdge) return;

            if (animator != null && animator.GetBool("IsLosingCtrl"))
                animator.SetBool("IsLosingCtrl", false);

            StopMoveTowardCoroutine();

            // Core rule:
            // If relic Attack2 is still armed/active, use Attack2.
            if (IsRelicAttack2Active)
            {
                if (!_attack2CooldownStarted)
                {
                    if (!TryConsumeAttack2Cooldown())
                    {
                        if (_attack2ArmedByRelic)
                            RevertAttack2ToDefault();

                        if (!fallbackToAttack1IfAttack2OnCooldown)
                            return;

                        SetAttackMode(AttackAnimMode.Attack1);
                    }
                    else
                    {
                        _attack2CooldownStarted = true;

                        if (IsAnyAttackPlaying())
                            ForceCancelCurrentAttack();

                        SetAttackMode(AttackAnimMode.Attack2);
                    }
                }
                else
                {
                    if (IsAnyAttackPlaying())
                        ForceCancelCurrentAttack();

                    SetAttackMode(AttackAnimMode.Attack2);
                }
            }
            else
            {
                if (_attack2ArmedByRelic)
                    RevertAttack2ToDefault();

                if (!fallbackToAttack1IfAttack2OnCooldown)
                    return;

                SetAttackMode(AttackAnimMode.Attack1);
            }

            _attack1HitEventConsumed = false;
            GuardIdleAfterAttackRequest();
            PlayCurrentAttackAnimation();
        }

        /// <summary>
        /// Called by UI controls to prevent world touch handling for a short time.
        /// </summary>
        public void NotifyUIConsumedInput(float duration = -1f)
        {
            float d = duration > 0f ? duration : uiInputGuardDuration;
            _uiInputBlockUntil = Mathf.Max(_uiInputBlockUntil, Time.time + d);
        }

        private bool IsPointerOverUI()
        {
            if (EventSystem.current == null) return false;

#if UNITY_ANDROID || UNITY_IOS
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (EventSystem.current.IsPointerOverGameObject(t.fingerId))
                    return true;
            }
            return false;
#else
            return EventSystem.current.IsPointerOverGameObject();
#endif
        }

        /// <summary>
        /// Called by relic UI button.
        /// Applies cooldown, switches to Attack2 mode for cooldown window, optionally triggers now.
        /// </summary>
        public bool TryUseRelicAttack2(float duration, float cooldownOverride = -1f, bool triggerNow = false)
        {
            if (CanDie) return false;
            if (!CanMove || !CanAttackWarrior) return false;
            if (activesJumpCoroutine != null || IsFalling || IsFallingGrazesEdge) return false;

            // If already armed/active, ignore (optional behavior)
            if (_attack2ArmedByRelic)
                return false;

            // DO NOT consume cooldown here anymore.
            // We only arm Attack2 and will start cooldown on first attack button press.
            _attack2ArmedByRelic = true;
            _attack2CooldownStarted = false;
            _armedAttack2Duration = Mathf.Max(0.05f, duration);

            SetAttackMode(AttackAnimMode.Attack2);

            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, 0.20f));

            // Optional immediate trigger still works:
            // if triggerNow = true, the button-path logic below will consume cooldown there.
            if (triggerNow)
                RequestPrimaryAttackFromUIButton();

            return true;
        }

        private bool TryConsumeAttack2Cooldown(float cooldownOverride = -1f)
        {
            float cd = (cooldownOverride > 0f) ? cooldownOverride : attack2Cooldown;

            if (Time.time < _nextAttack2ReadyTime)
                return false;

            _nextAttack2ReadyTime = Time.time + cd;
            return true;
        }

        private void RefreshRelicAttack2State()
        {
            // Don't auto-revert before first actual attack has started the cooldown/window
            if (!_attack2ArmedByRelic) return;
            if (!_attack2CooldownStarted) return;

            if (Time.time >= _nextAttack2ReadyTime)
                RevertAttack2ToDefault();
        }

        private void RevertAttack2ToDefault()
        {
            SetAttackMode(AttackAnimMode.Attack1);
            _attack2ArmedByRelic = false;
            _attack2CooldownStarted = false;
            _attack2WindowStarted = false;
            _armedAttack2Duration = 0f;
        }

        private IEnumerator RevertAttackModeAfter(float t)
        {
            yield return new WaitForSeconds(t);
            RevertAttack2ToDefault();
        }
        #endregion

        #region spectacular action scoring

        // In Warrior
        public void ShowLosingBalance()
        {
            GetComponent<Assets.Scripts.Scoring.SpectacularActionScorer>()
                ?.NotifyLosingBalanceDisplayed();

            LosingBalanceAnimationDisplay();
        }

        public float LastJumpStartTime { get; private set; } = -999f;
        public int LastJumpStartFrame { get; private set; } = -999;

        public void MarkJumpStarted()
        {
            LastJumpStartTime = Time.time;
            LastJumpStartFrame = Time.frameCount;

            PlayJumpSfx(); // <-- NEW
        }
        #endregion
    }
}

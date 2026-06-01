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
        private bool _canMoveBeforeStoneRepulse;
        private bool _canAttackWarriorBeforeStoneRepulse;
        private bool _canAttackBeforeStoneRepulse;
        #region Attack / FX / Damage

        public void AE_Attack1_HitExplosion_Fist() => DoAttack1HitExplosion(HitFxPoint.FistSocket);
        public void AE_Attack1_HitExplosion_Kick()
        {
            // We moved the yeeahClip logic inside PlayAttack1HitSfx 
            // so it only plays if you actually hit an enemy.
            DoAttack1HitExplosion(HitFxPoint.KickSocket);
        }
        public void AE_Attack1_HitExplosion() => DoAttack1HitExplosion(HitFxPoint.ContactOnEnemy);

        private void DoAttack1HitExplosion(HitFxPoint hitPoint)
        {
            // 1. Safety check for the current attack mode
            if (attackMode != AttackAnimMode.Attack1) return;

            _attack1HitEventConsumed = true;

            // 2. Scan for enemies in the predefined radius
            Enemy[] enemiesInRange = GetEnemiesInAttackRange();

            bool anyHit = false;
            int valid = 0;
            Vector2 sumHitPoints = Vector2.zero;

            // 3. Handle MISS logic if no enemies are nearby
            if (enemiesInRange == null || enemiesInRange.Length == 0)
            {
                PlayAttack1MissSfx();
                return;
            }

            // 4. Process each enemy detected in range
            foreach (Enemy enemy in enemiesInRange)
            {
                if (enemy == null) continue;

                // Filter based on character facing direction
                if (leftFacing && enemy.NormalCollider.bounds.center.x > collider2.bounds.center.x) continue;
                if (rightFacing && enemy.NormalCollider.bounds.center.x < collider2.bounds.center.x) continue;

                anyHit = true;
                valid++;

                // Calculate visual feedback positions
                Vector3 hp3 = GetNovaPosition(enemy);
                sumHitPoints += new Vector2(hp3.x, hp3.y);

                Vector3 hitPos = GetHitFxPosition(enemy, hitPoint);
                SpawnHitExplosion(hitPos);

                // 5. Determine individual enemy knockback and damage values
                float KnockBack = enemy switch
                {
                    M97Monster => 0.134f,
                    CrawlingMonster => 0.4f,
                    RakaMonster => 0.2f,
                    _ => attack1KnockbackForce
                };

                int damage = enemy switch
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

            // 6. Final Sound Layering: Determine which SFX and Voice to overlay
            if (anyHit)
            {
                // Check if this specific hit event is the Kick (Frame 27)
                bool isKick = (hitPoint == HitFxPoint.KickSocket);

                // Pass the kick status to play the correct overlay (Hit SFX + "Yeeah")
                PlayAttack1HitSfx(isKick);
            }
            else
            {
                PlayAttack1MissSfx();
            }

            // 7. Scoring and Crowd Feedback for multi-hits
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
                    if (renderer != null)
                        renderer.flip = new Vector3(1, 0, 0);
                }
            }

            Destroy(twirl, 0.07f);

            Enemy[] enemiesInRange = GetEnemiesInAttackRange();
            if (enemiesInRange.Length > 0)
            {
                SpawnNovaForCollidingEnemies(enemiesInRange);

                int valid = 0;
                Vector2 sumHp = Vector2.zero;

                foreach (Enemy enemy in enemiesInRange)
                {
                    if (enemy == null) continue;
                    if (enemy.IsDeadOrDying) continue;

                    valid++;

                    Vector3 hp3 = GetNovaPosition(enemy);
                    sumHp += new Vector2(hp3.x, hp3.y);

                    // Zalayty is not allowed to go through the generic enemy stun/step-back path.
                    // That generic path can interrupt his independent platform movement and create a
                    // bad CurrentplatForm / ignored-collision state. Route him through the Zalayty-only guard.
                    if (TryApplyZalaytyWarriorSafeHit(
                            enemy,
                            WarriorZalaytyHitKind.GenericWarriorConstraint,
                            knockbackForce: 0.35f,
                            damage: 10,
                            stunSeconds: 0.3f,
                            applyStun: true))
                    {
                        continue;
                    }

                    // This is the important part.
                    // It blocks the enemy attack and calls OnStunStarted().
                    enemy.ApplyStun(0.3f);

                    // Damage + physical step back.
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

            WarriorZalaytyHitKind hitKind = attackMode == AttackAnimMode.Attack2
                ? WarriorZalaytyHitKind.Attack2
                : WarriorZalaytyHitKind.Attack1;

            if (TryApplyZalaytyWarriorSafeHit(
                    enemy,
                    hitKind,
                    knockbackForce,
                    damage,
                    stunSeconds: 0f,
                    applyStun: false))
            {
                return;
            }

            enemy.DisableAttackTemporarily();

            Vector2 knockbackDir = (enemy.transform.position - transform.position).normalized;

            enemy.StopMoveTowardCoroutine();
            enemy.stepBackDistance = enemy.ComputeStepBackDistance(knockbackForce);

            bool killed = enemy.TakeDamageAndReturnKilled(damage);

            NotifyEnemyHit(enemy, damage);

            StartCoroutine(knockbackDir.x > 0f ? enemy.SmoothStepBack(true) : enemy.SmoothStepBack(false));
        }

        private bool TryApplyZalaytyWarriorSafeHit(
            Enemy enemy,
            WarriorZalaytyHitKind kind,
            float knockbackForce,
            int damage,
            float stunSeconds,
            bool applyStun)
        {
            if (enemy is not ZalaytyMonster zalayty)
                return false;

            Vector2 direction = enemy.transform.position - transform.position;
            if (direction.sqrMagnitude < 0.0001f)
                direction = rightFacing ? Vector2.right : Vector2.left;
            else
                direction.Normalize();

            WarriorZalaytyHitContext context = new WarriorZalaytyHitContext
            {
                Kind = kind,
                RequestedDamage = damage,
                RequestedKnockback = knockbackForce,
                RequestedStunSeconds = stunSeconds,
                ApplyStun = applyStun,
                ForceDirection = direction,

                // The important safety choices:
                // - no Warrior-origin vertical force is allowed on Zalayty;
                // - no generic coroutine interruption is allowed while Zalayty is platform-supported;
                // - physical step-back is cancelled/reduced by Zalayty's own support snapshot.
                AllowVerticalForce = false,
                AllowCoroutineInterrupt = false,
                AllowPhysicalStepBackWhenSupported = false
            };

            bool killed = zalayty.ApplyWarriorSafeHit(this, context);

            NotifyEnemyHit(enemy, damage);
            return true;
        }

        private void NotifyEnemyHit(Enemy enemy, int damage)
        {
            if (enemy == null)
                return;

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

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null)
                    continue;

                Enemy enemy = hits[i].GetComponent<Enemy>() ?? hits[i].GetComponentInParent<Enemy>();

                if (enemy != null && !enemy.IsDeadOrDying)
                    return true;
            }

            return false;
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
                if (hit == null)
                    continue;

                Enemy enemy = hit.GetComponent<Enemy>() ?? hit.GetComponentInParent<Enemy>();

                if (enemy != null && !enemy.IsDeadOrDying && !enemies.Contains(enemy))
                {

                    enemies.Add(enemy);
                }
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

            if (attackMode == AttackAnimMode.Attack2)
            {
                DoAttack2Repultion();
                AttackAnimation2Display();
                return;
            }

            if (attackMode == AttackAnimMode.Attack3)
            {
                AttackAnimation3Display();
                return;
            }
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
            bool forceAttack2Counter = CanForceAttack2CounterWhileLocked();

            if (IsHardActionLocked && !forceAttack2Counter)
                return;

            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, 0.15f));

            if (_attack3Casting)
                return;

            if (CanDie) return;

            // Normal attacks still obey CanMove / CanAttackWarrior.
            // But armed Attack2 can be used as a counter against P39.
            if ((!CanMove || !CanAttackWarrior) && !forceAttack2Counter)
                return;

            if (activesJumpCoroutine != null || IsFalling || IsFallingGrazesEdge)
                return;

            if (forceAttack2Counter)
            {
                CanMove = true;
                CanAttackWarrior = true;
                CanAttack = true;
                _blockAction = false;

                StopMoveTowardCoroutine();
                StopJumpTowardCoroutine();

                if (rigidbody2 != null)
                {
                    Vector2 v = rigidbody2.linearVelocity;
                    v.x = 0f;
                    rigidbody2.linearVelocity = v;
                    rigidbody2.angularVelocity = 0f;
                }

                if (animator != null)
                {
                    animator.SetBool("IsLosingCtrl", false);
                    animator.SetBool("isRunning", false);
                    animator.SetBool("isWalking", false);
                }
            }

            if (animator != null && animator.GetBool("IsLosingCtrl"))
                animator.SetBool("IsLosingCtrl", false);

            StopMoveTowardCoroutine();

            if (IsRelicAttack2Active)
            {
                if (!_attack2CooldownStarted)
                {
                    if (!IsAttack2CooldownReady())
                    {
                        if (_attack2ArmedByRelic)
                            RevertAttack2ToDefault();

                        if (!fallbackToAttack1IfAttack2OnCooldown)
                            return;

                        SetAttackMode(AttackAnimMode.Attack1);
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

            // IMPORTANT:
            // Commit cooldown only after Attack2 animation was really requested.
            if (attackMode == AttackAnimMode.Attack2 && animator != null && animator.GetBool("isAttacking2"))
            {
                if (!_attack2CooldownStarted)
                {
                    _attack2CooldownStarted = true;
                    CommitAttack2Cooldown();
                }
            }

            _attack1HitEventConsumed = false;
            GuardIdleAfterAttackRequest();
            PlayCurrentAttackAnimation();
        }

        private void DoAttack2Repultion()
        {
            Enemy[] enemies = GetEnemiesInAttackRange();

            foreach (var enemy in enemies)
            {
                enemy.IsAttacked = true;
                // 5. Determine individual enemy knockback and damage values
                float KnockBack = enemy switch
                {
                    P39Monster_WithHealthBar => 1f,
                    M97Monster => 0.134f,
                    CrawlingMonster => 0.4f,
                    RakaMonster => 0.2f,
                    _ => attack1KnockbackForce
                };

                int damage = enemy switch
                {
                    M97Monster => 6,
                    CrawlingMonster => 8,
                    P39Monster_WithHealthBar => 3,
                    RakaMonster => 4,
                    ZalaytyMonster => 5,
                    HashagarMonster => 2,
                    _ => attack1Damage
                };
                if (TryApplyZalaytyWarriorSafeHit(
                        enemy,
                        WarriorZalaytyHitKind.Attack2,
                        knockbackForce: KnockBack,
                        damage: damage,
                        stunSeconds: KnockBack,
                        applyStun: true))
                {
                    continue;
                }

                enemy.ApplyStun(KnockBack);
                KnockbackEnemiesInRange(KnockBack, enemy, damage);


            }
        }

        private bool CanForceAttack2CounterWhileLocked()
        {
            if (!_attack2ArmedByRelic)
                return false;

            if (_attack2CooldownStarted)
                return false;

            if (CanDie || IsDeadOrDying)
                return false;

            if (_frozenByHivernox || _hivernoxHitLockRoutine != null)
                return false;

            if (activesJumpCoroutine != null || IsFalling || IsFallingGrazesEdge)
                return false;

            Enemy[] enemies = GetEnemiesInAttackRange();
            if (enemies == null || enemies.Length == 0)
                return false;

            for (int i = 0; i < enemies.Length; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null || enemy.IsDeadOrDying)
                    continue;

                if (enemy is P39Monster_WithHealthBar)
                    return true;
            }

            return false;
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
            if (IsHardActionLocked)
                return false;
            if (CanDie) return false;
            if (!CanMove || !CanAttackWarrior) return false;
            if (activesJumpCoroutine != null || IsFalling || IsFallingGrazesEdge) return false;

            // If already armed/active, ignore. The UI controller handles click-again cancel
            // before calling this method.
            if (_attack2ArmedByRelic)
                return false;

            // DO NOT consume cooldown here.
            // We only arm Attack2 and will start cooldown on first attack button press.
            _attack2ArmedByRelic = true;
            _attack2CooldownStarted = false;
            _armedAttack2Duration = Mathf.Max(0.05f, duration);

            SetAttackMode(AttackAnimMode.Attack2);

            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, 0.20f));

            // Optional immediate trigger still works for old setups, but the new relic UI
            // passes false so PowerCombo remains a reversible waiting stage.
            if (triggerNow)
                RequestPrimaryAttackFromUIButton();

            return true;
        }

        public bool DisarmPowerComboRelic()
        {
            // Cancellation is allowed only before the real Attack2 use starts.
            // After _attack2CooldownStarted becomes true, the relic action has already begun
            // and must not be refunded by the UI.
            if (!_attack2ArmedByRelic || _attack2CooldownStarted)
                return false;

            RevertAttack2ToDefault();
            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, 0.15f));
            return true;
        }

        private bool IsAttack2CooldownReady(float cooldownOverride = -1f)
        {
            return Time.time >= _nextAttack2ReadyTime;
        }

        private void CommitAttack2Cooldown(float cooldownOverride = -1f)
        {
            float cd = (cooldownOverride > 0f) ? cooldownOverride : attack2Cooldown;
            _nextAttack2ReadyTime = Time.time + cd;
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

        [Header("Losing Balance -> Physics Fall")]
        [SerializeField] private bool losingBalanceStartsPhysicsFall = true;

        [Tooltip("Delay after ShowLosingBalance before the Warrior is allowed to fall through the source platform by pure Rigidbody2D gravity.")]
        [SerializeField, Min(0f)] private float losingBalanceFallDelay = 0.75f;

        [Tooltip("Gravity used once the losing-balance delay has finished and the Warrior is still on the edge.")]
        [SerializeField, Min(0f)] private float losingBalanceFallGravityScale = 2.5f;

        [Tooltip("Small downward velocity injected only to start the physical fall. No vertical seating/snap is done.")]
        [SerializeField, Min(0f)] private float losingBalanceFallMinDownVelocity = 0.05f;

        private Coroutine _losingBalanceFallRoutine;

        // In Warrior
        public void ShowLosingBalance()
        {
            GetComponent<Assets.Scripts.Scoring.SpectacularActionScorer>()
                ?.NotifyLosingBalanceDisplayed();

            LosingBalanceAnimationDisplay();
            BeginLosingBalancePhysicsFallCountdown();
        }

        private void BeginLosingBalancePhysicsFallCountdown()
        {
            if (!losingBalanceStartsPhysicsFall)
                return;

            if (_deathStarted || CanDie || IsDeadOrDying || _frozenByHivernox)
                return;

            if (activesJumpCoroutine != null)
                return;

            if (CountGroundPoints() > 1)
                return;

            if (_losingBalanceFallRoutine != null)
                return;

            PlatFormColliderTrigger sourcePlatform = CurrentplatForm;
            _losingBalanceFallRoutine = StartCoroutine(LosingBalancePhysicsFallRoutine(sourcePlatform));
        }

        private IEnumerator LosingBalancePhysicsFallRoutine(PlatFormColliderTrigger sourcePlatform)
        {
            float timer = 0f;

            while (timer < losingBalanceFallDelay)
            {
                if (_deathStarted || CanDie || IsDeadOrDying || _frozenByHivernox)
                {
                    _losingBalanceFallRoutine = null;
                    yield break;
                }

                if (activesJumpCoroutine != null)
                {
                    _losingBalanceFallRoutine = null;
                    yield break;
                }

                // If Warrior recovered stable support during the losing-balance window,
                // do not force a fall. This preserves the existing safe landing behavior.
                if (CountGroundPoints() > 1)
                {
                    _losingBalanceFallRoutine = null;
                    yield break;
                }

                timer += Time.deltaTime;
                yield return null;
            }

            _losingBalanceFallRoutine = null;

            if (_deathStarted || CanDie || IsDeadOrDying || _frozenByHivernox)
                yield break;

            if (activesJumpCoroutine != null || CountGroundPoints() > 1)
                yield break;

            ForceSimplePhysicsFallAfterLosingBalance(sourcePlatform);
        }

        private void ForceSimplePhysicsFallAfterLosingBalance(PlatFormColliderTrigger sourcePlatform)
        {
            // Refresh the source at the last moment. The platform that matters here is
            // the platform currently supporting the edge, not a future destination.
            if (CurrentplatForm != null)
                sourcePlatform = CurrentplatForm;

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();

            IsFallingEdge = true;
            IsFallingGrazesEdge = true;
            IsFallingPlfExit = false;
            IsFallingHitEnemy = false;
            CanMove = false;
            _blockAction = false;

            // Do not snap or seat the Warrior. We only make the SOURCE platform
            // pass-through so Rigidbody2D gravity can naturally pull him down.
            if (sourcePlatform != null)
                sourcePlatform.ForceCharacterToFallThroughSourcePlatform(this);

            if (rigidbody2 != null)
            {
                rigidbody2.gravityScale = Mathf.Max(rigidbody2.gravityScale, losingBalanceFallGravityScale);

                RigidbodyConstraints2D constraints = rigidbody2.constraints;
                constraints &= ~RigidbodyConstraints2D.FreezePositionY;
                rigidbody2.constraints = constraints;

                Vector2 velocity = rigidbody2.linearVelocity;
                if (velocity.y > -losingBalanceFallMinDownVelocity)
                    velocity.y = -losingBalanceFallMinDownVelocity;

                rigidbody2.linearVelocity = velocity;
                rigidbody2.WakeUp();
            }

            JumpAnimationDisplay();
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

        #region Falling Stone Platform Repulse

        private Coroutine _platformStoneRepulseRoutine;

        [Header("Falling Stone Platform Repulse - Enemy Stop")]
        [Tooltip("ON = when the falling-stone platform repulse moves the Warrior into any Enemy, the repulse stops immediately.")]
        [SerializeField] private bool stopPlatformStoneRepulseOnEnemyContact = true;

        [Tooltip("Small overlap margin used while the repulse moves the Warrior by transform.position.")]
        [SerializeField, Min(0f)] private float platformStoneEnemyContactSkin = 0.03f;

        [Header("Enemy External Push / Edge Exit")]
        [Tooltip("ON = enemy push/repulse is allowed to send the Warrior past the platform edge instead of clamping him at the edge.")]
        [SerializeField] private bool enemyExternalPushCanForcePlatformExit = true;

        [Tooltip("Small X tolerance used when deciding that the Warrior has crossed a platform edge because of enemy push/repulse.")]
        [SerializeField, Min(0f)] private float enemyExternalPushEdgeTolerance = 0.02f;

        [Tooltip("Minimum horizontal velocity kept when enemy push/repulse becomes a natural fall past the edge.")]
        [SerializeField, Min(0f)] private float enemyExternalPushFallMinXVelocity = 2.5f;

        [Tooltip("Gravity used after enemy push/repulse sends the Warrior off the platform.")]
        [SerializeField, Min(0f)] private float enemyExternalPushFallGravityScale = 3f;

        [Tooltip("Safety timeout before restoring old platform collision if the Warrior never fully leaves the platform trigger.")]
        [SerializeField, Min(0f)] private float enemyExternalPushRestorePlatformCollisionTimeout = 2f;

        private bool _platformStoneRepulseStoppedByEnemyContact;
        private Enemy _platformStoneRepulseBlockingEnemy;

        public void TryRepulseFromPlatformStoneImpact(
            PlatFormColliderTrigger impactPlatform,
            Vector2 impactWorldPosition,
            float distance,
            float duration,
            float controlLockSeconds)
        {
            if (impactPlatform == null)
                return;

            if (_deathStarted || CanDie || IsDeadOrDying)
                return;

            if (CurrentplatForm != impactPlatform)
                return;

            // Warrior must be grounded. If airborne, jumping, or falling, no repulse.
            if (CountGroundPoints() <= 0)
                return;

            if (activesJumpCoroutine != null || IsJumping)
                return;

            if (IsFallingEdge || IsFallingPlfExit || IsFallingHitEnemy || IsFallingGrazesEdge)
                return;

            if (collider2 == null)
                return;

            _platformStoneRepulseStoppedByEnemyContact = false;
            _platformStoneRepulseBlockingEnemy = null;

            float repulseDirection = GetRepulseDirectionFromImpact(impactWorldPosition.x);

            if (_platformStoneRepulseRoutine != null)
            {
                StopCoroutine(_platformStoneRepulseRoutine);
                _platformStoneRepulseRoutine = null;

                // Important: prevents the old stopped coroutine from leaving the Warrior locked.
                EndPlatformStoneRepulseLock(false);
            }

            _platformStoneRepulseRoutine = StartCoroutine(
                PlatformStoneRepulseRoutine(
                    impactPlatform,
                    repulseDirection,
                    distance,
                    duration,
                    controlLockSeconds
                )
            );
        }

        private IEnumerator PlatformStoneRepulseRoutine(
            PlatFormColliderTrigger platform,
            float direction,
            float distance,
            float duration,
            float controlLockSeconds)
        {
            BeginPlatformStoneRepulseLock();

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();
            ForceCancelCurrentAttack();

            // Cancel Attack3 / Ice Ball if armed or casting.
            CancelPendingIceBallCast();

            IsFallingEdge = false;
            IsFallingPlfExit = false;
            IsFallingHitEnemy = false;
            IsFallingGrazesEdge = false;

            LosingBalanceAnimationDisplay();

            if (rigidbody2 != null)
            {
                rigidbody2.linearVelocity = Vector2.zero;
                rigidbody2.angularVelocity = 0f;
            }

            Vector3 start = transform.position;

            // IMPORTANT:
            // Do NOT clamp this target X to the platform bounds.
            // Enemy/external repulse must be allowed to push the Warrior past the edge.
            float targetX = start.x + direction * Mathf.Abs(distance);
            Vector3 end = new Vector3(targetX, start.y, start.z);

            float elapsed = 0f;
            duration = Mathf.Max(0.01f, duration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / duration);
                t = Mathf.SmoothStep(0f, 1f, t);

                transform.position = Vector3.Lerp(start, end, t);
                Physics2D.SyncTransforms();

                // Highest priority: if the external force sends the Warrior beyond the edge,
                // stop controlling the X position and let gravity/velocity continue the fall.
                if (TryBeginEnemyExternalPushFallIfNeeded(
                        platform,
                        direction,
                        _canAttackWarriorBeforeStoneRepulse,
                        _canAttackBeforeStoneRepulse))
                {
                    _platformStoneRepulseRoutine = null;
                    yield break;
                }

                // Still keep your requested behavior: if the repulse pushes Warrior into an enemy,
                // stop the repulse immediately.
                Enemy touchedEnemy = GetEnemyTouchedDuringPlatformStoneRepulse();
                if (touchedEnemy != null)
                {
                    StopPlatformStoneRepulseBecauseEnemyContact(touchedEnemy, false);
                    _platformStoneRepulseRoutine = null;
                    yield break;
                }

                yield return null;
            }

            transform.position = end;
            Physics2D.SyncTransforms();

            if (TryBeginEnemyExternalPushFallIfNeeded(
                    platform,
                    direction,
                    _canAttackWarriorBeforeStoneRepulse,
                    _canAttackBeforeStoneRepulse))
            {
                _platformStoneRepulseRoutine = null;
                yield break;
            }

            Enemy finalTouchedEnemy = GetEnemyTouchedDuringPlatformStoneRepulse();
            if (finalTouchedEnemy != null)
            {
                StopPlatformStoneRepulseBecauseEnemyContact(finalTouchedEnemy, false);
                _platformStoneRepulseRoutine = null;
                yield break;
            }

            float remainingLock = Mathf.Max(0f, controlLockSeconds - duration);
            if (remainingLock > 0f)
                yield return new WaitForSeconds(remainingLock);

            EndPlatformStoneRepulseLock(true);

            _platformStoneRepulseRoutine = null;
        }

        private void BeginPlatformStoneRepulseLock()
        {
            _canMoveBeforeStoneRepulse = CanMove;
            _canAttackWarriorBeforeStoneRepulse = CanAttackWarrior;
            _canAttackBeforeStoneRepulse = CanAttack;

            _platformStoneRepulseActive = true;
            _platformStoneRepulseStoppedByEnemyContact = false;
            _platformStoneRepulseBlockingEnemy = null;

            CanMove = false;
            CanAttackWarrior = false;
            CanAttack = false;

            _blockAction = true;

            _blockedByEnemyContact = false;
            _blockingEnemy = null;
        }

        public bool TryStopPlatformStoneRepulseOnEnemyContact(Enemy enemy)
        {
            if (!_platformStoneRepulseActive)
                return false;

            if (!stopPlatformStoneRepulseOnEnemyContact)
                return false;

            if (enemy == null)
                return false;

            StopPlatformStoneRepulseBecauseEnemyContact(enemy, true);
            return true;
        }

        private void StopPlatformStoneRepulseBecauseEnemyContact(Enemy enemy, bool stopRunningCoroutine)
        {
            if (!_platformStoneRepulseActive)
                return;

            _platformStoneRepulseStoppedByEnemyContact = true;
            _platformStoneRepulseBlockingEnemy = enemy;

            if (stopRunningCoroutine && _platformStoneRepulseRoutine != null)
            {
                StopCoroutine(_platformStoneRepulseRoutine);
                _platformStoneRepulseRoutine = null;
            }

            _platformStoneRepulseActive = false;
            _blockAction = false;

            _blockedByEnemyContact = true;
            _blockingEnemy = enemy;

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();

            if (rigidbody2 != null)
            {
                Vector2 v = rigidbody2.linearVelocity;
                v.x = 0f;
                rigidbody2.linearVelocity = v;
                rigidbody2.angularVelocity = 0f;
            }

            if (_deathStarted || CanDie || IsDeadOrDying)
                return;

            CanMove = true;
            CanAttackWarrior = _canAttackWarriorBeforeStoneRepulse;
            CanAttack = _canAttackBeforeStoneRepulse;

            if (animator != null)
            {
                if (animator.GetBool("IsLosingCtrl"))
                    animator.SetBool("IsLosingCtrl", false);

                if (CountGroundPoints() <= 1)
                    ShowLosingBalance();
                else
                    WaitAnimationDisplay();
            }

            Debug.Log($"[StoneRepulse STOPPED BY ENEMY] enemy={enemy.name}, CanMove={CanMove}, CanAttackWarrior={CanAttackWarrior}, CanAttack={CanAttack}");
        }

        private Enemy GetEnemyTouchedDuringPlatformStoneRepulse()
        {
            if (!stopPlatformStoneRepulseOnEnemyContact)
                return null;

            if (!_platformStoneRepulseActive)
                return null;

            if (collider2 == null)
                return null;

            Bounds b = collider2.bounds;
            b.Expand(platformStoneEnemyContactSkin);

            Collider2D[] hits = Physics2D.OverlapBoxAll(b.center, b.size, 0f);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                    continue;

                if (hit == collider2)
                    continue;

                // Use solid enemy colliders only. This avoids stopping on detection/attack trigger zones.
                if (hit.isTrigger)
                    continue;

                Enemy enemy = hit.GetComponentInParent<Enemy>();
                if (enemy == null)
                    continue;

                if (enemy.IsDeadOrDying)
                    continue;

                return enemy;
            }

            return null;
        }

        private void EndPlatformStoneRepulseLock(bool playIdleAnimation)
        {
            _platformStoneRepulseActive = false;
            _platformStoneRepulseStoppedByEnemyContact = false;
            _platformStoneRepulseBlockingEnemy = null;
            _blockAction = false;

            _blockedByEnemyContact = false;
            _blockingEnemy = null;

            if (_deathStarted || CanDie || IsDeadOrDying)
                return;

            StopMoveTowardCoroutine();

            if (rigidbody2 != null)
            {
                Vector2 v = rigidbody2.linearVelocity;
                v.x = 0f;
                rigidbody2.linearVelocity = v;
            }

            // Restore movement.
            CanMove = true;

            // Restore attack permission to what it was before repulse.
            // Important: do NOT force CanAttack = true.
            CanAttackWarrior = _canAttackWarriorBeforeStoneRepulse;
            CanAttack = _canAttackBeforeStoneRepulse;

            if (playIdleAnimation && animator != null)
            {
                if (animator.GetBool("IsLosingCtrl"))
                    animator.SetBool("IsLosingCtrl", false);

                WaitAnimationDisplay();
            }

            Debug.Log(
                $"[StoneRepulse END] CanMove={CanMove}, CanAttackWarrior={CanAttackWarrior}, CanAttack={CanAttack}, HardLock={IsHardActionLocked}"
            );
        }

        private bool TryBeginEnemyExternalPushFallIfNeeded(
            PlatFormColliderTrigger platform,
            float direction,
            bool restoreCanAttackWarrior,
            bool restoreCanAttack)
        {
            if (!enemyExternalPushCanForcePlatformExit)
                return false;

            if (platform == null || platform.platformCollider == null || collider2 == null)
                return false;

            direction = Mathf.Sign(direction);

            if (Mathf.Approximately(direction, 0f))
                direction = GetOppositeFacingDirectionX();

            Bounds platformBounds = platform.platformCollider.bounds;
            Bounds warriorBounds = collider2.bounds;

            float warriorCenterX = warriorBounds.center.x;

            bool passedRightEdge =
                direction > 0f &&
                warriorCenterX >= platformBounds.max.x - enemyExternalPushEdgeTolerance;

            bool passedLeftEdge =
                direction < 0f &&
                warriorCenterX <= platformBounds.min.x + enemyExternalPushEdgeTolerance;

            bool noGroundLeft = CountGroundPoints() <= 0;

            if (!passedRightEdge && !passedLeftEdge && !noGroundLeft)
                return false;

            BeginEnemyExternalPushFall(
                platform,
                direction,
                restoreCanAttackWarrior,
                restoreCanAttack
            );

            return true;
        }

        private void BeginEnemyExternalPushFall(
            PlatFormColliderTrigger platform,
            float direction,
            bool restoreCanAttackWarrior,
            bool restoreCanAttack)
        {
            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();

            _platformStoneRepulseActive = false;
            _platformStoneRepulseStoppedByEnemyContact = false;
            _platformStoneRepulseBlockingEnemy = null;

            _blockedByEnemyContact = false;
            _blockingEnemy = null;
            _blockAction = false;

            // Movement remains locked while falling. Your landing/platform code can restore it.
            CanMove = false;

            // Restore attack permissions so the Warrior does not stay permanently attack-locked
            // after the external push has turned into a fall.
            CanAttackWarrior = restoreCanAttackWarrior;
            CanAttack = restoreCanAttack;

            IsFallingEdge = true;
            IsFallingPlfExit = true;
            IsFallingHitEnemy = false;
            IsFallingGrazesEdge = false;

            if (platform != null)
                LastSafePlatform = platform;

            if (platform != null && platform.platformCollider != null && collider2 != null)
            {
                Bounds pb = platform.platformCollider.bounds;
                float yOffset = collider2.bounds.extents.y;

                LastSafePosition = new Vector3(
                    Mathf.Clamp(transform.position.x, pb.min.x, pb.max.x),
                    pb.max.y + yOffset + 0.05f,
                    transform.position.z
                );

                // Prevent the old platform from catching the Warrior immediately at the edge.
                IgnoreOldPlatformDuringExternalPushFall(platform);
            }

            if (rigidbody2 != null)
            {
                rigidbody2.gravityScale = enemyExternalPushFallGravityScale;
                rigidbody2.angularVelocity = 0f;

                Vector2 v = rigidbody2.linearVelocity;
                float wantedX = direction * enemyExternalPushFallMinXVelocity;

                if (Mathf.Abs(v.x) < Mathf.Abs(wantedX))
                    v.x = wantedX;

                if (v.y > -0.1f)
                    v.y = -0.1f;

                rigidbody2.linearVelocity = v;
            }

            JumpAnimationDisplay();

            Debug.Log("[EnemyExternalPush] Warrior was pushed past platform edge and is now falling naturally.");
        }

        private void IgnoreOldPlatformDuringExternalPushFall(PlatFormColliderTrigger platform)
        {
            if (platform == null || platform.platformCollider == null)
                return;

            Collider2D[] warriorColliders = GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < warriorColliders.Length; i++)
            {
                Collider2D col = warriorColliders[i];

                if (col == null)
                    continue;

                Physics2D.IgnoreCollision(platform.platformCollider, col, true);
            }

            StartCoroutine(RestoreOldPlatformCollisionWhenExternalPushIsClear(platform, warriorColliders));
        }

        private IEnumerator RestoreOldPlatformCollisionWhenExternalPushIsClear(
            PlatFormColliderTrigger platform,
            Collider2D[] warriorColliders)
        {
            float timeoutAt = Time.time + enemyExternalPushRestorePlatformCollisionTimeout;

            while (platform != null && platform.platformCollider != null)
            {
                bool stillInsidePlatformTrigger = false;

                if (platform.platformTrigger != null && warriorColliders != null)
                {
                    for (int i = 0; i < warriorColliders.Length; i++)
                    {
                        Collider2D col = warriorColliders[i];

                        if (col == null)
                            continue;

                        if (platform.platformTrigger.IsTouching(col))
                        {
                            stillInsidePlatformTrigger = true;
                            break;
                        }
                    }
                }

                if (!stillInsidePlatformTrigger)
                    break;

                if (Time.time >= timeoutAt)
                    break;

                yield return new WaitForFixedUpdate();
            }

            if (platform == null || platform.platformCollider == null || warriorColliders == null)
                yield break;

            for (int i = 0; i < warriorColliders.Length; i++)
            {
                Collider2D col = warriorColliders[i];

                if (col == null)
                    continue;

                Physics2D.IgnoreCollision(platform.platformCollider, col, false);
            }
        }

        private float GetRepulseDirectionFromImpact(float impactX)
        {
            float warriorX = collider2 != null
                ? collider2.bounds.center.x
                : transform.position.x;

            float diff = warriorX - impactX;

            // Stone hit left side of Warrior/platform area -> push Warrior right.
            if (diff > 0.05f)
                return 1f;

            // Stone hit right side -> push Warrior left.
            if (diff < -0.05f)
                return -1f;

            // If the stone hit almost exactly under the Warrior,
            // fallback to opposite facing direction.
            return GetOppositeFacingDirectionX();
        }

        private float GetOppositeFacingDirectionX()
        {
            if (Front != null && Back != null)
            {
                bool facingRight = Front.position.x > Back.position.x;
                bool facingLeft = Front.position.x < Back.position.x;

                if (facingRight)
                    return -1f;

                if (facingLeft)
                    return 1f;
            }

            return transform.localScale.x >= 0f ? -1f : 1f;
        }

        #endregion
        /// <summary>
        /// True from the instant the player's Attack2 button press is accepted
        /// (cooldown consumed, mode set to Attack2) through the entire 6-second window.
        /// Unlike IsAttack2CounterActive(), this does NOT require the animation to
        /// already be playing — it fires one full animation-event delay earlier.
        /// </summary>
        public bool IsAttack2CounterActive()
        {
            if (animator == null)
                return false;

            if (attackMode != AttackAnimMode.Attack2)
                return false;

            // ── Original animation-state check ──────────────────────────────────
            if (animator.GetBool("isAttacking2"))
                return true;

            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (current.IsTag("Attack") || next.IsTag("Attack"))
                return true;

            // ── NEW: cooldown window started = button press was accepted this window.
            // Catches the gap between PlayCurrentAttackAnimation() and the first
            // animation event frame, which is where P39 can still land a hit.
            if (_attack2ArmedByRelic && _attack2CooldownStarted && Time.time < _nextAttack2ReadyTime)
                return true;

            return false;
        }

    }
}
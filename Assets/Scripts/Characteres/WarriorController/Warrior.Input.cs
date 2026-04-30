using Assets.Scripts.Tools;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {

        #region Input / Movement / Idle

        private void HandleInput()
        {
            // Hivernox freeze/hit-lock must stop world input before IceBallRelic touch handling.
            if (IsHardActionLocked || _frozenByHivernox || !CanAttackWarrior)
                return;

            if (Time.time < _uiInputBlockUntil)
                return;
            if (blockWorldInputWhenPointerOverUI && IsPointerOverUI())
                return;
            if (!InputMgr.Instance.IsScreenTouched())
                return;

            // ICE BALL RELIC HAS PRIORITY OVER NORMAL WORLD TOUCH
            if (TryHandleArmedIceBallTouch())
                return;

            // IMPORTANT: must be before CanJump / CanAttack block
            if (activesJumpCoroutine == null
                && !IsFallingEdge
                && CountGroundPoints() >= 1
                && !IsFallingGrazesEdge
                && !CanDie
                && !CanMove
                && CanAttackWarrior
                && activesMoveCoroutine == null)
                CanMove = true; // rustine

            if (!CanMove || !CanAttackWarrior)
                return;

            if (activesJumpCoroutine != null || IsFallingEdge || CountGroundPoints() == 0 || IsFallingGrazesEdge || CanDie)
                return;

            SetDirectionVariables(InputMgr.Instance.TouchedVector.x);
            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();

            if (CanJump)
            {
                CheckIfAvoidCollider(true);

                MarkJumpStarted();

                JumpAnimationDisplay();
                activesJumpCoroutine = JumpTowardPositionAction(
                    new Vector2(GetJumpMaxX(), transform.position.y),
                    maxJump,
                    0.5f
                );
                StartCoroutine(activesJumpCoroutine);
                return;
            }

            bool shouldAttackFromWorldTouch =
       CanAttackWarrior &&
       CanAttack &&
       !_sprintArmed &&
       !_sprintActive &&
       HasEnemyInAttackRange();

            if (shouldAttackFromWorldTouch)
            {
                if (attackMode == AttackAnimMode.Attack2 && _attack2ArmedByRelic)
                    return;

                _attack1HitEventConsumed = false;
                GuardIdleAfterAttackRequest();
                PlayCurrentAttackAnimation();
                return;
            }

            if (!CanMove) return;

            if (animator.GetBool("IsLosingCtrl"))
                animator.SetBool("IsLosingCtrl", false);


            //Sprint relic uses only when movement starts
            TryStartArmedSprintFromMove();

            activesMoveCoroutine = MoveTowardPostionAction(InputMgr.Instance.TouchedVector.x);
            StartCoroutine(activesMoveCoroutine);

            int groundPoints = CountGroundPoints();
            float minMoveDistance = collider2.bounds.size.x * 0.2f;
            float clickDistance = Mathf.Abs(InputMgr.Instance.TouchedVector.x - transform.position.x);
            bool shouldRun = clickDistance > minMoveDistance;

            if (groundPoints <= 1)
            {
                bool clickedOnPlatform = false;

                if (CurrentplatForm != null && CurrentplatForm.platformCollider != null)
                {
                    var b = CurrentplatForm.platformCollider.bounds;
                    clickedOnPlatform =
                        InputMgr.Instance.TouchedVector.x > b.min.x &&
                        InputMgr.Instance.TouchedVector.x < b.max.x;
                }

                // NEW: if warrior is allowed to exit edge and click is outside safe range,
                // treat it as intentional run (not losing balance)
                bool wantsEdgeExit =
                    AllowEdgeExitWhenTargetOutside &&
                    !clickedOnPlatform &&
                    IsTargetOutsideCurrentPlatformSafeRange(InputMgr.Instance.TouchedVector.x);

                if (clickedOnPlatform || wantsEdgeExit)
                {
                    if (shouldRun) RunAnimationDisplay();
                    else
                        WaitAnimationDisplay();
                }
                else
                {
                    // Only show losing balance if truly not moving
                    bool movingNow = activesMoveCoroutine != null ||
                                     (rigidbody2 != null && Mathf.Abs(rigidbody2.linearVelocity.x) > 0.05f);

                    if (!movingNow)
                        ShowLosingBalance();
                }

                return;
            }

            if (shouldRun) RunAnimationDisplay();
        }

        private void HandleFallingAndDeath()
        {
            if (IsFalling)
            {
                rigidbody2.gravityScale = 3f;
                StopMoveTowardCoroutine();
                JumpAnimationDisplay();
            }

            if (_deathStarted || CanDie)
            {
                StopMoveTowardCoroutine();
                DeathAnimationDisplay();
                return;
            }
        }

        private void CheckIfStopRunDisplay()
        {
            if (Time.time < _idleGuardUntil) return;
            if (activesJumpCoroutine != null) return;
            if (activesMoveCoroutine != null) return;
            if (CountGroundPoints() <= 1) return;
            if (IsFalling) return;
            if (IsAnyAttackPlaying()) return;

            WaitAnimationDisplay();
        }

        private void GuardIdleAfterAttackRequest()
        {
            _idleGuardUntil = Time.time + idleGuardAfterAttackRequest;
        }

        private bool HasBoolParam(string paramName)
        {
            if (animator == null) return false;

            foreach (var p in animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Bool && p.name == paramName)
                    return true;
            }

            return false;
        }

        private bool IsAnyAttackPlaying()
        {
            if (animator == null) return false;

            bool a1 = HasBoolParam("isAttacking") && animator.GetBool("isAttacking");
            bool a2 = HasBoolParam("isAttacking2") && animator.GetBool("isAttacking2");
            bool a3 = HasBoolParam("isAttacking3") && animator.GetBool("isAttacking3");

            if (a1 || a2 || a3)
                return true;

            var s = animator.GetCurrentAnimatorStateInfo(0);
            var n = animator.GetNextAnimatorStateInfo(0);
            return s.IsTag("Attack") || n.IsTag("Attack");
        }
        private void UpdateEcho()
        {
            if (_echoGenerator == null) return;

            if (CanDie)
            {
                _echoGenerator.SetEchoActive(false);
                return;
            }

            // Sprint has top priority
            if (_sprintActive)
            {
                _echoGenerator.SetEchoActive(true);
                _echoGenerator.SetEchoColor(sprintEchoColor);
                return;
            }

            if (IsFalling || activesJumpCoroutine != null)
            {
                _echoGenerator.SetEchoActive(true);
                _echoGenerator.SetEchoColor(jumpEchoColor);
                return;
            }

            bool grounded = CountGroundPoints() > 0;
            bool runningByCoroutine = activesMoveCoroutine != null && grounded;
            bool runningByVelocity = Mathf.Abs(rigidbody2.linearVelocity.x) > 0.5f && grounded;

            if (runningByCoroutine || runningByVelocity)
            {
                _echoGenerator.SetEchoActive(true);
                _echoGenerator.SetEchoColor(runEchoColor);
            }
            else
            {
                _echoGenerator.SetEchoActive(false);
            }
        }

        bool IsAirborne() => CountGroundPoints() == 0 && activesJumpCoroutine != null;

        public int CountGroundPoints()
        {
            if (PlatformLayer.value == 0)
            {
                Debug.LogWarning($"{name}: PlatformLayer is empty in CountGroundPoints().");
                return 0;
            }

            int count = 0;
            foreach (Transform pt in GroundPoints)
            {
                if (Physics2D.OverlapCircleAll(pt.position, Groundradius, PlatformLayer).Length > 0)
                    count++;
            }
            return count;
        }

        private float GetJumpMaxX()
        {
            float x = InputMgr.Instance.TouchedVector.x;
            float maxDistance = collider2.bounds.size.x * 2f;

            if (x.GetDistanceXAxis(transform.position.x) > maxDistance)
            {
                if (GoLeft) x = transform.position.x - maxDistance;
                if (GoRight) x = transform.position.x + maxDistance;
            }

            return x;
        }

        public void ForceCancelCurrentAttack()
        {
            ForceCancelCurrentAttackInternal(restoreMovementAfterAttack3Cancel: true);
        }

        private void ForceCancelCurrentAttackForExternalLock()
        {
            ForceCancelCurrentAttackInternal(restoreMovementAfterAttack3Cancel: false);
        }

        private void ForceCancelCurrentAttackInternal(bool restoreMovementAfterAttack3Cancel)
        {
            StopAttack2Sfx();

            // Important fix:
            // If Hivernox freezes Warrior during Attack3, the Attack3 animation event
            // AE_EndAttack3 may never run. So we clean the Attack3 body/VFX/state here.
            if (HasAnyIceBallCastState())
            {
                if (restoreMovementAfterAttack3Cancel)
                    CancelPendingIceBallCast();
                else
                    CancelIceBallCastForExternalLock();
            }

            if (animator != null)
            {
                if (HasBoolParam("isAttacking") && animator.GetBool("isAttacking"))
                    animator.SetBool("isAttacking", false);

                if (HasBoolParam("isAttacking2") && animator.GetBool("isAttacking2"))
                    animator.SetBool("isAttacking2", false);

                if (HasBoolParam("isAttacking3") && animator.GetBool("isAttacking3"))
                    animator.SetBool("isAttacking3", false);

                if (HasBoolParam("IsLosingCtrl") && animator.GetBool("IsLosingCtrl"))
                    animator.SetBool("IsLosingCtrl", false);
            }

            _attack1HitEventConsumed = true;
        }

        #endregion
    }
}

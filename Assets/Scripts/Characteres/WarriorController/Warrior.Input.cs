using Assets.Scripts.Tools;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {

        #region Input / Movement / Idle

        private void HandleInput()
        {
            // Direct-control (joystick) mode fully bypasses tap-to-navigate.
            // DirectControlHud drives the Warrior through DirectMove/DirectJump instead.
            if (ControlScheme.IsDirect)
                return;

            // Hivernox freeze/hit-lock must stop world input before IceBallRelic touch handling.
            if (IsHardActionLocked || _frozenByHivernox || !CanAttackWarrior)
            {
                NoteSqueezeJumpRefusedIfWindowOpen(
                    "entree tap coupee: verrouDur=" + IsHardActionLocked +
                    ", gele=" + _frozenByHivernox +
                    ", CanAttackWarrior=" + CanAttackWarrior);

                if (InputMgr.Instance != null && InputMgr.Instance.IsScreenTouched())
                    NoteTapOutcome("IGNORE en entree: verrouDur=" + IsHardActionLocked +
                                   ", gele=" + _frozenByHivernox +
                                   ", CanAttackWarrior=" + CanAttackWarrior);

                return;
            }

            if (Time.time < _uiInputBlockUntil)
            {
                if (InputMgr.Instance != null && InputMgr.Instance.IsScreenTouched())
                    NoteTapOutcome("IGNORE: blocage d'entree apres une UI");

                return;
            }

            if (blockWorldInputWhenPointerOverUI && IsPointerOverUI())
            {
                if (InputMgr.Instance != null && InputMgr.Instance.IsScreenTouched())
                    NoteTapOutcome("IGNORE: pointeur au-dessus de l'interface");

                return;
            }

            if (!InputMgr.Instance.IsScreenTouched())
                return;

            // ICE BALL RELIC HAS PRIORITY OVER NORMAL WORLD TOUCH
            if (TryHandleArmedIceBallTouch())
            {
                NoteTapOutcome("consomme par la relique de boule de glace");
                return;
            }

            // IMPORTANT: must be before CanJump / CanAttack block.
            // Rustine: only restore CanMove when warrior is genuinely standing on a
            // platform surface, not just touching a collider from the side/below.
            if (activesJumpCoroutine == null
                && !IsFallingEdge
                && CountGroundPoints() >= 1
                && !IsFallingGrazesEdge
                && !CanDie
                && !CanMove
                && CanAttackWarrior
                && activesMoveCoroutine == null
                && IsStandingOnPlatformSurface())
                CanMove = true; // rustine

            // Ecrasement lateral: meme sursis que sur le chemin joystick. Sans point de sol le
            // saut serait refuse alors que c'est la sortie offerte au joueur.
            bool squeezeEscapeJump = IsSqueezeEscapeJumpAllowed;

            // Meme regle que le chemin joystick: un support confirme sous les pieds rend le saut
            // independant des points de sol et de CanMove.
            bool supportedJump = HasConfirmedSupportForJump;

            if ((!CanMove && !squeezeEscapeJump && !supportedJump) || !CanAttackWarrior)
            {
                NoteSqueezeJumpRefusedIfWindowOpen(
                    "CanMove=" + CanMove + ", CanAttackWarrior=" + CanAttackWarrior);

                NoteTapOutcome("IGNORE: CanMove=" + CanMove +
                               ", support=" + HasConfirmedSupportForJump +
                               ", CanAttackWarrior=" + CanAttackWarrior);

                return;
            }

            if (activesJumpCoroutine != null || CanDie)
            {
                NoteSqueezeJumpRefusedIfWindowOpen(
                    "sautDejaEnCours=" + (activesJumpCoroutine != null) + ", CanDie=" + CanDie);

                NoteTapOutcome("IGNORE: sautDejaEnCours=" + (activesJumpCoroutine != null) +
                               ", CanDie=" + CanDie);

                return;
            }

            if (!squeezeEscapeJump && !supportedJump &&
                (IsFallingEdge || CountGroundPoints() == 0 || IsFallingGrazesEdge))
            {
                NoteTapOutcome("IGNORE: chuteBord=" + IsFallingEdge +
                               ", pointsSol=" + CountGroundPoints() +
                               ", support=" + HasConfirmedSupportForJump +
                               ", frolementBord=" + IsFallingGrazesEdge);

                return;
            }

            if (supportedJump && CountGroundPoints() == 0 && IsTapAimedAtJump())
                NoteJumpGrantedByMovingPlatformSupport("tap");

            SetDirectionVariables(InputMgr.Instance.TouchedVector.x);
            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();

            NoteSqueezeTapDuringWindow(CanJump, InputMgr.Instance.TouchedVector.y);

            if (CanJump)
                NoteTapOutcome("-> SAUT");

            if (CanJump)
            {
                CheckIfAvoidCollider(true);

                MarkJumpStarted();

                JumpAnimationDisplay();

                if (squeezeEscapeJump)
                    NotifySqueezeEscapeJumpStarted();

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

            if (!shouldAttackFromWorldTouch)
                NoteTapOutcome("appui sans saut ni attaque -> tentative de deplacement" +
                               " | CanMove=" + CanMove +
                               " aligne=" + IsWarriorBodyBottomAlignedWithCurrentPlatformTop() +
                               " | porteurReel=" + DescribePlatformUnderFeet());

            if (shouldAttackFromWorldTouch)
            {
                NoteTapOutcome("-> ATTAQUE");

                if (attackMode == AttackAnimMode.Attack2 && _attack2ArmedByRelic)
                    return;

                _attack1HitEventConsumed = false;
                GuardIdleAfterAttackRequest();
                PlayCurrentAttackAnimation();
                return;
            }

            if (!CanMove)
            {
                NoteTapOutcome("DEPLACEMENT REFUSE: CanMove=false | porteurReel=" + DescribePlatformUnderFeet());
                return;
            }

            // Warrior-only movement authorization:
            // do not start MoveTowardPostionAction unless the Warrior body bottom
            // is aligned with the current platform's solid platformCollider top.
            if (!IsWarriorBodyBottomAlignedWithCurrentPlatformTop())
            {
                float ecart = (CurrentplatForm != null && CurrentplatForm.platformCollider != null && collider2 != null)
                    ? collider2.bounds.min.y - CurrentplatForm.platformCollider.bounds.max.y
                    : float.NaN;

                NoteTapOutcome("DEPLACEMENT REFUSE: corps non aligne (ecart=" + ecart.ToString("F3") +
                               ", tolerance 0,060) | porteurReel=" + DescribePlatformUnderFeet());
                return;
            }

            if (animator.GetBool("IsLosingCtrl"))
                animator.SetBool("IsLosingCtrl", false);


            //Sprint relic uses only when movement starts
            TryStartArmedSprintFromMove();

            NoteTapOutcome("-> DEPLACEMENT lance vers x=" +
                           InputMgr.Instance.TouchedVector.x.ToString("F2"));

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

        //private void HandleFallingAndDeath()
        //{
        //    if (IsFalling)
        //    {
        //        rigidbody2.gravityScale = 3f;
        //        StopMoveTowardCoroutine();
        //        JumpAnimationDisplay();
        //    }

        //    if (_deathStarted || CanDie)
        //    {
        //        StopMoveTowardCoroutine();
        //        DeathAnimationDisplay();
        //        return;
        //    }
        //}

        private void HandleFallingAndDeath()
        {
            // Death must always win over jump/fall animation.
            if (_deathStarted || CanDie)
            {
                StopMoveTowardCoroutine();
                DeathAnimationDisplay();
                return;
            }

            int groundPoints = CountGroundPoints();

            // A warrior carried by a moving vertical lift is genuinely grounded even when
            // CountGroundPoints() momentarily reads 0: while the lift descends, his foot
            // ground points float a hair above the lift top (riderSurfaceOffset) and the
            // platform keeps sliding out from under them each FixedUpdate. Without this, the
            // descent leg would get stuck replaying JumpAnimationDisplay() and StopMoveToward-
            // Coroutine() every frame, blocking all interaction while the platform goes down.
            bool carriedOnMovingLift =
                activesJumpCoroutine == null &&
                CurrentplatForm is Assets.Scripts.Platforms.MovingVerticalPlatform lift &&
                lift.IsCarrying(this);

            bool noGroundPoint = groundPoints == 0 && !carriedOnMovingLift;

            // Do not break Attack1/Attack2/Attack3.
            // If an attack ends while airborne, ExitAttackToBestState() already sends him to jump.
            if (IsAnyAttackPlaying())
                return;

            // Do not break Hivernox freeze / hard lock visuals.
            if (IsHardActionLocked || _frozenByHivernox)
                return;

            bool shouldDisplayJump =
                IsFalling ||
                IsFallingEdge ||
                IsFallingGrazesEdge ||
                noGroundPoint ||
                activesJumpCoroutine != null;

            if (!shouldDisplayJump)
                return;

            if (rigidbody2 != null)
                rigidbody2.gravityScale = Mathf.Max(rigidbody2.gravityScale, 3f);

            // If ground points are zero, movement coroutine must not keep RunAnimationDisplay alive.
            if (noGroundPoint || IsFalling || IsFallingEdge || IsFallingGrazesEdge)
                StopMoveTowardCoroutine();

            JumpAnimationDisplay();
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

        /// <summary>
        /// Returns true only when the warrior's feet (collider2 bottom) are at or above
        /// the current platform's top surface (platformCollider.bounds.max.y), within a
        /// small tolerance.  Falls back to CountGroundPoints >= 1 when no CurrentplatForm
        /// is available, so landing paths driven by PlatFormColliderTrigger / predicted
        /// anti-tunnel are never blocked.
        /// </summary>
        private bool IsStandingOnPlatformSurface()
        {
            // No platform reference → trust the ground-point check (e.g. very first frame,
            // or after a landing that hasn't set CurrentplatForm yet).
            if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
                return CountGroundPoints() >= 1;

            if (collider2 == null)
                return CountGroundPoints() >= 1;

            float warriorBottom = collider2.bounds.min.y;
            float platformTop = CurrentplatForm.platformCollider.bounds.max.y;

            // Allow a tolerance equal to the platform's edgeRadius (0.02 f set in Start())
            // plus a small physics skin so micro-bounces don't flicker the state.
            const float tolerance = 0.06f;

            return warriorBottom >= platformTop - tolerance;
        }

        protected override bool CanContinueMoveTowardPositionAction()
        {
            return IsWarriorBodyBottomAlignedWithCurrentPlatformTop();
        }

        [Header("Autorisation de deplacement - alignement sur la plateforme")]
        [Tooltip("Enfoncement maximal sous le sommet de la plateforme encore considere comme 'pose'. Le Warrior repose naturellement SOUS le sommet (depenetration du solveur + edgeRadius du collider): mesure sur plf-blk_1 (6), il stationne a 0,060 exactement, soit la valeur de l'ancienne tolerance unique, ce qui refusait tout deplacement en boucle.")]
        [SerializeField, Min(0.06f)] private float platformAlignSinkTolerance = 0.30f;

        [Tooltip("Hauteur maximale au-dessus du sommet encore consideree comme 'pose'. Au-dela il est en l'air et le deplacement doit rester refuse.")]
        [SerializeField, Min(0.02f)] private float platformAlignFloatTolerance = 0.12f;

        /// <summary>
        /// Le Warrior est-il reellement pose sur sa plateforme courante ? Garde-fou d'origine
        /// contre un deplacement lance alors qu'il n'est pas assis dessus (accroche au flanc,
        /// suspendu au-dessus apres un mauvais atterrissage).
        ///
        /// Deux corrections apres mesure en jeu:
        ///   1. Deux points de sol prouvent qu'il est pose. Aucune comparaison de hauteur ne peut
        ///      etre plus fiable que ca.
        ///   2. La tolerance n'est plus symetrique ni serree a 0,06. Il repose naturellement SOUS
        ///      le sommet, et il stationnait a -0,060 pile: la comparaison basculait sur le bruit
        ///      du flottant et refusait tout deplacement, en boucle, alors que CanMove etait vrai,
        ///      la plateforme la bonne et les deux pieds au sol.
        /// </summary>
        private bool IsWarriorBodyBottomAlignedWithCurrentPlatformTop()
        {
            if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
                return false;

            if (collider2 == null)
                return false;

            if (CountGroundPoints() >= 2)
                return true;

            float delta = collider2.bounds.min.y - CurrentplatForm.platformCollider.bounds.max.y;

            return delta <= platformAlignFloatTolerance && delta >= -platformAlignSinkTolerance;
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
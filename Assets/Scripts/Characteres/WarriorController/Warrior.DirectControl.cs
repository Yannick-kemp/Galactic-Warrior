using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    // ─────────────────────────────────────────────────────────────────────────
    // PROTOTYPE — "CoD Mobile style" direct control (joystick + buttons).
    //
    // This file is PURELY ADDITIVE. It does NOT touch the tap-to-navigate path
    // (HandleInput). It exposes 3 public entry points that a virtual joystick /
    // action buttons can call, and internally REUSES the already-tuned movement
    // and jump coroutines (MoveTowardPostionAction / JumpTowardPositionAction) so
    // platform clamp, edge-exit, anti-tunnel, moving-platform surface follow all
    // keep working for free.
    //
    // The approach here is deliberately the cheap "hybrid" (a held joystick is
    // translated into a far target fed to the existing target-based mover) — this
    // is the fastest way to JUDGE THE FEEL. If the feel is validated, step 2 will
    // replace this with a genuine per-frame velocity mover.
    //
    // To rip the whole prototype out: delete this file + DirectControlPrototype.cs.
    // ─────────────────────────────────────────────────────────────────────────
    public partial class Warrior : CharacterController
    {
        // Sign of the direction the direct mover is currently driving (-1,0,+1).
        private int _directMoveSign;

        // Far distance used as the "target" for the reused target-based mover.
        private const float DirectMoveFarDistance = 1000f;

        /// <summary>
        /// True while direct control (joystick) driving is desired. Set by the
        /// prototype driver. Only used to stop cleftover coroutines when toggled off.
        /// </summary>
        public bool DirectControlActive { get; private set; }

        public void SetDirectControlActive(bool active)
        {
            DirectControlActive = active;
            if (!active)
                DirectStop();
        }

        /// <summary>
        /// Drive continuous horizontal movement in the held direction.
        /// dirX: negative = left, positive = right. Magnitude is ignored (constant
        /// walk speed) — only the sign matters for this prototype.
        /// Mirrors the guard sequence used by the tap HandleInput move branch.
        /// </summary>
        public void DirectMove(float dirX)
        {
            if (IsHardActionLocked || _frozenByHivernox || !CanAttackWarrior)
            {
                DirectStop();
                return;
            }

            int sign = dirX > 0.001f ? 1 : (dirX < -0.001f ? -1 : 0);
            if (sign == 0)
            {
                DirectStop();
                return;
            }

            // Same "rustine" as HandleInput: restore CanMove when genuinely standing
            // on a surface, so a landing re-enables walking.
            if (activesJumpCoroutine == null
                && !IsFallingEdge
                && CountGroundPoints() >= 1
                && !IsFallingGrazesEdge
                && !CanDie
                && !CanMove
                && CanAttackWarrior
                && activesMoveCoroutine == null
                && IsStandingOnPlatformSurface())
                CanMove = true;

            if (!CanMove || !CanAttackWarrior)
                return;

            // Cannot start/continue a ground walk while airborne or falling.
            if (activesJumpCoroutine != null || IsFallingEdge || CountGroundPoints() == 0
                || IsFallingGrazesEdge || CanDie)
                return;

            // No movement while an attack animation is playing (tap parity: an attack stops
            // movement). The attack itself already called StopMoveTowardCoroutine; here we
            // simply refuse to re-issue a walk until the attack clip finishes.
            if (IsAnyAttackPlaying())
            {
                _directMoveSign = 0;
                return;
            }

            // Enemy-contact stop (tap parity): while body-blocked by an enemy, pushing
            // TOWARD it must not keep shoving into it. Pushing the OPPOSITE way is allowed
            // so the Warrior can disengage (the block clears on OnCollisionExit2D).
            if (_blockedByEnemyContact && _blockingEnemy != null)
            {
                float enemyX = _blockingEnemy.NormalCollider != null
                    ? _blockingEnemy.NormalCollider.bounds.center.x
                    : _blockingEnemy.transform.position.x;

                bool pushingIntoEnemy = (int)Mathf.Sign(enemyX - transform.position.x) == sign;
                if (pushingIntoEnemy)
                {
                    DirectStop();
                    return;
                }
            }

            // EXACT tap-parity at platform edges — NO custom edge logic here. Edge handling is
            // owned entirely by the shared platform trigger (PlatFormPlfColliderTrigger.
            // OnCollisionStay2D):
            //   • While the Warrior is actively moving (activesMoveCoroutine != null) and reaches
            //     the edge (c==1), the trigger keeps him going → he walks off (c==0) → falls
            //     normally. So holding the joystick toward the void = keep running and fall.
            //   • Only when he STOPS at the edge (joystick released → DirectStop nulls the
            //     coroutine, c==1) does the trigger call ShowLosingBalance() + start its
            //     platform-specific fall timer. So teeter ⟺ he stopped at the edge.
            // We therefore just feed a far target (like tap's edge-exit) and let physics run.
            float targetX = transform.position.x + sign * DirectMoveFarDistance;
            SetDirectionVariables(targetX);

            if (animator != null && animator.GetBool("IsLosingCtrl"))
                animator.SetBool("IsLosingCtrl", false);

            // (Re)issue the reused target-based mover only on a direction change or when
            // no walk is active. Starting a fresh walk keeps the same seated-on-platform
            // safety gate as the tap path, but a walk already in progress is never blocked.
            bool needNewMove = sign != _directMoveSign || activesMoveCoroutine == null;
            if (needNewMove && IsWarriorBodyBottomAlignedWithCurrentPlatformTop())
            {
                StopMoveTowardCoroutine();
                _directMoveSign = sign;
                activesMoveCoroutine = MoveTowardPostionAction(targetX);
                StartCoroutine(activesMoveCoroutine);
            }

            // Drive the RUN animation whenever a ground walk is actually active. This is
            // deliberately DECOUPLED from the alignment gate above — otherwise the warrior
            // slides while stuck on the idle pose. CheckIfStopRunDisplay is bypassed in
            // direct mode (see Warrior.Update), so DirectMove/DirectStop own the anim.
            if (activesMoveCoroutine != null && CountGroundPoints() >= 1 && !IsAnyAttackPlaying())
            {
                // Sprint relic parity: tap starts an armed sprint when movement begins.
                // Idempotent — only does work on the frame it actually starts (guarded by
                // _sprintArmed / _sprintActive / cooldown inside).
                TryStartArmedSprintFromMove();
                RunAnimationDisplay();
            }
        }

        /// <summary>
        /// Release the joystick: stop the continuous walk. Idle animation is left to
        /// the existing CheckIfStopRunDisplay path so we do not fight it.
        /// </summary>
        public void DirectStop()
        {
            _directMoveSign = 0;
            if (activesMoveCoroutine != null)
                StopMoveTowardCoroutine();

            // CheckIfStopRunDisplay is bypassed in direct mode, so settle the pose here.
            // Mirror tap's CheckIfStopRunDisplay: only go idle when SOLIDLY grounded
            // (>= 2 ground points). At an edge (<= 1) do nothing — the shared platform
            // trigger (PlatFormPlfColliderTrigger) owns the losing-balance / edge-fall,
            // exactly like tap mode.
            if (activesJumpCoroutine == null && !IsAnyAttackPlaying() && !IsFalling
                && CountGroundPoints() >= 2)
                WaitAnimationDisplay();
        }

        /// <summary>
        /// Button jump. Unlike the tap jump, this does not require tapping above the
        /// head (CanJump). It jumps in the held direction (or facing if neutral),
        /// reusing the exact same JumpTowardPositionAction arc as the tap path.
        /// </summary>
        public void DirectJump(float dirX)
        {
            if (IsHardActionLocked || _frozenByHivernox || !CanAttackWarrior)
                return;

            if (!CanMove)
                return;

            if (activesJumpCoroutine != null || IsFallingEdge || CountGroundPoints() == 0
                || IsFallingGrazesEdge || CanDie)
                return;

            float dist = collider2 != null ? collider2.bounds.size.x * 2f : 1.5f;
            int sign = dirX > 0.001f ? 1 : (dirX < -0.001f ? -1 : (rightFacing ? 1 : -1));
            float targetX = transform.position.x + sign * dist;

            SetDirectionVariables(targetX);
            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();
            _directMoveSign = 0;

            CheckIfAvoidCollider(true);
            MarkJumpStarted();
            JumpAnimationDisplay();

            activesJumpCoroutine = JumpTowardPositionAction(
                new Vector2(targetX, transform.position.y),
                maxJump,
                0.5f
            );
            StartCoroutine(activesJumpCoroutine);
        }

        /// <summary>Attack button — reuse the existing UI attack entry point.</summary>
        public void DirectAttack()
        {
            RequestPrimaryAttackFromUIButton();
        }

        /// <summary>
        /// Cast the armed Ice-Ball relic in the joystick direction (falls back to facing
        /// when neutral). The tap path aims at a touched world point; here we build an aim
        /// world point from the joystick direction and feed the same cast entry point.
        /// Returns false if no Ice-Ball is armed / castable.
        /// </summary>
        public bool DirectCastIceBall(Vector2 aimDir)
        {
            if (!_iceBallArmed || _iceBallShotPending || _attack3Casting)
                return false;

            if (IsHardActionLocked || _frozenByHivernox || !CanAttackWarrior)
                return false;

            if (aimDir.sqrMagnitude < 0.0001f)
                aimDir = rightFacing ? Vector2.right : Vector2.left;

            aimDir = aimDir.normalized;

            // Far enough that (aimWorld - spawnSocket) is dominated by aimDir, so the shot
            // direction matches the joystick tilt rather than the small socket offset.
            const float aimDistance = 3f;
            Vector2 aimWorld = (Vector2)transform.position + aimDir * aimDistance;

            BeginArmedIceBallCast(aimWorld);
            return true;
        }

        /// <summary>True when the Ice-Ball relic is armed (drives the HUD Cast button).</summary>
        public bool IsIceBallReadyToCastDirect =>
            _iceBallArmed && !_iceBallShotPending && !_attack3Casting;
    }
}

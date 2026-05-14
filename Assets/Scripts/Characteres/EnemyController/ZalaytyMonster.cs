using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.EnemyController;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Services;
using Assets.Scripts.Platforms;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ZalaytyMonster : Enemy
{
    [Header("A* Follow Settings")]
    [SerializeField] private float repathInterval = 0.25f;

    [Header("Temporary Platform Loss - Zalayty Only")]
    [Tooltip("Prevents Zalayty from entering Wait animation during tiny take-off / temporary CurrentplatForm null gaps.")]
    [SerializeField] private bool keepMotionDuringTemporaryPlatformLoss = true;

    [Tooltip("How long Zalayty may keep his current chase/jump state after losing CurrentplatForm for a short take-off.")]
    [SerializeField, Min(0f)] private float temporaryPlatformLossGraceTime = 0.45f;

    [Tooltip("Small retry delay while platform is temporarily null. Lower than repathInterval to avoid visible stutter.")]
    [SerializeField, Min(0.01f)] private float temporaryPlatformLossRetryDelay = 0.04f;

    [Tooltip("When CurrentplatForm is null, try to recover it immediately from Zalayty ground points before forcing wait.")]
    [SerializeField] private bool recoverPlatformFromGroundPointsWhenMissing = true;

    [Header("Warrior Same-Platform Jump Grace")]
    [Tooltip("When Warrior was on Zalayty's platform and then jumps / loses ground, delay A* path recompute instead of instantly switching to different-platform logic.")]
    [SerializeField] private bool delayPathRecomputeAfterWarriorLeavesSamePlatform = true;

    [Tooltip("How long Zalayty keeps the previous same-platform chase/attack decision after Warrior leaves ground from the same platform.")]
    [SerializeField, Min(0f)] private float warriorSamePlatformLeaveGraceTime = 0.5f;

    [Tooltip("Small polling delay while the same-platform jump grace is active. Keep it low so Zalayty quickly resumes normal chase if Warrior lands back.")]
    [SerializeField, Min(0.01f)] private float warriorSamePlatformLeaveRetryDelay = 0.04f;

    private PlatFormColliderTrigger _lastKnownPlatform;
    private float _lastKnownPlatformTime = -999f;
    private PlatFormColliderTrigger _lastKnownWarriorPlatform;
    private float _lastKnownWarriorPlatformTime = -999f;
    private PlatFormColliderTrigger _lastConfirmedSharedPlatform;
    private float _lastConfirmedSharedPlatformTime = -999f;
    private PlatFormColliderTrigger _warriorSamePlatformLeaveGracePlatform;
    private float _warriorSamePlatformLeaveGraceUntil = -999f;
    private Warrior _warriorSamePlatformLeaveGraceInstance;

    [Tooltip("Extra landing Y offset so we land safely on the next platform.")]
    [SerializeField] private float landingYOffset = 0.2f;

    [Tooltip("How close to the platform edge Zalayty goes before jumping.")]
    [SerializeField] private float takeoffEdgeMargin = 0.25f;

    [Tooltip("When dropping down, we nudge takeoff slightly outside current platform so we don't 'land back' on it.")]
    [SerializeField] private float dropSideOutEpsilon = 0.35f;

    [Tooltip("If true: Zalayty can jump up/down to ANY platform (within horizontal gap + not blocked by obstacles).")]
    [SerializeField] private bool ignoreVerticalLimits = true;

    [Tooltip("Max horizontal gap Zalayty can traverse between platforms.")]
    [SerializeField] private float zalaytyHorizontalGap = 18f;

    [Tooltip("CircleCast body width used by reachability check.")]
    [SerializeField] private float zalaytyBodyWidthCheck = 0.5f;

    [Header("Attack Behavior")]
    [Tooltip("Legacy behavior. When enabled alone, Zalayty stops moving while attacking.")]
    [SerializeField] private bool stopAndAttackWhenInRange = true;

    [Tooltip("When Zalayty and Warrior are on the same platform, ignore stopAndAttackWhenInRange so Zalayty keeps pressing toward Warrior.")]
    [SerializeField] private bool neverStopOnSamePlatformWithWarrior = true;

    [Header("Same Platform Physical Contact Attack")]
    [Tooltip("When true, Zalayty only stops/attacks on the same platform after his MAIN BoxCollider2D physically touches/overlaps Warrior's MAIN BoxCollider2D.")]
    [SerializeField] private bool requireMainBoxContactToStopAndAttackOnSamePlatform = true;

    [Tooltip("When true, top/bottom body contact is NOT valid attack contact. Zalayty must be beside Warrior with real vertical overlap.")]
    [SerializeField] private bool requireSideContactForMainBoxAttack = true;

    [Tooltip("Minimum vertical overlap, in world units, required between Zalayty and Warrior main boxes before the contact is considered side melee contact.")]
    [SerializeField, Min(0f)] private float sideAttackMinVerticalOverlap = 0.12f;

    [Tooltip("Minimum vertical overlap ratio of the smaller main-box height required for side melee contact.")]
    [SerializeField, Range(0f, 1f)] private float sideAttackMinVerticalOverlapRatio = 0.18f;

    [Tooltip("Small skin used by Physics2D.Distance so tiny solver gaps still count as body contact. Keep this very small.")]
    [SerializeField, Min(0f)] private float samePlatformMainBoxContactSkin = 0.015f;

    [Tooltip("Small tolerance used only on the animation-event hit frame. This prevents a visible Zalayty hit from missing because of a tiny physics solver gap.")]
    [SerializeField, Min(0f)] private float zalaytyImpactHitSkin = 0.10f;

    [Tooltip("While Zalayty is already stopped by Warrior body contact, keep him stopped until the bodies are separated by at least this distance. This hysteresis prevents attack/chase jitter caused by tiny physics solver gaps.")]
    [SerializeField, Min(0f)] private float samePlatformMainBoxContactReleaseSkin = 0.12f;

    [Tooltip("When true, real MAIN BoxCollider2D contact with Warrior stops Zalayty even if CurrentplatForm values are temporarily null or mismatched for one frame.")]
    [SerializeField] private bool stopOnWarriorMainBoxContactEvenDuringPlatformMismatch = true;

    [Tooltip("When true, Zalayty does not restart chase while a previous body-contact lock is still inside the release skin.")]
    [SerializeField] private bool useContactReleaseHysteresis = true;

    [Tooltip("When true, Zalayty keeps repeating contact attacks after cooldown while the MAIN BoxCollider2D bodies remain touching on the same platform.")]
    [SerializeField] private bool repeatAttackWhileMainBoxesStillTouchingOnSamePlatform = true;
    [Header("ExtraJump Conditions")]
    [SerializeField] private float warriorEdgeMargin = 1.80f;

    [Header("Hit Spark FX (Zalayty)")]
    [SerializeField] private GameObject sparkPrefab;          // assign Sparks.prefab
    [SerializeField] private float sparkScale = 1f;
    [SerializeField] private float sparkDestroyAfter = 1.2f; // very fast
    [SerializeField] private Vector3 sparkOffset = Vector3.zero;
    [SerializeField] private Transform hitPoint;

    [Header("Hit Gate")]
    [SerializeField] private bool requireTouchingToHit = true;
    [SerializeField] private bool requireSamePlatformToHit = true;
    [SerializeField] private bool requireTargetInFront = false;
    [Header("Squeeze ExtraJump (Group Logic)")]

    // how often we evaluate the group condition (cheap, but don’t do every frame)
    [SerializeField] private float squeezeCheckInterval = 0.12f;

    // prevent repeated hop spam
    [SerializeField] private float squeezeExtraJumpCooldown = 1.25f;

    // choose enemy types by requiring the marker on them
    [SerializeField] private bool requireSqueezeThreatMarker = true;

    // small epsilon so "same X" doesn't count as left/right
    [SerializeField] private float sideEpsilon = 0.05f;

    [Header("Squeeze ExtraJump")]
    [SerializeField] private bool allowSqueezeExtraJump = true; //checkbox in Inspector

    [Header("Takeoff Rules - Zalayty Only")]
    [Tooltip("When true, Zalayty can start an airborne arc only to change platforms or to perform the Warrior-top rebound. Same-platform extra jumps are blocked.")]
    [SerializeField] private bool onlyTakeOffToChangePlatformsOrRebound = true;

    [Tooltip("When true, any small upward lift caused by physics contact is cancelled while Zalayty is not in an authorized platform-change/rebound arc.")]
    [SerializeField] private bool cancelUnauthorizedUpwardLift = true;

    [Tooltip("Maximum distance above the last known platform top that may be corrected back to grounded position. Keep this small so real falls are not cancelled.")]
    [SerializeField, Min(0f)] private float unauthorizedLiftSnapBand = 0.25f;

    [Tooltip("Horizontal inset used when checking whether Zalayty is still above the last known platform before cancelling unauthorized lift.")]
    [SerializeField, Min(0f)] private float unauthorizedLiftPlatformXInset = 0.02f;

    private float _nextSqueezeCheckTime;
    private float _lastSqueezeJumpTime;
    private float _nextAllowedSamePlatformContactAttackTime;
    private bool _samePlatformContactCombatLocked;
    private bool _samePlatformContactAttackWasActive;

    private Coroutine followRoutine;

    private bool isOnEdgePlatform = false;
    public bool IsOnEdgePlatform => isOnEdgePlatform;

    public void SetJumping(bool v) => isOnEdgePlatform = v;



    public bool inRangeOrAttacking;

    [Header("Independent Movement - Zalayty Only")]
    [Tooltip("If true, Zalayty does not use CharacterController MoveToward/JumpToward and is not clamped by platform edges.")]
    [SerializeField] private bool useIndependentMovement = true;

    [Tooltip("Distance on X considered reached by Zalayty's independent horizontal movement.")]
    [SerializeField, Min(0.01f)] private float independentMoveArriveDistance = 0.08f;

    [Tooltip("Small tolerance used when deciding if Zalayty landed on a platform top.")]
    [SerializeField, Min(0f)] private float independentLandingBand = 0.16f;

    [Header("Moving Platform Landing Validation - Zalayty Only")]
    [Tooltip("When true, Zalayty does not finish a jump as grounded unless his body / ground points are really on the destination platform.")]
    [SerializeField] private bool validateZalaytyLandingBeforeGrounded = true;

    [Tooltip("When true, a jump target that belongs to a moving platform is refreshed during the arc using the platform's current bounds.")]
    [SerializeField] private bool trackMovingLandingPlatformDuringJump = true;

    [Tooltip("Extra horizontal tolerance used when validating that Zalayty is really above the target platform top.")]
    [SerializeField, Min(0f)] private float movingLandingHorizontalSkin = 0.04f;

    [Tooltip("How far above the platform top Zalayty may be and still be safely snapped onto the surface after a moving-platform jump.")]
    [SerializeField, Min(0f)] private float movingLandingSnapMaxVerticalDistance = 0.42f;

    [Tooltip("How far outside the platform side Zalayty may be and still be snapped back during a moving-platform landing correction.")]
    [SerializeField, Min(0f)] private float movingLandingSnapMaxHorizontalDistance = 0.28f;

    [Tooltip("Minimum time after a missed moving-platform landing where run/walk animations are blocked.")]
    [SerializeField, Min(0f)] private float missedLandingAirborneAnimationLockTime = 0.18f;

    [Tooltip("Gravity scale forced after a missed moving-platform landing so Zalayty falls/recover naturally instead of staying visually suspended.")]
    [SerializeField, Min(0f)] private float missedLandingGravityScale = 2.5f;

    [Header("Grounded Chase Stability - Zalayty Only")]
    [Tooltip("Prevents chase jitter by keeping Zalayty grounded for a few frames after a valid platform contact. This does NOT validate jump landing; it is only used after Zalayty was already confirmed grounded.")]
    [SerializeField] private bool useGroundedStickGraceDuringChase = true;

    [Tooltip("Very short grounded grace used only during normal chase to absorb one-frame moving-platform/contact gaps.")]
    [SerializeField, Min(0f)] private float groundedStickGraceTime = 0.08f;

    [Tooltip("Maximum body-bottom distance above the platform top allowed during the short grounded chase grace.")]
    [SerializeField, Min(0f)] private float groundedStickMaxVerticalSeparation = 0.12f;

    [Header("Same Platform Micro-Separation - Zalayty Only")]
    [Tooltip("When true, tiny same-platform body/ground-point gaps do not cancel Zalayty's ground chase or force airborne animation.")]
    [SerializeField] private bool preserveSamePlatformChaseDuringMicroSeparation = true;

    [Tooltip("How long after a confirmed shared platform Zalayty may treat a tiny gap as micro-separation instead of real takeoff.")]
    [SerializeField, Min(0f)] private float samePlatformMicroSeparationGraceTime = 0.18f;

    [Tooltip("Maximum Zalayty body-bottom gap above the platform top that is still considered same-platform micro-separation.")]
    [SerializeField, Min(0f)] private float samePlatformMicroSeparationMaxVerticalGap = 0.18f;

    [Tooltip("Horizontal tolerance used when checking whether Zalayty's body is still above the shared platform during micro-separation.")]
    [SerializeField, Min(0f)] private float samePlatformMicroSeparationHorizontalSkin = 0.04f;

    public override bool HardAnchorToMovingPlatforms => false;
    protected override bool UsesCommittedPatrolEdge => false;

    private bool _missedMovingPlatformLandingRecoveryActive;
    private float _forceAirborneAnimationUntil = -999f;
    private PlatFormColliderTrigger _lastReallyGroundedPlatform;
    private float _lastReallyGroundedTime = -999f;

    // Heartbeat sent by MovingHorizontalPlatform / moving lift passenger systems
    // while Zalayty is physically supported on their top surface.
    // This is intentionally separate from GroundPoints because child-collider contact
    // or Rigidbody2D.MovePosition timing can make ground points miss one/few frames
    // even though the lift is correctly carrying Zalayty.
    private PlatFormColliderTrigger _lastMovingPlatformTopSupportPlatform;
    private float _lastMovingPlatformTopSupportTime = -999f;

    [Header("Independent Jump Anti-Tunneling")]
    [Tooltip("If true, Zalayty predicts destination platform top crossing during independent jumps instead of waiting for collision callbacks.")]
    [SerializeField] private bool enableIndependentJumpAntiTunneling = true;

    [Tooltip("Extra Y tolerance when checking if Zalayty crossed the destination platform top between two physics steps.")]
    [SerializeField, Min(0f)] private float predictedLandingTopSkin = 0.08f;

    [Tooltip("Safety value used by the predictive landing test. Keep this generous so fast arc steps cannot skip the platform top.")]
    [SerializeField, Min(0f)] private float predictedLandingMaxDropPastTop = 0.45f;

    [Tooltip("Small X tolerance used when checking if Zalayty is horizontally above the destination platform.")]
    [SerializeField, Min(0f)] private float predictedLandingHorizontalSkin = 0.03f;

    [Tooltip("Stops only Zalayty horizontal velocity when his independent movement is interrupted.")]
    [SerializeField] private bool stopHorizontalVelocityOnIndependentStop = true;

    // MovingVerticalPlatform can execute after Zalayty's independent MovePosition
    // inside the same physics step. Rigidbody2D.MovePosition does not immediately
    // update rigidbody2.position, so the lift must know Zalayty's pending X target
    // and add only its vertical elevator correction instead of overwriting X.
    private Vector2 _lastRequestedIndependentMovePosition;
    private float _lastRequestedIndependentMoveFixedTime = -999f;

    // Dynamic same-platform chase target.
    // IMPORTANT: activesMoveCoroutine can stay non-null while the coroutine is alive.
    // Therefore FollowWarriorLoop must be able to retarget the existing coroutine
    // instead of waiting for activesMoveCoroutine to become null.
    private float _currentIndependentMoveTargetX;
    private Warrior _currentIndependentMoveTargetWarrior;
    private bool _hasCurrentIndependentMoveTarget;

    [Header("Different-Platform Warrior Impact - Zalayty Only")]
    [Tooltip("When true, a fast Zalayty platform-change/body collision with Warrior on another platform is converted into controlled Warrior impact absorption instead of raw physics push.")]
    [SerializeField] private bool enableDifferentPlatformWarriorImpactAbsorption = true;

    [Tooltip("Minimum impact speed required before the different-platform absorber runs. This prevents normal small touches from triggering stun/knockback.")]
    [SerializeField, Min(0f)] private float differentPlatformImpactMinSpeed = 3.0f;

    [Tooltip("Minimum dot product required to prove Zalayty was moving into Warrior, not sliding away from him.")]
    [SerializeField, Range(-1f, 1f)] private float differentPlatformImpactMinIntoWarriorDot = 0.15f;

    [Tooltip("Cooldown that prevents repeated absorption every physics frame while the bodies remain in contact.")]
    [SerializeField, Min(0.01f)] private float differentPlatformImpactCooldown = 0.16f;

    [Tooltip("Short movement lock applied to Zalayty after a different-platform impact so the jump arc cannot keep driving through Warrior.")]
    [SerializeField, Min(0f)] private float differentPlatformImpactZalaytyLockSeconds = 0.10f;

    [Tooltip("When true, a side impact while Zalayty is in a controlled platform-change jump interrupts the arc and lets him recover/fall naturally.")]
    [SerializeField] private bool interruptZalaytyJumpOnDifferentPlatformImpact = true;

    [Tooltip("If true, the absorber requires both CurrentplatForm values to be known and different. This keeps same-platform combat untouched.")]
    [SerializeField] private bool requireKnownDifferentPlatformsForImpactAbsorption = true;

    private Vector2 _lastZalaytyBodyMoveVelocity;
    private float _lastZalaytyBodyMoveVelocityTime = -999f;
    private bool _lastZalaytyBodyMoveWasPlatformChangeJump;
    private float _nextAllowedDifferentPlatformImpactTime = -999f;
    private float _differentPlatformImpactLockUntil = -999f;

    public bool TryGetIndependentMoveRequestForCurrentFixedStep(out Vector2 requestedPosition)
    {
        requestedPosition = _lastRequestedIndependentMovePosition;

        if (!useIndependentMovement)
            return false;

        if (rigidbody2 == null)
            return false;

        if (!_isMoving || _isJumping || activesMoveCoroutine == null || activesJumpCoroutine != null)
            return false;

        return Mathf.Abs(_lastRequestedIndependentMoveFixedTime - Time.fixedTime) <= 0.0001f;
    }

    public void ApplyMovingPlatformMergedIndependentMove(Vector2 mergedPosition)
    {
        _lastRequestedIndependentMovePosition = mergedPosition;
        _lastRequestedIndependentMoveFixedTime = Time.fixedTime;

        if (rigidbody2 != null)
            rigidbody2.MovePosition(mergedPosition);
        else
            transform.position = new Vector3(mergedPosition.x, mergedPosition.y, transform.position.z);
    }

    /// <summary>
    /// MovingHorizontalPlatform uses this to avoid overwriting Zalayty's own
    /// WaitForFixedUpdate movement. When this returns true, the platform exposes its
    /// delta and Zalayty adds it inside MoveZalaytyBody().
    /// </summary>
    public bool ShouldApplyHorizontalMovingPlatformCarryInsideOwnMove()
    {
        if (!useIndependentMovement)
            return false;

        if (!_isMoving || !_hasCurrentIndependentMoveTarget || activesMoveCoroutine == null)
            return false;

        if (_isJumping || activesJumpCoroutine != null)
            return false;

        if (!CanMove || _deathStarted || IsStunned)
            return false;

        return true;
    }

    /// <summary>
    /// MovingVerticalPlatform uses this to avoid double-applying the elevator
    /// movement while Zalayty is actively moving with independent MovePosition.
    /// The platform exposes its Y delta, and MoveZalaytyBody() adds that delta
    /// to Zalayty's own requested X movement.
    /// </summary>
    public bool ShouldApplyVerticalMovingPlatformCarryInsideOwnMove()
    {
        return ShouldApplyHorizontalMovingPlatformCarryInsideOwnMove();
    }

    private bool TryGetHorizontalMovingPlatformCarryDeltaForOwnMove(out Vector2 carryDelta)
    {
        carryDelta = Vector2.zero;

        if (!useIndependentMovement)
            return false;

        if (_isJumping || activesJumpCoroutine != null)
            return false;

        PlatFormColliderTrigger platform = CurrentplatForm;

        if (platform == null)
            platform = _lastKnownPlatform;

        if (platform == null)
            return false;

        if (!HasRecentMovingPlatformTopSupportForGroundMovement(platform))
            return false;

        if (platform is MovingHorizontalPlatform movingPlatform)
        {
            if (!movingPlatform.TryGetHorizontalCarryDeltaForCurrentFixedStep(this, out carryDelta))
                return false;

            return carryDelta.sqrMagnitude > 0.0000001f;
        }

        if (platform is RotatingPlatform rotatingPlatform)
        {
            if (!rotatingPlatform.TryGetCarryDeltaForCurrentFixedStep(this, out carryDelta))
                return false;

            return carryDelta.sqrMagnitude > 0.0000001f;
        }

        return false;
    }

    private bool TryGetVerticalMovingPlatformCarryDeltaForOwnMove(out Vector2 carryDelta)
    {
        carryDelta = Vector2.zero;

        if (!useIndependentMovement)
            return false;

        if (_isJumping || activesJumpCoroutine != null)
            return false;

        PlatFormColliderTrigger platform = CurrentplatForm;

        if (platform == null)
            platform = _lastKnownPlatform;

        if (platform == null)
            return false;

        if (!HasRecentMovingPlatformTopSupportForGroundMovement(platform))
            return false;

        if (platform is MovingVerticalPlatform movingPlatform)
        {
            if (!movingPlatform.TryGetVerticalCarryDeltaForCurrentFixedStep(this, out carryDelta))
                return false;

            carryDelta.x = 0f;
            return Mathf.Abs(carryDelta.y) > 0.000001f;
        }

        return false;
    }

    private void RememberIndependentMoveRequest(Vector2 position)
    {
        if (!useIndependentMovement)
            return;

        _lastRequestedIndependentMovePosition = position;
        _lastRequestedIndependentMoveFixedTime = Time.fixedTime;
    }

    private void ClearIndependentMoveRequest()
    {
        _lastRequestedIndependentMoveFixedTime = -999f;
    }

    /// <summary>
    /// Called by a moving platform that has already verified that Zalayty is a real
    /// top-surface passenger. This lets the chase guard accept the moving platform
    /// as valid ground even when GroundPoints miss for one physics step.
    /// </summary>
    public void NotifyMovingPlatformTopSupport(PlatFormColliderTrigger platform)
    {
        if (platform == null || platform.platformCollider == null)
            return;

        if (_deathStarted || currentHealth <= 0f)
            return;

        // Do not convert a Warrior-top rebound into normal ground chase.
        if (_warriorTopReboundActive)
            return;

        // A verified top-surface contact with this moving platform is the landing
        // confirmation. Clear stale jump state so CanUseZalaytyGroundMovementNow()
        // does not reject the chase while Zalayty is visibly seated on the platform.
        if (activesJumpCoroutine != null)
        {
            StopCoroutine(activesJumpCoroutine);
            activesJumpCoroutine = null;
        }

        _isJumping = false;
        SetJumping(false);
        DescendentPhase = false;
        targetReached = true;

        CurrentplatForm = platform;
        _lastKnownPlatform = platform;
        _lastKnownPlatformTime = Time.time;

        _lastReallyGroundedPlatform = platform;
        _lastReallyGroundedTime = Time.time;

        _lastMovingPlatformTopSupportPlatform = platform;
        _lastMovingPlatformTopSupportTime = Time.time;

        _missedMovingPlatformLandingRecoveryActive = false;
        _forceAirborneAnimationUntil = -999f;
    }

    private bool HasRecentMovingPlatformTopSupportForGroundMovement(PlatFormColliderTrigger platform)
    {
        if (platform == null || platform.platformCollider == null)
            return false;

        if (_lastMovingPlatformTopSupportPlatform != platform)
            return false;

        if (_deathStarted || currentHealth <= 0f)
            return false;

        if (_warriorTopReboundActive)
            return false;

        if (_missedMovingPlatformLandingRecoveryActive)
            return false;

        // Slightly larger than groundedStickGraceTime so a coroutine running between
        // two FixedUpdate ticks does not reject a valid lift passenger.
        float grace = Mathf.Max(groundedStickGraceTime, Time.fixedDeltaTime * 3f, 0.10f);
        return Time.time <= _lastMovingPlatformTopSupportTime + grace;
    }

    [Header("Platform Jump Landing - Avoid Warrior Top")]
    [Tooltip("When true, Zalayty adjusts platform-change landing X so he lands beside Warrior instead of on Warrior's top bound.")]
    [SerializeField] private bool avoidWarriorTopLandingDuringPlatformJumps = true;

    [Tooltip("Extra horizontal gap kept between Zalayty's main body and Warrior's main body when choosing a platform-jump landing beside Warrior.")]
    [SerializeField, Min(0f)] private float platformJumpWarriorSideGap = 0.18f;

    [Tooltip("Inset from the destination platform edges used only when choosing a safe beside-Warrior landing point.")]
    [SerializeField, Min(0f)] private float platformJumpLandingPlatformInset = 0.30f;

    [Tooltip("When true, the landing correction is applied only when the destination platform is Warrior's current platform.")]
    [SerializeField] private bool onlyAvoidWarriorTopLandingOnWarriorPlatform = true;

    [Tooltip("If the requested landing is not above Warrior, keep the original landing X. If false, Zalayty may still choose a better beside-Warrior attack position on Warrior's platform.")]
    [SerializeField] private bool keepOriginalLandingWhenAlreadyBesideWarrior = true;

    [Header("Warrior Top Rebound - Zalayty Only")]
    [SerializeField] private bool enableWarriorTopRebound = true;

    [Tooltip("Minimum contact normal Y that means Zalayty is standing on the Warrior top surface.")]
    [SerializeField, Range(0f, 1f)] private float warriorTopReboundNormalY = 0.65f;

    [Tooltip("Bounds fallback tolerance for detecting that Zalayty's bottom is on Warrior's top.")]
    [SerializeField, Min(0f)] private float warriorTopReboundBoundsBand = 0.22f;

    [Tooltip("Small horizontal gap kept between Zalayty and Warrior after the rebound.")]
    [SerializeField, Min(0f)] private float warriorTopReboundSideGap = 0.08f;

    [Tooltip("Minimum extra clearance used when choosing a rebound landing beside Warrior. This prevents landing back on Warrior's top bound.")]
    [SerializeField, Min(0f)] private float warriorTopReboundMinSideClearance = 0.18f;

    [Tooltip("Extra vertical lift used by the Warrior-top rebound so Zalayty exits the current Warrior contact cleanly.")]
    [SerializeField, Min(0f)] private float warriorTopReboundExtraLift = 0.35f;

    [Tooltip("During the short Warrior-top rebound, temporarily ignore Warrior colliders until Zalayty is clear, then restore them.")]
    [SerializeField] private bool ignoreWarriorCollisionDuringTopRebound = true;

    [Tooltip("Maximum time allowed before Warrior collision is restored if the clear test cannot complete.")]
    [SerializeField, Min(0.05f)] private float warriorTopReboundIgnoreRestoreTimeout = 0.65f;

    [Tooltip("Keeps the rebound target away from platform edges when there is enough room.")]
    [SerializeField, Min(0f)] private float warriorTopReboundPlatformInset = 0.30f;

    [Tooltip("Controlled short arc duration used to move Zalayty beside Warrior.")]
    [SerializeField, Min(0.05f)] private float warriorTopReboundDuration = 0.28f;

    [Tooltip("Controlled short arc height used to move Zalayty beside Warrior.")]
    [SerializeField, Min(0f)] private float warriorTopReboundHeight = 0.75f;

    [Tooltip("Prevents repeated rebound spam while Zalayty is already correcting his position.")]
    [SerializeField, Min(0f)] private float warriorTopReboundCooldown = 0.35f;

    protected override bool ClampMoveToCurrentPlatform => !useIndependentMovement;
    protected override bool AllowEdgeExitWhenTargetOutside => true;

    private void Awake()
    {
#if UNITY_ANDROID
        Time.fixedDeltaTime = 0.02f;
        Application.targetFrameRate = 60;
#endif
    }
    protected override void Start()
    {
        base.Start();

        if (rigidbody2 != null)
        {
            rigidbody2.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rigidbody2.interpolation = RigidbodyInterpolation2D.Interpolate;
            rigidbody2.sleepMode = RigidbodySleepMode2D.NeverSleep;
        }

        // Zalayty defaults
        CanMove = true;
        Speed = 4f;
        Range = 2f;
        attackCooldown = 1f;

        // IMPORTANT: apply spawn overrides ONCE, after defaults
        ApplySpawnOverridesNow();
        RememberKnownPlatforms(GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null);

        if (followRoutine != null) StopCoroutine(followRoutine);
        followRoutine = StartCoroutine(FollowWarriorLoop());
    }

    protected override void Update()
    {
        // Do NOT call base.Update() here. Enemy.Update() contains patrol/edge safety
        // code that stops movement and clamps enemies back onto the current platform.
        // Zalayty owns his movement, so we keep only the generic death fallback.
        CheckWorldYDeathFallback();
        initDirection();
    }

    private void FixedUpdate()
    {
        PreventUnauthorizedTakeoffLift();
    }

    private void initDirection()
    {
        if (CurrentplatForm != null)
        {
            if (xEdge == 0f)
            {
                if (OddValue)
                    xEdge = CurrentplatForm.platformCollider.bounds.max.x;
                else
                    xEdge = CurrentplatForm.platformCollider.bounds.min.x;
            }
        }
    }

    void extraJump()
    {
        var warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null || warrior.collider2 == null) return;

        // default behavior = switch sides relative to warrior
        bool iAmLeft = transform.position.x < warrior.collider2.bounds.center.x;
        TryExtraJumpToSide(warrior, landOnLeftOfWarrior: !iAmLeft);
    }

    private bool TryExtraJumpToSide(Warrior warrior, bool landOnLeftOfWarrior)
    {
        if (warrior == null || warrior.collider2 == null) return false;
        if (CurrentplatForm == null || CurrentplatForm.platformCollider == null) return false;

        Bounds platB = CurrentplatForm.platformCollider.bounds;
        float safeMinX = platB.min.x + 0.35f;
        float safeMaxX = platB.max.x - 0.35f;

        Bounds warB = warrior.collider2.bounds;

        float clearance = (warB.extents.x * 1.2f) + 0.6f;

        float landingX = landOnLeftOfWarrior
            ? (warB.min.x - clearance)   // land left of warrior
            : (warB.max.x + clearance);  // land right of warrior

        if (!useIndependentMovement)
            landingX = Mathf.Clamp(landingX, safeMinX, safeMaxX);

        // validate that we truly got to the requested side
        if (landOnLeftOfWarrior && landingX >= warB.min.x - 0.1f) return false;
        if (!landOnLeftOfWarrior && landingX <= warB.max.x + 0.1f) return false;

        Vector2 landing = new Vector2(landingX, platB.max.y + landingYOffset);

        float dx = Mathf.Abs(landing.x - transform.position.x);
        float jumpHeight = Mathf.Clamp(2.5f + dx * 0.05f, 2.5f, 6.0f);
        float duration = 0.4f;

        ExitWaitAnimation();
        PlatFormColliderTrigger expectedPlatform = IsXInsidePlatform(CurrentplatForm, landingX)
            ? CurrentplatForm
            : null;

        return StartZalaytyJumpTo(landing, jumpHeight, duration, expectedPlatform);
    }

    private bool TrySqueezeExtraJump(Warrior warrior)
    {
        if (warrior == null || warrior.collider2 == null) return false;
        if (CurrentplatForm == null || warrior.CurrentplatForm == null) return false;
        if (CurrentplatForm != warrior.CurrentplatForm) return false;

        if (Time.time < _nextSqueezeCheckTime) return false;
        _nextSqueezeCheckTime = Time.time + squeezeCheckInterval;

        if (Time.time < _lastSqueezeJumpTime + squeezeExtraJumpCooldown) return false;

        // don't do it mid-air / while jump coroutine already active
        if (_isJumping || activesJumpCoroutine != null) return false;

        float wX = warrior.collider2.bounds.center.x;

        int inRangeCount = 0;
        bool includesMe = false;

        // count non-me threats on each side (we want "all on one side" before we hop)
        int leftOther = 0, rightOther = 0;

        Enemy closestOther = null;
        float closestDist = float.MaxValue;

        foreach (var e in Enemy.ActiveEnemies)
        {
            if (e == null) continue;
            if (e.CurrentplatForm != CurrentplatForm) continue;

            // choose which enemy types count
            if (requireSqueezeThreatMarker && e.GetComponent<SqueezeThreat>() == null)
                continue;

            // range check (match your EnemyRangeService behavior: x-distance)
            float dx = Mathf.Abs(e.transform.position.x - wX);
            if (dx > e.Range) continue;

            inRangeCount++;

            if (e == this) includesMe = true;
            else
            {
                float signed = e.transform.position.x - wX;
                if (signed < -sideEpsilon) leftOther++;
                else if (signed > sideEpsilon) rightOther++;

                if (dx < closestDist)
                {
                    closestDist = dx;
                    closestOther = e;
                }
            }
        }

        // Conditions:
        // - warrior in range of 2+ enemies
        // - Zalayty is part of that in-range group
        // - at least 1 other enemy besides Zalayty
        if (!includesMe) return false;
        if (inRangeCount < 2) return false;
        if (closestOther == null) return false;

        // If there are already "others" on BOTH sides, squeeze already exists -> don't hop
        if (leftOther > 0 && rightOther > 0) return false;

        // Decide where Zalayty should land: opposite side of the closest other enemy
        bool otherIsLeft = closestOther.transform.position.x < wX - sideEpsilon;
        bool wantLandOnLeft = !otherIsLeft; // if other is left, we land right (wantLandOnLeft = false)

        bool iAmLeft = transform.position.x < wX;
        if (iAmLeft == wantLandOnLeft) return false; // already positioned for squeeze

        bool jumped = TryExtraJumpToSide(warrior, wantLandOnLeft);
        if (jumped)
        {
            _lastSqueezeJumpTime = Time.time;
            return true;
        }

        return false;
    }

    private bool IsWarriorNearPlatformEdge(Warrior warrior)
    {
        if (CurrentplatForm == null || CurrentplatForm.platformCollider == null) return false;
        if (warrior == null || warrior.collider2 == null) return false;

        Bounds plat = CurrentplatForm.platformCollider.bounds;
        Bounds war = warrior.collider2.bounds;

        // Warrior near left edge OR near right edge.
        bool nearLeft = war.min.x <= plat.min.x + warriorEdgeMargin;
        bool nearRight = war.max.x >= plat.max.x - warriorEdgeMargin;
        return nearLeft || nearRight;
    }

    private IEnumerator FollowWarriorLoop()
    {
        yield return null;

        while (this != null)
        {
            var warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            // Same-platform takeoff is disabled by the new rule.
            // Zalayty may leave the ground only for real platform changes or Warrior-top rebound.
            if (!onlyTakeOffToChangePlatformsOrRebound &&
                isOnEdgePlatform &&
                inRangeOrAttacking &&
                warrior != null &&
                CurrentplatForm == warrior.CurrentplatForm &&
                IsWarriorNearPlatformEdge(warrior))
            {
                StopMoveTowardCoroutine();
                ExitWaitAnimation();
                extraJump();
                yield return new WaitForSeconds(0.1f); // no continue
            }

            var graph = PlatformGraphAStar.Instance;

            if (graph == null || warrior == null)
            {
                StopMoveTowardCoroutine();
                EnterWaitAnimation();
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            RememberKnownPlatforms(warrior);
            TryRecoverCurrentPlatformFromGroundPoints();
            RememberKnownPlatforms(warrior);
            UpdateWarriorSamePlatformLeaveGraceState(warrior);

            // Zalayty-only moonwalk guard:
            // If a moving platform has shifted away or a stale CurrentplatForm says
            // "grounded" while the body/ground-points are not really on that platform,
            // block chase/run immediately and stay in airborne recovery.
            if (ShouldBlockGroundLogicBecauseZalaytyIsAirborne())
            {
                yield return new WaitForSeconds(temporaryPlatformLossRetryDelay);
                continue;
            }

            // First priority: real body contact with Warrior means STOP NOW.
            // Do this before platform-null/path checks so a temporary CurrentplatForm
            // mismatch cannot restart chase and push Warrior for one frame.
            if (!_samePlatformContactCombatLocked && ShouldStopSamePlatformChaseForMainBoxContact(warrior))
            {
                inRangeOrAttacking = true;
                MaintainSamePlatformContactCombat(warrior, true);
                yield return null;
                continue;
            }

            // Contact-combat release is GLOBAL, not platform-dependent.
            // If Zalayty was locked because the MAIN BoxCollider2D bodies touched,
            // separation must immediately return him to chase/path logic, even if
            // CurrentplatForm/Warrior.CurrentplatForm changed or became temporarily null.
            if (_samePlatformContactCombatLocked)
            {
                bool contactStillTouching = AreMainBoxesTouchingForContactCombat(warrior);

                if (contactStillTouching)
                {
                    inRangeOrAttacking = true;
                    MaintainSamePlatformContactCombat(warrior, true);
                    yield return null;
                    continue;
                }

                ReleaseContactCombatForImmediateChase();
                inRangeOrAttacking = false;
            }

            if (ShouldDelayPathRecomputeBecauseWarriorLeftSamePlatform(warrior))
            {
                // Warrior just jumped / temporarily lost ground from our shared platform.
                // Keep the same-platform chase decision alive; do NOT enter the A* branch yet.
                ContinueSamePlatformChaseDuringWarriorLeaveGrace(warrior);
                yield return new WaitForSeconds(warriorSamePlatformLeaveRetryDelay);
                continue;
            }

            if (CurrentplatForm == null || warrior.CurrentplatForm == null)
            {
                if (ShouldKeepMotionDuringTemporaryPlatformLoss(warrior))
                {
                    // Important: during a tiny take-off / temporary platform-null gap,
                    // do NOT stop the movement coroutine and do NOT enter WaitAnimation.
                    // Stopping here zeroes X velocity and creates visible same-platform stutter.
                    ExitWaitAnimation();
                    DisplayAirborneOrRunAnimationDuringTemporaryLoss();
                    yield return new WaitForSeconds(temporaryPlatformLossRetryDelay);
                    continue;
                }

                StopMoveTowardCoroutine();
                EnterWaitAnimation();
                yield return new WaitForSeconds(repathInterval);
                continue;
            }

            // SAME PLATFORM: chase until the MAIN BoxCollider2D bodies physically touch.
            // Do not use range-only attack decisions here. This keeps Zalayty moving
            // toward Warrior and prevents early Wait/attack before real contact.
            if (CurrentplatForm == warrior.CurrentplatForm)
            {
                float targetX = GetIndependentFollowTargetX(warrior);
                SetDirectionVariables(targetX);

                bool mainBoxesTouching = ShouldStopSamePlatformChaseForMainBoxContact(warrior);

                // Contact-combat is controlled ONLY by real MAIN BoxCollider2D contact.
                // As soon as the bodies separate, do not keep Zalayty stopped because
                // an old attack animation/cooldown is still active. Fall through and chase.
                if (mainBoxesTouching)
                {
                    inRangeOrAttacking = true;
                    MaintainSamePlatformContactCombat(warrior, true);
                    yield return null;
                    continue;
                }

                if (_samePlatformContactCombatLocked)
                    ReleaseContactCombatForImmediateChase();

                inRangeOrAttacking = false;

                // NEW: squeeze-driven extraJump (group logic) //  Only if checkbox is enabled
                if (!onlyTakeOffToChangePlatformsOrRebound &&
                    allowSqueezeExtraJump &&
                    TrySqueezeExtraJump(warrior))
                {
                    yield return new WaitForSeconds(0.08f);
                    continue;
                }

                // Legacy range-stop behavior is intentionally disabled by default on
                // same-platform chase. Zalayty should not Wait/attack from distance;
                // he stops only when the main bodies touch.
                if (!requireMainBoxContactToStopAndAttackOnSamePlatform)
                {
                    inRangeOrAttacking = TryToPerformAttack(warrior.transform);

                    if (inRangeOrAttacking && stopAndAttackWhenInRange && !neverStopOnSamePlatformWithWarrior)
                    {
                        StopMoveTowardCoroutine();
                        yield return new WaitForSeconds(repathInterval);
                        continue;
                    }
                }

                bool canUseGroundMovement = CanUseZalaytyGroundMovementNow(warrior);

                if (CanMove && canUseGroundMovement)
                {
                    ExitWaitAnimation();
                    RunAnimationDisplay();

                    // Do NOT gate this with activesMoveCoroutine == null.
                    // On a moving platform the coroutine may stay alive while Warrior's
                    // world X changes. StartZalaytyMoveToX now retargets the active loop.
                    StartZalaytyMoveToX(targetX, warrior);
                }
                else if (!canUseGroundMovement)
                {
                    StopMoveTowardCoroutine();
                    ForceZalaytyAirborneAnimationOnly();
                }

                yield return new WaitForSeconds(repathInterval);
                continue;
            }

            // DIFFERENT PLATFORM: A*
            StopMoveTowardCoroutine();
            EnterWaitAnimation(); // <-- PERFECT place
            var settings = PlatformGraphAStar.ReachabilitySettings.Default(
                ignoreVerticalLimits ? 9999f : 7f,
                ignoreVerticalLimits ? 9999f : 12f,
                zalaytyHorizontalGap,
                PlatformGraphAStar.Instance != null ? PlatformGraphAStar.Instance.obstacleMask : 0,
                zalaytyBodyWidthCheck
            );

            List<PlatformNode> path = graph.FindPath(CurrentplatForm, warrior.CurrentplatForm, settings);

            if (path == null || path.Count < 2)
            {
                //  WaitAnimationDisplay();
                yield return new WaitForSeconds(repathInterval);
                continue;
            }

            PlatformNode next = path[1];
            if (next == null || next.Platform == null || next.Platform.platformCollider == null)
            {
                // WaitAnimationDisplay();
                yield return new WaitForSeconds(repathInterval);
                continue;
            }

            // We are about to MOVE → exit wait animation
            ExitWaitAnimation();

            yield return StartCoroutine(MoveAndJumpToPlatform(next.Platform, warrior.transform));
            yield return new WaitForSeconds(0.02f);
        }
    }

    private void RememberKnownPlatforms(Warrior warrior)
    {
        if (CurrentplatForm != null)
        {
            _lastKnownPlatform = CurrentplatForm;
            _lastKnownPlatformTime = Time.time;
        }

        if (warrior != null && warrior.CurrentplatForm != null)
        {
            _lastKnownWarriorPlatform = warrior.CurrentplatForm;
            _lastKnownWarriorPlatformTime = Time.time;
        }
    }

    private void UpdateWarriorSamePlatformLeaveGraceState(Warrior warrior)
    {
        if (!delayPathRecomputeAfterWarriorLeavesSamePlatform)
            return;

        if (warrior == null || warrior.IsDead || _deathStarted || currentHealth <= 0f)
        {
            ClearWarriorSamePlatformLeaveGrace();
            return;
        }

        if (_warriorSamePlatformLeaveGraceInstance != null &&
            _warriorSamePlatformLeaveGraceInstance != warrior)
        {
            ClearWarriorSamePlatformLeaveGrace();
        }

        bool samePlatformNow =
            CurrentplatForm != null &&
            warrior.CurrentplatForm != null &&
            CurrentplatForm == warrior.CurrentplatForm;

        bool warriorGroundedNow = IsWarriorGroundedForSamePlatformLeaveGrace(warrior);

        // This is the only state that is allowed to arm the grace:
        // Zalayty and Warrior are confirmed on the same platform while Warrior is grounded.
        if (samePlatformNow && warriorGroundedNow)
        {
            _lastConfirmedSharedPlatform = CurrentplatForm;
            _lastConfirmedSharedPlatformTime = Time.time;
            ClearWarriorSamePlatformLeaveGrace();
            return;
        }

        if (_lastConfirmedSharedPlatform == null)
            return;

        bool recentlySharedPlatform =
            Time.time <= _lastConfirmedSharedPlatformTime + warriorSamePlatformLeaveGraceTime;

        if (!recentlySharedPlatform)
            return;

        PlatFormColliderTrigger myRelevantPlatform = CurrentplatForm != null
            ? CurrentplatForm
            : _lastKnownPlatform;

        bool zStillBelongsToLastSharedPlatform = myRelevantPlatform == _lastConfirmedSharedPlatform;
        if (!zStillBelongsToLastSharedPlatform)
            return;

        bool warriorLeftBecauseOfJumpOrAirborneState =
            !warriorGroundedNow ||
            warrior.IsJumping ||
            warrior.activesJumpCoroutine != null ||
            warrior.CurrentplatForm == null;

        if (!warriorLeftBecauseOfJumpOrAirborneState)
            return;

        if (!IsWarriorSamePlatformLeaveGraceRunning())
        {
            _warriorSamePlatformLeaveGracePlatform = _lastConfirmedSharedPlatform;
            _warriorSamePlatformLeaveGraceUntil = Time.time + warriorSamePlatformLeaveGraceTime;
            _warriorSamePlatformLeaveGraceInstance = warrior;
        }
    }

    private bool ShouldDelayPathRecomputeBecauseWarriorLeftSamePlatform(Warrior warrior)
    {
        if (!delayPathRecomputeAfterWarriorLeavesSamePlatform)
            return false;

        if (!IsWarriorSamePlatformLeaveGraceRunning())
            return false;

        if (warrior == null || warrior.IsDead || _deathStarted || currentHealth <= 0f)
        {
            ClearWarriorSamePlatformLeaveGrace();
            return false;
        }

        if (_warriorSamePlatformLeaveGraceInstance != null &&
            _warriorSamePlatformLeaveGraceInstance != warrior)
        {
            ClearWarriorSamePlatformLeaveGrace();
            return false;
        }

        bool samePlatformNow =
            CurrentplatForm != null &&
            warrior.CurrentplatForm != null &&
            CurrentplatForm == warrior.CurrentplatForm;

        if (samePlatformNow && IsWarriorGroundedForSamePlatformLeaveGrace(warrior))
        {
            // Warrior landed back where he started within the delay.
            // Normal same-platform chase/attack can continue without any A* recompute.
            ClearWarriorSamePlatformLeaveGrace();
            return false;
        }

        PlatFormColliderTrigger myRelevantPlatform = CurrentplatForm != null
            ? CurrentplatForm
            : _lastKnownPlatform;

        if (myRelevantPlatform != _warriorSamePlatformLeaveGracePlatform)
        {
            ClearWarriorSamePlatformLeaveGrace();
            return false;
        }

        return true;
    }

    private bool IsWarriorSamePlatformLeaveGraceRunning()
    {
        if (_warriorSamePlatformLeaveGracePlatform == null)
            return false;

        if (Time.time <= _warriorSamePlatformLeaveGraceUntil)
            return true;

        ClearWarriorSamePlatformLeaveGrace();
        return false;
    }

    private void ClearWarriorSamePlatformLeaveGrace()
    {
        _warriorSamePlatformLeaveGracePlatform = null;
        _warriorSamePlatformLeaveGraceUntil = -999f;
        _warriorSamePlatformLeaveGraceInstance = null;
    }

    private bool IsWarriorGroundedForSamePlatformLeaveGrace(Warrior warrior)
    {
        if (warrior == null)
            return false;

        // CountGroundPoints is the reliable signal for the exact "temporary lost ground" case.
        // CurrentplatForm alone can stay assigned for a few frames during a jump.
        return warrior.CountGroundPoints() > 0;
    }

    private void ContinueSamePlatformChaseDuringWarriorLeaveGrace(Warrior warrior)
    {
        if (warrior == null || warrior.IsDead)
            return;

        if (!CanUseZalaytyGroundMovementNow(warrior))
        {
            ForceZalaytyAirborneAnimationOnly();
            return;
        }

        float targetX = GetIndependentFollowTargetX(warrior);
        SetDirectionVariables(targetX);

        bool mainBoxesTouching = ShouldStopSamePlatformChaseForMainBoxContact(warrior);
        if (mainBoxesTouching)
        {
            inRangeOrAttacking = true;
            MaintainSamePlatformContactCombat(warrior, true);
            return;
        }

        if (_samePlatformContactCombatLocked)
            ReleaseContactCombatForImmediateChase();

        inRangeOrAttacking = false;
        ExitWaitAnimation();

        if (IsAttackAnimationActive())
            return;

        if (CanMove && !_isJumping)
        {
            RunAnimationDisplay();

            if (activesMoveCoroutine == null)
                StartZalaytyMoveToX(targetX, warrior);
        }
    }

    private bool TryRecoverCurrentPlatformFromGroundPoints()
    {
        if (!recoverPlatformFromGroundPointsWhenMissing)
            return CurrentplatForm != null;

        if (CurrentplatForm != null)
            return true;

        if (GroundPoints == null)
            return false;

        foreach (Transform pt in GroundPoints)
        {
            if (pt == null)
                continue;

            Collider2D[] hits = Physics2D.OverlapCircleAll(pt.position, Groundradius, PlatformLayer);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                    continue;

                PlatFormColliderTrigger platform = hit.GetComponentInParent<PlatFormColliderTrigger>();
                if (platform == null || platform.platformCollider == null)
                    continue;

                if (!IsZalaytyReallyGroundedOnPlatform(platform))
                    continue;

                CurrentplatForm = platform;
                _lastKnownPlatform = platform;
                _lastKnownPlatformTime = Time.time;
                return true;
            }
        }

        return false;
    }

    private bool ShouldKeepMotionDuringTemporaryPlatformLoss(Warrior warrior)
    {
        if (!keepMotionDuringTemporaryPlatformLoss)
            return false;

        float now = Time.time;
        bool zalaytyPlatformMissing = CurrentplatForm == null;
        bool warriorPlatformMissing = warrior != null && warrior.CurrentplatForm == null;

        bool hasRecentZalaytyPlatform =
            _lastKnownPlatform != null &&
            now <= _lastKnownPlatformTime + temporaryPlatformLossGraceTime;

        bool hasRecentWarriorPlatform =
            _lastKnownWarriorPlatform != null &&
            now <= _lastKnownWarriorPlatformTime + temporaryPlatformLossGraceTime;

        bool zalaytyIsInControlledJump = _isJumping || activesJumpCoroutine != null;
        bool zalaytyJustLostGround = CountGroundPoints() == 0 && hasRecentZalaytyPlatform;

        if (zalaytyPlatformMissing && (zalaytyIsInControlledJump || zalaytyJustLostGround))
            return true;

        // Warrior can also briefly clear his CurrentplatForm during a hop.
        // If his last known platform is still Zalayty's current/last platform,
        // do not force Zalayty into WaitAnimation for that short gap.
        if (warriorPlatformMissing && hasRecentWarriorPlatform)
        {
            PlatFormColliderTrigger myRelevantPlatform = CurrentplatForm != null
                ? CurrentplatForm
                : _lastKnownPlatform;

            if (myRelevantPlatform != null && _lastKnownWarriorPlatform == myRelevantPlatform)
                return true;
        }

        return false;
    }

    private void DisplayAirborneOrRunAnimationDuringTemporaryLoss()
    {
        if (IsAttackAnimationActive())
            return;

        Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;

        if (!CanUseZalaytyGroundMovementNow(warrior))
        {
            ForceZalaytyAirborneAnimationOnly();
            return;
        }

        if (CanMove)
            RunAnimationDisplay();
    }

    private float GetIndependentFollowTargetX(Warrior warrior)
    {
        if (warrior == null)
            return transform.position.x;

        if (!useIndependentMovement)
            return ClampToCurrentPlatform(warrior.transform.position.x);

        return warrior.transform.position.x;
    }

    private void StartZalaytyMoveToX(float targetX, Warrior warrior = null)
    {
        if (!CanUseZalaytyGroundMovementNow(warrior))
        {
            StopMoveTowardCoroutine();
            ForceZalaytyAirborneAnimationOnly();
            return;
        }

        ExitWaitAnimation();
        RunAnimationDisplay();

        if (useIndependentMovement)
        {
            // Retarget the current chase. This is required on moving platforms:
            // activesMoveCoroutine may remain non-null while Warrior's world X changes.
            _currentIndependentMoveTargetX = targetX;
            _currentIndependentMoveTargetWarrior = warrior;
            _hasCurrentIndependentMoveTarget = true;

            // If the existing independent coroutine is alive, do not start another one.
            // It will read the refreshed target on the next FixedUpdate.
            if (activesMoveCoroutine != null && _isMoving)
                return;

            // Stale case: an IEnumerator reference exists, but no movement loop owns it.
            // Clear it before starting a clean coroutine.
            if (activesMoveCoroutine != null)
                StopMoveTowardCoroutine();

            _currentIndependentMoveTargetX = targetX;
            _currentIndependentMoveTargetWarrior = warrior;
            _hasCurrentIndependentMoveTarget = true;

            activesMoveCoroutine = MoveTowardXIndependent(targetX);
            StartCoroutine(activesMoveCoroutine);
            return;
        }

        // Legacy CharacterController movement still uses one fixed target, so restart it.
        StopMoveTowardCoroutine();
        activesMoveCoroutine = MoveTowardPostionAction(targetX);
        StartCoroutine(activesMoveCoroutine);
    }

    private IEnumerator MoveTowardXIndependent(float targetX)
    {
        if (_isMoving)
            yield break;

        _currentIndependentMoveTargetX = targetX;
        _hasCurrentIndependentMoveTarget = true;

        _isMoving = true;
        WaitForFixedUpdate wait = new WaitForFixedUpdate();

        while (this != null && _hasCurrentIndependentMoveTarget)
        {
            if (!CanMove || _deathStarted || IsStunned)
                break;

            if (Time.time < _differentPlatformImpactLockUntil)
            {
                StopHorizontalVelocityIfNeeded();
                yield return wait;
                continue;
            }

            Warrior warrior = _currentIndependentMoveTargetWarrior != null
                ? _currentIndependentMoveTargetWarrior
                : (GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null);

            if (warrior != null)
            {
                // Dynamic target: keep following Warrior's current world X while the
                // horizontal platform is also moving both bodies.
                _currentIndependentMoveTargetX = GetIndependentFollowTargetX(warrior);
            }

            targetX = _currentIndependentMoveTargetX;

            // Important: this check must receive Warrior context. Without it, a tiny
            // same-platform ground-point/body gap makes Zalayty look airborne even
            // while he is still logically chasing Warrior on the shared platform.
            if (!CanUseZalaytyGroundMovementNow(warrior))
            {
                ForceZalaytyAirborneAnimationOnly();
                break;
            }

            // Stop as soon as the MAIN bodies touch. If a lock already exists, use
            // the wider release skin so tiny solver gaps do not cause chase/attack jitter.
            bool lockedContactStillClose = _samePlatformContactCombatLocked &&
                                          AreMainBoxesTouchingForContactCombat(warrior);
            bool mainBoxesTouching = lockedContactStillClose ||
                                     ShouldStopSamePlatformChaseForMainBoxContact(warrior);
            if (mainBoxesTouching)
            {
                MaintainSamePlatformContactCombat(warrior, true);
                break;
            }

            // Separation wins immediately, but only after the release hysteresis says
            // the bodies are really clear. This avoids one-frame chase restarts.
            if (_samePlatformContactCombatLocked)
                ReleaseContactCombatForImmediateChase();

            Vector2 current = rigidbody2 != null
                ? rigidbody2.position
                : (Vector2)transform.position;

            float distanceX = Mathf.Abs(targetX - current.x);
            if (distanceX <= independentMoveArriveDistance)
                break;

            FlipCharacter(targetX);

            float step = Speed * Time.fixedDeltaTime;
            float nextX = Mathf.MoveTowards(current.x, targetX, step);

            MoveZalaytyBody(new Vector2(nextX, current.y));
            yield return wait;
        }

        StopHorizontalVelocityIfNeeded();
        _hasCurrentIndependentMoveTarget = false;
        _currentIndependentMoveTargetWarrior = null;
        _isMoving = false;
        activesMoveCoroutine = null;
    }

    private bool StartZalaytyJumpTo(
        Vector2 landing,
        float jumpHeight,
        float duration,
        PlatFormColliderTrigger expectedLandingPlatform,
        bool forceJumpTowardPositionAction = false)
    {
        if (!CanStartZalaytyTakeoff(expectedLandingPlatform, forceJumpTowardPositionAction))
            return false;

        // Final Zalayty-only safety net: any platform-change jump targeting
        // Warrior's platform must land beside Warrior, not on Warrior's top bound.
        landing = ResolvePlatformJumpLandingAwayFromWarriorTop(
            landing,
            expectedLandingPlatform,
            GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null);

        StopMoveTowardCoroutine();
        StopJumpTowardCoroutine();
        ExitWaitAnimation();
        JumpAnimationDisplay();

        _activeJumpTargetPlatform = expectedLandingPlatform;

        // Normal Zalayty movement stays independent.
        // Only special cases can explicitly request the CharacterController-style arc.
        bool useCharacterControllerJump = forceJumpTowardPositionAction || !useIndependentMovement;

        activesJumpCoroutine = useCharacterControllerJump
            ? JumpTowardPositionAction(landing, jumpHeight, duration, null)
            : JumpArcIndependent(landing, jumpHeight, duration, expectedLandingPlatform);

        StartCoroutine(activesJumpCoroutine);
        return true;
    }

    private bool CanStartZalaytyTakeoff(
        PlatFormColliderTrigger expectedLandingPlatform,
        bool forceJumpTowardPositionAction)
    {
        if (!onlyTakeOffToChangePlatformsOrRebound)
            return true;

        // Warrior-top rebound is explicitly allowed, even when the rebound lands back
        // on the same platform. This is the only same-platform airborne correction.
        if (forceJumpTowardPositionAction && _warriorTopReboundActive)
            return true;

        PlatFormColliderTrigger sourcePlatform = CurrentplatForm != null
            ? CurrentplatForm
            : _lastKnownPlatform;

        // Normal Zalayty takeoff is allowed only when it has a real destination
        // platform and that destination is not the source platform.
        bool changesPlatform =
            sourcePlatform != null &&
            expectedLandingPlatform != null &&
            expectedLandingPlatform != sourcePlatform;

        return changesPlatform;
    }

    private void PreventUnauthorizedTakeoffLift()
    {
        if (!onlyTakeOffToChangePlatformsOrRebound || !cancelUnauthorizedUpwardLift)
            return;

        if (rigidbody2 == null)
            return;

        if (_deathStarted || currentHealth <= 0f)
            return;

        // Do not interfere with the two authorized airborne arcs.
        if (_isJumping || activesJumpCoroutine != null || _warriorTopReboundActive)
            return;

        PlatFormColliderTrigger platform = CurrentplatForm != null
            ? CurrentplatForm
            : _lastKnownPlatform;

        if (platform == null || platform.platformCollider == null)
            return;

        bool recentlyBelongedToPlatform =
            CurrentplatForm == platform ||
            Time.time <= _lastKnownPlatformTime + temporaryPlatformLossGraceTime;

        if (!recentlyBelongedToPlatform)
            return;

        Collider2D body = GetZalaytyBodyCollider();
        if (body == null)
            return;

        Bounds platformBounds = platform.platformCollider.bounds;
        Bounds bodyBounds = body.bounds;

        float inset = Mathf.Max(0f, unauthorizedLiftPlatformXInset);
        bool stillAbovePlatform =
            bodyBounds.max.x > platformBounds.min.x + inset &&
            bodyBounds.min.x < platformBounds.max.x - inset;

        if (!stillAbovePlatform)
            return;

        Vector2 rbPos = rigidbody2.position;
        float bodyBottomOffsetFromPosition = bodyBounds.min.y - rbPos.y;
        float groundedPositionY =
            platformBounds.max.y + landingYOffset - bodyBottomOffsetFromPosition;

        float upwardLift = rbPos.y - groundedPositionY;

        // Cancel only small upward solver lifts. Do not cancel real falls or large
        // displacement, because those may be platform-edge or gameplay situations.
        if (upwardLift > 0f && upwardLift <= unauthorizedLiftSnapBand)
        {
            rbPos.y = groundedPositionY;
            rigidbody2.position = rbPos;

            if (CurrentplatForm == null)
                CurrentplatForm = platform;

            _lastKnownPlatform = platform;
            _lastKnownPlatformTime = Time.time;
        }

        Vector2 velocity = rigidbody2.linearVelocity;
        if (velocity.y > 0f)
        {
            velocity.y = 0f;
            rigidbody2.linearVelocity = velocity;
        }
    }

    private IEnumerator JumpArcIndependent(
        Vector2 landing,
        float height,
        float duration,
        PlatFormColliderTrigger expectedLandingPlatform)
    {
        if (_isJumping)
            yield break;

        _isJumping = true;
        targetReached = false;
        DescendentPhase = false;
        SetJumping(true);
        _missedMovingPlatformLandingRecoveryActive = false;
        _forceAirborneAnimationUntil = Time.time + missedLandingAirborneAnimationLockTime;
        JumpAnimationDisplay();

        Vector2 start = rigidbody2 != null
            ? rigidbody2.position
            : (Vector2)transform.position;

        float targetOffsetFromPlatformCenterX = 0f;
        bool canTrackPlatformTarget =
            trackMovingLandingPlatformDuringJump &&
            expectedLandingPlatform != null &&
            expectedLandingPlatform.platformCollider != null;

        if (canTrackPlatformTarget)
            targetOffsetFromPlatformCenterX = landing.x - expectedLandingPlatform.platformCollider.bounds.center.x;

        float elapsed = 0f;
        float previousY = start.y;
        WaitForFixedUpdate wait = new WaitForFixedUpdate();

        duration = Mathf.Max(0.05f, duration);

        while (elapsed < duration)
        {
            if (_deathStarted || IsStunned)
                break;

            Vector2 currentPosition = rigidbody2 != null
                ? rigidbody2.position
                : (Vector2)transform.position;

            Vector2 currentLanding = ResolveLiveLandingTargetForPlatform(
                landing,
                expectedLandingPlatform,
                targetOffsetFromPlatformCenterX,
                canTrackPlatformTarget);

            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            Vector2 nextPosition = Vector2.Lerp(start, currentLanding, t);
            nextPosition.y += height * Mathf.Sin(Mathf.PI * t);

            DescendentPhase = nextPosition.y < previousY;
            previousY = nextPosition.y;

            FlipCharacter(currentLanding.x);

            if (TryResolveIndependentPredictedTopLanding(
                    currentPosition,
                    nextPosition,
                    expectedLandingPlatform,
                    out Vector2 resolvedLanding,
                    out PlatFormColliderTrigger resolvedPlatform))
            {
                MoveZalaytyBody(resolvedLanding);

                // Whether the landing is accepted or rejected, this controlled arc
                // is finished. If rejected, TryFinish... already switched Zalayty
                // to natural airborne recovery and must not be overwritten by the
                // rest of this coroutine.
                TryFinishIndependentJumpOnPlatformIfReallyGrounded(resolvedPlatform, resolvedLanding.x);
                yield break;
            }

            MoveZalaytyBody(nextPosition);
            ForceZalaytyAirborneAnimationOnly();
            yield return wait;
        }

        Vector2 finalLanding = ResolveLiveLandingTargetForPlatform(
            landing,
            expectedLandingPlatform,
            targetOffsetFromPlatformCenterX,
            canTrackPlatformTarget);

        MoveZalaytyBody(finalLanding);

        if (TryFinishIndependentJumpOnPlatformIfReallyGrounded(expectedLandingPlatform, finalLanding.x))
            yield break;

        BeginMissedMovingPlatformLandingRecovery(expectedLandingPlatform);
    }

    private bool TryResolveIndependentPredictedTopLanding(
        Vector2 from,
        Vector2 to,
        PlatFormColliderTrigger expectedLandingPlatform,
        out Vector2 resolvedLanding,
        out PlatFormColliderTrigger resolvedPlatform)
    {
        resolvedLanding = to;
        resolvedPlatform = null;

        if (!enableIndependentJumpAntiTunneling)
            return false;

        if (expectedLandingPlatform == null || expectedLandingPlatform.platformCollider == null)
            return false;

        Collider2D body = GetZalaytyBodyCollider();
        if (body == null)
            return false;

        Vector2 delta = to - from;

        // Only solve top landing while descending.
        if (delta.y >= -0.0001f)
            return false;

        Bounds platformBounds = expectedLandingPlatform.platformCollider.bounds;
        Bounds currentBodyBounds = body.bounds;

        Bounds nextBodyBounds = currentBodyBounds;
        nextBodyBounds.center += (Vector3)delta;

        float platformTopY = platformBounds.max.y;

        bool crossedPlatformTop =
            currentBodyBounds.min.y >= platformTopY - predictedLandingTopSkin &&
            nextBodyBounds.min.y <= platformTopY + predictedLandingMaxDropPastTop;

        if (!crossedPlatformTop)
            return false;

        Bounds sweptBodyBounds = currentBodyBounds;
        sweptBodyBounds.Encapsulate(nextBodyBounds.min);
        sweptBodyBounds.Encapsulate(nextBodyBounds.max);

        bool horizontallyOverPlatform =
            sweptBodyBounds.max.x > platformBounds.min.x + predictedLandingHorizontalSkin &&
            sweptBodyBounds.min.x < platformBounds.max.x - predictedLandingHorizontalSkin;

        if (!horizontallyOverPlatform)
            return false;

        // Critical anti-tunneling line:
        // The destination platform must become solid before MovePosition crosses its top.
        expectedLandingPlatform.TryPrepareForPredictedTopLanding(
            this,
            expectedLandingPlatform.platformCollider);

        resolvedLanding = BuildIndependentTopLandingPosition(expectedLandingPlatform, to.x);
        resolvedPlatform = expectedLandingPlatform;

        return true;
    }

    private Vector2 BuildIndependentTopLandingPosition(
        PlatFormColliderTrigger platform,
        float wantedBodyPositionX)
    {
        if (platform == null || platform.platformCollider == null)
            return new Vector2(wantedBodyPositionX, transform.position.y);

        Collider2D body = GetZalaytyBodyCollider();
        Bounds platformBounds = platform.platformCollider.bounds;

        if (body == null)
            return new Vector2(wantedBodyPositionX, platformBounds.max.y + landingYOffset);

        Vector2 bodyPosition = rigidbody2 != null
            ? rigidbody2.position
            : (Vector2)transform.position;

        Bounds bodyBounds = body.bounds;

        float bodyBottomOffsetFromPosition = bodyBounds.min.y - bodyPosition.y;
        float bodyCenterOffsetFromPositionX = bodyBounds.center.x - bodyPosition.x;
        float bodyHalfWidth = Mathf.Max(0.01f, bodyBounds.extents.x);

        float safeMinBodyCenterX =
            platformBounds.min.x + bodyHalfWidth + predictedLandingHorizontalSkin;

        float safeMaxBodyCenterX =
            platformBounds.max.x - bodyHalfWidth - predictedLandingHorizontalSkin;

        float wantedBodyCenterX = wantedBodyPositionX + bodyCenterOffsetFromPositionX;

        float resolvedBodyCenterX = safeMaxBodyCenterX >= safeMinBodyCenterX
            ? Mathf.Clamp(wantedBodyCenterX, safeMinBodyCenterX, safeMaxBodyCenterX)
            : platformBounds.center.x;

        float resolvedPositionX = resolvedBodyCenterX - bodyCenterOffsetFromPositionX;

        // Place Zalayty using his body bottom, not the transform pivot.
        float resolvedPositionY =
            platformBounds.max.y + landingYOffset - bodyBottomOffsetFromPosition;

        Vector2 resolvedPosition = new Vector2(resolvedPositionX, resolvedPositionY);

        // Predicted top landing can happen before the final arc target is reached.
        // If that predicted crossing is still above Warrior, shift the resolved X
        // beside Warrior before Zalayty is seated on the destination platform.
        return ResolvePlatformJumpLandingAwayFromWarriorTop(
            resolvedPosition,
            platform,
            GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null);
    }

    private void FinishIndependentJumpOnPlatform(PlatFormColliderTrigger platform)
    {
        if (platform != null)
        {
            CurrentplatForm = platform;
            _lastKnownPlatform = platform;
            _lastKnownPlatformTime = Time.time;
        }

        if (_activeJumpTargetPlatform == platform)
            _activeJumpTargetPlatform = null;

        RestoreActiveJumpDownSourcePlatformNow();

        StopHorizontalVelocityIfNeeded();
        DescendentPhase = false;
        targetReached = true;
        _isJumping = false;
        activesJumpCoroutine = null;
        SetJumping(false);
        _missedMovingPlatformLandingRecoveryActive = false;
        _forceAirborneAnimationUntil = -999f;
        RememberReallyGroundedOnPlatform(platform);
    }

    private Vector2 ResolveLiveLandingTargetForPlatform(
        Vector2 originalLanding,
        PlatFormColliderTrigger expectedLandingPlatform,
        float targetOffsetFromPlatformCenterX,
        bool canTrackPlatformTarget)
    {
        if (!canTrackPlatformTarget || expectedLandingPlatform == null || expectedLandingPlatform.platformCollider == null)
            return originalLanding;

        float liveWantedX = expectedLandingPlatform.platformCollider.bounds.center.x + targetOffsetFromPlatformCenterX;
        return BuildIndependentTopLandingPosition(expectedLandingPlatform, liveWantedX);
    }

    private bool TryFinishIndependentJumpOnPlatformIfReallyGrounded(
        PlatFormColliderTrigger platform,
        float preferredLandingX)
    {
        if (platform == null || platform.platformCollider == null)
        {
            BeginMissedMovingPlatformLandingRecovery(platform);
            return false;
        }

        if (!validateZalaytyLandingBeforeGrounded)
        {
            FinishIndependentJumpOnPlatform(platform);
            return true;
        }

        Physics2D.SyncTransforms();

        if (IsZalaytyReallyGroundedOnPlatform(platform))
        {
            FinishIndependentJumpOnPlatform(platform);
            return true;
        }

        if (TrySeatZalaytyOnPlatformIfClose(platform, preferredLandingX, out Vector2 seatedPosition))
        {
            MoveZalaytyBody(seatedPosition);
            Physics2D.SyncTransforms();

            if (IsZalaytyReallyGroundedOnPlatform(platform))
            {
                FinishIndependentJumpOnPlatform(platform);
                return true;
            }
        }

        BeginMissedMovingPlatformLandingRecovery(platform);
        return false;
    }

    public bool IsZalaytyReallyGroundedOnPlatform(PlatFormColliderTrigger platform)
    {
        if (platform == null || platform.platformCollider == null)
            return false;

        Physics2D.SyncTransforms();

        Collider2D body = GetZalaytyBodyCollider();
        if (body == null || !body.enabled)
            return false;

        Bounds platformBounds = platform.platformCollider.bounds;
        Bounds bodyBounds = body.bounds;

        bool horizontallyOverPlatform =
            bodyBounds.max.x > platformBounds.min.x + movingLandingHorizontalSkin &&
            bodyBounds.min.x < platformBounds.max.x - movingLandingHorizontalSkin;

        if (!horizontallyOverPlatform)
            return false;

        // IMPORTANT:
        // Ground points are the strongest signal. Do this BEFORE checking vertical
        // velocity, because a MovingVerticalPlatform that carries Zalayty upward can
        // give his Rigidbody2D a positive Y velocity while he is still perfectly seated.
        // If we reject that frame, the follow loop alternates Run/Jump/Stop and creates
        // chase jitter.
        if (CountGroundPointsOnSpecificPlatform(platform) > 0)
        {
            RememberReallyGroundedOnPlatform(platform);
            return true;
        }

        // Bounds fallback is weaker than real ground points. Use it for landing and
        // one-frame contact timing, but only when Zalayty is descending/stable. This
        // keeps the anti-moonwalk protection: a real upward takeoff is not accepted
        // as grounded just because the bounds are still close to the platform top.
        bool movingDownOrStable =
            rigidbody2 == null ||
            rigidbody2.linearVelocity.y <= 0.08f ||
            DescendentPhase;

        if (!movingDownOrStable)
            return false;

        float bottomAboveTop = bodyBounds.min.y - platformBounds.max.y;
        float maxAllowedAboveTop = Mathf.Max(
            independentLandingBand,
            landingYOffset + movingLandingSnapMaxVerticalDistance);

        bool bottomCloseToTop =
            bottomAboveTop >= -independentLandingBand &&
            bottomAboveTop <= maxAllowedAboveTop;

        if (bottomCloseToTop)
        {
            RememberReallyGroundedOnPlatform(platform);
            return true;
        }

        return false;
    }

    private void RememberReallyGroundedOnPlatform(PlatFormColliderTrigger platform)
    {
        if (platform == null)
            return;

        _lastReallyGroundedPlatform = platform;
        _lastReallyGroundedTime = Time.time;
    }

    private bool IsZalaytyGroundedEnoughForGroundMovement(PlatFormColliderTrigger platform)
    {
        if (IsZalaytyReallyGroundedOnPlatform(platform))
            return true;

        if (!useGroundedStickGraceDuringChase)
            return false;

        if (platform == null || platform.platformCollider == null)
            return false;

        if (_missedMovingPlatformLandingRecoveryActive)
            return false;

        if (_isJumping || activesJumpCoroutine != null)
            return false;

        if (_lastReallyGroundedPlatform != platform)
            return false;

        if (Time.time > _lastReallyGroundedTime + groundedStickGraceTime)
            return false;

        return IsBodyStillCloseEnoughToPlatformForGroundMovement(platform);
    }

    private bool IsBodyStillCloseEnoughToPlatformForGroundMovement(PlatFormColliderTrigger platform)
    {
        if (platform == null || platform.platformCollider == null)
            return false;

        Collider2D body = GetZalaytyBodyCollider();
        if (body == null || !body.enabled)
            return false;

        Physics2D.SyncTransforms();

        Bounds platformBounds = platform.platformCollider.bounds;
        Bounds bodyBounds = body.bounds;

        bool horizontallyOverPlatform =
            bodyBounds.max.x > platformBounds.min.x + movingLandingHorizontalSkin &&
            bodyBounds.min.x < platformBounds.max.x - movingLandingHorizontalSkin;

        if (!horizontallyOverPlatform)
            return false;

        float bottomAboveTop = bodyBounds.min.y - platformBounds.max.y;
        float maxAllowedAboveTop = landingYOffset + groundedStickMaxVerticalSeparation;

        return bottomAboveTop >= -independentLandingBand &&
               bottomAboveTop <= maxAllowedAboveTop;
    }

    private int CountGroundPointsOnSpecificPlatform(PlatFormColliderTrigger platform)
    {
        if (platform == null || platform.platformCollider == null || GroundPoints == null)
            return 0;

        int count = 0;

        for (int p = 0; p < GroundPoints.Length; p++)
        {
            Transform point = GroundPoints[p];
            if (point == null)
                continue;

            Collider2D[] hits = Physics2D.OverlapCircleAll(point.position, Groundradius, PlatformLayer);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                    continue;

                if (hit == platform.platformCollider || hit.GetComponentInParent<PlatFormColliderTrigger>() == platform)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private bool TrySeatZalaytyOnPlatformIfClose(
        PlatFormColliderTrigger platform,
        float preferredLandingX,
        out Vector2 seatedPosition)
    {
        seatedPosition = rigidbody2 != null
            ? rigidbody2.position
            : (Vector2)transform.position;

        if (platform == null || platform.platformCollider == null)
            return false;

        Collider2D body = GetZalaytyBodyCollider();
        if (body == null)
            return false;

        Physics2D.SyncTransforms();

        Bounds platformBounds = platform.platformCollider.bounds;
        Bounds bodyBounds = body.bounds;

        bool horizontallyClose =
            bodyBounds.max.x > platformBounds.min.x - movingLandingSnapMaxHorizontalDistance &&
            bodyBounds.min.x < platformBounds.max.x + movingLandingSnapMaxHorizontalDistance;

        if (!horizontallyClose)
            return false;

        float bottomAboveTop = bodyBounds.min.y - platformBounds.max.y;
        bool verticallyClose =
            bottomAboveTop >= -independentLandingBand &&
            bottomAboveTop <= landingYOffset + movingLandingSnapMaxVerticalDistance;

        if (!verticallyClose)
            return false;

        if (rigidbody2 != null && rigidbody2.linearVelocity.y > 0.08f && !DescendentPhase)
            return false;

        seatedPosition = BuildIndependentTopLandingPosition(platform, preferredLandingX);
        return true;
    }

    private void BeginMissedMovingPlatformLandingRecovery(PlatFormColliderTrigger missedPlatform)
    {
        if (CurrentplatForm == missedPlatform)
            CurrentplatForm = null;
        else if (CurrentplatForm != null && !IsZalaytyReallyGroundedOnPlatform(CurrentplatForm))
            CurrentplatForm = null;

        if (_activeJumpTargetPlatform == missedPlatform)
            _activeJumpTargetPlatform = null;

        RestoreActiveJumpDownSourcePlatformNow();

        _missedMovingPlatformLandingRecoveryActive = true;
        _forceAirborneAnimationUntil = Time.time + missedLandingAirborneAnimationLockTime;
        _lastReallyGroundedPlatform = null;
        _lastReallyGroundedTime = -999f;

        targetReached = false;
        DescendentPhase = true;
        _isJumping = false;
        activesJumpCoroutine = null;
        SetJumping(true);

        StopHorizontalVelocityIfNeeded();

        if (rigidbody2 != null)
        {
            rigidbody2.gravityScale = Mathf.Max(rigidbody2.gravityScale, missedLandingGravityScale);

            Vector2 v = rigidbody2.linearVelocity;
            if (v.y > -0.05f)
                v.y = -0.05f;
            rigidbody2.linearVelocity = v;
        }

        ForceZalaytyAirborneAnimationOnly();
    }

    private bool CanUseZalaytyGroundMovementNow(Warrior warrior = null)
    {
        if (_deathStarted || currentHealth <= 0f)
            return false;

        if (_isJumping || activesJumpCoroutine != null)
            return false;

        if (Time.time < _forceAirborneAnimationUntil)
            return false;

        if (CurrentplatForm != null && CurrentplatForm.platformCollider != null)
        {
            if (IsZalaytyGroundedEnoughForGroundMovement(CurrentplatForm))
                return true;

            // Moving platforms can have valid top support even when Zalayty's
            // GroundPoints / main body bounds miss for a frame because the passenger
            // system applies its own MovePosition later in the same physics step.
            if (HasRecentMovingPlatformTopSupportForGroundMovement(CurrentplatForm))
                return true;
        }

        if (HasRecentMovingPlatformTopSupportForGroundMovement(_lastKnownPlatform))
            return true;

        return ShouldPreserveSamePlatformChaseDuringMicroSeparation(warrior);
    }

    private bool ShouldPreserveSamePlatformChaseDuringMicroSeparation(Warrior warrior)
    {
        if (!preserveSamePlatformChaseDuringMicroSeparation)
            return false;

        if (warrior == null || warrior.IsDead)
            return false;

        if (_deathStarted || currentHealth <= 0f)
            return false;

        if (_isJumping || activesJumpCoroutine != null || _warriorTopReboundActive)
            return false;

        if (_missedMovingPlatformLandingRecoveryActive || Time.time < _forceAirborneAnimationUntil)
            return false;

        PlatFormColliderTrigger myPlatform = CurrentplatForm != null
            ? CurrentplatForm
            : _lastKnownPlatform;

        if (myPlatform == null || myPlatform.platformCollider == null)
            return false;

        bool warriorCurrentlySamePlatform =
            warrior.CurrentplatForm != null &&
            warrior.CurrentplatForm == myPlatform;

        bool recentlyConfirmedSharedPlatform =
            _lastConfirmedSharedPlatform == myPlatform &&
            Time.time <= _lastConfirmedSharedPlatformTime + samePlatformMicroSeparationGraceTime;

        bool warriorRecentlySamePlatform =
            _lastKnownWarriorPlatform == myPlatform &&
            Time.time <= _lastKnownWarriorPlatformTime + samePlatformMicroSeparationGraceTime;

        if (!warriorCurrentlySamePlatform && !recentlyConfirmedSharedPlatform && !warriorRecentlySamePlatform)
            return false;

        return IsBodyWithinSamePlatformMicroSeparationTolerance(myPlatform);
    }

    private bool IsBodyWithinSamePlatformMicroSeparationTolerance(PlatFormColliderTrigger platform)
    {
        if (platform == null || platform.platformCollider == null)
            return false;

        Collider2D body = GetZalaytyBodyCollider();
        if (body == null || !body.enabled)
            return false;

        Physics2D.SyncTransforms();

        Bounds platformBounds = platform.platformCollider.bounds;
        Bounds bodyBounds = body.bounds;

        float horizontalSkin = Mathf.Max(
            samePlatformMicroSeparationHorizontalSkin,
            movingLandingHorizontalSkin);

        bool horizontallyStillOnPlatform =
            bodyBounds.max.x > platformBounds.min.x - horizontalSkin &&
            bodyBounds.min.x < platformBounds.max.x + horizontalSkin;

        if (!horizontallyStillOnPlatform)
            return false;

        float bottomAboveTop = bodyBounds.min.y - platformBounds.max.y;
        float maxAllowedGap = Mathf.Max(
            samePlatformMicroSeparationMaxVerticalGap,
            groundedStickMaxVerticalSeparation,
            Groundradius);

        return bottomAboveTop >= -independentLandingBand &&
               bottomAboveTop <= maxAllowedGap;
    }

    private bool ShouldBlockGroundLogicBecauseZalaytyIsAirborne()
    {
        if (_deathStarted || currentHealth <= 0f)
            return false;

        if (IsAttackAnimationActive())
            return false;

        bool controlledJumpActive = _isJumping || activesJumpCoroutine != null;
        if (controlledJumpActive)
        {
            ForceZalaytyAirborneAnimationOnly();
            return true;
        }

        Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
        bool preserveSamePlatformMicroSeparation = ShouldPreserveSamePlatformChaseDuringMicroSeparation(warrior);

        bool platformMissing = CurrentplatForm == null || CurrentplatForm.platformCollider == null;
        bool platformIsStale = !platformMissing && !IsZalaytyGroundedEnoughForGroundMovement(CurrentplatForm);
        bool forcedAirborneLock = Time.time < _forceAirborneAnimationUntil;

        if (preserveSamePlatformMicroSeparation)
        {
            _missedMovingPlatformLandingRecoveryActive = false;
            return false;
        }

        if (!platformMissing && !platformIsStale && !forcedAirborneLock)
        {
            _missedMovingPlatformLandingRecoveryActive = false;
            return false;
        }

        if (platformIsStale)
            CurrentplatForm = null;

        bool noGroundPoints = CountGroundPoints() == 0;

        if (platformMissing || platformIsStale || noGroundPoints || forcedAirborneLock || _missedMovingPlatformLandingRecoveryActive)
        {
            StopMoveTowardCoroutine();
            ForceZalaytyAirborneAnimationOnly();
            return true;
        }

        return false;
    }

    private void ForceZalaytyAirborneAnimationOnly()
    {
        ExitWaitAnimation();

        if (IsAttackAnimationActive())
            return;

        JumpAnimationDisplay();
    }

    private void MoveZalaytyBody(Vector2 position)
    {
        Vector2 finalPosition = position;

        // Moving platforms can behave like elevators/carriers. Zalayty's independent
        // chase coroutine can run after the platform FixedUpdate, so the platform must
        // not be the only object applying the carry delta. When Zalayty is actively
        // chasing, add the current platform delta here:
        // finalPosition = ownMovePosition + platformDelta.
        if (TryGetHorizontalMovingPlatformCarryDeltaForOwnMove(out Vector2 carryDelta))
            finalPosition += carryDelta;

        if (TryGetVerticalMovingPlatformCarryDeltaForOwnMove(out Vector2 verticalCarryDelta))
            finalPosition += verticalCarryDelta;

        RememberZalaytyBodyMoveVelocity(finalPosition);
        RememberIndependentMoveRequest(finalPosition);

        if (rigidbody2 != null)
            rigidbody2.MovePosition(finalPosition);
        else
            transform.position = new Vector3(finalPosition.x, finalPosition.y, transform.position.z);
    }

    private void RememberZalaytyBodyMoveVelocity(Vector2 finalPosition)
    {
        Vector2 currentPosition = rigidbody2 != null
            ? rigidbody2.position
            : (Vector2)transform.position;

        Vector2 delta = finalPosition - currentPosition;

        _lastZalaytyBodyMoveVelocity = Time.fixedDeltaTime > 0f
            ? delta / Time.fixedDeltaTime
            : Vector2.zero;

        _lastZalaytyBodyMoveVelocityTime = Time.time;
        _lastZalaytyBodyMoveWasPlatformChangeJump =
            _isJumping ||
            activesJumpCoroutine != null ||
            _activeJumpTargetPlatform != null ||
            CurrentplatForm == null;
    }

    private void StopHorizontalVelocityIfNeeded()
    {
        ClearIndependentMoveRequest();

        if (!stopHorizontalVelocityOnIndependentStop || rigidbody2 == null)
            return;

        Vector2 v = rigidbody2.linearVelocity;
        v.x = 0f;
        rigidbody2.linearVelocity = v;
    }

    private bool IsAttackAnimationActive()
    {
        return animator != null &&
               (animator.GetBool("isAttacking") ||
                animator.GetBool("isAttacking2") ||
                animator.GetBool("isAttacking3") ||
                animator.GetBool("isDying"));
    }

    private bool IsXInsidePlatform(PlatFormColliderTrigger platform, float x)
    {
        if (platform == null || platform.platformCollider == null)
            return false;

        Bounds b = platform.platformCollider.bounds;
        return x >= b.min.x && x <= b.max.x;
    }

    public override void StopMoveTowardCoroutine()
    {
        base.StopMoveTowardCoroutine();
        _hasCurrentIndependentMoveTarget = false;
        _currentIndependentMoveTargetWarrior = null;
        ClearIndependentMoveRequest();
        StopHorizontalVelocityIfNeeded();
    }

    public void NotifyIndependentPlatformCollision(PlatFormColliderTrigger platform, Collision2D collision)
    {
        if (platform == null)
            return;

        if (!useIndependentMovement)
        {
            CurrentplatForm = platform;
            _lastKnownPlatform = platform;
            _lastKnownPlatformTime = Time.time;
            return;
        }

        if (!IsIndependentTopLanding(platform))
            return;

        if (activesJumpCoroutine != null)
            StopCoroutine(activesJumpCoroutine);

        Vector2 preferredPosition = rigidbody2 != null
            ? rigidbody2.position
            : (Vector2)transform.position;

        TryFinishIndependentJumpOnPlatformIfReallyGrounded(platform, preferredPosition.x);
    }

    private bool IsIndependentTopLanding(PlatFormColliderTrigger platform)
    {
        if (platform == null || platform.platformCollider == null)
            return false;

        Collider2D body = NormalCollider != null ? NormalCollider : collider2;
        if (body == null)
            return false;

        Bounds pb = platform.platformCollider.bounds;
        Bounds cb = body.bounds;

        bool horizontallyOverTop =
            cb.max.x > pb.min.x + movingLandingHorizontalSkin &&
            cb.min.x < pb.max.x - movingLandingHorizontalSkin;

        float bottomAboveTop = cb.min.y - pb.max.y;
        bool closeToTop =
            bottomAboveTop >= -independentLandingBand &&
            bottomAboveTop <= landingYOffset + movingLandingSnapMaxVerticalDistance;

        bool movingDownOrStable = rigidbody2 == null || rigidbody2.linearVelocity.y <= 0.08f;

        return horizontallyOverTop && closeToTop && movingDownOrStable;
    }

    private enum Side { Left, Right }

    private PlatFormColliderTrigger _activeJumpDownSourcePlatform;
    private PlatFormColliderTrigger _activeJumpTargetPlatform;
    private bool _warriorTopReboundActive;
    private float _nextAllowedWarriorTopReboundTime;
    private float _warriorTopReboundBusyUntil;
    private Coroutine _restoreWarriorTopReboundCollisionCoroutine;
    private readonly List<Collider2D> _ignoredWarriorTopReboundColliders = new List<Collider2D>();

    private IEnumerator MoveAndJumpToPlatform(PlatFormColliderTrigger nextPlatform, Transform warrior)
    {
        PlatFormColliderTrigger sourcePlatform = CurrentplatForm;

        if (sourcePlatform == null || sourcePlatform.platformCollider == null ||
            nextPlatform == null || nextPlatform.platformCollider == null)
            yield break;

        if (_isJumping) yield break;

        Bounds curB = sourcePlatform.platformCollider.bounds;
        Bounds nextB = nextPlatform.platformCollider.bounds;

        bool goingDown = nextB.max.y < curB.max.y - 0.05f;

        float desiredX = warrior != null ? warrior.position.x : nextB.center.x;
        Side side = ChooseBestSideForTransition(curB, nextB, desiredX);

        // 1) Move to takeoff position.
        // Going down is now different from a side/up transition:
        // Zalayty may take off above the lower target and pass through the source platform.
        float takeoffXInside;
        if (goingDown)
        {
            takeoffXInside = ChooseBestLocationForTransition(curB, nextB, desiredX);
            takeoffXInside = Mathf.Clamp(
                takeoffXInside,
                curB.min.x + takeoffEdgeMargin,
                curB.max.x - takeoffEdgeMargin
            );
        }
        else
        {
            takeoffXInside = (side == Side.Right)
                ? (curB.max.x - takeoffEdgeMargin)
                : (curB.min.x + takeoffEdgeMargin);
        }

        if (!useIndependentMovement)
            takeoffXInside = ClampToCurrentPlatform(takeoffXInside);

        SetDirectionVariables(takeoffXInside);

        if (CanMove && activesMoveCoroutine == null)
        {
            StartZalaytyMoveToX(takeoffXInside);
        }

        float moveTimeout = 1.25f;
        while (moveTimeout > 0f)
        {
            if (CurrentplatForm == null && sourcePlatform == null) yield break;
            if (CurrentplatForm == nextPlatform) yield break;
            if (activesMoveCoroutine == null) break;

            moveTimeout -= Time.deltaTime;
            yield return null;
        }
        StopMoveTowardCoroutine();
        ExitWaitAnimation();

        // 2) Landing X:
        // - going down: use your ChooseBestLocationForTransition (fixed + clamped)
        // - going up/side: still bias toward warrior X
        float safeMinX = nextB.min.x + 0.35f;
        float safeMaxX = nextB.max.x - 0.35f;

        float landingX;
        if (goingDown)
        {
            landingX = ChooseBestLocationForTransition(curB, nextB, desiredX);
            landingX = Mathf.Clamp(landingX, safeMinX, safeMaxX);
        }
        else
        {
            landingX = Mathf.Clamp(desiredX, safeMinX, safeMaxX);
        }

        Vector2 landing = new Vector2(landingX, nextB.max.y + landingYOffset);

        Warrior warriorInstance = warrior != null
            ? warrior.GetComponent<Warrior>()
            : (GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null);

        landing = ResolvePlatformJumpLandingAwayFromWarriorTop(
            landing,
            nextPlatform,
            warriorInstance);

        // 3) Zalayty-only jump-down pass-through.
        // Do NOT move Warrior logic and do NOT globally disable the platform.
        // Only the source platform ignores Zalayty's colliders, then restores itself
        // when Zalayty is no longer physically touching/overlapping that source body.
        if (goingDown)
        {
            _activeJumpDownSourcePlatform = sourcePlatform;

            bool sourcePassThroughStarted =
                sourcePlatform.RequestZalaytyJumpDownThroughSourcePlatform(this);

            if (!sourcePassThroughStarted)
                _activeJumpDownSourcePlatform = null;

            // Fallback for an unusual platform that refuses the request.
            // Normal PlatFormColliderTrigger / PlatFormPlfColliderTrigger should return true.
            if (!sourcePassThroughStarted && rigidbody2 != null)
            {
                float outsideX = (side == Side.Right)
                    ? (curB.max.x + dropSideOutEpsilon)
                    : (curB.min.x - dropSideOutEpsilon);

                rigidbody2.position = new Vector2(outsideX, rigidbody2.position.y);
            }

            if (rigidbody2 != null)
            {
                Vector2 v = rigidbody2.linearVelocity;
                if (v.y > -0.05f)
                    v.y = -0.05f;
                rigidbody2.linearVelocity = v;
            }
        }

        // 4) Jump
        float dx = Mathf.Abs(landing.x - transform.position.x);
        float dy = landing.y - transform.position.y;

        float jumpHeight = goingDown ? 1.25f : Mathf.Clamp(dy + 2.5f, 2.5f, 10f);
        float duration = Mathf.Clamp((dx / Mathf.Max(0.01f, Speed)) * 0.65f, 0.25f, 0.8f);
        // ExitWaitAnimation();
        bool platformJumpStarted = StartZalaytyJumpTo(landing, jumpHeight, duration, nextPlatform);
        if (!platformJumpStarted)
            yield break;

        float landTimeout = 2.0f;
        while (landTimeout > 0f)
        {
            if (CurrentplatForm == nextPlatform)
                break;

            // If the moving target was missed, JumpArcIndependent has already
            // switched Zalayty to natural airborne recovery. Do not wait the full
            // timeout while the follow loop is blocked inside this transition.
            if (!_isJumping && activesJumpCoroutine == null)
                break;

            // If he landed safely on another platform, let the main loop recompute
            // from that real grounded state instead of forcing the old target.
            if (CurrentplatForm != null &&
                CurrentplatForm != nextPlatform &&
                IsZalaytyReallyGroundedOnPlatform(CurrentplatForm))
                break;

            landTimeout -= Time.deltaTime;
            yield return null;
        }

        if (activesJumpCoroutine != null)
            StopJumpTowardCoroutine();
    }

    private Side ChooseBestSideForTransition(Bounds curB, Bounds nextB, float desiredX)
    {
        // Next platform is fully to the left/right
        if (nextB.max.x < curB.min.x) return Side.Left;
        if (nextB.min.x > curB.max.x) return Side.Right;

        // Overlapping (stacked): choose side toward desiredX (warrior)
        return (desiredX >= curB.center.x) ? Side.Right : Side.Left;
    }

    /// <summary>
    ///
    /// </summary>
    private float ChooseBestLocationForTransition(Bounds curB, Bounds nextB, float desiredX)
    {
        float safeMinX = nextB.min.x + 0.35f;
        float safeMaxX = nextB.max.x - 0.35f;

        // Compute overlap region in X (where dropping down is most natural)
        float overlapMin = Mathf.Max(curB.min.x, nextB.min.x);
        float overlapMax = Mathf.Min(curB.max.x, nextB.max.x);

        bool hasOverlap = overlapMax > overlapMin + 0.01f;

        if (hasOverlap)
        {
            // Prefer desiredX if inside overlap, else clamp to overlap edge
            float x = Mathf.Clamp(desiredX, overlapMin, overlapMax);
            return Mathf.Clamp(x, safeMinX, safeMaxX);
        }

        // No overlap:
        // If current is left of next -> land near left side of next
        if (curB.max.x <= nextB.min.x)
            return safeMinX;

        // If current is right of next -> land near right side of next
        if (curB.min.x >= nextB.max.x)
            return safeMaxX;

        // Fallback (should be rare): clamp desired into next safe bounds
        return Mathf.Clamp(desiredX, safeMinX, safeMaxX);
    }

    private bool ShouldStopSamePlatformChaseForMainBoxContact(Warrior warrior)
    {
        if (!requireMainBoxContactToStopAndAttackOnSamePlatform)
            return false;

        if (warrior == null || warrior.IsDead)
            return false;

        if (!CanUseWarriorMainBoxContactStop(warrior))
            return false;

        return HasValidMainBoxSideContactForAttack(warrior, samePlatformMainBoxContactSkin);
    }

    private bool CanUseWarriorMainBoxContactStop(Warrior warrior)
    {
        if (warrior == null)
            return false;

        // Best case: both are known to be on the same platform.
        if (CurrentplatForm != null && warrior.CurrentplatForm != null)
            return CurrentplatForm == warrior.CurrentplatForm ||
                   stopOnWarriorMainBoxContactEvenDuringPlatformMismatch;

        // During jumps, trigger exits, or one-frame platform refresh gaps, CurrentplatForm
        // may be null even though the main bodies are already physically touching.
        // Stop anyway, otherwise Zalayty restarts chase and pushes Warrior.
        return stopOnWarriorMainBoxContactEvenDuringPlatformMismatch;
    }
    private void MaintainSamePlatformContactCombat(Warrior warrior, bool mainBoxesTouching)
    {
        // Separation wins before animation/cooldown. This is what lets Zalayty
        // immediately resume chase as soon as the MAIN BoxCollider2D bodies are apart.
        if (!mainBoxesTouching)
        {
            ReleaseContactCombatForImmediateChase();
            return;
        }

        StopMoveTowardCoroutine();
        StopHorizontalVelocityIfNeeded();
        ExitWaitAnimation();

        if (warrior == null || warrior.IsDead)
        {
            ClearSamePlatformContactCombatLock();
            return;
        }

        if (requireSideContactForMainBoxAttack && IsZalaytyOnWarriorTopByBounds(warrior))
        {
            ReleaseContactCombatForImmediateChase();
            inRangeOrAttacking = false;
            return;
        }

        FaceTowards(warrior.transform);
        _samePlatformContactCombatLocked = true;

        bool attackActive = IsAttackAnimationActive();

        // Attack animation is still active AND bodies are still touching:
        // stay stopped and do not restart/stack attacks.
        if (attackActive)
        {
            _samePlatformContactAttackWasActive = true;
            return;
        }

        // Attack has just ended while the bodies are still touching. Start the
        // cooldown window so Warrior has time to answer before Zalayty attacks again.
        if (_samePlatformContactAttackWasActive)
        {
            _samePlatformContactAttackWasActive = false;
            _nextAllowedSamePlatformContactAttackTime = Time.time + Mathf.Max(0.01f, attackCooldown);
            return;
        }

        // Main boxes are still touching. Stay locked in contact combat.
        // If cooldown has expired, start a new attack. Otherwise remain stopped.
        if (repeatAttackWhileMainBoxesStillTouchingOnSamePlatform)
            TryStartSamePlatformContactAttack(warrior);
    }

    private void ClearSamePlatformContactCombatLock()
    {
        _samePlatformContactCombatLocked = false;
        _samePlatformContactAttackWasActive = false;
        _nextAllowedSamePlatformContactAttackTime = 0f;
    }

    private void ReleaseContactCombatForImmediateChase()
    {
        ClearSamePlatformContactCombatLock();
        ResetContactAttackAnimationBooleansForImmediateChase();
        ExitWaitAnimation();
    }

    private void ResetContactAttackAnimationBooleansForImmediateChase()
    {
        if (animator == null)
            return;

        // We only cancel attack bools. Do not touch isDying or other state flags.
        animator.SetBool("isAttacking", false);
        animator.SetBool("isAttacking2", false);
        animator.SetBool("isAttacking3", false);
    }

    private bool AreMainBoxesTouchingForContactCombat(Warrior warrior)
    {
        if (!requireMainBoxContactToStopAndAttackOnSamePlatform)
            return false;

        if (warrior == null || warrior.IsDead)
            return false;

        float skinToUse = useContactReleaseHysteresis && _samePlatformContactCombatLocked
            ? Mathf.Max(samePlatformMainBoxContactSkin, samePlatformMainBoxContactReleaseSkin)
            : samePlatformMainBoxContactSkin;

        return HasValidMainBoxSideContactForAttack(warrior, skinToUse);
    }

    private bool TryStartSamePlatformContactAttack(Warrior warrior)
    {
        if (warrior == null || warrior.collider2 == null || warrior.IsDead)
            return false;

        if (_deathStarted || currentHealth <= 0f || IsStunned || IsAttackTemporarilyDisabled)
            return false;

        if (Time.time < _nextAllowedSamePlatformContactAttackTime)
            return false;

        // Starting a NEW attack must require strict real side contact.
        // Do not use AreMainBoxesTouchingForContactCombat() here, because that method
        // may use the wider release hysteresis skin while a previous contact lock is active.
        // Using the wide release skin to START a new attack is what can create a visible
        // attack with no damage when there is a tiny solver gap.
        if (!HasValidMainBoxSideContactForAttack(warrior, samePlatformMainBoxContactSkin))
            return false;

        // The contact-combat rule requires Zalayty to face Warrior, not to pass
        // another range/front gate after the real main BoxCollider2D contact is confirmed.
        FaceTowards(warrior.transform);

        _samePlatformContactCombatLocked = true;
        _samePlatformContactAttackWasActive = true;
        AttackAnimationDisplay();
        return true;
    }

    private bool AreMainBoxCollidersTouchingOrOverlapping(Warrior warrior, float contactSkin)
    {
        BoxCollider2D zalaytyBox = GetMainBoxColliderForPhysicalContact();
        BoxCollider2D warriorBox = GetWarriorMainBoxCollider(warrior);

        if (zalaytyBox == null || warriorBox == null)
            return false;

        if (!zalaytyBox.enabled || !warriorBox.enabled)
            return false;

        if (zalaytyBox.isTrigger || warriorBox.isTrigger)
            return false;

        if (zalaytyBox.IsTouching(warriorBox))
            return true;

        float skin = Mathf.Max(0f, contactSkin);

        ColliderDistance2D distance = Physics2D.Distance(zalaytyBox, warriorBox);
        if (distance.isValid && (distance.isOverlapped || distance.distance <= skin))
            return true;

        // Bounds fallback is only a safety net for rare timing cases where MovePosition
        // has overlapped the bodies before the physics contact pair is available.
        Bounds z = zalaytyBox.bounds;
        Bounds w = warriorBox.bounds;
        z.Expand(skin * 2f);
        return z.Intersects(w);
    }

    private BoxCollider2D GetMainBoxColliderForPhysicalContact()
    {
        if (NormalCollider is BoxCollider2D normalBox && normalBox.enabled)
            return normalBox;

        if (collider2 is BoxCollider2D baseBox && baseBox.enabled)
            return baseBox;

        BoxCollider2D directBox = GetComponent<BoxCollider2D>();
        if (directBox != null && directBox.enabled)
            return directBox;

        return null;
    }

    private BoxCollider2D GetWarriorMainBoxCollider(Warrior warrior)
    {
        if (warrior == null)
            return null;

        if (warrior.collider2 is BoxCollider2D warriorBox && warriorBox.enabled)
            return warriorBox;

        BoxCollider2D directBox = warrior.GetComponent<BoxCollider2D>();
        if (directBox != null && directBox.enabled)
            return directBox;

        return null;
    }

    private bool TryToPerformAttack(Transform warrior)
    {
        if (EnemyRangeService == null || warrior == null)
            return false;

        EnemyRangeService.SetAttackRange(Range);

        // Some services update IsInRange internally; also keep a distance fallback
        float dist = Vector2.Distance(transform.position, warrior.position);
        bool inRangeByDistance = dist <= Range;

        if (EnemyRangeService.IsInRange || inRangeByDistance)
        {
            // Zalayty must attack from the front. If Warrior moved behind him,
            // flip before trying to trigger the attack animation.
            FaceTowards(warrior);

            EnemyRangeService.TryAction(warrior, Range, OnAttackPerformed);
            return true;
        }

        return false;
    }

    public override void OnAttackPerformed(IAttacker attacker, Transform attackedTarget)
    {
        if (_deathStarted) return;                 // or currentHealth <= 0
        if (currentHealth <= 0f) return;
        if (IsStunned) return;
        if (IsAttackTemporarilyDisabled) return;   // same guard here

        var warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null || warrior.collider2 == null) return;
        if (warrior.IsDead) return;

        // respect your front rule if needed
        if (requireTargetInFront && !IsWarriorInFront(warrior.transform))
            return;

        FaceTowards(warrior.transform);

        if (!IsWarriorInFront(warrior.transform))
            return;

        AttackAnimationDisplay();
    }
    private bool _isWaitingAnimActive = false;

    private void EnterWaitAnimation()
    {
        if (_isWaitingAnimActive) return;

        _isWaitingAnimActive = true;
        WaitAnimationDisplay();
    }
    private void ExitWaitAnimation()
    {
        _isWaitingAnimActive = false;
    }

    public bool IsAirborne() => CountGroundPoints() == 0 && activesJumpCoroutine != null;

    public int CountGroundPoints()
    {
        int count = 0;
        foreach (Transform pt in GroundPoints)
        {
            if (Physics2D.OverlapCircleAll(pt.position, Groundradius, PlatformLayer).Length > 0)
                count++;
        }
        return count;
    }

    // Animation Event: put this on the exact HIT frame in the attack clip
    public void AE_Zalayty_Impact()
    {
        // 0) Hard guards (dead / disabled / stunned)
        if (!CanDealDamageNow()) return;                // if accessible; otherwise use currentHealth <= 0
        //if (currentHealth <= 0f) return;
        //if (IsStunned) return;
        //if (IsAttackTemporarilyDisabled) return;   // IMPORTANT (this is the key fix)

        var warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null || warrior.collider2 == null) return;
        if (warrior.IsDead) return;

        // Optional extra guard: if warrior is currently attacking, don't let Zalayty "trade" on same frame window
        // (only if this matches your CrawlingMonster behavior)
        // if (warriorIsAttackingNow(warrior)) return;

        // Same-platform damage must be validated by the real MAIN BoxCollider2D contact.
        // This avoids range-only damage and matches the stop/attack rule above.
        bool samePlatform =
            warrior.CurrentplatForm != null &&
            CurrentplatForm != null &&
            warrior.CurrentplatForm == CurrentplatForm;

        // The attack was already started by strict contact. On the exact impact frame,
        // allow a small extra skin so a visible hit is not rejected by a tiny physics gap.
        // This skin is still side-contact validated, so top-bound landings remain invalid.
        float impactSkin = Mathf.Max(samePlatformMainBoxContactSkin, zalaytyImpactHitSkin);
        bool mainBoxesTouching = HasValidMainBoxSideContactForAttack(warrior, impactSkin);

        bool canHit =
            (!requireSamePlatformToHit || samePlatform) &&
            NormalCollider != null &&
            (!requireTouchingToHit || mainBoxesTouching);

        if (!canHit) return;

        // Optional front check if you want it enabled by inspector flag
        if (requireTargetInFront && !IsWarriorInFront(warrior.transform))
            return;

        // Spawn spark only after the side-contact hit has been validated.
        // This prevents top-bound landings from showing a fake hit.
        SpawnSparkAtContact(warrior);

        // Shield block (front-only handled inside Warrior.TryBlockEnemyHit)
        bool blocked = warrior.TryBlockEnemyHit(this);
        if (blocked) return;

        // Re-check in case the block/stun/other effect disabled attack on same frame
        if (IsAttackTemporarilyDisabled || IsStunned) return;

        // Normal damage
        warrior.TakeDamage(attackDamage);
        warrior.SpawnBloodshedEffectFromEnemy(this);
    }

    public void FaceTowards(Transform t)
    {
        if (t == null) return;

        float dx = t.position.x - transform.position.x;
        if (dx > 0f && leftFacing) Flip();
        else if (dx < 0f && rightFacing) Flip();
    }

    private void SpawnSparkAtContact(Warrior warrior)
    {
        if (sparkPrefab == null || warrior == null)
            return;

        Collider2D warriorMainCollider = GetWarriorMainBoxCollider(warrior);
        Collider2D zalaytyMainCollider = GetMainBoxColliderForPhysicalContact();

        if (warriorMainCollider == null)
            warriorMainCollider = warrior.collider2;

        if (zalaytyMainCollider == null)
            zalaytyMainCollider = NormalCollider != null ? NormalCollider : collider2;

        if (warriorMainCollider == null || zalaytyMainCollider == null)
            return;

        Bounds w = warriorMainCollider.bounds;
        Bounds e = zalaytyMainCollider.bounds;

        // Contact point on the side between enemy and warrior.
        float x = e.center.x < w.center.x ? w.min.x : w.max.x;
        float y = Mathf.Clamp(e.center.y, w.min.y, w.max.y);
        Vector3 computedContactPos = new Vector3(x, y, transform.position.z) + sparkOffset;

        // If you assigned a hitPoint in the inspector, use it. Otherwise use the
        // computed side-contact point. This prevents a null hitPoint crash and fixes
        // the previous bug where computedContactPos was calculated but ignored.
        Vector3 spawnPos = hitPoint != null ? hitPoint.position + sparkOffset : computedContactPos;
        GameObject fx = Instantiate(sparkPrefab, spawnPos, Quaternion.identity);

        // IMPORTANT: do not parent to Warrior, or the spark will follow him.
        fx.transform.localScale *= sparkScale;

        // World-space particles stay visually at the impact point even if the enemy/warrior moves.
        var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ps.Play(true);
        }

        Destroy(fx, sparkDestroyAfter);
    }

    private static readonly ContactPoint2D[] _contacts = new ContactPoint2D[16];

    private void OnCollisionExit2D(Collision2D collision)
    {
        Warrior warrior = collision.collider != null
            ? collision.collider.GetComponentInParent<Warrior>()
            : null;

        if (warrior == null)
            return;

        // Do not release only because Unity sent CollisionExit. With MovePosition and
        // solver separation, Exit can happen for one physics frame while the bodies are
        // still inside the release skin. The Follow loop will release as soon as the
        // bodies are truly clear.
        if (_samePlatformContactCombatLocked && !AreMainBoxesTouchingForContactCombat(warrior))
        {
            ReleaseContactCombatForImmediateChase();
            inRangeOrAttacking = false;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (TryHandleWarriorTopReboundCollision(collision))
            return;

        if (TryHandleDifferentPlatformWarriorImpact(collision))
            return;

        TryHandleSamePlatformWarriorMainBoxContact(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (TryHandleWarriorTopReboundCollision(collision))
            return;

        if (TryHandleDifferentPlatformWarriorImpact(collision))
            return;

        TryHandleSamePlatformWarriorMainBoxContact(collision);
    }

    private bool TryHandleDifferentPlatformWarriorImpact(Collision2D collision)
    {
        if (!enableDifferentPlatformWarriorImpactAbsorption)
            return false;

        if (collision == null || collision.collider == null)
            return false;

        if (Time.time < _nextAllowedDifferentPlatformImpactTime)
            return false;

        Warrior warrior = collision.collider.GetComponentInParent<Warrior>();
        if (warrior == null || warrior.collider2 == null || warrior.IsDead)
            return false;

        if (!IsKnownDifferentPlatformFromWarrior(warrior))
            return false;

        // Do not steal the special top-bound rule. Top contact is handled by
        // TryHandleWarriorTopReboundCollision() above. This method is only for
        // side/body shock while Zalayty is moving between different platforms.
        if (IsZalaytyOnWarriorTop(collision, warrior))
            return false;

        bool platformChangeMotion =
            _isJumping ||
            activesJumpCoroutine != null ||
            _activeJumpTargetPlatform != null ||
            _lastZalaytyBodyMoveWasPlatformChangeJump;

        if (!platformChangeMotion)
            return false;

        Collider2D zalaytyBody = GetMainBoxColliderForPhysicalContact();
        Collider2D warriorBody = GetWarriorMainBoxCollider(warrior);

        if (zalaytyBody == null || warriorBody == null)
            return false;

        if (!AreBodyBoundsCloseEnoughForDifferentPlatformImpact(zalaytyBody, warriorBody))
            return false;

        Vector2 incomingVelocity = ResolveDifferentPlatformImpactVelocity(
            collision,
            zalaytyBody,
            warriorBody,
            out float incomingSpeed);

        if (incomingSpeed < differentPlatformImpactMinSpeed)
            return false;

        Vector2 directionToWarrior =
            (Vector2)warriorBody.bounds.center - (Vector2)zalaytyBody.bounds.center;

        if (directionToWarrior.sqrMagnitude > 0.0001f && incomingVelocity.sqrMagnitude > 0.0001f)
        {
            float dot = Vector2.Dot(incomingVelocity.normalized, directionToWarrior.normalized);
            if (dot < differentPlatformImpactMinIntoWarriorDot)
                return false;
        }

        bool absorbed = warrior.TryAbsorbZalaytyDifferentPlatformImpact(
            this,
            zalaytyBody.bounds.center,
            incomingVelocity,
            incomingSpeed);

        if (!absorbed)
            return false;

        _nextAllowedDifferentPlatformImpactTime =
            Time.time + Mathf.Max(0.01f, differentPlatformImpactCooldown);

        InterruptZalaytyAfterDifferentPlatformWarriorImpact();
        return true;
    }

    private bool IsKnownDifferentPlatformFromWarrior(Warrior warrior)
    {
        if (warrior == null)
            return false;

        if (CurrentplatForm != null && warrior.CurrentplatForm != null)
            return CurrentplatForm != warrior.CurrentplatForm;

        // Strict mode avoids changing same-platform combat behavior during one-frame
        // CurrentplatForm refresh gaps. Turn this off only if you also want null-platform
        // airborne impacts to be absorbed.
        if (requireKnownDifferentPlatformsForImpactAbsorption)
            return false;

        return CurrentplatForm != warrior.CurrentplatForm;
    }

    private bool AreBodyBoundsCloseEnoughForDifferentPlatformImpact(Collider2D zalaytyBody, Collider2D warriorBody)
    {
        Bounds z = zalaytyBody.bounds;
        Bounds w = warriorBody.bounds;

        // Tiny expansion catches one-frame solver gaps during fast MovePosition arcs.
        z.Expand(0.04f);
        w.Expand(0.04f);

        return z.Intersects(w);
    }

    private Vector2 ResolveDifferentPlatformImpactVelocity(
        Collision2D collision,
        Collider2D zalaytyBody,
        Collider2D warriorBody,
        out float incomingSpeed)
    {
        Vector2 trackedVelocity = Vector2.zero;
        if (Time.time - _lastZalaytyBodyMoveVelocityTime <= 0.20f)
            trackedVelocity = _lastZalaytyBodyMoveVelocity;

        Vector2 rbVelocity = rigidbody2 != null
            ? rigidbody2.linearVelocity
            : Vector2.zero;

        Vector2 relativeVelocity = collision != null
            ? collision.relativeVelocity
            : Vector2.zero;

        float trackedSpeed = trackedVelocity.magnitude;
        float rbSpeed = rbVelocity.magnitude;
        float relativeSpeed = relativeVelocity.magnitude;

        incomingSpeed = Mathf.Max(trackedSpeed, rbSpeed, relativeSpeed);

        Vector2 bestVelocity = trackedVelocity;

        if (rbSpeed > bestVelocity.magnitude)
            bestVelocity = rbVelocity;

        // Collision relativeVelocity is useful for magnitude, but MovePosition can make
        // its direction awkward. Prefer tracked/rigidbody direction when available.
        if (bestVelocity.sqrMagnitude < 0.0001f && relativeVelocity.sqrMagnitude > 0.0001f)
            bestVelocity = relativeVelocity;

        if (bestVelocity.sqrMagnitude < 0.0001f && incomingSpeed > 0f)
        {
            Vector2 directionToWarrior =
                (Vector2)warriorBody.bounds.center - (Vector2)zalaytyBody.bounds.center;

            if (directionToWarrior.sqrMagnitude > 0.0001f)
                bestVelocity = directionToWarrior.normalized * incomingSpeed;
        }

        return bestVelocity.sqrMagnitude > 0.0001f
            ? bestVelocity.normalized * incomingSpeed
            : Vector2.zero;
    }

    private void InterruptZalaytyAfterDifferentPlatformWarriorImpact()
    {
        _differentPlatformImpactLockUntil =
            Time.time + Mathf.Max(0f, differentPlatformImpactZalaytyLockSeconds);

        StopMoveTowardCoroutine();
        StopHorizontalVelocityIfNeeded();

        bool wasControlledPlatformJump =
            _isJumping ||
            activesJumpCoroutine != null ||
            _activeJumpTargetPlatform != null ||
            _lastZalaytyBodyMoveWasPlatformChangeJump;

        if (interruptZalaytyJumpOnDifferentPlatformImpact && wasControlledPlatformJump)
        {
            PlatFormColliderTrigger missedPlatform = _activeJumpTargetPlatform;
            StopJumpTowardCoroutine();
            BeginMissedMovingPlatformLandingRecovery(missedPlatform);
            return;
        }

        if (rigidbody2 != null)
        {
            Vector2 v = rigidbody2.linearVelocity;
            v.x = 0f;
            rigidbody2.linearVelocity = v;
        }
    }

    private bool TryHandleSamePlatformWarriorMainBoxContact(Collision2D collision)
    {
        Warrior warrior = collision.collider != null
            ? collision.collider.GetComponentInParent<Warrior>()
            : null;

        if (warrior == null || warrior.collider2 == null || warrior.IsDead)
            return false;

        if (!CanUseWarriorMainBoxContactStop(warrior))
            return false;

        // Top contact is handled by the rebound rule, not by the melee stop/attack rule.
        if (IsZalaytyOnWarriorTop(collision, warrior))
            return false;

        if (!ShouldStopSamePlatformChaseForMainBoxContact(warrior))
            return false;

        MaintainSamePlatformContactCombat(warrior, true);
        return true;
    }

    private bool TryHandleWarriorTopReboundCollision(Collision2D collision)
    {
        if (!enableWarriorTopRebound)
            return false;

        if (_warriorTopReboundActive && Time.time < _warriorTopReboundBusyUntil)
            return false;

        if (_warriorTopReboundActive && Time.time >= _warriorTopReboundBusyUntil)
            _warriorTopReboundActive = false;

        if (Time.time < _nextAllowedWarriorTopReboundTime)
            return false;

        Warrior warrior = collision.collider != null
            ? collision.collider.GetComponentInParent<Warrior>()
            : null;

        if (warrior == null || warrior.collider2 == null || warrior.IsDead)
            return false;

        if (!IsZalaytyOnWarriorTop(collision, warrior))
            return false;

        return StartControlledWarriorTopRebound(warrior);
    }

    private bool IsZalaytyOnWarriorTop(Collision2D collision, Warrior warrior)
    {
        int count = collision.GetContacts(_contacts);

        for (int i = 0; i < count; i++)
        {
            // For this Zalayty collision, a strong upward normal means he is standing
            // on the Warrior's top surface instead of touching the Warrior side.
            if (_contacts[i].normal.y >= warriorTopReboundNormalY)
                return true;
        }

        Collider2D body = GetZalaytyBodyCollider();
        if (body == null || warrior == null || warrior.collider2 == null)
            return false;

        Bounds z = body.bounds;
        Bounds w = warrior.collider2.bounds;

        bool xOverlap = z.max.x > w.min.x && z.min.x < w.max.x;
        bool bottomIsNearWarriorTop =
            z.min.y >= w.max.y - warriorTopReboundBoundsBand &&
            z.min.y <= w.max.y + warriorTopReboundBoundsBand * 2f;
        bool movingDownOrStable = rigidbody2 == null || rigidbody2.linearVelocity.y <= 0.65f;

        return xOverlap && bottomIsNearWarriorTop && movingDownOrStable;
    }

    private bool IsZalaytyOnWarriorTopByBounds(Warrior warrior)
    {
        BoxCollider2D zalaytyBox = GetMainBoxColliderForPhysicalContact();
        BoxCollider2D warriorBox = GetWarriorMainBoxCollider(warrior);

        if (zalaytyBox == null || warriorBox == null)
            return false;

        Bounds z = zalaytyBox.bounds;
        Bounds w = warriorBox.bounds;

        bool xOverlap = z.max.x > w.min.x && z.min.x < w.max.x;
        bool zalaytyAboveWarrior = z.center.y > w.center.y;
        bool bottomIsNearWarriorTop =
            z.min.y >= w.max.y - warriorTopReboundBoundsBand &&
            z.min.y <= w.max.y + warriorTopReboundBoundsBand * 2f;
        bool movingDownOrStable = rigidbody2 == null || rigidbody2.linearVelocity.y <= 0.65f;

        return xOverlap && zalaytyAboveWarrior && bottomIsNearWarriorTop && movingDownOrStable;
    }

    private bool HasValidMainBoxSideContactForAttack(Warrior warrior, float contactSkin)
    {
        if (!AreMainBoxCollidersTouchingOrOverlapping(warrior, contactSkin))
            return false;

        if (!requireSideContactForMainBoxAttack)
            return true;

        // Landing on Warrior's top bound is rebound-only. It must never start, keep,
        // or validate a Zalayty melee attack.
        if (IsZalaytyOnWarriorTopByBounds(warrior))
            return false;

        return HasEnoughVerticalOverlapForSideAttack(warrior);
    }

    private bool HasEnoughVerticalOverlapForSideAttack(Warrior warrior)
    {
        BoxCollider2D zalaytyBox = GetMainBoxColliderForPhysicalContact();
        BoxCollider2D warriorBox = GetWarriorMainBoxCollider(warrior);

        if (zalaytyBox == null || warriorBox == null)
            return false;

        Bounds z = zalaytyBox.bounds;
        Bounds w = warriorBox.bounds;

        float verticalOverlap = Mathf.Min(z.max.y, w.max.y) - Mathf.Max(z.min.y, w.min.y);
        if (verticalOverlap <= 0f)
            return false;

        float requiredByRatio = Mathf.Min(z.size.y, w.size.y) * sideAttackMinVerticalOverlapRatio;
        float requiredOverlap = Mathf.Max(sideAttackMinVerticalOverlap, requiredByRatio);

        return verticalOverlap >= requiredOverlap;
    }

    private Vector2 ResolvePlatformJumpLandingAwayFromWarriorTop(
        Vector2 requestedLanding,
        PlatFormColliderTrigger destinationPlatform,
        Warrior warrior)
    {
        if (!avoidWarriorTopLandingDuringPlatformJumps)
            return requestedLanding;

        if (destinationPlatform == null || destinationPlatform.platformCollider == null)
            return requestedLanding;

        if (warrior == null || warrior.IsDead)
            return requestedLanding;

        if (onlyAvoidWarriorTopLandingOnWarriorPlatform && warrior.CurrentplatForm != destinationPlatform)
            return requestedLanding;

        Collider2D zalaytyBody = GetZalaytyBodyCollider();
        BoxCollider2D warriorBox = GetWarriorMainBoxCollider(warrior);

        if (zalaytyBody == null || warriorBox == null || !warriorBox.enabled)
            return requestedLanding;

        Bounds platformBounds = destinationPlatform.platformCollider.bounds;
        Bounds zBounds = zalaytyBody.bounds;
        Bounds wBounds = warriorBox.bounds;

        Vector2 referencePosition = rigidbody2 != null
            ? rigidbody2.position
            : (Vector2)transform.position;

        float bodyHalfX = Mathf.Max(0.01f, zBounds.extents.x);
        float bodyCenterOffsetX = zBounds.center.x - referencePosition.x;
        float requestedCenterX = requestedLanding.x + bodyCenterOffsetX;

        float clearGap = Mathf.Max(
            platformJumpWarriorSideGap,
            samePlatformMainBoxContactSkin,
            0.02f);

        float safeLeftMaxCenterX = wBounds.min.x - bodyHalfX - clearGap;
        float safeRightMinCenterX = wBounds.max.x + bodyHalfX + clearGap;

        bool requestedWouldLandOnWarriorTop =
            requestedCenterX > safeLeftMaxCenterX &&
            requestedCenterX < safeRightMinCenterX;

        if (keepOriginalLandingWhenAlreadyBesideWarrior && !requestedWouldLandOnWarriorTop)
            return requestedLanding;

        GetSafeCenterXRangeOnPlatform(
            platformBounds,
            bodyHalfX,
            platformJumpLandingPlatformInset,
            out float safeMinCenterX,
            out float safeMaxCenterX);

        float leftMin = safeMinCenterX;
        float leftMax = Mathf.Min(safeMaxCenterX, safeLeftMaxCenterX);
        float rightMin = Mathf.Max(safeMinCenterX, safeRightMinCenterX);
        float rightMax = safeMaxCenterX;

        bool leftFits = leftMax >= leftMin;
        bool rightFits = rightMax >= rightMin;

        bool preferLeft = PreferLandingLeftOfWarrior(
            zBounds,
            wBounds,
            destinationPlatform,
            bodyHalfX,
            requestedCenterX);

        float chosenCenterX;

        if (leftFits || rightFits)
        {
            if (preferLeft && leftFits)
                chosenCenterX = Mathf.Clamp(requestedCenterX, leftMin, leftMax);
            else if (!preferLeft && rightFits)
                chosenCenterX = Mathf.Clamp(requestedCenterX, rightMin, rightMax);
            else if (leftFits)
                chosenCenterX = Mathf.Clamp(requestedCenterX, leftMin, leftMax);
            else
                chosenCenterX = Mathf.Clamp(requestedCenterX, rightMin, rightMax);
        }
        else
        {
            // Tiny platform fallback: choose the valid platform point with the best
            // separation from Warrior. This may not be a perfect side interval, but
            // it still avoids choosing Warrior's center/top whenever possible.
            float leftEdgeCandidate = safeMinCenterX;
            float rightEdgeCandidate = safeMaxCenterX;

            float leftSeparation = Mathf.Abs(leftEdgeCandidate - wBounds.center.x);
            float rightSeparation = Mathf.Abs(rightEdgeCandidate - wBounds.center.x);

            if (preferLeft && leftEdgeCandidate <= wBounds.center.x)
                chosenCenterX = leftEdgeCandidate;
            else if (!preferLeft && rightEdgeCandidate >= wBounds.center.x)
                chosenCenterX = rightEdgeCandidate;
            else
                chosenCenterX = leftSeparation >= rightSeparation ? leftEdgeCandidate : rightEdgeCandidate;
        }

        chosenCenterX = Mathf.Clamp(chosenCenterX, safeMinCenterX, safeMaxCenterX);

        // Convert from desired body-center X back to the Rigidbody/transform position X.
        requestedLanding.x = chosenCenterX - bodyCenterOffsetX;
        return requestedLanding;
    }

    private bool PreferLandingLeftOfWarrior(
        Bounds zBounds,
        Bounds warriorBounds,
        PlatFormColliderTrigger destinationPlatform,
        float bodyHalfX,
        float requestedCenterX)
    {
        // Best attack position = stay on the side from which Zalayty approached,
        // so after landing he faces Warrior instead of landing on top/crossing over.
        if (zBounds.center.x < warriorBounds.center.x - sideEpsilon)
            return true;

        if (zBounds.center.x > warriorBounds.center.x + sideEpsilon)
            return false;

        if (requestedCenterX < warriorBounds.center.x - sideEpsilon)
            return true;

        if (requestedCenterX > warriorBounds.center.x + sideEpsilon)
            return false;

        return GetAvailableLeftSpaceForPlatformJump(destinationPlatform, warriorBounds, bodyHalfX) >=
               GetAvailableRightSpaceForPlatformJump(destinationPlatform, warriorBounds, bodyHalfX);
    }

    private float GetAvailableLeftSpaceForPlatformJump(
        PlatFormColliderTrigger platform,
        Bounds warriorBounds,
        float bodyHalfX)
    {
        if (platform == null || platform.platformCollider == null)
            return 0f;

        Bounds p = platform.platformCollider.bounds;
        GetSafeCenterXRangeOnPlatform(
            p,
            bodyHalfX,
            platformJumpLandingPlatformInset,
            out float safeMinCenterX,
            out _);

        float clearGap = Mathf.Max(platformJumpWarriorSideGap, samePlatformMainBoxContactSkin, 0.02f);
        float leftMax = warriorBounds.min.x - bodyHalfX - clearGap;
        return Mathf.Max(0f, leftMax - safeMinCenterX);
    }

    private float GetAvailableRightSpaceForPlatformJump(
        PlatFormColliderTrigger platform,
        Bounds warriorBounds,
        float bodyHalfX)
    {
        if (platform == null || platform.platformCollider == null)
            return 0f;

        Bounds p = platform.platformCollider.bounds;
        GetSafeCenterXRangeOnPlatform(
            p,
            bodyHalfX,
            platformJumpLandingPlatformInset,
            out _,
            out float safeMaxCenterX);

        float clearGap = Mathf.Max(platformJumpWarriorSideGap, samePlatformMainBoxContactSkin, 0.02f);
        float rightMin = warriorBounds.max.x + bodyHalfX + clearGap;
        return Mathf.Max(0f, safeMaxCenterX - rightMin);
    }

    private bool StartControlledWarriorTopRebound(Warrior warrior)
    {
        if (!TryComputeWarriorTopReboundLanding(
                warrior,
                out Vector2 landing,
                out PlatFormColliderTrigger landingPlatform,
                out float jumpHeight,
                out float duration))
        {
            return false;
        }

        RestoreActiveJumpDownSourcePlatformNow();

        _warriorTopReboundActive = true;
        _nextAllowedWarriorTopReboundTime = Time.time + warriorTopReboundCooldown;
        _warriorTopReboundBusyUntil = Time.time + duration + 0.15f;

        TemporarilyIgnoreWarriorForTopRebound(warrior, duration);

        // Important: Warrior-top rebound must enter the normal Zalayty jump entry point.
        // Only this special rebound forces JumpTowardPositionAction. Normal Zalayty
        // movement remains independent and Warrior platform behavior remains untouched.
        bool reboundStarted = StartZalaytyJumpTo(
            landing,
            jumpHeight,
            duration,
            landingPlatform,
            forceJumpTowardPositionAction: true);

        if (!reboundStarted)
        {
            _warriorTopReboundActive = false;
            RestoreIgnoredWarriorTopReboundCollisions();
            return false;
        }

        return true;
    }

    private bool TryComputeWarriorTopReboundLanding(
        Warrior warrior,
        out Vector2 landing,
        out PlatFormColliderTrigger landingPlatform,
        out float jumpHeight,
        out float duration)
    {
        landing = transform.position;
        landingPlatform = null;
        jumpHeight = warriorTopReboundHeight;
        duration = warriorTopReboundDuration;

        if (warrior == null || warrior.collider2 == null)
            return false;

        Collider2D body = GetZalaytyBodyCollider();
        if (body == null)
            return false;

        landingPlatform = warrior.CurrentplatForm != null && warrior.CurrentplatForm.platformCollider != null
            ? warrior.CurrentplatForm
            : null;

        if (landingPlatform == null && _activeJumpTargetPlatform != null && _activeJumpTargetPlatform.platformCollider != null)
            landingPlatform = _activeJumpTargetPlatform;

        if (landingPlatform == null && CurrentplatForm != null && CurrentplatForm.platformCollider != null)
            landingPlatform = CurrentplatForm;

        Bounds z = body.bounds;
        Bounds w = warrior.collider2.bounds;

        float bodyHalfX = Mathf.Max(0.01f, z.extents.x);
        float bodyHalfY = Mathf.Max(0.01f, z.extents.y);
        float bodyCenterOffsetX = z.center.x - transform.position.x;

        // This is the important part: the target center must be OUTSIDE Warrior's
        // expanded horizontal bounds. A small side gap is often not enough when the
        // physics solver still has a contact pair from the top-bound landing.
        float clearGap = Mathf.Max(
            warriorTopReboundSideGap,
            warriorTopReboundMinSideClearance,
            0.02f);

        float outsideLeftCenterX = w.min.x - bodyHalfX - clearGap;
        float outsideRightCenterX = w.max.x + bodyHalfX + clearGap;

        bool preferLeft;
        if (z.center.x < w.center.x - sideEpsilon)
            preferLeft = true;
        else if (z.center.x > w.center.x + sideEpsilon)
            preferLeft = false;
        else
            preferLeft = GetAvailableLeftSpace(warrior, landingPlatform, bodyHalfX) >=
                         GetAvailableRightSpace(warrior, landingPlatform, bodyHalfX);

        float chosenCenterX;

        if (landingPlatform != null && landingPlatform.platformCollider != null)
        {
            Bounds p = landingPlatform.platformCollider.bounds;
            GetSafeCenterXRangeOnPlatform(p, bodyHalfX, out float safeMinCenterX, out float safeMaxCenterX);

            // Valid side intervals. These are the only ranges that are both on the
            // platform surface and beside Warrior instead of above him.
            float leftMin = safeMinCenterX;
            float leftMax = Mathf.Min(safeMaxCenterX, outsideLeftCenterX);
            float rightMin = Mathf.Max(safeMinCenterX, outsideRightCenterX);
            float rightMax = safeMaxCenterX;

            bool leftFits = leftMax >= leftMin;
            bool rightFits = rightMax >= rightMin;

            if (leftFits || rightFits)
            {
                if (preferLeft && leftFits)
                    chosenCenterX = Mathf.Clamp(z.center.x, leftMin, leftMax);
                else if (!preferLeft && rightFits)
                    chosenCenterX = Mathf.Clamp(z.center.x, rightMin, rightMax);
                else if (leftFits)
                    chosenCenterX = Mathf.Clamp(z.center.x, leftMin, leftMax);
                else
                    chosenCenterX = Mathf.Clamp(z.center.x, rightMin, rightMax);
            }
            else
            {
                // If there is literally no valid side interval on this platform, it
                // is impossible to be both fully beside Warrior and fully on the
                // platform. In that rare case, choose the platform edge that gives
                // the greatest separation, but still prefer moving away from Warrior.
                float leftEdgeCandidate = safeMinCenterX;
                float rightEdgeCandidate = safeMaxCenterX;

                float leftSeparation = Mathf.Abs(leftEdgeCandidate - w.center.x);
                float rightSeparation = Mathf.Abs(rightEdgeCandidate - w.center.x);

                if (preferLeft && leftEdgeCandidate < w.center.x)
                    chosenCenterX = leftEdgeCandidate;
                else if (!preferLeft && rightEdgeCandidate > w.center.x)
                    chosenCenterX = rightEdgeCandidate;
                else
                    chosenCenterX = leftSeparation >= rightSeparation ? leftEdgeCandidate : rightEdgeCandidate;
            }

            chosenCenterX = Mathf.Clamp(chosenCenterX, safeMinCenterX, safeMaxCenterX);
            landing = new Vector2(chosenCenterX - bodyCenterOffsetX, p.max.y + landingYOffset);
        }
        else
        {
            chosenCenterX = preferLeft ? outsideLeftCenterX : outsideRightCenterX;
            landing = new Vector2(chosenCenterX - bodyCenterOffsetX, transform.position.y + 0.05f);
        }

        float horizontalDistance = Mathf.Abs((landing.x + bodyCenterOffsetX) - z.center.x);

        // The arc must lift Zalayty enough to break the current Warrior-top contact.
        // Too low/too short makes the body slide on Warrior and land back on top.
        float minimumLiftFromContact = Mathf.Max(0.20f, bodyHalfY * 0.45f) + warriorTopReboundExtraLift;
        jumpHeight = Mathf.Max(
            warriorTopReboundHeight,
            minimumLiftFromContact,
            horizontalDistance * 0.35f);

        float speed = Mathf.Max(0.01f, Speed);
        duration = Mathf.Clamp(
            horizontalDistance / speed * 0.75f,
            Mathf.Max(0.16f, warriorTopReboundDuration * 0.75f),
            Mathf.Max(0.32f, warriorTopReboundDuration * 1.8f));

        return true;
    }

    private float GetAvailableLeftSpace(Warrior warrior, PlatFormColliderTrigger platform, float bodyHalfX)
    {
        if (warrior == null || warrior.collider2 == null || platform == null || platform.platformCollider == null)
            return 0f;

        Bounds p = platform.platformCollider.bounds;
        Bounds w = warrior.collider2.bounds;
        GetSafeCenterXRangeOnPlatform(p, bodyHalfX, out float safeMinCenterX, out _);
        return Mathf.Max(0f, (w.min.x - warriorTopReboundSideGap - bodyHalfX) - safeMinCenterX);
    }

    private float GetAvailableRightSpace(Warrior warrior, PlatFormColliderTrigger platform, float bodyHalfX)
    {
        if (warrior == null || warrior.collider2 == null || platform == null || platform.platformCollider == null)
            return 0f;

        Bounds p = platform.platformCollider.bounds;
        Bounds w = warrior.collider2.bounds;
        GetSafeCenterXRangeOnPlatform(p, bodyHalfX, out _, out float safeMaxCenterX);
        return Mathf.Max(0f, safeMaxCenterX - (w.max.x + warriorTopReboundSideGap + bodyHalfX));
    }

    private void GetSafeCenterXRangeOnPlatform(Bounds platformBounds, float bodyHalfX, out float safeMinCenterX, out float safeMaxCenterX)
    {
        GetSafeCenterXRangeOnPlatform(
            platformBounds,
            bodyHalfX,
            warriorTopReboundPlatformInset,
            out safeMinCenterX,
            out safeMaxCenterX);
    }

    private void GetSafeCenterXRangeOnPlatform(
        Bounds platformBounds,
        float bodyHalfX,
        float requestedInset,
        out float safeMinCenterX,
        out float safeMaxCenterX)
    {
        float inset = Mathf.Max(0f, requestedInset);

        safeMinCenterX = platformBounds.min.x + inset + bodyHalfX;
        safeMaxCenterX = platformBounds.max.x - inset - bodyHalfX;

        if (safeMaxCenterX >= safeMinCenterX)
            return;

        // Fallback for very small platforms: reduce the inset before giving up.
        safeMinCenterX = platformBounds.min.x + bodyHalfX;
        safeMaxCenterX = platformBounds.max.x - bodyHalfX;

        if (safeMaxCenterX >= safeMinCenterX)
            return;

        float center = platformBounds.center.x;
        safeMinCenterX = center;
        safeMaxCenterX = center;
    }

    private void OnDisable()
    {
        RestoreIgnoredWarriorTopReboundCollisions();
        RestoreActiveJumpDownSourcePlatformNow();
    }

    private void TemporarilyIgnoreWarriorForTopRebound(Warrior warrior, float reboundDuration)
    {
        RestoreIgnoredWarriorTopReboundCollisions();

        if (!ignoreWarriorCollisionDuringTopRebound || warrior == null)
            return;

        Collider2D body = GetZalaytyBodyCollider();
        if (body == null)
            return;

        Collider2D[] warriorColliders = warrior.GetComponentsInChildren<Collider2D>(true);
        if (warriorColliders == null || warriorColliders.Length == 0)
            return;

        for (int i = 0; i < warriorColliders.Length; i++)
        {
            Collider2D c = warriorColliders[i];
            if (c == null || c == body)
                continue;

            Physics2D.IgnoreCollision(body, c, true);
            _ignoredWarriorTopReboundColliders.Add(c);
        }

        float timeout = Mathf.Max(
            warriorTopReboundIgnoreRestoreTimeout,
            reboundDuration + 0.20f);

        _restoreWarriorTopReboundCollisionCoroutine =
            StartCoroutine(RestoreWarriorTopReboundCollisionsWhenClear(body, warrior, timeout));
    }

    private IEnumerator RestoreWarriorTopReboundCollisionsWhenClear(
        Collider2D zalaytyBody,
        Warrior warrior,
        float timeout)
    {
        float timer = 0f;
        WaitForFixedUpdate wait = new WaitForFixedUpdate();

        while (timer < timeout)
        {
            if (zalaytyBody == null || warrior == null || warrior.collider2 == null)
                break;

            Bounds z = zalaytyBody.bounds;
            Bounds w = warrior.collider2.bounds;

            bool horizontallyClear = z.min.x >= w.max.x || z.max.x <= w.min.x;
            bool verticallyClear = z.min.y > w.max.y + warriorTopReboundBoundsBand || z.max.y < w.min.y;

            if (horizontallyClear || verticallyClear)
                break;

            timer += Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : Time.deltaTime;
            yield return wait;
        }

        RestoreIgnoredWarriorTopReboundCollisions();
    }

    private void RestoreIgnoredWarriorTopReboundCollisions()
    {
        if (_restoreWarriorTopReboundCollisionCoroutine != null)
        {
            StopCoroutine(_restoreWarriorTopReboundCollisionCoroutine);
            _restoreWarriorTopReboundCollisionCoroutine = null;
        }

        Collider2D body = GetZalaytyBodyCollider();
        if (body != null)
        {
            for (int i = 0; i < _ignoredWarriorTopReboundColliders.Count; i++)
            {
                Collider2D c = _ignoredWarriorTopReboundColliders[i];
                if (c != null)
                    Physics2D.IgnoreCollision(body, c, false);
            }
        }

        _ignoredWarriorTopReboundColliders.Clear();
    }

    private Collider2D GetZalaytyBodyCollider()
    {
        if (NormalCollider != null && NormalCollider.enabled)
            return NormalCollider;

        return collider2;
    }

    private void RestoreActiveJumpDownSourcePlatformNow()
    {
        if (_activeJumpDownSourcePlatform == null)
            return;

        _activeJumpDownSourcePlatform.ForceRestoreZalaytyJumpDownSourcePlatform(this);
        _activeJumpDownSourcePlatform = null;
    }

    public override float ComputeStepBackDistance(float incoming)
    {
        // If profile missing, fallback
        if (hitReaction == null) return incoming;

        // Example:  ignores whileAttackingMultiplier
        float v = incoming * hitReaction.stepBackMultiplier;

        // Different clamp for  (extra heavy feel)
        return Mathf.Clamp(v, hitReaction.min, hitReaction.max * 0.6f);
    }

    protected override void ApplySpecificSpawnOverrides()
    {
        base.ApplySpecificSpawnOverrides();

        if (SpawnOverrides == null || SpawnOverrides.zalayty == null)
            return;

        var z = SpawnOverrides.zalayty;

        if (z.overrideSqueezeCheckInterval)
            squeezeCheckInterval = Mathf.Max(0.01f, z.squeezeCheckInterval);

        if (z.overrideRepathInterval)
            repathInterval = Mathf.Max(0.01f, z.repathInterval);
    }

    public void ForceNaturalEdgeFallFromPlatform(
    PlatFormColliderTrigger sourcePlatform,
    float gravityScale = 2.5f,
    float minimumDownVelocity = 0.08f)
    {
        if (_deathStarted || currentHealth <= 0f)
            return;

        StopMoveTowardCoroutine();
        StopJumpTowardCoroutine();

        _activeJumpTargetPlatform = null;
        _isJumping = false;
        activesJumpCoroutine = null;
        targetReached = false;
        DescendentPhase = true;

        // Important:
        // Do not keep stale platform ownership while Zalayty is beside / leaving the platform.
        if (CurrentplatForm == sourcePlatform)
            CurrentplatForm = null;

        if (_lastKnownPlatform == sourcePlatform)
        {
            _lastKnownPlatform = null;
            _lastKnownPlatformTime = -999f;
        }

        if (_lastReallyGroundedPlatform == sourcePlatform)
        {
            _lastReallyGroundedPlatform = null;
            _lastReallyGroundedTime = -999f;
        }

        if (_lastConfirmedSharedPlatform == sourcePlatform)
        {
            _lastConfirmedSharedPlatform = null;
            _lastConfirmedSharedPlatformTime = -999f;
        }

        _missedMovingPlatformLandingRecoveryActive = true;
        _forceAirborneAnimationUntil = Time.time + missedLandingAirborneAnimationLockTime;

        SetJumping(true);

        if (rigidbody2 != null)
        {
            RigidbodyConstraints2D constraints = rigidbody2.constraints;
            constraints &= ~RigidbodyConstraints2D.FreezePositionY;
            constraints |= RigidbodyConstraints2D.FreezeRotation;
            rigidbody2.constraints = constraints;

            rigidbody2.gravityScale = Mathf.Max(rigidbody2.gravityScale, gravityScale);

            Vector2 velocity = rigidbody2.linearVelocity;

            if (velocity.y > -minimumDownVelocity)
                velocity.y = -minimumDownVelocity;

            rigidbody2.linearVelocity = velocity;
            rigidbody2.WakeUp();
        }

        ForceZalaytyAirborneAnimationOnly();
    }
}

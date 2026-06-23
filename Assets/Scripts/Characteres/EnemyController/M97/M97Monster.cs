using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Services;
using System.Collections;
using Assets.Scripts.Platforms;
using UnityEngine;


public class M97Monster : Enemy
{
    [Header("Laser Configuration")]
    public GameObject laserPrefab;
    public float laserActivationDelay = 0.5f;

    [Header("Gun Aim")]
    [SerializeField] private Transform gunAimPivot;
    [SerializeField] private float aimAngleOffset = 0f;   // tweak if gun sprite points different axis
    [SerializeField] private float aimSmoothing = 0f;

    [Header("Aim Stabilizer")]
    [SerializeField] private float noRotateDistance = 0.7f;
    [SerializeField] private bool aimStraightWhenClose = false;

    private float _smoothedAimAngle;
    private bool _aimAngleInitialized;

    [Header("Movement Speeds")]
    public float runSpeed = 4.2f;
    public float walkSpeed = 2f;

    [SerializeField] private float overlapRetreatSpeed = 2.5f;
    [SerializeField] private float overlapRetreatDuration = 0.35f;

    private float _overlapRetreatEndTime;

    private LaserVFXController laserVFX;
    private bool laserInitialized = false;
    private bool isLaserActive = false;
    public bool warriorInLaserScope = false;

    [Header("Laser Damage")]
    public int laserDamage = 3;
    public float laserDamageTick = 0.2f;
    private Coroutine laserDamageCoroutine;

    [Header("Barrel Axis")]
    [SerializeField] private Vector2 barrelLocalAxis = Vector2.right;

    [Header("No-jitter (no smoothing)")]
    [SerializeField] private float angleDeadzoneDeg = 0.6f;
    private float _lockedAngle;
    private bool _hasLockedAngle;

    [SerializeField] private float noRotateEnter = 0.7f;
    [SerializeField] private float noRotateExit = 0.9f;
    private bool _inNoRotateZone;
    private float _aimVel;
    [SerializeField] private float aimSmoothTime = 0.06f;
    [SerializeField] private float maxAimSpeed = 720f;

    [Header("Fire Gate (must face to fire)")]
    [SerializeField] private bool mustFaceToFire = true;
    [SerializeField] private float faceThenFireDelay = 0.06f;
    [SerializeField] private float frontEpsilon = 0.02f;

    [Header("Flip While Laser Active")]
    [SerializeField] private bool allowFlipWhileLaserActive = true;
    [SerializeField] private float flipWhileFiringMinInterval = 0.15f;
    [SerializeField] private float flipWhileFiringMaxDistance = 12f;

    [Header("Laser Stop Conditions")]
    [SerializeField] private float walkEngageRange = 12f;
    [SerializeField] private float laserStopHysteresis = 0.35f;

    [Header("Attack State Safety")]
    [SerializeField] private bool requireAttackStateToKeepLaser = false;

    private float _lastFlipTime;
    private bool _wasInAttackRange;

    private bool IsLaserShowing => laserVFX != null && laserVFX.IsLaserActive;

    [SerializeField] private float aimMaxDegPerSec = 1200f;

    [SerializeField] private bool faceMoveDirectionWhenNotFiring = true;
    [SerializeField] private float faceMoveEpsilon = 0.03f;

    [Header("Dodge Aim Freeze")]
    [SerializeField] private bool freezeAimWhileWarriorDodges = true;

    private bool _dodgeAimFrozen;
    private float _dodgeFrozenGunAngle;
    private Vector2 _dodgeFrozenLaserDir = Vector2.right;

    [Header("Dodge Side Flip")]
    [SerializeField] private bool dodgeFlipHorizontallyToWarrior = true;
    [SerializeField] private float dodgeFlipCenterEpsilon = 0.03f;

    private int _dodgeSideSign = 1;
    private float _lastDodgeSideSwitchTime;

    [Header("Gun Solve")]
    [SerializeField] private int gunSolvePasses = 2;
    [SerializeField] private float gunSolveEpsilonDeg = 0.05f;
    [SerializeField] private bool invertMuzzleDir = false;

    [Header("Anti-Blink Center Tuning")]
    [SerializeField] private float centerDeadZone = 0.08f;
    [SerializeField] private float bodyFlipMinInterval = 0.12f;
    [SerializeField] private float dodgeSideSwitchMinInterval = 0.08f;

    [SerializeField] private bool useLocalDodgeGunRotation = false;

    [Header("Flip Delay")]
    [SerializeField] private float flipDelayBeforeExecute = 1.5f;

    private bool _flipRequested;
    private bool stuckInFlipDelay;



    [Header("Body Overlap Push")]
    [SerializeField] private float overlapPushSpeed = 6f;
    [SerializeField] private float overlapSeparationSkin = 0.03f;
    [SerializeField] private float overlapCenterEpsilon = 0.02f;
    [SerializeField] private bool stopLaserWhileResolvingOverlap = true;

    private Warrior _overlapWarrior;
    private bool _resolveOverlapThisStep;
    private bool _isResolvingBodyOverlap;

    [SerializeField] private float overlapPushDuration = 2f;

    private float _overlapPushEndTime;
    private bool _overlapPushCycleActive;
    private bool _overlapCycleConsumedUntilSeparation;

    [Header("Damage Count Stun")]
    [SerializeField] private int hitsToStun = 3;
    [SerializeField] private float stunAfterHitsSeconds = 1f;
    [SerializeField] private bool resetHitCountOnStun = true;

    [Header("Moving Platform Surface Stick")]
    [SerializeField] private bool stickToDescendingPlatformSurface = true;
    [SerializeField] private float platformSurfaceSeatOffset = 0.02f;
    [SerializeField] private float platformSurfaceTopTolerance = 0.35f;
    [SerializeField] private float platformSurfaceBottomTolerance = -0.08f;
    [SerializeField] private bool zeroNegativeVerticalVelocityOnStick = true;

    [Header("Laser Grounded Target Gate")]
    [SerializeField] private bool laserRequiresWarriorGrounded = true;
    [SerializeField, Min(1)] private int laserMinWarriorGroundPoints = 1;
    [SerializeField] private bool stopLaserWhenWarriorJumps = true;

    [Header("M97 Moving Lift Anti-Tunneling")]
    [Tooltip("M97-only safety layer. It never changes MovingVerticalPlatform rules for Warrior, Zalayty, or other enemies.")]
    [SerializeField] private bool enableM97MovingLiftAntiTunnel = true;

    [Tooltip("For M97 only: force safer Rigidbody2D settings at runtime.")]
    [SerializeField] private bool forceM97ContinuousCollision = true;

    [Tooltip("Small offset between M97 bottom and MovingVerticalPlatform top after an emergency seat.")]
    [SerializeField, Min(0f)] private float m97LiftSeatOffset = 0.025f;

    [Tooltip("Horizontal overlap skin used only by M97 lift recovery.")]
    [SerializeField, Min(0f)] private float m97LiftHorizontalSkin = 0.025f;

    [Tooltip("Maximum normal gap above a moving lift that M97 may be recovered from.")]
    [SerializeField, Min(0f)] private float m97LiftMaxRecoverGap = 0.65f;

    [Tooltip("Maximum sink through the moving lift top that M97 may be recovered from.")]
    [SerializeField, Min(0f)] private float m97LiftMaxRecoverSink = 0.35f;

    [Tooltip("Extra dynamic tolerance added to lift/body step prediction. Increase only if your lift speed is extreme.")]
    [SerializeField, Min(0f)] private float m97LiftDynamicPadding = 0.08f;

    [Tooltip("How long M97 remembers the last valid moving lift after a one-frame solver/raycast gap.")]
    [SerializeField, Min(0f)] private float m97LiftSupportGraceTime = 0.25f;

    [Tooltip("Allows recovery when the M97 body center is still close to the platform bounds after a push/stepback.")]
    [SerializeField, Min(0f)] private float m97LiftHorizontalSearchPadding = 0.20f;

    [Tooltip("When seated by the anti-tunnel layer, cancel only vertical velocity. Horizontal patrol/chase is preserved.")]
    [SerializeField] private bool m97LiftZeroVerticalVelocityOnSeat = true;

    [Tooltip("If true, emergency recovery writes Rigidbody2D.position. If false, it uses MovePosition.")]
    [SerializeField] private bool m97LiftHardSnapInEmergency = true;

    private MovingVerticalPlatform _m97LastLiftPlatform;
    private float _m97LastLiftSupportTime = -999f;
    private float _m97LastFixedBodyBottomY;
    private bool _m97HasLastFixedBody;
    private bool _m97InLiftAntiTunnelCorrection;

    private bool _isDeadOrDying;

    private int _damageHitCount;

    protected override void Start()
    {
        base.Start();

        // M97 defaults
        Range = 9f;
        attackCooldown = 0.9f;
        attackDamage = 18;
        stepBackDistance = 1f;
        CanMove = true;
        Speed = runSpeed;

        // IMPORTANT: per-spawn overrides win after defaults
        ApplySpawnOverridesNow();

        ConfigureM97AntiTunnelRigidbody();

        if (NormalCollider != null && TriggerColliderLeft != null)
            Physics2D.IgnoreCollision(NormalCollider, TriggerColliderLeft, true);
        if (NormalCollider != null && TriggerColliderRight != null)
            Physics2D.IgnoreCollision(NormalCollider, TriggerColliderRight, true);

        InitializeLaserVFX();
        HideVisualsOnStart();
    }

    protected override void Update()
    {
        if (_isDeadOrDying)
            return;

        if (StopMovingWhenWarriorDie)
            return;

        initDirection();

        base.Update();

        // M97-only: recover a one-frame CurrentplatForm loss caused by lift/raycast timing
        // without moving the body from Update().
        RefreshM97MovingLiftAntiTunnel("update-after-base", allowPositionCorrection: false);

        // Re-assert committed patrol target after base update
        CommitPatrolEdgeForMovingVerticalPlatform();

        Warrior warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null || warrior.IsDeadOrDying)
        {
            if (IsLaserShowing || warriorInLaserScope || laserDamageCoroutine != null)
                DeactivateLaser();

            return;
        }
        StopLaserIfWarriorCannotBeTargeted(warrior);
        // M97 is only frozen by stun if warrior is in front
        if (IsStunned && IsWarriorInFrontStrict(warrior))
        {
            StopMoveTowardCoroutine();
            CanMove = false;

            _isResolvingBodyOverlap = false;
            _overlapPushCycleActive = false;
            _overlapCycleConsumedUntilSeparation = false;
            _resolveOverlapThisStep = false;
            _overlapWarrior = null;
            _overlapRetreatEndTime = 0f;
            return;
        }

        bool overlappingWarrior = ShouldPushOverlappingWarrior(warrior);

        if (overlappingWarrior && _damageHitCount < 3)
        {
            if (!_overlapPushCycleActive && !_overlapCycleConsumedUntilSeparation)
            {
                _overlapPushCycleActive = true;
                _overlapPushEndTime = Time.time + overlapPushDuration;
            }

            if (_overlapPushCycleActive)
            {
                if (Time.time < _overlapPushEndTime)
                {
                    _isResolvingBodyOverlap = true;

                    if (stopLaserWhileResolvingOverlap && IsLaserShowing)
                        DeactivateLaser();

                    _overlapWarrior = warrior;
                    _resolveOverlapThisStep = true;
                }
                else
                {
                    _overlapPushCycleActive = false;
                    _overlapCycleConsumedUntilSeparation = true;
                    _isResolvingBodyOverlap = false;
                    _resolveOverlapThisStep = false;
                    _overlapWarrior = null;

                    CanMove = false;
                    StopMoveTowardCoroutine();
                }
            }

            if (!_overlapPushCycleActive && _overlapCycleConsumedUntilSeparation)
            {
                if (_overlapRetreatEndTime <= 0f)
                    _overlapRetreatEndTime = Time.time + overlapRetreatDuration;

                if (Time.time < _overlapRetreatEndTime)
                {
                    CanMove = false;
                    MoveBackwardWithoutFlip();
                }
                else
                {
                    StopMoveTowardCoroutine();
                }

                return;
            }

            return;
        }
        else
        {
            bool hadOverlapState =
                _isResolvingBodyOverlap ||
                _overlapPushCycleActive ||
                _overlapCycleConsumedUntilSeparation;

            _isResolvingBodyOverlap = false;
            _overlapPushCycleActive = false;
            _overlapCycleConsumedUntilSeparation = false;
            _resolveOverlapThisStep = false;
            _overlapWarrior = null;
            _overlapRetreatEndTime = 0f;

            if (hadOverlapState)
                EnableRunningMovementAfterAttack();
        }

        // Body facing logic only when laser is not active
        if (target != null && EnemyRangeService != null)
        {
            bool laserActive = laserVFX != null && laserVFX.IsLaserActive;

            if (!laserActive)
            {
                float myX = GetMyCenterX();
                float warriorX = GetWarriorCenterX(warrior);
                float dx = warriorX - myX;

                float distX = Mathf.Abs(dx);
                bool warriorInRange = distX <= EnemyRangeService._range;

                if (warriorInRange && Mathf.Abs(dx) > centerDeadZone)
                {
                    bool shouldFaceRight = dx > 0f;
                    bool needFlip = (shouldFaceRight && !rightFacing) || (!shouldFaceRight && rightFacing);

                    if (needFlip && (Time.time - _lastFlipTime) >= bodyFlipMinInterval)
                    {
                        Flip();
                        _lastFlipTime = Time.time;

                        _hasLockedAngle = false;
                        _inNoRotateZone = false;
                    }
                }
            }
        }

        CheckAndHideLaserIfNotAttacking();
        CheckAndHideLaserIfWarriorBeyondWalkRange();

        if (EnemyRangeService != null && target != null)
        {
            if (CanM97TargetWarriorWithLaser(warrior))
            {
                if (laserVFX == null)
                    InitializeLaserVFX();

                if (laserVFX != null && !laserVFX.IsLaserActive)
                {
                    bool flippedThisFrame = false;

                    if (mustFaceToFire)
                    {
                        float dist = Vector2.Distance(
                            (collider2 != null) ? (Vector2)collider2.bounds.center : (Vector2)transform.position,
                            (warrior.collider2 != null) ? (Vector2)warrior.collider2.bounds.center : (Vector2)warrior.transform.position
                        );

                        if (dist <= Range && !IsWarriorInFrontStrict(warrior))
                            flippedThisFrame = TryFlipTowardWarriorIfNeeded(warrior);
                    }

                    if (!flippedThisFrame && CanFireLaserNow(warrior))
                    {
                        if (!stuckInFlipDelay)
                        {
                            if (!EnemyRangeService.TryAction(target, Range, OnAttackPerformed))
                            {
                                if (!IsOverlapPushLocked())
                                    CanMove = true;
                            }
                        }
                    }
                    else
                    {
                        if (!IsOverlapPushLocked())
                            CanMove = true;
                    }

                    if (!EnemyRangeService.TryAction(target, walkEngageRange, OnWalkPerformed, Range))
                    {
                        if (CanMove)
                            RunAnimationDisplay();
                    }
                }
            }
            else
            {
                if (!CanMove && warrior.activesJumpCoroutine == null)
                    CanMove = true;
            }
        }

        // Patrol / movement
        if (CurrentplatForm != null && !stuckInFlipDelay && !IsOverlapPushLocked())
        {
            // Always keep committed target on moving vertical platforms
            CommitPatrolEdgeForMovingVerticalPlatform();

            if (CanMove && activesMoveCoroutine == null)
            {
                RunAnimationDisplay();
                activesMoveCoroutine = MoveTowardPostionAction(xEdge);
                StartCoroutine(activesMoveCoroutine);
            }
        }
    }


    private void MoveBackwardWithoutFlip()
    {
        if (CurrentplatForm == null) return;

        StopMoveTowardCoroutine();

        float backwardTargetX = GetBackwardRetreatTargetX();

        SetDirectionVariables(backwardTargetX);

        if (activesMoveCoroutine == null)
        {
            RunAnimationDisplay();
            activesMoveCoroutine = MoveTowardPositionNoFlipAction(backwardTargetX);
            StartCoroutine(activesMoveCoroutine);
        }
    }

    private IEnumerator MoveTowardPositionNoFlipAction(float x)
    {
        if (_isMoving) yield break;

        _isMoving = true;
        WaitForFixedUpdate wait = new WaitForFixedUpdate();

        try
        {
            bool wantsBeyondEdge = IsTargetOutsideCurrentPlatformSafeRange(x);

            bool shouldClamp = ClampMoveToCurrentPlatform &&
                               !(AllowEdgeExitWhenTargetOutside && wantsBeyondEdge);

            float targetX = shouldClamp ? ClampToCurrentPlatform(x) : x;

            while (Mathf.Abs(targetX - transform.position.x) > 0.1f)
            {
                RefreshM97MovingLiftAntiTunnel("no-flip-before-step", allowPositionCorrection: true);

                Vector2 currentPosition = rigidbody2 != null
                    ? rigidbody2.position
                    : (Vector2)transform.position;

                float nextX = Mathf.MoveTowards(
                    currentPosition.x,
                    targetX,
                    Speed * Time.fixedDeltaTime
                );

                Vector2 nextPosition = new Vector2(nextX, currentPosition.y);
                nextPosition = ConstrainM97PositionToMovingLiftSurface(nextPosition);

                MarkIntentionalEnemyDisplacement(Time.fixedDeltaTime * 4f);

                if (rigidbody2 != null)
                    rigidbody2.MovePosition(nextPosition);
                else
                    transform.position = new Vector3(nextPosition.x, nextPosition.y, transform.position.z);

                RefreshM97MovingLiftAntiTunnel("no-flip-after-step", allowPositionCorrection: true);

                yield return wait;
            }

            Vector2 finalPosition = rigidbody2 != null
                ? rigidbody2.position
                : (Vector2)transform.position;

            finalPosition.x = shouldClamp ? ClampToCurrentPlatform(targetX) : targetX;
            finalPosition = ConstrainM97PositionToMovingLiftSurface(finalPosition);

            MarkIntentionalEnemyDisplacement(Time.fixedDeltaTime * 4f);

            if (rigidbody2 != null)
                rigidbody2.MovePosition(finalPosition);
            else
                transform.position = new Vector3(finalPosition.x, finalPosition.y, transform.position.z);

            RefreshM97MovingLiftAntiTunnel("no-flip-final", allowPositionCorrection: true);
        }
        finally
        {
            _isMoving = false;

            if (activesMoveCoroutine != null)
                activesMoveCoroutine = null;

            RefreshM97MovingLiftAntiTunnel("no-flip-cleanup", allowPositionCorrection: true);
        }
    }


    private float GetBackwardRetreatTargetX()
    {
        if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
            return transform.position.x;

        Bounds pb = CurrentplatForm.platformCollider.bounds;
        float myX = (NormalCollider != null) ? NormalCollider.bounds.center.x : transform.position.x;

        // backward = opposite of facing
        float rawTarget = rightFacing ? pb.min.x : pb.max.x;

        return ClampToCurrentPlatform(rawTarget);
    }

    // Inside M97Monster.cs
    // Inside M97Monster.cs
    protected override void FixedUpdate()
    {
        CaptureM97FixedBodyState();

        // Parent ground/raycast and Warrior momentum guard stay intact.
        base.FixedUpdate();

        // First pass: recover from fast lift motion or a one-frame solver/raycast gap.
        RefreshM97MovingLiftAntiTunnel("fixed-after-base", allowPositionCorrection: true);

        if (_resolveOverlapThisStep && _overlapWarrior != null)
        {
            ResolveOverlapPush(_overlapWarrior);

            // Second pass: overlap push moves M97 horizontally. Re-seat immediately
            // if the push happened while he was standing on a moving lift.
            RefreshM97MovingLiftAntiTunnel("fixed-after-overlap-push", allowPositionCorrection: true);
        }

        _resolveOverlapThisStep = false;
        _overlapWarrior = null;

        CaptureM97FixedBodyState();
    }

    private bool ShouldPushOverlappingWarrior(Warrior w)
    {
        if (w == null) return false;
        if (!IsSamePlatform(w)) return false;
        if (collider2 == null || w.collider2 == null) return false;

        Bounds a = NormalCollider.bounds;
        Bounds b = w.collider2.bounds;

        Collider2D myCol = NormalCollider != null ? NormalCollider : collider2;
        Collider2D warCol = w.collider2;

        if (myCol == null || warCol == null)
            return false;

        if (!myCol.enabled || !warCol.enabled)
            return false;

        bool z = myCol.IsTouching(warCol);
        if (!z)
            return false;

        if (w.CountGroundPoints() <= 0)
            return false;

        float verticalDiff = Mathf.Abs(a.center.y - b.center.y);
        if (verticalDiff > 1.2f)
            return false;

        return true;
    }

    private void ResolveOverlapPush(Warrior w)
    {
        if (w == null || collider2 == null || w.collider2 == null)
            return;

        if (!IsSamePlatform(w))
            return;

        Bounds a = collider2.bounds;
        Bounds b = w.collider2.bounds;

        if (!a.Intersects(b))
            return;

        float overlapX = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
        if (overlapX <= 0f)
            return;

        float dx = b.center.x - a.center.x;

        float pushSign;
        if (Mathf.Abs(dx) <= overlapCenterEpsilon)
            pushSign = rightFacing ? 1f : -1f;
        else
            pushSign = Mathf.Sign(dx);

        float wantedSeparation = overlapX + overlapSeparationSkin;
        float totalStep = Mathf.Min(wantedSeparation, overlapPushSpeed * Time.fixedDeltaTime);

        float m97Part = totalStep * 0.25f;     // small forward drive
        float warriorPart = totalStep * 0.75f; // main pushed body

        Vector2 m97Delta = new Vector2(pushSign * m97Part, 0f);
        Vector2 warriorDelta = new Vector2(pushSign * warriorPart, 0f);

        Rigidbody2D myRb = collider2.attachedRigidbody != null
            ? collider2.attachedRigidbody
            : GetComponent<Rigidbody2D>();

        Vector2 m97TargetPosition = myRb != null
            ? myRb.position + m97Delta
            : (Vector2)transform.position + m97Delta;

        m97TargetPosition = ConstrainM97PositionToMovingLiftSurface(m97TargetPosition);
        MarkIntentionalEnemyDisplacement(Time.fixedDeltaTime * 4f);

        if (myRb != null)
            myRb.MovePosition(m97TargetPosition);
        else
            transform.position = new Vector3(m97TargetPosition.x, m97TargetPosition.y, transform.position.z);

        Rigidbody2D warriorRb = w.collider2.attachedRigidbody != null
            ? w.collider2.attachedRigidbody
            : w.GetComponent<Rigidbody2D>();

        if (warriorRb != null)
        {
            warriorRb.MovePosition(warriorRb.position + warriorDelta);

            if ((pushSign > 0f && warriorRb.linearVelocity.x < 0f) ||
                (pushSign < 0f && warriorRb.linearVelocity.x > 0f))
            {
                warriorRb.linearVelocity = new Vector2(0f, warriorRb.linearVelocity.y);
            }
        }
        else
        {
            w.transform.position += (Vector3)warriorDelta;
        }
    }

    public override void StopMoveTowardCoroutine()
    {
        base.StopMoveTowardCoroutine();

        // Safety for coroutine interruptions: M97 custom coroutines also set _isMoving.
        _isMoving = false;

        RefreshM97MovingLiftAntiTunnel("stop-move", allowPositionCorrection: true);
    }


    private void LateUpdate()
    {
        if (_isDeadOrDying)
            return;

        if (laserVFX == null) return;

        Warrior w = GameMgr.Instance?.WarriorInstance;
        if (w == null) return;

        bool warriorDodging = freezeAimWhileWarriorDodges && w.IsDodging;

        // Don't run standard flip handler while dodge-freeze behavior is active
        if (!warriorDodging)
            HandleFlipWhileLaserActive();

        if (gunAimPivot == null) return;
        if (!laserVFX.IsLaserActive) return;

        // ===== DODGE FREEZE MODE =====
        if (warriorDodging)
        {
            if (!_dodgeAimFrozen)
                BeginDodgeAimFreeze();

            // NEW: predictive pre-flip before warrior reaches/crosses muzzle
            TryPreFlipBeforeMuzzleWhileDodging(w);

            // Hard forward gate while dodging
            // Hard gate based on muzzle, not center
            if (mustFaceToFire && !IsWarriorInFrontFromMuzzle(w))
            {
                bool flipped = false;

                if (allowFlipWhileLaserActive &&
                    (Time.time - _lastFlipTime) >= flipWhileFiringMinInterval)
                {
                    flipped = TryFlipTowardWarriorIfNeeded(w);
                }

                // Re-check with muzzle gate after flip attempt
                if (!IsWarriorInFrontFromMuzzle(w))
                {
                    DeactivateLaser();
                    return;
                }

                // Short settle delay after flip: keep safe forward only
                if (flipped && (Time.time - _lastFlipTime) < faceThenFireDelay)
                {
                    Vector2 safeDir = GetForwardFlatDir();
                    AimGunPivotToWorldDir(safeDir);
                    laserVFX.SetExternalAimDir(safeDir);
                    laserVFX.TickLaser();
                    return;
                }
            }

            Vector2 flatDir;
            if (dodgeFlipHorizontallyToWarrior)
            {
                UpdateDodgeSideAndRotate180IfChanged(w);
                flatDir = GetDodgeFlatDir();
            }
            else
            {
                flatDir = GetForwardFlatDir();
            }

            // Critical anti-crossing clamp
            if (mustFaceToFire && clampDodgeAimToForward)
                flatDir = ForceForwardIfBackward(flatDir);

            AimGunPivotToWorldDir(flatDir);
            laserVFX.SetExternalAimDir(flatDir);
            laserVFX.TickLaser();
            return;
        }
        else if (_dodgeAimFrozen)
        {
            EndDodgeAimFreeze();
        }

        // ===== NORMAL AIMING =====
        Vector2 aimPoint = (w.collider2 != null)
            ? (Vector2)w.collider2.bounds.center
            : (Vector2)w.transform.position;

        Vector2 toTarget = aimPoint - (Vector2)gunAimPivot.position;
        float dist = toTarget.magnitude;

        if (_inNoRotateZone)
        {
            if (dist > noRotateExit) _inNoRotateZone = false;
        }
        else
        {
            if (dist < noRotateEnter) _inNoRotateZone = true;
        }

        if (!_hasLockedAngle)
        {
            _lockedAngle = gunAimPivot.eulerAngles.z;
            _hasLockedAngle = true;
        }

        if (_inNoRotateZone)
        {
            if (aimStraightWhenClose)
            {
                _lockedAngle = (leftFacing ? 180f : 0f) + aimAngleOffset;
                gunAimPivot.rotation = Quaternion.Euler(0f, 0f, _lockedAngle);
            }
            // else keep frozen _lockedAngle
        }
        else
        {
            if (toTarget.sqrMagnitude < 0.0001f) return;

            float targetAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg + aimAngleOffset;
            if (leftFacing) targetAngle += 180f;

            float delta = Mathf.Abs(Mathf.DeltaAngle(_lockedAngle, targetAngle));
            if (delta >= angleDeadzoneDeg)
                _lockedAngle = targetAngle;

            gunAimPivot.rotation = Quaternion.Euler(0f, 0f, _lockedAngle);
        }

        // Final front gate in normal mode
        if (mustFaceToFire && !IsWarriorInFrontFromMuzzle(w))
        {
            DeactivateLaser();
            return;
        }

        Vector2 origin = (laserVFX.firePoint != null)
            ? (Vector2)laserVFX.firePoint.position
            : (Vector2)gunAimPivot.position;

        aimPoint = (w.collider2 != null)
            ? (Vector2)w.collider2.bounds.center
            : (Vector2)w.transform.position;

        Vector2 gunDir = (aimPoint - origin).normalized;
        if (mustFaceToFire)
            gunDir = ForceForwardIfBackward(gunDir);

        laserVFX.SetExternalAimDir(gunDir);
        laserVFX.TickLaser();
    }

    private void CheckAndHideLaserIfWarriorBeyondWalkRange()
    {
        if (!IsLaserShowing) return;

        Warrior w = GameMgr.Instance?.WarriorInstance;
        if (w == null)
        {
            DeactivateLaser();
            return;
        }

        if (!IsSamePlatform(w))
        {
            DeactivateLaser();
            return;
        }

        Vector2 myCenter = (collider2 != null) ? (Vector2)collider2.bounds.center : (Vector2)transform.position;
        Vector2 warCenter = (w.collider2 != null) ? (Vector2)w.collider2.bounds.center : (Vector2)w.transform.position;
        float dist = Vector2.Distance(myCenter, warCenter);

        if (dist > walkEngageRange + laserStopHysteresis)
        {
            warriorInLaserScope = false;
            isLaserActive = false;

            DeactivateLaser();

            CanMove = true;
            Speed = runSpeed;
        }
    }

    private void HandleFlipWhileLaserActive()
    {
        if (!allowFlipWhileLaserActive) return;
        if (laserVFX == null || !laserVFX.IsLaserActive) return;

        Warrior w = GameMgr.Instance?.WarriorInstance;
        if (w == null) return;

        float dxCenter = GetWarriorCenterX(w) - GetMyCenterX();
        if (Mathf.Abs(dxCenter) <= centerDeadZone) return;

        if (freezeAimWhileWarriorDodges && w.IsDodging) return;

        float dist = Vector2.Distance(
            (collider2 != null) ? (Vector2)collider2.bounds.center : (Vector2)transform.position,
            (w.collider2 != null) ? (Vector2)w.collider2.bounds.center : (Vector2)w.transform.position
        );
        if (dist > flipWhileFiringMaxDistance) return;

        if (IsWarriorInFront(w.transform)) return;

        if (Time.time - _lastFlipTime < flipWhileFiringMinInterval) return;

        if (TryFlipTowardWarriorIfNeeded(w))
        {
            _hasLockedAngle = false;
            _inNoRotateZone = false;
        }
    }

    private void InitializeLaserVFX()
    {
        Transform lineTransform = transform.Find("LaserVFX/Line");

        if (lineTransform != null)
        {
            laserVFX = lineTransform.GetComponent<LaserVFXController>();

            if (laserVFX == null)
                Debug.LogWarning($"M97 {name}: LaserVFXController not found on Line GameObject!");
            else
                Debug.Log($"M97 {name}: LaserVFXController initialized successfully");
        }
        else
        {
            Debug.LogWarning($"M97 {name}: LaserVFX/Line GameObject not found!");
        }
    }

    private void HideVisualsOnStart()
    {
        if (laserVFX != null)
            laserVFX.DisableLaser();

        SetGunAimVisible(false);
    }

    public void ActivateLaser()
    {
        if (_isDeadOrDying) return;
        if (laserVFX == null) return;

        SetGunAimVisible(true);
        laserVFX.EnableLaser();
        laserVFX.ApplyPreset(LaserVFXController.NoisePreset.CracklingLightning);

        isLaserActive = true;
    }

    public void DeactivateLaser()
    {
        _dodgeAimFrozen = false;

        CancelInvoke(nameof(EnableWalkingMovement));
        StopLaserDamage();

        warriorInLaserScope = false;
        isLaserActive = false;

        if (laserVFX != null)
        {
            laserVFX.DisableLaser();
            laserVFX.ClearExternalAimDir();
        }

        _hasLockedAngle = false;
        _inNoRotateZone = false;
        _aimAngleInitialized = false;

        SetGunAimVisible(false);
    }

    private void CheckAndHideLaserIfNotAttacking()
    {
        if (!IsLaserShowing) return;
        if (!requireAttackStateToKeepLaser) return;

        if (animator == null)
        {
            DeactivateLaser();
            return;
        }

        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
        bool isAttacking = st.IsName(targetAnimationName) || animator.GetBool("isAttacking");

        if (!isAttacking)
            DeactivateLaser();
    }

    protected override void OnDeath()
    {
        _isDeadOrDying = true;

        CancelInvoke();
        StopLaserDamage();
        DeactivateLaser();

        _damageHitCount = 0;
        _overlapPushCycleActive = false;
        _overlapCycleConsumedUntilSeparation = false;
        _isResolvingBodyOverlap = false;
        _resolveOverlapThisStep = false;
        _overlapWarrior = null;

        Debug.Log($"M97Monster '{gameObject.name}' has been defeated!");

        base.OnDeath();
    }

    public override void OnAttackPerformed(IAttacker attacker, Transform attackedTarget)
    {
        if (_isDeadOrDying)
            return;
        Warrior w = GameMgr.Instance?.WarriorInstance;

        // Hard gate: never start laser if warrior is behind
        if (mustFaceToFire && !IsWarriorInFrontFromMuzzle(w))
        {
            TryFlipTowardWarriorIfNeeded(w);
            DeactivateLaser();
            return;
        }

        CanMove = false;
        StopMoveTowardCoroutine();

        // Optional safety flip
        if (w != null)
        {
            float myX = GetMyCenterX();
            float wx = GetWarriorCenterX(w);
            bool shouldFaceRight = wx > myX;

            if ((shouldFaceRight && leftFacing) || (!shouldFaceRight && rightFacing))
            {
                Flip();
                _lastFlipTime = Time.time;
            }
        }

        base.OnAttackPerformed(attacker, attackedTarget);

        if (animator != null)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            bool isAttacking = st.IsName(targetAnimationName) || animator.GetBool("isAttacking");
            if (!isAttacking) return;
        }

        // Final re-check after state change
        if (!CanM97TargetWarriorWithLaser(w) || (mustFaceToFire && !CanFireLaserNow(w)))
        {
            DeactivateLaser();
            return;
        }
        ActivateLaser();
    }

    private void OnWalkPerformed(IAttacker attacker, Transform attackedTarget)
    {
        Speed = walkSpeed;

        if (IsLaserShowing) return;

        WalkAnimationDisplay();
    }

    //public override void OnWarriorDetectedInLaser()
    //{
    //    Debug.Log($"M97 {name}: Warrior detected in laser!");
    //    warriorInLaserScope = true;
    //    isLaserActive = true;

    //    StartLaserDamage();
    //    Invoke(nameof(EnableWalkingMovement), laserActivationDelay);
    //}

    public override void OnWarriorDetectedInLaser()
    {
        if (_isDeadOrDying)
            return;

        Warrior w = GameMgr.Instance?.WarriorInstance;
        if (w == null) return;
        if (w.IsDeadOrDying) return;
        if (w.IsDodging) return;

        if (!CanM97TargetWarriorWithLaser(w))
        {
            DeactivateLaser();
            return;
        }

        Debug.Log($"M97 {name}: Warrior detected in laser!");
        warriorInLaserScope = true;
        isLaserActive = true;

        StartLaserDamage();
        Invoke(nameof(EnableWalkingMovement), laserActivationDelay);
    }

    public override void OnWarriorLeftLaser()
    {
        Debug.Log($"M97 {name}: Warrior left laser!");
        warriorInLaserScope = false;

        StopLaserDamage();
        EnableRunningMovementAfterAttack();
    }

    public override void OnLaserDeactivated()
    {
        warriorInLaserScope = false;
        isLaserActive = false;

        StopLaserDamage();
        SetGunAimVisible(false);
    }
    private void OnDisable()
    {
        DeactivateLaser();
    }
    private void EnableWalkingMovement()
    {
        if (!warriorInLaserScope) return;

        Speed = walkSpeed;

        if (IsLaserShowing) return;
        WalkAnimationDisplay();
    }

    public override void EnableRunningMovementAfterAttack()
    {
        CanMove = true;
        Speed = runSpeed;
        RunAnimationDisplay();
    }

    public override void PlayAttackAnimation()
    {
        CanMove = false;
        StopMoveTowardCoroutine();
        base.PlayAttackAnimation();
    }

    protected override void OnTriggerEnter2D(Collider2D collision)
    {
        base.OnTriggerEnter2D(collision);
    }

    protected override void OnTriggerExit2D(Collider2D collision)
    {
        base.OnTriggerExit2D(collision);
    }

    protected override void OnTriggerStay2D(Collider2D collision)
    {
        base.OnTriggerStay2D(collision);
    }

    private void initDirection()
    {
        if (CurrentplatForm == null) return;

        if (xEdge == 0f)
        {
            if (OddValue)
                xEdge = CurrentplatForm.platformCollider.bounds.max.x;
            else
                xEdge = CurrentplatForm.platformCollider.bounds.min.x;
        }
    }

    private void StartLaserDamage()
    {
        if (laserDamageCoroutine != null) return;
        laserDamageCoroutine = StartCoroutine(LaserDamageLoop());
    }

    private void StopLaserDamage()
    {
        if (laserDamageCoroutine == null) return;
        StopCoroutine(laserDamageCoroutine);
        laserDamageCoroutine = null;
    }

    private void SetGunAimVisible(bool visible)
    {
        if (gunAimPivot == null) return;
        gunAimPivot.gameObject.SetActive(visible);
    }

    private IEnumerator LaserDamageLoop()
    {
        while (true)
        {
            if (_isDeadOrDying)
                break;

            Warrior warrior = GameMgr.Instance?.WarriorInstance;
            if (warrior == null || warrior.IsDeadOrDying || !warriorInLaserScope || laserVFX == null || !laserVFX.IsLaserActive)
                break;

            if (!IsSamePlatform(warrior))
                break;

            if (!CanM97TargetWarriorWithLaser(warrior))
                break;

            if (warrior.IsDodging)
            {
                yield return new WaitForSeconds(laserDamageTick);
                continue;
            }

            Vector2 origin = laserVFX.CurrentStartPos;

            if (!warrior.IsDeadOrDying)
            {
                warrior.ApplyHitReaction(
                    HitKind.Laser,
                    origin,
                    stunSeconds: 0.08f,
                    knockbackVel: 2.0f
                );

                warrior.TakeDamage(laserDamage);

                if (!warrior.IsDeadOrDying)
                    warrior.SpawnBloodshedEffectFromEnemy(this);
            }

            yield return new WaitForSeconds(laserDamageTick);
        }

        warriorInLaserScope = false;
        laserDamageCoroutine = null;
    }


    private float GetCenterX(Collider2D col, Transform t)
    {
        if (col != null) return col.bounds.center.x;
        return t != null ? t.position.x : transform.position.x;
    }

    private new float GetMyCenterX()
    {
        return GetCenterX(collider2, transform);
    }

    private float GetWarriorCenterX(Warrior w)
    {
        if (w == null) return float.NaN;
        return GetCenterX(w.collider2, w.transform);
    }

    private bool IsSamePlatform(Warrior w)
    {
        return w != null && CurrentplatForm != null && CurrentplatForm == w.CurrentplatForm;
    }

    private bool IsWarriorGroundedEnoughForLaser(Warrior w)
    {
        if (w == null || w.IsDeadOrDying)
            return false;

        if (!laserRequiresWarriorGrounded)
            return true;

        // Controlled jump must immediately cancel M97 laser targeting.
        if (w.activesJumpCoroutine != null)
            return false;

        // Edge fall / platform exit / grazing edge must also cancel the laser.
        if (w.IsFallingEdge || w.IsFallingPlfExit || w.IsFallingHitEnemy || w.IsFallingGrazesEdge)
            return false;

        return w.CountGroundPoints() >= laserMinWarriorGroundPoints;
    }

    private bool CanM97TargetWarriorWithLaser(Warrior w)
    {
        if (w == null || w.IsDeadOrDying)
            return false;

        if (!IsSamePlatform(w))
            return false;

        if (!IsWarriorGroundedEnoughForLaser(w))
            return false;

        return true;
    }

    private void StopLaserIfWarriorCannotBeTargeted(Warrior w)
    {
        if (!stopLaserWhenWarriorJumps)
            return;

        if (CanM97TargetWarriorWithLaser(w))
            return;

        if (IsLaserShowing || warriorInLaserScope || laserDamageCoroutine != null)
        {
            DeactivateLaser();

            CanMove = true;
            Speed = runSpeed;

            if (!_isDeadOrDying)
                RunAnimationDisplay();
        }
    }

    private bool TryFlipTowardWarriorIfNeeded(Warrior w)
    {
        if (w == null) return false;

        RefreshFacingFlags();

        float myX = GetMyCenterX();
        float wx = GetWarriorCenterX(w);

        bool warriorOnLeft = wx < myX - frontEpsilon;
        bool warriorOnRight = wx > myX + frontEpsilon;

        bool needFlip = (leftFacing && warriorOnRight) || (rightFacing && warriorOnLeft);
        if (!needFlip) return false;

        Flip();
        _lastFlipTime = Time.time;

        return true;
    }

    public override float StepRightMaxamize()
    {
        return base.StepRightMaxamize() + 1.5f;
    }

    public override float StepLeftMaxamize()
    {
        return base.StepLeftMaxamize() - 1.5f;
    }

    private void OnDestroy()
    {
        CancelInvoke();
        StopLaserDamage();
    }

    private Vector2 GetGunWorldDir()
    {
        if (gunAimPivot == null)
            return rightFacing ? Vector2.right : Vector2.left;

        Vector2 localAxis = (barrelLocalAxis.sqrMagnitude > 0.0001f)
            ? barrelLocalAxis.normalized
            : Vector2.right;

        Vector2 worldDir = gunAimPivot.TransformDirection(localAxis);
        if (worldDir.sqrMagnitude < 0.0001f)
            worldDir = rightFacing ? Vector2.right : Vector2.left;

        return worldDir.normalized;
    }

    private void BeginDodgeAimFreeze()
    {
        _dodgeAimFrozen = true;

        _dodgeFrozenGunAngle = (gunAimPivot != null)
            ? gunAimPivot.eulerAngles.z
            : _lockedAngle;

        Vector2 fallback = GetGunWorldDir();
        _dodgeFrozenLaserDir = fallback;

        if (laserVFX != null && laserVFX.IsLaserActive)
        {
            Vector2 start = (laserVFX.firePoint != null)
                ? (Vector2)laserVFX.firePoint.position
                : (Vector2)(gunAimPivot != null ? gunAimPivot.position : transform.position);

            Vector2 end = laserVFX.CurrentEndPos;
            Vector2 d = end - start;
            if (d.sqrMagnitude > 0.0001f)
                _dodgeFrozenLaserDir = d.normalized;
        }

        if (Mathf.Abs(_dodgeFrozenLaserDir.x) > 0.001f)
            _dodgeSideSign = _dodgeFrozenLaserDir.x >= 0f ? 1 : -1;
        else
            _dodgeSideSign = rightFacing ? 1 : -1;
    }

    private void EndDodgeAimFreeze()
    {
        _dodgeAimFrozen = false;

        _hasLockedAngle = false;
        _inNoRotateZone = false;
        _aimAngleInitialized = false;
    }

    private Vector2 GetCurrentMuzzleDirWorld()
    {
        if (gunAimPivot == null)
            return rightFacing ? Vector2.right : Vector2.left;

        if (laserVFX != null && laserVFX.firePoint != null)
        {
            Vector2 d = (Vector2)laserVFX.firePoint.position - (Vector2)gunAimPivot.position;
            if (d.sqrMagnitude > 0.000001f)
            {
                d.Normalize();
                return invertMuzzleDir ? -d : d;
            }
        }

        Vector2 localAxis = (barrelLocalAxis.sqrMagnitude > 0.0001f) ? barrelLocalAxis.normalized : Vector2.right;
        Vector3 world = gunAimPivot.localToWorldMatrix.MultiplyVector(new Vector3(localAxis.x, localAxis.y, 0f));
        Vector2 w = new Vector2(world.x, world.y);
        if (w.sqrMagnitude > 0.000001f)
        {
            w.Normalize();
            return invertMuzzleDir ? -w : w;
        }

        return rightFacing ? Vector2.right : Vector2.left;
    }

    private void AimGunPivotToWorldDir(Vector2 desiredWorldDir)
    {
        if (gunAimPivot == null) return;
        if (desiredWorldDir.sqrMagnitude < 0.000001f) return;

        desiredWorldDir.Normalize();

        for (int i = 0; i < Mathf.Max(1, gunSolvePasses); i++)
        {
            Vector2 current = GetCurrentMuzzleDirWorld();
            float delta = Vector2.SignedAngle(current, desiredWorldDir);

            if (Mathf.Abs(delta) <= gunSolveEpsilonDeg)
                break;

            gunAimPivot.Rotate(0f, 0f, delta, Space.World);
        }
    }

    private void UpdateDodgeSideSign(Warrior w)
    {
        if (w == null) return;

        float refX = (gunAimPivot != null) ? gunAimPivot.position.x : GetMyCenterX();
        float wx = GetWarriorCenterX(w);
        float dx = wx - refX;

        int wantedSide = 0;
        if (dx > centerDeadZone) wantedSide = 1;
        else if (dx < -centerDeadZone) wantedSide = -1;
        else return;

        if (wantedSide != _dodgeSideSign)
        {
            if (Time.time - _lastDodgeSideSwitchTime < dodgeSideSwitchMinInterval)
                return;

            _dodgeSideSign = wantedSide;
            _lastDodgeSideSwitchTime = Time.time;
        }
    }


    private Vector2 GetDodgeFlatDir()
    {
        return _dodgeSideSign > 0 ? Vector2.right : Vector2.left;
    }

    private void UpdateDodgeSideAndRotate180IfChanged(Warrior w)
    {
        int before = _dodgeSideSign;
        UpdateDodgeSideSign(w);

        if (before != 0 && _dodgeSideSign != before)
            _dodgeFrozenGunAngle = Mathf.Repeat(_dodgeFrozenGunAngle + 180f, 360f);
    }

    private bool IsWarriorInFrontStrict(Warrior w)
    {
        if (w == null) return false;

        RefreshFacingFlags();

        float myX = GetMyCenterX();
        float wx = GetWarriorCenterX(w);
        float dx = wx - myX;

        if (Mathf.Abs(dx) <= frontEpsilon) return true;

        return rightFacing ? (dx > 0f) : (dx < 0f);
    }

    [Header("Dodge Pre-Flip (before muzzle cross)")]
    [SerializeField] private bool preFlipBeforeMuzzleWhileDodging = true;
    [SerializeField] private float preFlipMuzzleDistance = 0.45f;     // trigger zone around muzzle X
    [SerializeField] private float preFlipLookaheadTime = 0.10f;      // seconds to predict warrior X
    [SerializeField] private float preFlipMinWarriorSpeedX = 0.15f;   // ignore tiny jitter velocity
    [SerializeField] private float preFlipMuzzleEpsilon = 0.02f;      // cross tolerance

    private float GetMuzzleX()
    {
        if (laserVFX != null && laserVFX.firePoint != null)
            return laserVFX.firePoint.position.x;

        if (gunAimPivot != null)
            return gunAimPivot.position.x;

        return GetMyCenterX();
    }

    private float GetWarriorVelocityX(Warrior w)
    {
        if (w == null) return 0f;

        Rigidbody2D rb = (w.collider2 != null) ? w.collider2.attachedRigidbody : null;
        if (rb == null) rb = w.GetComponent<Rigidbody2D>();

        return rb != null ? rb.linearVelocity.x : 0f;
    }

    private bool TryPreFlipBeforeMuzzleWhileDodging(Warrior w)
    {
        if (!preFlipBeforeMuzzleWhileDodging || w == null) return false;
        if (!allowFlipWhileLaserActive) return false;
        if (Time.time - _lastFlipTime < flipWhileFiringMinInterval) return false;

        RefreshFacingFlags();

        float muzzleX = GetMuzzleX();
        float wxNow = GetWarriorCenterX(w);
        float vx = GetWarriorVelocityX(w);

        // Ignore near-zero x velocity
        if (Mathf.Abs(vx) < preFlipMinWarriorSpeedX) return false;

        float relNow = wxNow - muzzleX; // >0 warrior on right of muzzle, <0 on left

        // Only if warrior is near muzzle zone
        if (Mathf.Abs(relNow) > preFlipMuzzleDistance) return false;

        // Must be moving toward muzzle line
        if (relNow * vx >= 0f) return false;

        // Predict crossing
        float wxPred = wxNow + vx * preFlipLookaheadTime;
        float relPred = wxPred - muzzleX;

        bool willCrossSoon =
            (Mathf.Sign(relNow) != Mathf.Sign(relPred)) ||
            (Mathf.Abs(relPred) <= preFlipMuzzleEpsilon);

        if (!willCrossSoon) return false;

        // Face predicted side
        bool predictedRight = relPred > 0f;
        bool needFlip = (predictedRight && leftFacing) || (!predictedRight && rightFacing);
        if (!needFlip) return false;

        Flip();
        _lastFlipTime = Time.time;

        // Reset aim stabilizers after body flip
        _hasLockedAngle = false;
        _inNoRotateZone = false;
        _aimAngleInitialized = false;

        // Keep dodge-side logic coherent with body facing
        _dodgeSideSign = rightFacing ? 1 : -1;
        _lastDodgeSideSwitchTime = Time.time;

        return true;
    }

    #region test
    [Header("Forward Safety")]
    [SerializeField] private float muzzleFrontTolerance = 0.005f; // strict: near-zero is treated as NOT in front
    [SerializeField] private bool clampDodgeAimToForward = true;

    private Vector2 GetForwardFlatDir()
    {
        RefreshFacingFlags();
        return rightFacing ? Vector2.right : Vector2.left;
    }

    private bool IsWarriorInFrontFromMuzzle(Warrior w)
    {
        if (w == null) return false;

        RefreshFacingFlags();

        float muzzleX = GetMuzzleX();          // you already have GetMuzzleX()
        float warriorX = GetWarriorCenterX(w); // you already have GetWarriorCenterX()
        float dx = warriorX - muzzleX;

        // Strict check: if very close to muzzle line, do NOT consider "front"
        return rightFacing ? (dx > muzzleFrontTolerance) : (dx < -muzzleFrontTolerance);
    }

    private Vector2 ForceForwardIfBackward(Vector2 dir)
    {
        Vector2 fwd = GetForwardFlatDir();
        if (dir.sqrMagnitude < 0.0001f) return fwd;

        dir.Normalize();
        if (Vector2.Dot(dir, fwd) <= 0f)
            return fwd; // hard clamp (never shoot backward through own body)

        return dir;
    }

    private bool CanFireLaserNow(Warrior w)
    {
        if (!mustFaceToFire) return true;
        if (w == null) return false;

        if (!IsWarriorInFrontFromMuzzle(w)) return false;
        if (Time.time - _lastFlipTime < faceThenFireDelay) return false;

        return true;
    }
    #endregion
    protected override void ConfigureAttack()
    {
        // Do not assign defaults here.
        // Defaults belong in Start(), so spawn overrides can win cleanly.
        base.ConfigureAttack();
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
    private bool IsOverlapPushLocked()
    {
        return _overlapPushCycleActive || _overlapCycleConsumedUntilSeparation || _isResolvingBodyOverlap;
    }

    protected override void OnDamaged(float damage, bool killed)
    {
        base.OnDamaged(damage, killed);

        // Hit stepback / stun can interrupt a movement coroutine while the lift moves.
        // Re-seat immediately, but only for this M97 instance.
        RefreshM97MovingLiftAntiTunnel("damaged", allowPositionCorrection: true);

        if (killed) return;
        if (damage <= 0f) return;

        _damageHitCount++;

        if (_damageHitCount >= hitsToStun)
        {
            _damageHitCount = 0;

            Warrior warrior = GameMgr.Instance?.WarriorInstance;

            // PATCH:
            // Only block movement on stun if warrior is in front of M97
            if (warrior != null && IsWarriorInFrontStrict(warrior))
            {
                // DeactivateLaser();
                StopMoveTowardCoroutine();
                CanMove = false;
            }

            ApplyStun(stunAfterHitsSeconds);
        }
    }

    private void StickToDescendingMovingPlatformSurface()
    {
        if (!stickToDescendingPlatformSurface)
            return;

        RefreshM97MovingLiftAntiTunnel("legacy-stick", allowPositionCorrection: true);
    }

    private void ConfigureM97AntiTunnelRigidbody()
    {
        if (!enableM97MovingLiftAntiTunnel || rigidbody2 == null)
            return;

        if (forceM97ContinuousCollision)
            rigidbody2.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        rigidbody2.interpolation = RigidbodyInterpolation2D.Interpolate;
        rigidbody2.constraints |= RigidbodyConstraints2D.FreezeRotation;
    }

    private void CaptureM97FixedBodyState()
    {
        Collider2D support = GetM97SupportCollider();
        if (support == null)
            return;

        Physics2D.SyncTransforms();

        _m97LastFixedBodyBottomY = support.bounds.min.y;
        _m97HasLastFixedBody = true;
    }

    private Collider2D GetM97SupportCollider()
    {
        if (NormalCollider != null && NormalCollider.enabled && !NormalCollider.isTrigger)
            return NormalCollider;

        if (collider2 != null && collider2.enabled && !collider2.isTrigger)
            return collider2;

        return null;
    }

    private bool RefreshM97MovingLiftAntiTunnel(string reason, bool allowPositionCorrection)
    {
        if (!enableM97MovingLiftAntiTunnel)
            return false;

        if (_isDeadOrDying || _m97InLiftAntiTunnelCorrection)
            return false;

        if (rigidbody2 == null)
            return false;

        Collider2D support = GetM97SupportCollider();
        if (support == null)
            return false;

        Physics2D.SyncTransforms();

        MovingVerticalPlatform lift = GetBestM97MovingLiftCandidate(support);
        if (lift == null || lift.platformCollider == null)
            return false;

        if (!IsM97RecoverableOnMovingLift(lift, support, allowPredictiveRecovery: true))
            return false;

        _m97LastLiftPlatform = lift;
        _m97LastLiftSupportTime = Time.time;

        CurrentplatForm = lift;
        CommitPatrolEdgeForMovingVerticalPlatform();

        if (allowPositionCorrection)
            SeatM97OnMovingLift(lift, support, reason);

        return true;
    }

    private MovingVerticalPlatform GetBestM97MovingLiftCandidate(Collider2D support)
    {
        if (CurrentplatForm is MovingVerticalPlatform currentLift &&
            IsUsableM97MovingLift(currentLift) &&
            IsM97RecoverableOnMovingLift(currentLift, support, allowPredictiveRecovery: true))
        {
            return currentLift;
        }

        if (IsUsableM97MovingLift(_m97LastLiftPlatform) &&
            Time.time <= _m97LastLiftSupportTime + m97LiftSupportGraceTime &&
            IsM97RecoverableOnMovingLift(_m97LastLiftPlatform, support, allowPredictiveRecovery: true))
        {
            return _m97LastLiftPlatform;
        }

        return FindNearestRecoverableM97MovingLift(support);
    }

    private bool IsUsableM97MovingLift(MovingVerticalPlatform lift)
    {
        return lift != null &&
               lift.isActiveAndEnabled &&
               lift.platformCollider != null &&
               lift.platformCollider.enabled;
    }

    private MovingVerticalPlatform FindNearestRecoverableM97MovingLift(Collider2D support)
    {
        if (support == null)
            return null;

        MovingVerticalPlatform[] lifts = FindObjectsByType<MovingVerticalPlatform>(FindObjectsSortMode.None);
        MovingVerticalPlatform best = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < lifts.Length; i++)
        {
            MovingVerticalPlatform lift = lifts[i];
            if (!IsUsableM97MovingLift(lift))
                continue;

            if (!IsM97RecoverableOnMovingLift(lift, support, allowPredictiveRecovery: true))
                continue;

            float score = Mathf.Abs(support.bounds.min.y - lift.platformCollider.bounds.max.y);
            if (score < bestScore)
            {
                bestScore = score;
                best = lift;
            }
        }

        return best;
    }

    private bool IsM97RecoverableOnMovingLift(
        MovingVerticalPlatform lift,
        Collider2D support,
        bool allowPredictiveRecovery)
    {
        if (!IsUsableM97MovingLift(lift) || support == null)
            return false;

        Bounds platformBounds = lift.platformCollider.bounds;
        Bounds bodyBounds = support.bounds;

        bool horizontallyOverSurface =
            bodyBounds.max.x > platformBounds.min.x + m97LiftHorizontalSkin &&
            bodyBounds.min.x < platformBounds.max.x - m97LiftHorizontalSkin;

        bool centerStillBelongsToSurface =
            bodyBounds.center.x >= platformBounds.min.x - m97LiftHorizontalSearchPadding &&
            bodyBounds.center.x <= platformBounds.max.x + m97LiftHorizontalSearchPadding;

        if (!horizontallyOverSurface && !centerStillBelongsToSurface)
            return false;

        float dynamicTolerance = GetM97LiftDynamicTolerance(lift);
        float desiredBottom = platformBounds.max.y + m97LiftSeatOffset;
        float bottomDelta = bodyBounds.min.y - desiredBottom;

        float maxGap = Mathf.Max(m97LiftMaxRecoverGap, platformSurfaceTopTolerance, dynamicTolerance);
        float maxSink = Mathf.Max(m97LiftMaxRecoverSink, Mathf.Abs(platformSurfaceBottomTolerance), dynamicTolerance);

        bool bodyStillMostlyAboveTop =
            bodyBounds.center.y >= platformBounds.max.y - Mathf.Min(bodyBounds.extents.y * 0.35f, maxSink);

        if (!bodyStillMostlyAboveTop)
            return false;

        bool closeToSurface = bottomDelta >= -maxSink && bottomDelta <= maxGap;
        if (closeToSurface)
            return true;

        if (!allowPredictiveRecovery || !_m97HasLastFixedBody)
            return false;

        // Fast lift / solver gap fallback:
        // Previous bottom was close/above top and the current bottom skipped past it.
        bool crossedTopBetweenFixedSteps =
            _m97LastFixedBodyBottomY >= platformBounds.max.y - maxSink &&
            bodyBounds.min.y <= platformBounds.max.y + maxGap;

        return crossedTopBetweenFixedSteps;
    }

    private float GetM97LiftDynamicTolerance(MovingVerticalPlatform lift)
    {
        float fixedStep = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;

        float liftStep = lift != null
            ? Mathf.Abs(lift.LastLiftDelta.y) + m97LiftDynamicPadding
            : m97LiftDynamicPadding;

        float bodyStep = rigidbody2 != null
            ? Mathf.Abs(rigidbody2.linearVelocity.y) * fixedStep + m97LiftDynamicPadding
            : m97LiftDynamicPadding;

        return Mathf.Max(liftStep, bodyStep, m97LiftDynamicPadding);
    }

    private Vector2 ConstrainM97PositionToMovingLiftSurface(Vector2 desiredPosition)
    {
        if (!enableM97MovingLiftAntiTunnel)
            return desiredPosition;

        Collider2D support = GetM97SupportCollider();
        if (support == null)
            return desiredPosition;

        MovingVerticalPlatform lift = GetBestM97MovingLiftCandidate(support);
        if (lift == null || lift.platformCollider == null)
            return desiredPosition;

        if (!IsM97RecoverableOnMovingLift(lift, support, allowPredictiveRecovery: true))
            return desiredPosition;

        Bounds platformBounds = lift.platformCollider.bounds;
        Bounds bodyBounds = support.bounds;

        float bottomOffsetFromRoot = bodyBounds.min.y - transform.position.y;
        desiredPosition.y = platformBounds.max.y + m97LiftSeatOffset - bottomOffsetFromRoot;
        desiredPosition.x = ClampM97XToLiftSurface(desiredPosition.x, platformBounds, bodyBounds);

        _m97LastLiftPlatform = lift;
        _m97LastLiftSupportTime = Time.time;
        CurrentplatForm = lift;

        return desiredPosition;
    }

    private void SeatM97OnMovingLift(MovingVerticalPlatform lift, Collider2D support, string reason)
    {
        if (!IsUsableM97MovingLift(lift) || support == null)
            return;

        _m97InLiftAntiTunnelCorrection = true;

        try
        {
            Physics2D.SyncTransforms();

            Bounds platformBounds = lift.platformCollider.bounds;
            Bounds bodyBounds = support.bounds;

            float bottomOffsetFromRoot = bodyBounds.min.y - transform.position.y;
            float targetY = platformBounds.max.y + m97LiftSeatOffset - bottomOffsetFromRoot;

            Vector2 targetPosition = rigidbody2 != null
                ? rigidbody2.position
                : (Vector2)transform.position;

            targetPosition.y = targetY;

            // Preserve the character's intended horizontal movement. This routine seat runs
            // every FixedUpdate while M97 rides the lift; writing the *current* X here would
            // cancel the patrol coroutine's pending MovePosition each physics step and pin
            // M97 horizontally on the platform. When a fresh core-move request exists (the
            // patrol/chase is actively moving), seat to that requested X instead so the
            // anti-tunnel layer only owns Y and never fights horizontal travel.
            float seatX = targetPosition.x;
            if (TryGetCoreMoveRequestForRecentStep(out Vector2 requestedCoreMove))
                seatX = requestedCoreMove.x;

            targetPosition.x = ClampM97XToLiftSurface(seatX, platformBounds, bodyBounds);

            MarkIntentionalEnemyDisplacement(Time.fixedDeltaTime * 4f);

            if (rigidbody2 != null)
            {
                if (m97LiftHardSnapInEmergency)
                    rigidbody2.position = targetPosition;
                else
                    rigidbody2.MovePosition(targetPosition);

                if (m97LiftZeroVerticalVelocityOnSeat)
                {
                    Vector2 velocity = rigidbody2.linearVelocity;
                    velocity.y = 0f;
                    rigidbody2.linearVelocity = velocity;
                }

                rigidbody2.angularVelocity = 0f;
                rigidbody2.WakeUp();
            }
            else
            {
                transform.position = new Vector3(targetPosition.x, targetPosition.y, transform.position.z);
            }

            Physics2D.SyncTransforms();

            _m97LastLiftPlatform = lift;
            _m97LastLiftSupportTime = Time.time;
            CurrentplatForm = lift;
            CommitPatrolEdgeForMovingVerticalPlatform();
        }
        finally
        {
            _m97InLiftAntiTunnelCorrection = false;
        }
    }

    private float ClampM97XToLiftSurface(float worldX, Bounds platformBounds, Bounds bodyBounds)
    {
        float halfWidth = Mathf.Max(bodyBounds.extents.x, 0.01f);
        float minX = platformBounds.min.x + halfWidth + m97LiftHorizontalSkin;
        float maxX = platformBounds.max.x - halfWidth - m97LiftHorizontalSkin;

        if (minX <= maxX)
            return Mathf.Clamp(worldX, minX, maxX);

        return platformBounds.center.x;
    }

}
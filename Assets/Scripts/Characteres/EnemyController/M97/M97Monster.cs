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

        // Re-assert committed patrol target after base update
        CommitPatrolEdgeForMovingVerticalPlatform();

        Warrior warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null || warrior.IsDeadOrDying)
        {
            if (IsLaserShowing || warriorInLaserScope || laserDamageCoroutine != null)
                DeactivateLaser();

            return;
        }

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
            if (IsSamePlatform(warrior))
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

        bool wantsBeyondEdge = IsTargetOutsideCurrentPlatformSafeRange(x);

        bool shouldClamp = ClampMoveToCurrentPlatform &&
                           !(AllowEdgeExitWhenTargetOutside && wantsBeyondEdge);

        float targetX = shouldClamp ? ClampToCurrentPlatform(x) : x;

        // Inside MoveTowardPositionNoFlipAction
        while (Mathf.Abs(targetX - transform.position.x) > 0.1f)
        {
            Vector2 currentPosition = rigidbody2.position; // Use RB position
            Vector2 targetPosition = new Vector2(targetX, currentPosition.y);
            Vector2 newPosition = Vector2.MoveTowards(currentPosition, targetPosition, Speed * Time.deltaTime);

            // Use MovePosition for Dynamic RBs to keep physics happy
            rigidbody2.MovePosition(new Vector2(newPosition.x, rigidbody2.position.y));
            yield return null;
        }

        if (shouldClamp)
        {
            float finalX = ClampToCurrentPlatform(targetX);
            transform.position = new Vector3(finalX, transform.position.y, transform.position.z);
        }
        else
        {
            transform.position = new Vector3(targetX, transform.position.y, transform.position.z);
        }

        _isMoving = false;
        activesMoveCoroutine = null;
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
    protected override void FixedUpdate() // Added 'override'
    {
        // 1. Run the Parent's ground check first
        base.FixedUpdate();

        // 2. Run M97 specific sticking logic
        StickToDescendingMovingPlatformSurface();

        // 3. Run overlap resolution logic
        if (!_resolveOverlapThisStep)
            return;

        if (_overlapWarrior != null)
            ResolveOverlapPush(_overlapWarrior);

        _resolveOverlapThisStep = false;
        _overlapWarrior = null;
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

        if (myRb != null)
            myRb.MovePosition(myRb.position + m97Delta);
        else
            transform.position += (Vector3)m97Delta;

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
        if (mustFaceToFire && !CanFireLaserNow(w))
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
        if (!stickToDescendingPlatformSurface || _isDeadOrDying) return;
        if (CurrentplatForm is not MovingVerticalPlatform movingPlatform) return;

        // If we are already parented, the platform is already moving us.
        // We only need to ensure we don't "float" when the platform accelerates downward.
        if (transform.parent == movingPlatform.transform)
        {
            // Simply ensure vertical velocity isn't positive (jumping) 
            // while the platform is moving down.
            if (movingPlatform.IsMovingUpNow == false && rigidbody2.linearVelocity.y > 0)
            {
                rigidbody2.linearVelocity = new Vector2(rigidbody2.linearVelocity.x, 0);
            }
            return;
        }

        // Use the Rigidbody of the platform to get its exact velocity
        Rigidbody2D platformRb = movingPlatform.GetComponent<Rigidbody2D>();
        if (platformRb == null || rigidbody2 == null) return;

        Bounds pb = movingPlatform.platformCollider.bounds;
        Bounds eb = NormalCollider != null ? NormalCollider.bounds : collider2.bounds;

        float verticalGap = eb.min.y - pb.max.y;

        // If the enemy is within the "Snap Zone"
        if (verticalGap >= platformSurfaceBottomTolerance && verticalGap <= platformSurfaceTopTolerance)
        {
            // 1. Position Snapping (Keep them exactly on the surface)
            float targetY = pb.max.y + eb.extents.y + platformSurfaceSeatOffset;
            rigidbody2.position = new Vector2(rigidbody2.position.x, targetY);

            // 2. Velocity Matching (CRITICAL for resolution changes/lag)
            // If the platform is moving down, we MUST move down at the same speed
            Vector2 v = rigidbody2.linearVelocity;
            v.y = platformRb.linearVelocity.y;
            rigidbody2.linearVelocity = v;
        }
    }
}
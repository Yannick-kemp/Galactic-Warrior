using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.EnemyController;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ZalaytyMonster : Enemy
{
    [Header("A* Follow Settings")]
    [SerializeField] private float repathInterval = 0.25f;

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
    [Tooltip("If true: when in range, Zalayty stops moving and attacks.")]
    [SerializeField] private bool stopAndAttackWhenInRange = true;

    [Header("ExtraJump Conditions")]
    [SerializeField] private float closeToWarriorDistance = 0.9f;   // "very close" distance
    [SerializeField] private float warriorEdgeMargin = 1.80f;        // how near to platform edge counts as "near edge"
    [SerializeField] private float touchingExtraRadius = 0.05f;     // optional small inflate


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
    [SerializeField] private bool enableSqueezeExtraJump = true;

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


    private float _nextSqueezeCheckTime;
    private float _lastSqueezeJumpTime;


    private Coroutine followRoutine;

    private bool isOnEdgePlatform = false;
    public bool IsOnEdgePlatform => isOnEdgePlatform;

    public void SetJumping(bool v) => isOnEdgePlatform = v;


    public override bool HardAnchorToMovingPlatforms => false;

    public bool inRangeOrAttacking;

    [Header("Independent Movement - Zalayty Only")]
    [Tooltip("If true, Zalayty does not use CharacterController MoveToward/JumpToward and is not clamped by platform edges.")]
    [SerializeField] private bool useIndependentMovement = true;

    [Tooltip("Distance on X considered reached by Zalayty's independent horizontal movement.")]
    [SerializeField, Min(0.01f)] private float independentMoveArriveDistance = 0.08f;

    [Tooltip("Small tolerance used when deciding if Zalayty landed on a platform top.")]
    [SerializeField, Min(0f)] private float independentLandingBand = 0.16f;

    [Tooltip("Stops only Zalayty horizontal velocity when his independent movement is interrupted.")]
    [SerializeField] private bool stopHorizontalVelocityOnIndependentStop = true;

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

        if (followRoutine != null) StopCoroutine(followRoutine);
        followRoutine = StartCoroutine(FollowWarriorLoop());
    }

    [SerializeField] private float maxFallSpeed = 25f;

    [SerializeField] private LayerMask platformMask;

    [SerializeField] private float skin = 0.02f;

    protected override void Update()
    {
        // Do NOT call base.Update() here. Enemy.Update() contains patrol/edge safety
        // code that stops movement and clamps enemies back onto the current platform.
        // Zalayty owns his movement, so we keep only the generic death fallback.
        CheckWorldYDeathFallback();
        initDirection();
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

        StartZalaytyJumpTo(landing, jumpHeight, duration, expectedPlatform);
        return true;
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

        if (nearLeft || nearRight)
        {
            int t = 0;
        }
        return nearLeft || nearRight;
    }

    private IEnumerator FollowWarriorLoop()
    {
        yield return null;

        while (this != null)
        {
            var warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (isOnEdgePlatform && inRangeOrAttacking && CurrentplatForm == warrior.CurrentplatForm && IsWarriorNearPlatformEdge(warrior))
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

            if (CurrentplatForm == null || warrior.CurrentplatForm == null)
            {
                StopMoveTowardCoroutine();
                EnterWaitAnimation();
                yield return new WaitForSeconds(repathInterval);
                continue;
            }

            // SAME PLATFORM: warrior-like melee
            if (CurrentplatForm == warrior.CurrentplatForm)
            {
                float targetX = GetIndependentFollowTargetX(warrior);
                SetDirectionVariables(targetX);

                // NEW: squeeze-driven extraJump (group logic) //  Only if checkbox is enabled
                if (allowSqueezeExtraJump && TrySqueezeExtraJump(warrior))
                {
                    yield return new WaitForSeconds(0.08f);
                    continue;
                }

                //if (TrySqueezeExtraJump(warrior))
                //{
                //    yield return new WaitForSeconds(0.08f);
                //    continue;
                //}


                inRangeOrAttacking = TryToPerformAttack(warrior.transform);

                if (inRangeOrAttacking && stopAndAttackWhenInRange)
                {
                    StopMoveTowardCoroutine();
                    yield return new WaitForSeconds(repathInterval);
                    continue;
                }

                if (CanMove && activesMoveCoroutine == null && !_isJumping)
                {
                    ExitWaitAnimation();
                    RunAnimationDisplay();
                    StartZalaytyMoveToX(targetX);
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

    private float GetIndependentFollowTargetX(Warrior warrior)
    {
        if (warrior == null)
            return transform.position.x;

        if (!useIndependentMovement)
            return ClampToCurrentPlatform(warrior.transform.position.x);

        return warrior.transform.position.x;
    }

    private void StartZalaytyMoveToX(float targetX)
    {
        StopMoveTowardCoroutine();
        ExitWaitAnimation();
        RunAnimationDisplay();

        activesMoveCoroutine = useIndependentMovement
            ? MoveTowardXIndependent(targetX)
            : MoveTowardPostionAction(targetX);

        StartCoroutine(activesMoveCoroutine);
    }

    private IEnumerator MoveTowardXIndependent(float targetX)
    {
        if (_isMoving)
            yield break;

        _isMoving = true;
        WaitForFixedUpdate wait = new WaitForFixedUpdate();

        while (this != null && Mathf.Abs(targetX - transform.position.x) > independentMoveArriveDistance)
        {
            if (!CanMove || _deathStarted || IsStunned)
                break;

            if (!IsAttackAnimationActive())
                FlipCharacter(targetX);

            Vector2 current = rigidbody2 != null
                ? rigidbody2.position
                : (Vector2)transform.position;

            float step = Speed * Time.fixedDeltaTime;
            float nextX = Mathf.MoveTowards(current.x, targetX, step);

            MoveZalaytyBody(new Vector2(nextX, current.y));
            yield return wait;
        }

        StopHorizontalVelocityIfNeeded();
        _isMoving = false;
        activesMoveCoroutine = null;
    }

    private void StartZalaytyJumpTo(
        Vector2 landing,
        float jumpHeight,
        float duration,
        PlatFormColliderTrigger expectedLandingPlatform,
        bool forceJumpTowardPositionAction = false)
    {
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
    }

    private IEnumerator JumpArcIndependent(Vector2 landing, float height, float duration, PlatFormColliderTrigger expectedLandingPlatform)
    {
        if (_isJumping)
            yield break;

        _isJumping = true;
        targetReached = false;
        DescendentPhase = false;
        SetJumping(false);

        Vector2 start = rigidbody2 != null
            ? rigidbody2.position
            : (Vector2)transform.position;

        float elapsed = 0f;
        float previousY = start.y;
        WaitForFixedUpdate wait = new WaitForFixedUpdate();

        duration = Mathf.Max(0.05f, duration);

        while (elapsed < duration)
        {
            if (_deathStarted || IsStunned)
                break;

            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            Vector2 pos = Vector2.Lerp(start, landing, t);
            pos.y += height * Mathf.Sin(Mathf.PI * t);

            DescendentPhase = pos.y < previousY;
            previousY = pos.y;

            FlipCharacter(landing.x);
            MoveZalaytyBody(pos);
            yield return wait;
        }

        MoveZalaytyBody(landing);

        if (expectedLandingPlatform != null)
            CurrentplatForm = expectedLandingPlatform;

        if (_activeJumpTargetPlatform == expectedLandingPlatform)
            _activeJumpTargetPlatform = null;

        RestoreActiveJumpDownSourcePlatformNow();

        StopHorizontalVelocityIfNeeded();
        DescendentPhase = false;
        targetReached = true;
        _isJumping = false;
        activesJumpCoroutine = null;
        SetJumping(false);
    }

    private void MoveZalaytyBody(Vector2 position)
    {
        if (rigidbody2 != null)
            rigidbody2.MovePosition(position);
        else
            transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

    private void StopHorizontalVelocityIfNeeded()
    {
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
        StopHorizontalVelocityIfNeeded();
    }

    public void NotifyIndependentPlatformCollision(PlatFormColliderTrigger platform, Collision2D collision)
    {
        if (platform == null)
            return;

        if (!useIndependentMovement)
        {
            CurrentplatForm = platform;
            return;
        }

        if (!IsIndependentTopLanding(platform))
            return;

        CurrentplatForm = platform;

        if (activesJumpCoroutine != null)
            StopCoroutine(activesJumpCoroutine);

        targetReached = true;
        DescendentPhase = false;
        _isJumping = false;
        activesJumpCoroutine = null;
        SetJumping(false);

        if (rigidbody2 != null && rigidbody2.linearVelocity.y < 0f)
        {
            Vector2 v = rigidbody2.linearVelocity;
            v.y = 0f;
            rigidbody2.linearVelocity = v;
        }

        RestoreActiveJumpDownSourcePlatformNow();
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

        bool horizontallyOverTop = cb.max.x > pb.min.x && cb.min.x < pb.max.x;
        bool closeToTop = cb.min.y >= pb.max.y - independentLandingBand;
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
        StartZalaytyJumpTo(landing, jumpHeight, duration, nextPlatform);

        float landTimeout = 2.0f;
        while (landTimeout > 0f)
        {
            if (CurrentplatForm == nextPlatform)
                break;

            landTimeout -= Time.deltaTime;
            yield return null;
        }
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

        // Spawn spark at contact (visual) - you can keep it before or after canHit checks
        SpawnSparkAtContact(warrior);

        // must be same platform/in range like your original logic
        bool canHit =
            warrior.CurrentplatForm != null &&
            CurrentplatForm != null &&
            warrior.CurrentplatForm == CurrentplatForm &&
            NormalCollider != null &&
            EnemyRangeService != null &&
            EnemyRangeService.IsInRange;

        if (!canHit) return;

        // Optional stronger physical-touch check (recommended for Zalayty)
        if (requireTouchingToHit && !warrior.collider2.IsTouching(NormalCollider))
            return;

        // Optional front check if you want it enabled by inspector flag
        if (requireTargetInFront && !IsWarriorInFront(warrior.transform))
            return;

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
        if (sparkPrefab == null) return;

        Bounds w = warrior.collider2.bounds;
        Bounds e = (NormalCollider != null) ? NormalCollider.bounds : collider2.bounds;

        // Contact point on the side between enemy and warrior
        float x = (e.center.x < w.center.x) ? w.min.x : w.max.x;

        // Use a stable y (mid-height) or clamp to overlap region if you want:
        float y = Mathf.Clamp(e.center.y, w.min.y, w.max.y);

        Vector3 pos = new Vector3(x, y, 0f) + sparkOffset;

        GameObject fx = Instantiate(sparkPrefab, hitPoint.position, Quaternion.identity);

        // IMPORTANT: don't parent to warrior (parenting makes it follow!)
        fx.transform.localScale *= sparkScale;

        // If the prefab has ParticleSystems, ensure they run in world space (optional but helps)
        var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // <- follow parent
            ps.Play(true);
        }

        Destroy(fx, sparkDestroyAfter);
    }


    //private void SpawnSparkAtContact(Warrior warrior)
    //{
    //    if (sparkPrefab == null) return;

    //    Bounds w = warrior.collider2.bounds;
    //    Bounds e = (NormalCollider != null) ? NormalCollider.bounds : collider2.bounds;

    //    float x = (e.center.x < w.center.x) ? w.min.x : w.max.x;
    //    float y = Mathf.Clamp(e.center.y, w.min.y, w.max.y);

    //    Vector3 pos = new Vector3(x, y, 0f) + sparkOffset;

    //    GameObject fx = Instantiate(sparkPrefab, pos, Quaternion.identity);
    //    fx.transform.localScale *= sparkScale;

    //    var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
    //    foreach (var ps in systems)
    //    {
    //        var main = ps.main;
    //        main.simulationSpace = ParticleSystemSimulationSpace.Local;
    //        ps.Play(true);
    //    }

    //    Destroy(fx, sparkDestroyAfter);
    //}

    private static readonly ContactPoint2D[] _contacts = new ContactPoint2D[16];

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryHandleWarriorTopReboundCollision(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryHandleWarriorTopReboundCollision(collision);
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
        StartZalaytyJumpTo(
            landing,
            jumpHeight,
            duration,
            landingPlatform,
            forceJumpTowardPositionAction: true);

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
        float inset = Mathf.Max(0f, warriorTopReboundPlatformInset);

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

    private IEnumerator WarriorTopReboundArc(
        Vector2 landing,
        float height,
        float duration,
        PlatFormColliderTrigger expectedLandingPlatform)
    {
        // Zalayty-only rebound jump.
        // This intentionally follows the same controlled-arc pattern as
        // CharacterController.JumpTowardPositionAction(), but it is kept local to
        // Zalayty so Warrior platform behavior is not affected.
        if (_isJumping)
            yield break;

        _isJumping = true;
        targetReached = false;
        DescendentPhase = false;
        SetJumping(false);

        Vector2 startPosition = rigidbody2 != null
            ? rigidbody2.position
            : (Vector2)transform.position;

        Vector2 target = ClampWarriorTopReboundTargetToPlatform(landing, expectedLandingPlatform);

        float elapsedTime = 0f;
        FlipCharacter(target.x);

        float previousY = startPosition.y;
        Vector2 previousPosition = startPosition;
        WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();

        duration = Mathf.Max(0.05f, duration);
        height = Mathf.Max(0f, height);

        while (!targetReached)
        {
            if (_deathStarted || IsStunned)
                break;

            float stepTime = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : Time.deltaTime;
            elapsedTime += stepTime;
            float t = duration > 0f ? elapsedTime / duration : 1f;

            Vector2 desiredPosition;

            if (t <= 1f)
            {
                desiredPosition = Vector2.Lerp(startPosition, target, t);
                desiredPosition.y += height * Mathf.Sin(Mathf.PI * t);
            }
            else
            {
                // Safety fallback: if the sweep did not catch the platform before the
                // end of the short rebound, finish on the already-clamped target.
                // We do not keep horizontal momentum here because the requirement is
                // a controlled side rebound beside Warrior, not a long overshoot.
                desiredPosition = target;
            }

            bool movingDown = desiredPosition.y < previousPosition.y;

            // Destination-platform anti-tunneling:
            // Do not depend on CurrentplatForm here. The platform that matters is
            // the platform crossed by the sweep between previousPosition and
            // desiredPosition. This prevents Zalayty from passing through or
            // overshooting beyond the destination platform surface.
            if (movingDown &&
                TryResolveDestinationPlatformTopLanding(
                    previousPosition,
                    desiredPosition,
                    out Vector2 predictedLandingPosition,
                    out PlatFormColliderTrigger destinationPlatform))
            {
                MoveCharacterTo(predictedLandingPosition);
                CompleteWarriorTopReboundLanding(destinationPlatform);
                yield break;
            }

            MoveCharacterTo(desiredPosition);

            DescendentPhase = desiredPosition.y < previousY;
            previousY = desiredPosition.y;
            previousPosition = desiredPosition;

            // End exactly on the safe, clamped rebound target if no swept platform
            // landing was found. This prevents the old sliding/overshoot behavior.
            if (t >= 1f)
            {
                MoveCharacterTo(target);
                CompleteWarriorTopReboundLanding(expectedLandingPlatform);
                yield break;
            }

            yield return waitForFixedUpdate;
        }

        // Interrupted by death/stun: restore the source-platform ignore anyway.
        CompleteWarriorTopReboundLanding(expectedLandingPlatform);
    }

    private Vector2 ClampWarriorTopReboundTargetToPlatform(
        Vector2 target,
        PlatFormColliderTrigger expectedLandingPlatform)
    {
        if (expectedLandingPlatform == null || expectedLandingPlatform.platformCollider == null)
            return target;

        Collider2D body = GetZalaytyBodyCollider();
        if (body == null)
            return target;

        Bounds p = expectedLandingPlatform.platformCollider.bounds;
        Bounds z = body.bounds;

        float bodyHalfX = Mathf.Max(0.01f, z.extents.x);
        float bodyCenterOffsetX = z.center.x - transform.position.x;

        GetSafeCenterXRangeOnPlatform(p, bodyHalfX, out float safeMinCenterX, out float safeMaxCenterX);

        float targetCenterX = target.x + bodyCenterOffsetX;
        targetCenterX = Mathf.Clamp(targetCenterX, safeMinCenterX, safeMaxCenterX);

        return new Vector2(targetCenterX - bodyCenterOffsetX, p.max.y + landingYOffset);
    }

    private void CompleteWarriorTopReboundLanding(PlatFormColliderTrigger destinationPlatform)
    {
        if (destinationPlatform != null)
            CurrentplatForm = destinationPlatform;

        if (_activeJumpTargetPlatform == destinationPlatform)
            _activeJumpTargetPlatform = null;

        RestoreActiveJumpDownSourcePlatformNow();
        StopHorizontalVelocityIfNeeded();

        targetReached = true;
        DescendentPhase = false;
        _isJumping = false;
        activesJumpCoroutine = null;
        SetJumping(false);
        _warriorTopReboundActive = false;
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
}
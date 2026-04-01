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
        base.Update();
        initDirection();
        var warrior = GameMgr.Instance?.WarriorInstance;
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

        landingX = Mathf.Clamp(landingX, safeMinX, safeMaxX);

        // validate that we truly got to the requested side
        if (landOnLeftOfWarrior && landingX >= warB.min.x - 0.1f) return false;
        if (!landOnLeftOfWarrior && landingX <= warB.max.x + 0.1f) return false;

        Vector2 landing = new Vector2(landingX, platB.max.y + landingYOffset);

        float dx = Mathf.Abs(landing.x - transform.position.x);
        float jumpHeight = Mathf.Clamp(2.5f + dx * 0.05f, 2.5f, 6.0f);
        float duration = 0.4f;

        ExitWaitAnimation();
        StopMoveTowardCoroutine();
        StopJumpTowardCoroutine();

        JumpAnimationDisplay();
        activesJumpCoroutine = JumpTowardPositionAction(landing, jumpHeight, duration, null);
        StartCoroutine(activesJumpCoroutine);

        SetJumping(false);
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
                float targetX = ClampToCurrentPlatform(warrior.transform.position.x);
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
                    activesMoveCoroutine = MoveTowardPostionAction(targetX);
                    StartCoroutine(activesMoveCoroutine);
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

    private enum Side { Left, Right }

    private IEnumerator MoveAndJumpToPlatform(PlatFormColliderTrigger nextPlatform, Transform warrior)
    {
        if (CurrentplatForm == null || nextPlatform == null) yield break;

        if (_isJumping) yield break;

        Bounds curB = CurrentplatForm.platformCollider.bounds;
        Bounds nextB = nextPlatform.platformCollider.bounds;

        bool goingDown = nextB.max.y < curB.max.y - 0.05f;

        // Choose takeoff side (still needed to avoid re-landing on current platform footprint)
        float desiredX = warrior != null ? warrior.position.x : nextB.center.x;
        Side side = ChooseBestSideForTransition(curB, nextB, desiredX);

        // 1) Move to takeoff position
        float takeoffXInside = (side == Side.Right)
            ? (curB.max.x - takeoffEdgeMargin)
            : (curB.min.x + takeoffEdgeMargin);

        takeoffXInside = ClampToCurrentPlatform(takeoffXInside);
        SetDirectionVariables(takeoffXInside);

        if (CanMove && activesMoveCoroutine == null)
        {
            ExitWaitAnimation();
            RunAnimationDisplay();
            activesMoveCoroutine = MoveTowardPostionAction(takeoffXInside);
            StartCoroutine(activesMoveCoroutine);
        }

        float moveTimeout = 1.25f;
        while (moveTimeout > 0f)
        {
            if (CurrentplatForm == null) yield break;
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

        // 3) To prevent "landing back on current platform" (without IgnoreCollision):
        // when going DOWN we nudge outside current platform bounds on the chosen side.
        if (goingDown && rigidbody2 != null)
        {
            float outsideX = (side == Side.Right)
                ? (curB.max.x + dropSideOutEpsilon)
                : (curB.min.x - dropSideOutEpsilon);
            //µ
            //ExitWaitAnimation();
            JumpAnimationDisplay();
            rigidbody2.position = new Vector2(outsideX, rigidbody2.position.y);
        }

        // 4) Jump
        float dx = Mathf.Abs(landing.x - transform.position.x);
        float dy = landing.y - transform.position.y;

        float jumpHeight = goingDown ? 1.25f : Mathf.Clamp(dy + 2.5f, 2.5f, 10f);
        float duration = Mathf.Clamp((dx / Mathf.Max(0.01f, Speed)) * 0.65f, 0.25f, 0.8f);
        // ExitWaitAnimation();
        JumpAnimationDisplay();
        activesJumpCoroutine = JumpTowardPositionAction(landing, jumpHeight, duration, null);
        StartCoroutine(activesJumpCoroutine);

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
            if (!IsWarriorInFront(warrior))
            {
                if (leftFacing && transform.position.x < target.position.x || !leftFacing && transform.position.x > target.position.x)
                {
                    Flip();
                }
            }

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

        // Let base only trigger animation when appropriate
        base.OnAttackPerformed(attacker, attackedTarget);
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


    private void OnCollisionStay2D(Collision2D collision)
    {
        // Only care about Warrior
        if (!collision.collider.TryGetComponent<Assets.Scripts.Characteres.WarriorController.Warrior>(out var warrior))
            return;

        int count = collision.GetContacts(_contacts);

        for (int i = 0; i < count; i++)
        {
            // If Zalayty is above Warrior, the normal (for Zalayty) points UP
            if (_contacts[i].normal.y > 0.7f)
            {
                //Zalayty is on top of Warrior
                HandleZalaytyOnTopOfWarrior(warrior);
                return;
            }
        }
    }

    private void HandleZalaytyOnTopOfWarrior(Warrior warrior)
    {
        // Example fixes
        SetJumping(false);

        if (!_isJumping)
        {
            extraJump(); // or push off / ignore collision
        }
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
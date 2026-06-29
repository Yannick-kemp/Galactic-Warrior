using Assets.Scripts.Characteres;
using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public class CharacterController : Character, ICharacterController
{

    public float Speed;

    public Transform Front, Back;
    public Transform[] GroundPoints;
    public float Groundradius;
    public IEnumerator activesMoveCoroutine;
    public IEnumerator activesJumpCoroutine;
    public bool leftFacing;
    public bool rightFacing;
    public bool GoRight;
    public bool GoLeft;

    public PlatFormColliderTrigger CurrentplatForm;

    private struct PredictedLandingCandidate
    {
        public PlatFormColliderTrigger platform;
        public Collider2D platformCollider;
        public float topY;
        public float crossT;
    }

    public bool IsMoving => _isMoving;
    [Header("Enemy Health")]
    [SerializeField] public float maxHealth = 100f;
    public float currentHealth;


    protected bool targetReached;
    public bool DescendentPhase;
    public bool _isJumping;
    public bool _isMoving;
    public bool IsJumping => _isJumping;

    private static readonly ProfilerMarker WarriorJumpTowardPositionMarker =
        new ProfilerMarker("Warrior.JumpTowardPositionAction");
    // Moving platform merge support.
    // When this controller requests a MovePosition and a lift also moves in the
    // same physics step, the lift must merge into this requested position instead
    // of writing Rigidbody2D.position from an old X value. Otherwise the character
    // plays Run but stays in the same place.
    private Vector2 _lastCoreMoveRequestPosition;
    private int _lastCoreMoveRequestFrame = -100000;
    private float _lastCoreMoveRequestTime = -100000f;

    public bool TryGetCoreMoveRequestForRecentStep(out Vector2 requestedPosition)
    {
        requestedPosition = _lastCoreMoveRequestPosition;

        if (_lastCoreMoveRequestFrame == Time.frameCount)
            return true;

        float maxAge = Mathf.Max(Time.fixedDeltaTime * 1.5f, 0.02f);
        return Time.time - _lastCoreMoveRequestTime <= maxAge;
    }

    public void ApplyMovingPlatformMergedCoreMove(Vector2 mergedPosition)
    {
        RequestCoreMovePosition(mergedPosition);
    }

    // ─── Safe Animator access ─────────────────────────────────────────────
    // The Warrior and every enemy share this base controller, but each has its own
    // AnimatorController and does NOT necessarily declare every bool parameter used
    // here (isAttacking2, isAttacking3, IsLosingCtrl, …). Calling animator.GetBool /
    // SetBool with a missing parameter makes Unity emit a warning EVERY call. On a
    // per-frame path (MoveTowardPostionAction) with several enemies patrolling, those
    // warnings are written synchronously to the log sink every frame — Editor.log in
    // the Editor, logcat on Android — which BLOCKS the main thread on I/O (seen as a
    // 0%-CPU "not responding" freeze on both platforms). These helpers skip parameters
    // the current animator does not declare, so no warning is ever generated.
    private HashSet<string> _animatorBoolParams;

    private bool AnimatorHasBool(string paramName)
    {
        if (animator == null) return false;

        if (_animatorBoolParams == null)
        {
            _animatorBoolParams = new HashSet<string>();
            AnimatorControllerParameter[] all = animator.parameters;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].type == AnimatorControllerParameterType.Bool)
                    _animatorBoolParams.Add(all[i].name);
            }
        }

        return _animatorBoolParams.Contains(paramName);
    }

    protected bool GetAnimBool(string paramName)
    {
        return AnimatorHasBool(paramName) && animator.GetBool(paramName);
    }

    protected void SetAnimBool(string paramName, bool value)
    {
        if (AnimatorHasBool(paramName))
            animator.SetBool(paramName, value);
    }

    // Default: do not alter velocity (the Warrior keeps its own physics: falls, jumps, bounces).
    // Enemies override this to true so their scripted MovePosition cannot transfer a momentum
    // impulse onto the Warrior on contact.
    protected virtual bool NeutralizeImplicitMoveVelocity => false;

    // Last chance for a subclass to clamp a scripted move target before it is committed.
    // Enemies override this to stop their MovePosition from penetrating the Warrior's body
    // (the deep overlap is what Unity's solver depenetrates into a violent launch). Default: no-op.
    protected virtual Vector2 AdjustRequestedCoreMovePosition(Vector2 desiredPosition) => desiredPosition;

    protected void RequestCoreMovePosition(Vector2 position)
    {
        position = AdjustRequestedCoreMovePosition(position);

        _lastCoreMoveRequestPosition = position;
        _lastCoreMoveRequestFrame = Time.frameCount;
        _lastCoreMoveRequestTime = Time.time;

        if (rigidbody2 != null)
        {
            rigidbody2.MovePosition(position);
            NeutralizeMoveVelocityIfNeeded();
        }
        else
        {
            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }
    }

    // Cancels the implicit velocity Unity derives from a MovePosition call (for bodies whose
    // movement is fully scripted) so it is not transferred as a momentum impulse onto the
    // Warrior on contact. No-op unless NeutralizeImplicitMoveVelocity is true. Call right after
    // any direct rigidbody2.MovePosition that bypasses RequestCoreMovePosition.
    protected void NeutralizeMoveVelocityIfNeeded()
    {
        if (!NeutralizeImplicitMoveVelocity || rigidbody2 == null)
            return;

        rigidbody2.linearVelocity = Vector2.zero;
        rigidbody2.angularVelocity = 0f;
    }

    // Add near your other virtual properties
    protected virtual bool AllowEdgeExitWhenTargetOutside => false; // default for enemies
    protected virtual float PlatformSafeMargin => 0.40f;

    [Header("Destination Platform Anti-Tunneling")]
    [SerializeField] private bool enableDestinationPlatformAntiTunnel = true;

    [Tooltip("Tiny upward safety offset after a predicted top landing. This is not a platform snap; it only prevents floating-point re-overlap at the predicted impact point.")]
    [SerializeField, Min(0f)] private float antiTunnelLandingSkin = 0.015f;

    [Tooltip("Extra sweep distance added to the predicted movement so very fast downward arcs cannot skip the platform top between physics frames.")]
    [SerializeField, Min(0f)] private float antiTunnelCastExtraDistance = 0.08f;

    [Tooltip("Shrinks the anti-tunnel box cast horizontally. This prevents catching a platform that is only touched by the side of the character.")]
    [SerializeField, Min(0f)] private float antiTunnelHorizontalSkin = 0.03f;

    [Tooltip("The character bottom must start at least this close above the platform top to allow a predicted top landing.")]
    [SerializeField, Min(0f)] private float antiTunnelTopStartTolerance = 0.04f;

    protected virtual bool ClampMoveToCurrentPlatform => true;

    protected virtual bool CanContinueMoveTowardPositionAction()
    {
        return true;
    }

    public virtual bool CanJump { get; set; }
    public virtual bool CanMove { get; set; }
    public virtual bool CanAttack { get; set; }

    protected virtual void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (rigidbody2 == null) rigidbody2 = GetComponent<Rigidbody2D>();
        if (collider2 == null) collider2 = GetComponent<BoxCollider2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }
    protected override void Start()
    {
        currentHealth = maxHealth;
        Speed = 6;
        base.Start();
    }

    public void WaitAnimationDisplay()
    {
        SetAnimBool("isWaiting", true);
        SetAnimBool("isJumping", false);
        SetAnimBool("isRunning", false);
        SetAnimBool("isDying", false);
        SetAnimBool("isAttacking", false);
        SetAnimBool("isAttacking2", false);
        SetAnimBool("isAttacking3", false);
        SetAnimBool("IsLosingCtrl", false);
        SetAnimBool("isWalking", false);
    }

    public void JumpAnimationDisplay()
    {
        SetAnimBool("isWaiting", false);
        SetAnimBool("isRunning", false);
        SetAnimBool("isJumping", true);
        SetAnimBool("isAttacking", false);
        SetAnimBool("IsLosingCtrl", false);
        SetAnimBool("isAttacking2", false);
        SetAnimBool("isAttacking3", false);
        SetAnimBool("isDying", false);
        SetAnimBool("isWalking", false);
    }

    public void RunAnimationDisplay()
    {
        SetAnimBool("isWaiting", false);
        SetAnimBool("isJumping", false);
        SetAnimBool("isRunning", true);
        SetAnimBool("isAttacking", false);
        SetAnimBool("isWalking", false);
        SetAnimBool("isDying", false);
        SetAnimBool("isAttacking2", false);
        SetAnimBool("isAttacking3", false);
        SetAnimBool("IsLosingCtrl", false);
    }

    public void WalkAnimationDisplay()
    {
        SetAnimBool("isWaiting", false);
        SetAnimBool("isRunning", false);
        SetAnimBool("isJumping", false);
        SetAnimBool("isAttacking", false);
        SetAnimBool("isDying", false);
        SetAnimBool("isWalking", true);
        SetAnimBool("isAttacking2", false);
        SetAnimBool("isAttacking3", false);
        SetAnimBool("IsLosingCtrl", false);
    }

    public void AttackAnimationDisplay()
    {
        SetAnimBool("isWaiting", false);
        SetAnimBool("isWalking", false);
        SetAnimBool("isRunning", false);
        SetAnimBool("isJumping", false);
        SetAnimBool("isAttacking", true);
        SetAnimBool("isDying", false);
        SetAnimBool("isAttacking2", false);
        SetAnimBool("isAttacking3", false);
        SetAnimBool("IsLosingCtrl", false);
    }

    public void AttackAnimation2Display()
    {
        SetAnimBool("isWaiting", false);
        SetAnimBool("isWalking", false);
        SetAnimBool("isRunning", false);
        SetAnimBool("isJumping", false);
        SetAnimBool("isAttacking", false);
        SetAnimBool("isDying", false);
        SetAnimBool("isAttacking2", true);
        SetAnimBool("isAttacking3", false);
        SetAnimBool("IsLosingCtrl", false);
    }

    public void AttackAnimation3Display()
    {
        SetAnimBool("isWaiting", false);
        SetAnimBool("isWalking", false);
        SetAnimBool("isRunning", false);
        SetAnimBool("isJumping", false);
        SetAnimBool("isAttacking", false);
        SetAnimBool("isAttacking2", false);
        SetAnimBool("isAttacking3", true);
        SetAnimBool("isDying", false);
        SetAnimBool("IsLosingCtrl", false);
    }

    public void DeathAnimationDisplay()
    {
        SetAnimBool("isWaiting", false);
        SetAnimBool("isRunning", false);
        SetAnimBool("isJumping", false);
        SetAnimBool("isAttacking", false);
        SetAnimBool("isDying", true);
        SetAnimBool("isAttacking2", false);
        SetAnimBool("isAttacking3", false);
        SetAnimBool("isWalking", false);
        SetAnimBool("IsLosingCtrl", false);
    }

    public void LosingBalanceAnimationDisplay()
    {
        SetAnimBool("isWaiting", false);
        SetAnimBool("isRunning", false);
        SetAnimBool("isJumping", false);
        SetAnimBool("isAttacking", false);
        SetAnimBool("isAttacking2", false);
        SetAnimBool("isAttacking3", false);
        SetAnimBool("isDying", false);
        SetAnimBool("isWalking", false);
        SetAnimBool("IsLosingCtrl", true);
    }



    // ? ADD THIS METHOD to CharacterController
    public float ClampToCurrentPlatform(float targetX)
    {
        if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
            return targetX;

        Bounds platformBounds = CurrentplatForm.platformCollider.bounds;

        float minSafeX = platformBounds.min.x + PlatformSafeMargin;
        float maxSafeX = platformBounds.max.x - PlatformSafeMargin;

        return Mathf.Clamp(targetX, minSafeX, maxSafeX);
    }
    protected bool TryGetOppositePlatformTargetFromEdge(
    Transform groundCheckPoint,
    LayerMask platformLayer,
    float rayLength,
    ref float targetX,
    float forwardProbeDistance = 0.35f,
    float oppositePadding = 0.65f,
    float edgeEpsilon = 0.015f)
    {
        if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
            return false;

        if (groundCheckPoint == null)
            return false;

        float direction = Mathf.Sign(targetX - transform.position.x);

        if (Mathf.Approximately(direction, 0f))
            direction = rightFacing ? 1f : -1f;

        Vector2 aheadOrigin =
            (Vector2)groundCheckPoint.position +
            Vector2.right * direction * forwardProbeDistance;

        RaycastHit2D hitAhead = Physics2D.Raycast(
            aheadOrigin,
            Vector2.down,
            rayLength,
            platformLayer
        );

        bool noPlatformAhead =
            hitAhead.collider == null ||
            hitAhead.collider != CurrentplatForm.platformCollider;

        float testX = transform.position.x + direction * forwardProbeDistance;
        bool nextStepWouldBeClamped =
            Mathf.Abs(ClampToCurrentPlatform(testX) - testX) > edgeEpsilon;

        if (!noPlatformAhead && !nextStepWouldBeClamped)
            return false;

        Bounds bounds = CurrentplatForm.platformCollider.bounds;

        targetX = direction > 0f
            ? bounds.min.x + oppositePadding
            : bounds.max.x - oppositePadding;

        targetX = ClampToCurrentPlatform(targetX);

        FlipCharacter(targetX);

        return true;
    }

    protected bool IsTargetOutsideCurrentPlatformSafeRange(float x)
    {
        if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
            return false;

        return Mathf.Abs(ClampToCurrentPlatform(x) - x) > 0.001f;
    }

    // ADD: Check if we can move to a position safely
    protected bool CanMoveToPosition(float targetX)
    {
        if (CurrentplatForm == null)
            return true; // If no platform reference, allow movement

        float clampedX = ClampToCurrentPlatform(targetX);
        // If clamped position is different, we can't move there
        return Mathf.Abs(clampedX - targetX) < 0.01f;
    }
    //public IEnumerator MoveTowardPostionAction(float x)
    //{


    //    if (_isMoving) yield break;
    //    _isMoving = true;

    //    bool wantsBeyondEdge = IsTargetOutsideCurrentPlatformSafeRange(x);

    //    // Clamp only if needed. For warrior (override), if target is outside, allow run-off.
    //    bool shouldClamp = ClampMoveToCurrentPlatform &&
    //                       !(AllowEdgeExitWhenTargetOutside && wantsBeyondEdge);

    //    float targetX = shouldClamp ? ClampToCurrentPlatform(x) : x;

    //    FlipCharacter(targetX);

    //    while (Mathf.Abs(targetX - transform.position.x) > 0.1f)
    //    {
    //        if (animator == null ||
    //  (!GetAnimBool("isAttacking") &&
    //   !GetAnimBool("isAttacking2") &&
    //   !GetAnimBool("isAttacking3") &&
    //   !GetAnimBool("isDying") &&
    //   !GetAnimBool("IsLosingCtrl")))
    //        {
    //            FlipCharacter(targetX);
    //        }

    //        Vector2 currentPosition = transform.position;
    //        Vector2 targetPosition = new Vector2(targetX, currentPosition.y);
    //        Vector2 newPosition = Vector2.MoveTowards(currentPosition, targetPosition, Speed * Time.deltaTime);

    //        if (shouldClamp && CurrentplatForm != null)
    //            newPosition.x = ClampToCurrentPlatform(newPosition.x);

    //        transform.position = new Vector3(newPosition.x, newPosition.y, transform.position.z);
    //        yield return null;
    //    }

    //    if (shouldClamp)
    //    {
    //        float finalX = ClampToCurrentPlatform(targetX);
    //        transform.position = new Vector3(finalX, transform.position.y, transform.position.z);
    //    }
    //    else
    //    {
    //        transform.position = new Vector3(targetX, transform.position.y, transform.position.z);
    //    }

    //    _isMoving = false;
    //    activesMoveCoroutine = null;
    //}

    public IEnumerator MoveTowardPostionAction(float x)
    {
        if (_isMoving) yield break;
        _isMoving = true;

        if (!CanContinueMoveTowardPositionAction())
        {
            _isMoving = false;
            activesMoveCoroutine = null;
            yield break;
        }

        bool wantsBeyondEdge = IsTargetOutsideCurrentPlatformSafeRange(x);
        bool shouldClamp = ClampMoveToCurrentPlatform &&
                           !(AllowEdgeExitWhenTargetOutside && wantsBeyondEdge);
        float targetX = shouldClamp ? ClampToCurrentPlatform(x) : x;
        FlipCharacter(targetX);

        while (Mathf.Abs(targetX - transform.position.x) > 0.1f)
        {
            if (!CanContinueMoveTowardPositionAction())
            {
                _isMoving = false;
                activesMoveCoroutine = null;
                yield break;
            }

            if (animator == null ||
                (!GetAnimBool("isAttacking") &&
                 !GetAnimBool("isAttacking2") &&
                 !GetAnimBool("isAttacking3") &&
                 !GetAnimBool("isDying") &&
                 !GetAnimBool("IsLosingCtrl")))
            {
                FlipCharacter(targetX);
            }

            // --- X: clamp to platform safe margin ---
            float currentX = rigidbody2 != null ? rigidbody2.position.x : transform.position.x;
            float newX = Mathf.MoveTowards(currentX, targetX, Speed * Time.deltaTime);
            if (shouldClamp && CurrentplatForm != null)
                newX = ClampToCurrentPlatform(newX);

            // --- Y: follow platform surface (anti-tunnel + anti-takeoff) ---
            float surfaceY = GetMovingPlateSurfaceY();
            float newY = (surfaceY != float.MinValue)
                ? surfaceY                                       // locked to platform top
                : (rigidbody2 != null ? rigidbody2.position.y   // free-fall: keep current
                                      : transform.position.y);

            // Use the core move wrapper so moving platforms can merge their carry
            // without overwriting this character's requested X movement.
            RequestCoreMovePosition(new Vector2(newX, newY));
            Physics2D.SyncTransforms();

            yield return null;
        }

        if (!CanContinueMoveTowardPositionAction())
        {
            _isMoving = false;
            activesMoveCoroutine = null;
            yield break;
        }

        // Final snap
        float finalX = shouldClamp ? ClampToCurrentPlatform(targetX) : targetX;
        float finalSurfaceY = GetMovingPlateSurfaceY();
        float finalY = (finalSurfaceY != float.MinValue)
            ? finalSurfaceY
            : (rigidbody2 != null ? rigidbody2.position.y : transform.position.y);

        RequestCoreMovePosition(new Vector2(finalX, finalY));
        Physics2D.SyncTransforms();
        _isMoving = false;
        activesMoveCoroutine = null;
    }
    public IEnumerator JumpTowardPositionAction(Vector2 target, float height, float duration, Collider2D enemyCollider = null)
    {

        //bool isWarrior = this is Warrior;

        //if (isWarrior)
        //{
        //    WarriorJumpTowardPositionMarker.Begin();

        //    Debug.Log(
        //        $"[Warrior JumpTowardPositionAction START] " +
        //        $"frame={Time.frameCount}, " +
        //        $"time={Time.time:F3}, " +
        //        $"target={target}, " +
        //        $"height={height}, " +
        //        $"duration={duration}, " +
        //        $"enemyCollider={(enemyCollider != null ? enemyCollider.name : "NULL")}, " +
        //        $"currentPlatform={(CurrentplatForm != null ? CurrentplatForm.name : "NULL")}"
        //    );
        //}

        if (_isJumping) yield break;
        _isJumping = true;




        Vector2 startPosition = transform.position;
        float elapsedTime = 0f;
        FlipCharacter(target.x);
        targetReached = false;
        DescendentPhase = false;

        float previousY = startPosition.y;
        Vector2 previousPosition = startPosition;
        Vector2 currentVelocity = Vector2.zero;

        WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();

        while (!targetReached)
        {
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
                if (rigidbody2 != null)
                    rigidbody2.gravityScale = Mathf.Max(rigidbody2.gravityScale, 2.5f);

                desiredPosition = (Vector2)transform.position + currentVelocity * stepTime;
            }

            bool movingDown = desiredPosition.y < previousPosition.y;

            // Destination-platform anti-tunneling:
            // We do NOT use CurrentplatForm here. The platform that matters is the
            // destination platform hit by the sweep between previousPosition and desiredPosition.
            if (movingDown &&
       TryResolveDestinationPlatformTopLanding(previousPosition, desiredPosition, out Vector2 predictedLandingPosition, out PlatFormColliderTrigger destinationPlatform))
            {
                MoveCharacterTo(predictedLandingPosition);
                CompletePredictedTopLanding(destinationPlatform);

                //if (isWarrior)
                //{
                //    Debug.Log(
                //        $"[Warrior JumpTowardPositionAction PREDICTED LANDING] " +
                //        $"frame={Time.frameCount}, " +
                //        $"time={Time.time:F3}, " +
                //        $"landing={predictedLandingPosition}, " +
                //        $"destinationPlatform={(destinationPlatform != null ? destinationPlatform.name : "NULL")}"
                //    );

                //    WarriorJumpTowardPositionMarker.End();
                //}

                yield break;
            }
            MoveCharacterTo(desiredPosition);

            if (stepTime > 0f)
                currentVelocity = (desiredPosition - previousPosition) / stepTime;

            DescendentPhase = desiredPosition.y < previousY;
            previousY = desiredPosition.y;
            previousPosition = desiredPosition;

            yield return waitForFixedUpdate;
        }

        _isJumping = false;
        activesJumpCoroutine = null;

        //if (isWarrior)
        //{
        //    Debug.Log(
        //        $"[Warrior JumpTowardPositionAction END] " +
        //        $"frame={Time.frameCount}, " +
        //        $"time={Time.time:F3}, " +
        //        $"position={transform.position}, " +
        //        $"currentPlatform={(CurrentplatForm != null ? CurrentplatForm.name : "NULL")}"
        //    );

        //    WarriorJumpTowardPositionMarker.End();
        //}
    }

    protected bool TryResolveDestinationPlatformTopLanding(
        Vector2 fromPosition,
        Vector2 toPosition,
        out Vector2 landingPosition,
        out PlatFormColliderTrigger destinationPlatform,
        bool ignoreCurrentPlatform = false)
    {
        landingPosition = toPosition;
        destinationPlatform = null;

        if (!enableDestinationPlatformAntiTunnel || PlatformLayer.value == 0)
            return false;

        if (collider2 == null)
            return false;

        Vector2 movement = toPosition - fromPosition;

        // Only solve top landing while descending. Upward movement, side-only movement,
        // and platform-under-trigger pass-through must keep their natural behavior.
        if (movement.y >= -0.0001f)
            return false;

        Bounds currentBounds = collider2.bounds;

        // Important:
        // Bounds are the CURRENT physics bounds, but this method can be used for:
        //   1) current -> next predicted movement, and
        //   2) previous -> current movement when physics already moved the Warrior.
        // Therefore we reconstruct the collider center at fromPosition/toPosition using
        // the current collider center offset relative to the Rigidbody/transform position.
        Vector2 referencePosition = rigidbody2 != null
            ? rigidbody2.position
            : (Vector2)transform.position;

        Vector2 centerOffset = (Vector2)currentBounds.center - referencePosition;
        Vector2 startCenter = fromPosition + centerOffset;
        Vector2 endCenter = toPosition + centerOffset;

        Vector2 castSize = currentBounds.size;
        castSize.x = Mathf.Max(0.02f, castSize.x - antiTunnelHorizontalSkin * 2f);
        castSize.y = Mathf.Max(0.02f, castSize.y - antiTunnelLandingSkin * 2f);

        float halfWidth = castSize.x * 0.5f;
        float halfHeight = castSize.y * 0.5f;

        float startBottomY = startCenter.y - halfHeight;
        float endBottomY = endCenter.y - halfHeight;

        if (endBottomY >= startBottomY)
            return false;

        // Swept box: covers the whole vertical path between previous/current/next.
        // This is more robust than a diagonal BoxCast normal, because when Warrior falls
        // from a platform edge with horizontal velocity, a diagonal cast can report a side
        // normal and miss the valid top crossing.
        float verticalTravel = startBottomY - endBottomY;
        Vector2 sweepCenter = new Vector2(
            (startCenter.x + endCenter.x) * 0.5f,
            (startCenter.y + endCenter.y) * 0.5f);

        Vector2 sweepSize = new Vector2(
            castSize.x + antiTunnelHorizontalSkin * 2f,
            castSize.y + verticalTravel + antiTunnelCastExtraDistance);

        Collider2D[] candidates = Physics2D.OverlapBoxAll(
            sweepCenter,
            sweepSize,
            0f,
            PlatformLayer);

        if (candidates == null || candidates.Length == 0)
            return false;

        List<PredictedLandingCandidate> landingCandidates =
            new List<PredictedLandingCandidate>();

        float sweptMinX = Mathf.Min(startCenter.x, endCenter.x) - halfWidth;
        float sweptMaxX = Mathf.Max(startCenter.x, endCenter.x) + halfWidth;

        for (int i = 0; i < candidates.Length; i++)
        {
            Collider2D candidate = candidates[i];

            if (candidate == null || candidate.isTrigger)
                continue;

            PlatFormColliderTrigger platform = candidate.GetComponentInParent<PlatFormColliderTrigger>();
            if (platform == null || platform.platformCollider == null)
                continue;

            // Important: use the destination platform found by the sweep, not CurrentplatForm.
            if (candidate != platform.platformCollider)
                continue;

            // For physical edge-fall checks, CurrentplatForm is usually the source platform.
            // Do not recapture that source platform while Warrior is intentionally leaving its edge.
            if (ignoreCurrentPlatform && platform == CurrentplatForm)
                continue;

            Bounds pb = platform.platformCollider.bounds;
            float topY = pb.max.y;

            bool crossesThisTop =
                startBottomY >= topY - antiTunnelTopStartTolerance &&
                endBottomY <= topY + antiTunnelLandingSkin;

            if (!crossesThisTop)
                continue;

            bool horizontallyCrossesDestination =
                sweptMaxX > pb.min.x + antiTunnelHorizontalSkin &&
                sweptMinX < pb.max.x - antiTunnelHorizontalSkin;

            if (!horizontallyCrossesDestination)
                continue;

            // Candidate found. We do not immediately trust the highest surface,
            // because it may still be the source platform locked in fall-through.
            // TryPrepareForPredictedTopLanding can reject that source platform; then
            // we must try the next valid platform destination instead of returning false.
            float crossT = Mathf.InverseLerp(startBottomY, endBottomY, topY);

            landingCandidates.Add(new PredictedLandingCandidate
            {
                platform = platform,
                platformCollider = candidate,
                topY = topY,
                crossT = crossT
            });
        }

        if (landingCandidates.Count == 0)
            return false;

        landingCandidates.Sort((a, b) =>
        {
            int topCompare = b.topY.CompareTo(a.topY);
            if (topCompare != 0)
                return topCompare;

            return a.crossT.CompareTo(b.crossT);
        });

        for (int i = 0; i < landingCandidates.Count; i++)
        {
            PredictedLandingCandidate candidate = landingCandidates[i];

            if (candidate.platform == null || candidate.platformCollider == null)
                continue;

            if (!candidate.platform.TryPrepareForPredictedTopLanding(this, candidate.platformCollider))
                continue;

            Vector2 naturalPositionAtCrossing =
                Vector2.Lerp(fromPosition, toPosition, Mathf.Clamp01(candidate.crossT));

            // Keep X natural. Only prevent the body bottom from passing below the crossed top.
            naturalPositionAtCrossing.y =
                candidate.topY + antiTunnelLandingSkin + halfHeight - centerOffset.y;

            landingPosition = naturalPositionAtCrossing;
            destinationPlatform = candidate.platform;
            return true;
        }

        return false;
    }

    protected void MoveCharacterTo(Vector2 position)
    {
        RequestCoreMovePosition(position);
    }

    protected void CompletePredictedTopLanding(PlatFormColliderTrigger destinationPlatform)
    {
        CurrentplatForm = destinationPlatform;
        targetReached = true;
        DescendentPhase = false;
        _isJumping = false;
        activesJumpCoroutine = null;

        if (rigidbody2 != null)
        {
            Vector2 velocity = rigidbody2.linearVelocity;
            if (velocity.y < 0f)
                velocity.y = 0f;
            rigidbody2.linearVelocity = velocity;
        }
    }

    public void StopJumpTowardCoroutine()
    {
        targetReached = true;
        _isJumping = false;
        if (activesJumpCoroutine != null)
        {
            StopCoroutine(activesJumpCoroutine);
        }
        activesJumpCoroutine = null;
        //  WaitAnimationDisplay();
    }
    public virtual void StopMoveTowardCoroutine()
    {
        _isMoving = false;
        if (activesMoveCoroutine != null)
        {
            StopCoroutine(activesMoveCoroutine);
            activesMoveCoroutine = null;
        }
    }
    public void FlipCharacter(float x)
    {
        if (collider2 == null) return;

        //refresh facing flags here so we never depend on stale values
        if (Front != null && Back != null)
        {
            leftFacing = Front.position.x < Back.position.x;
            rightFacing = Front.position.x > Back.position.x;
        }
        else
        {
            rightFacing = transform.localScale.x >= 0f;
            leftFacing = !rightFacing;
        }

        float myX = collider2.bounds.center.x;

        // small deadzone prevents flip spam when x ~ center
        const float eps = 0.02f;
        if (Mathf.Abs(x - myX) < eps) return;

        bool wantRight = x > myX;

        if ((wantRight && leftFacing) || (!wantRight && rightFacing))
        {
            Vector3 theScale = transform.localScale;
            theScale.x *= -1;
            transform.localScale = theScale;

            if (Front != null)
            {
                Vector3 frontScale = Front.localScale;
                frontScale.x *= -1;
                Front.localScale = frontScale;
            }

            if (Back != null)
            {
                Vector3 backScale = Back.localScale;
                backScale.x *= -1;
                Back.localScale = backScale;
            }
        }
    }

    public void FlipCharacter()
    {
        Vector3 theScale = transform.localScale;
        theScale.x *= -1;
        transform.localScale = theScale;
    }

    public void SetDirectionVariables(float x)
    {

        leftFacing = Front.transform.position.x < Back.transform.position.x;
        rightFacing = Front.transform.position.x > Back.transform.position.x;
        GoRight = x > collider2.bounds.center.x;
        GoLeft = x < collider2.bounds.center.x;

        if (GoRight)
            GoLeft = false;
        if (GoLeft)
            GoRight = false;
    }
    public virtual void TakeDamage(float damage)
    {
        currentHealth -= damage;
        currentHealth = Mathf.Max(0, currentHealth);

        if (currentHealth <= 0)
        {
            // Die(); Todo be handled by derived classes
        }
    }
    /// <summary>
    /// Returns the Y position the monster should stand at on the lift,
    /// or float.MinValue if not on a lift.
    /// </summary>
    public float GetMovingPlateSurfaceY()
    {
        if (CurrentplatForm is not MovingVerticalPlatform movingPlatform)
            return float.MinValue;

        Rigidbody2D platformRb = movingPlatform.GetComponent<Rigidbody2D>();
        if (platformRb == null)
            return float.MinValue;

        Collider2D platformCol = movingPlatform.platformCollider;
        if (platformCol == null)
            return float.MinValue;

        // NormalCollider exists only on Enemy subclasses (M97, Zalayty, etc.)
        // Warrior (and any other non-Enemy CharacterController) uses collider2 directly.
        Enemy enemy = this as Enemy;
        Collider2D enemyNormalCol = enemy?.NormalCollider;

        Collider2D myCol = (enemyNormalCol != null && enemyNormalCol.enabled)
            ? enemyNormalCol
            : collider2;

        if (myCol == null)
            return float.MinValue;

        float platformTopY = platformCol.bounds.max.y;

        // Offset: distance from transform.position to the bottom of the standing collider.
        // Works even if the collider has an offset or the pivot is not centered.
        float bottomOffsetFromTransform = transform.position.y - myCol.bounds.min.y;

        return platformTopY + bottomOffsetFromTransform;
    }
}
using Assets.Scripts.Characteres;
using System.Collections;
using UnityEngine;

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
    public bool IsMoving => _isMoving;
    [Header("Enemy Health")]
    [SerializeField] public float maxHealth = 100f;
    public float currentHealth;


    protected bool targetReached;
    public bool DescendentPhase;
    public bool _isJumping;
    public bool _isMoving;
    public bool IsJumping => _isJumping;

    // Add near your other virtual properties
    protected virtual bool AllowEdgeExitWhenTargetOutside => false; // default for enemies
    protected virtual float PlatformSafeMargin => 0.40f;

    protected virtual bool ClampMoveToCurrentPlatform => true;
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
        animator.SetBool("isWaiting", true);
        animator.SetBool("isJumping", false);
        animator.SetBool("isRunning", false);
        animator.SetBool("isDying", false);
        animator.SetBool("isAttacking", false);
        animator.SetBool("isAttacking2", false);
        animator.SetBool("isAttacking3", false);
        animator.SetBool("IsLosingCtrl", false);
        animator.SetBool("isWalking", false);
    }

    public void JumpAnimationDisplay()
    {
        animator.SetBool("isWaiting", false);
        animator.SetBool("isRunning", false);
        animator.SetBool("isJumping", true);
        animator.SetBool("isAttacking", false);
        animator.SetBool("IsLosingCtrl", false);
        animator.SetBool("isAttacking2", false);
        animator.SetBool("isAttacking3", false);
        animator.SetBool("isDying", false);
        animator.SetBool("isWalking", false);
    }

    public void RunAnimationDisplay()
    {
        animator.SetBool("isWaiting", false);
        animator.SetBool("isJumping", false);
        animator.SetBool("isRunning", true);
        animator.SetBool("isAttacking", false);
        animator.SetBool("isWalking", false);
        animator.SetBool("isDying", false);
        animator.SetBool("isAttacking2", false);
        animator.SetBool("isAttacking3", false);
        animator.SetBool("IsLosingCtrl", false);
    }

    public void WalkAnimationDisplay()
    {
        animator.SetBool("isWaiting", false);
        animator.SetBool("isRunning", false);
        animator.SetBool("isJumping", false);
        animator.SetBool("isAttacking", false);
        animator.SetBool("isDying", false);
        animator.SetBool("isWalking", true);
        animator.SetBool("isAttacking2", false);
        animator.SetBool("isAttacking3", false);
        animator.SetBool("IsLosingCtrl", false);
    }

    public void AttackAnimationDisplay()
    {
        animator.SetBool("isWaiting", false);
        animator.SetBool("isWalking", false);
        animator.SetBool("isRunning", false);
        animator.SetBool("isJumping", false);
        animator.SetBool("isAttacking", true);
        animator.SetBool("isDying", false);
        animator.SetBool("isAttacking2", false);
        animator.SetBool("isAttacking3", false);
        animator.SetBool("IsLosingCtrl", false);
    }

    public void AttackAnimation2Display()
    {
        animator.SetBool("isWaiting", false);
        animator.SetBool("isWalking", false);
        animator.SetBool("isRunning", false);
        animator.SetBool("isJumping", false);
        animator.SetBool("isAttacking", false);
        animator.SetBool("isDying", false);
        animator.SetBool("isAttacking2", true);
        animator.SetBool("isAttacking3", false);
        animator.SetBool("IsLosingCtrl", false);
    }

    public void AttackAnimation3Display()
    {
        animator.SetBool("isWaiting", false);
        animator.SetBool("isWalking", false);
        animator.SetBool("isRunning", false);
        animator.SetBool("isJumping", false);
        animator.SetBool("isAttacking", false);
        animator.SetBool("isAttacking2", false);
        animator.SetBool("isAttacking3", true);
        animator.SetBool("isDying", false);
        animator.SetBool("IsLosingCtrl", false);
    }

    public void DeathAnimationDisplay()
    {
        animator.SetBool("isWaiting", false);
        animator.SetBool("isRunning", false);
        animator.SetBool("isJumping", false);
        animator.SetBool("isAttacking", false);
        animator.SetBool("isDying", true);
        animator.SetBool("isAttacking2", false);
        animator.SetBool("isAttacking3", false);
        animator.SetBool("isWalking", false);
        animator.SetBool("IsLosingCtrl", false);
    }

    public void LosingBalanceAnimationDisplay()
    {
        animator.SetBool("isWaiting", false);
        animator.SetBool("isRunning", false);
        animator.SetBool("isJumping", false);
        animator.SetBool("isAttacking", false);
        animator.SetBool("isAttacking2", false);
        animator.SetBool("isAttacking3", false);
        animator.SetBool("isDying", false);
        animator.SetBool("isWalking", false);
        animator.SetBool("IsLosingCtrl", true);
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
    public IEnumerator MoveTowardPostionAction(float x)
    {
        if (_isMoving) yield break;
        _isMoving = true;

        bool wantsBeyondEdge = IsTargetOutsideCurrentPlatformSafeRange(x);

        // Clamp only if needed. For warrior (override), if target is outside, allow run-off.
        bool shouldClamp = ClampMoveToCurrentPlatform &&
                           !(AllowEdgeExitWhenTargetOutside && wantsBeyondEdge);

        float targetX = shouldClamp ? ClampToCurrentPlatform(x) : x;

        FlipCharacter(targetX);

        while (Mathf.Abs(targetX - transform.position.x) > 0.1f)
        {
            if (animator == null ||
      (!animator.GetBool("isAttacking") &&
       !animator.GetBool("isAttacking2") &&
       !animator.GetBool("isAttacking3") &&
       !animator.GetBool("isDying") &&
       !animator.GetBool("IsLosingCtrl")))
            {
                FlipCharacter(targetX);
            }

            Vector2 currentPosition = transform.position;
            Vector2 targetPosition = new Vector2(targetX, currentPosition.y);
            Vector2 newPosition = Vector2.MoveTowards(currentPosition, targetPosition, Speed * Time.deltaTime);

            if (shouldClamp && CurrentplatForm != null)
                newPosition.x = ClampToCurrentPlatform(newPosition.x);

            transform.position = new Vector3(newPosition.x, newPosition.y, transform.position.z);
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


    public IEnumerator JumpTowardPositionAction(Vector2 target, float height, float duration, Collider2D enemyCollider = null)
    {
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

        while (!targetReached)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / duration; // Normalized time (0 to 1)

            if (t <= 1f) // Before reaching the target
            {

                Vector2 currentPosition = Vector2.Lerp(startPosition, target, t);
                currentPosition.y += height * Mathf.Sin(Mathf.PI * t); // Add parabolic height
                transform.position = currentPosition;

                // Calculate current velocity (for tracking angle of motion)
                if (Time.deltaTime > 0)
                {
                    currentVelocity = (currentPosition - previousPosition) / Time.deltaTime;
                    previousPosition = currentPosition;
                }

                // Determine descending phase
                if (currentPosition.y < previousY)
                {
                    DescendentPhase = true;
                }
                else
                {
                    DescendentPhase = false;
                }
                previousY = currentPosition.y;
            }
            else // After reaching the target
            {
                rigidbody2.gravityScale = 2.5f;

                // Continue moving with the same velocity vector from the end of the jump
                transform.position += (Vector3)currentVelocity * Time.deltaTime;
            }
            yield return null;
        }
        _isJumping = false;
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


}
using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Services;
using UnityEngine;

/// <summary>
/// P39Monster - with health bar + moving-platform-safe edge patrol (anti flicker/jitter)
/// Compliant with the spawn override system:
/// - set this enemy's defaults in Start()
/// - then call ApplySpawnOverridesNow()
/// - do NOT hard-reset override-managed values inside ConfigureAttack()
/// </summary>
public class P39Monster_WithHealthBar : Enemy
{
    [Header("Edge patrol (moving platform safe)")]
    [SerializeField] private float edgeSafeMargin = 0.45f; // keep consistent with platform clamp
    [SerializeField] private float edgeTolerance = 0.05f;  // 0.03–0.08 usually

    // Direction instead of caching xEdge as an absolute X
    private bool _goRight;
    private bool _lastGoRight;

    protected override void Start()
    {
        totalFramesInAnimation = 19;
        autoCreateHealthBar = true;

        base.Start();

        // P39 defaults
        Range = 4f;
        attackCooldown = 1f;
        attackDamage = 15;
        stepBackDistance = 2.2f;
        CanMove = true;

        // Optional P39 default speed:
        // Speed = 3.5f;

        // IMPORTANT: apply per-spawn overrides AFTER defaults
        ApplySpawnOverridesNow();

        if (NormalCollider != null && TriggerColliderLeft != null)
            Physics2D.IgnoreCollision(NormalCollider, TriggerColliderLeft, true);

        if (NormalCollider != null && TriggerColliderRight != null)
            Physics2D.IgnoreCollision(NormalCollider, TriggerColliderRight, true);

        // Initial direction (keep your randomness behavior)
        _goRight = OddValue;
        _lastGoRight = _goRight;
    }

    protected override void Update()
    {
        if (StopMovingWhenWarriorDie)
            return;

        // Base updates (target acquisition, frame index, edge clamp, etc.)
        base.Update();

        // Attack logic
        if (EnemyRangeService != null && target != null && !IsAttacked)
        {
            EnemyRangeService.TryAction(target, Range, OnAttackPerformed);
        }

        // No platform => nothing to patrol
        if (CurrentplatForm == null || collider2 == null || CurrentplatForm.platformCollider == null)
            return;

        // Decide direction based on CURRENT platform bounds + CURRENT collider bounds
        UpdateEdgeTargetSide();

        // Recompute edge X every frame (moving platform safe)
        xEdge = GetCurrentEdgeX();

        // Keep facing correct
        SetDirectionVariables(xEdge);

        // Start movement if none, or restart only when direction flips
        if (activesMoveCoroutine == null || _goRight != _lastGoRight)
        {
            StopMoveTowardCoroutine();
            activesMoveCoroutine = null;

            RunAnimationDisplay();
            activesMoveCoroutine = MoveTowardPostionAction(xEdge);
            StartCoroutine(activesMoveCoroutine);
        }

        _lastGoRight = _goRight;
    }

    public override void OnAttackPerformed(IAttacker attacker, Transform attackedTarget)
    {
        var warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null || warrior.collider2 == null)
            return;

        // Same platform + physical touch rule
        if (warrior.CurrentplatForm?.GetHashCode() != CurrentplatForm?.GetHashCode())
            return;

        if (NormalCollider == null || !warrior.collider2.IsTouching(NormalCollider))
            return;

        // Must be in front at the moment of the hit
        if (!IsWarriorInFront(warrior.transform))
            return;

        // Play animation (base also checks front)
        base.OnAttackPerformed(attacker, attackedTarget);

        if (warrior.TryBlockEnemyHit(this))
            return;

        const int HIT_FRAME = 10;
        if (frameIndex > HIT_FRAME)
            return;

        warrior.TakeDamage(attackDamage);
        warrior.SpawnBloodshedEffectFromEnemy(this);
    }

    protected override void OnDeath()
    {
        base.OnDeath();
        Debug.Log($"P39Monster '{gameObject.name}' has been defeated!");
    }

    protected override void UpdateHealthBarDisplay()
    {
        base.UpdateHealthBarDisplay();

        if (currentHealth <= maxHealth * 0.1f && worldHealthBar != null)
        {
            Debug.Log($"P39 '{gameObject.name}' health is critical!");
        }
    }

    // --------------------------
    // Moving-platform-safe edges
    // --------------------------

    private float GetCurrentEdgeX()
    {
        Bounds pb = CurrentplatForm.platformCollider.bounds;

        // Prevent safe margin from exceeding half width (tiny platforms)
        float safe = Mathf.Min(edgeSafeMargin, pb.extents.x - 0.01f);

        return _goRight ? (pb.max.x - safe) : (pb.min.x + safe);
    }

    private void UpdateEdgeTargetSide()
    {
        Bounds pb = CurrentplatForm.platformCollider.bounds;
        Bounds eb = collider2.bounds;

        float safe = Mathf.Min(edgeSafeMargin, pb.extents.x - 0.01f);

        float leftLimit = pb.min.x + safe;
        float rightLimit = pb.max.x - safe;

        // Going right: if our RIGHT side reaches right edge => flip left
        if (_goRight && eb.max.x >= rightLimit - edgeTolerance)
        {
            _goRight = false;
        }
        // Going left: if our LEFT side reaches left edge => flip right
        else if (!_goRight && eb.min.x <= leftLimit + edgeTolerance)
        {
            _goRight = true;
        }
    }

    public override float StepRightMaxamize() => base.StepRightMaxamize() + 1.5f;
    public override float StepLeftMaxamize() => base.StepLeftMaxamize() - 1.5f;

    protected override void OnTriggerEnter2D(Collider2D collision) => base.OnTriggerEnter2D(collision);
    protected override void OnTriggerExit2D(Collider2D collision) => base.OnTriggerExit2D(collision);
    protected override void OnTriggerStay2D(Collider2D collision) => base.OnTriggerStay2D(collision);

    protected override void ConfigureAttack()
    {
        // IMPORTANT:
        // Do NOT assign Range / attackCooldown / attackDamage here.
        // Those defaults belong in Start(), so spawn overrides can win cleanly.
        base.ConfigureAttack();
    }

    public override float ComputeStepBackDistance(float incoming)
    {
        if (hitReaction == null) return incoming;

        float v = incoming * hitReaction.stepBackMultiplier;

        // Different clamp for P39 (extra heavy feel)
        return Mathf.Clamp(v, hitReaction.min, hitReaction.max * 0.6f);
    }
}
using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Services;
using System.Collections;
using UnityEngine;

public class CrawlingMonster : Enemy
{
    [Header("Edge patrol (moving platform safe)")]
    [SerializeField] private float edgeSafeMargin = 0.45f;
    [SerializeField] private float edgeTolerance = 0.05f;

    private bool _goRight;
    private bool _lastGoRight;

    [Header("Recoil")]
    [Tooltip("How long patrol is paused after a Warrior knockback so the recoil is not immediately " +
             "cancelled by the patrol coroutine restarting and driving its own MovePosition.")]
    [SerializeField] private float recoilPatrolSuppressSeconds = 0.15f;
    private float _recoilPatrolSuppressUntil = -999f;

    [Tooltip("Edge inset used when clamping movement / recoil to the platform. Smaller lets the " +
             "recoil push much closer to the platform edge so it is visible on (almost) every hit; " +
             "the ground-bound Y-lock prevents falling even at the edge. Patrol still turns around " +
             "at edgeSafeMargin, so this does not change patrol behaviour.")]
    [SerializeField] private float platformSafeMargin = 0.12f;
    protected override float PlatformSafeMargin => platformSafeMargin;

    protected override void Start()
    {
        base.Start();

        // Terrestrial crawler: never allowed to leave its platform surface vertically.
        groundBound = true;

        // Crawling defaults
        Range = 3.5f;
        attackCooldown = 0.75f;
        attackDamage = 5;
        Speed = 2f;
        stepBackDistance = 0.75f;
        CanMove = true;

        // IMPORTANT: apply per-spawn overrides AFTER defaults
        ApplySpawnOverridesNow();

        if (NormalCollider != null && TriggerColliderLeft != null)
            Physics2D.IgnoreCollision(NormalCollider, TriggerColliderLeft, true);

        // if needed later:
        // if (NormalCollider != null && TriggerColliderRight != null)
        //     Physics2D.IgnoreCollision(NormalCollider, TriggerColliderRight, true);

        _goRight = OddValue;
        _lastGoRight = _goRight;
    }

    protected override void Update()
    {
        if (StopMovingWhenWarriorDie)
        {
            if (GameMgr.Instance?.WarriorInstance?.CanDie == false)
                StopMovingWhenWarriorDie = false;
            else
                return;
        }

        base.Update();

        // While recoiling from a Warrior hit, let SmoothStepBack own the horizontal movement; do
        // not restart patrol -- its competing MovePosition would cancel the recoil on some hits.
        if (Time.time < _recoilPatrolSuppressUntil)
            return;

        if (target != null && !IsAttacked)
        {
            EnemyRangeService.TryAction(target, Range, OnAttackPerformed);
        }

        if (CurrentplatForm == null || collider2 == null || CurrentplatForm.platformCollider == null)
            return;

        UpdateEdgeTargetSide();
        xEdge = GetCurrentEdgeX();

        if (activesMoveCoroutine == null || _goRight != _lastGoRight || !CanMove)
        {
            StopMoveTowardCoroutine();
            activesMoveCoroutine = null;

            SetDirectionVariables(xEdge);
            WalkAnimationDisplay();

            activesMoveCoroutine = MoveTowardPostionAction(xEdge);
            StartCoroutine(activesMoveCoroutine);
        }
        else
        {
            SetDirectionVariables(xEdge);
        }

        _lastGoRight = _goRight;
    }

    public override void OnAttackPerformed(IAttacker attacker, Transform attackedTarget)
    {
        if (IsAttackTemporarilyDisabled)
            return;

        var warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null || warrior.collider2 == null)
            return;

        if (warrior.CurrentplatForm?.GetHashCode() == CurrentplatForm?.GetHashCode()
            && NormalCollider != null
            && warrior.collider2.IsTouching(NormalCollider))
        {
            base.OnAttackPerformed(attacker, attackedTarget);

            if (warrior.TryBlockEnemyHit(this))
                return;

            warrior.TakeDamage(attackDamage);
            warrior.SpawnBloodshedEffectFromEnemy(this);
        }
    }

    private float GetCurrentEdgeX()
    {
        Bounds pb = CurrentplatForm.platformCollider.bounds;
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

        if (_goRight && eb.max.x >= rightLimit - edgeTolerance)
        {
            _goRight = false;
        }
        else if (!_goRight && eb.min.x <= leftLimit + edgeTolerance)
        {
            _goRight = true;
        }
    }

    protected override void OnDeath()
    {
        base.OnDeath();
        Debug.Log($"CrawlingMonster '{gameObject.name}' has been defeated!");
    }

    protected override void ConfigureAttack()
    {
        // Do not set defaults here.
        // Defaults belong in Start(), so spawn overrides can win cleanly.
        base.ConfigureAttack();
    }

    public override float ComputeStepBackDistance(float incoming)
    {
        // The per-enemy KnockBack value from the Warrior attack switch is authoritative for
        // CrawlingMonster: return it as-is instead of clamping it to hitReaction.max * 0.6f, so
        // changing the switch value (e.g. 0.5f vs 20f) actually changes the recoil. The anti-fall
        // clamp (ClampToCurrentPlatform) in SmoothStepBack still keeps Crawling on its platform,
        // so large values simply push it to the platform edge rather than off it.
        return Mathf.Max(0f, incoming);
    }

    public override bool CanStepBack(bool positif)
    {
        // The base CanStepBack returns false as soon as the requested distance exceeds the
        // platform safe band, which makes SmoothStepBack bail out with NO recoil at all for a
        // large switch value (e.g. 20f). Crawling instead lets the recoil proceed: SmoothStepBack
        // clamps the target to the platform edge (anti-fall preserved), so a large value pushes it
        // to the edge and a small value moves it a little -> the switch value is actually visible.
        // Only refuse when there is no platform to stand on.
        return CurrentplatForm != null;
    }

    public override IEnumerator SmoothStepBack(bool positif)
    {
        // Pause patrol for the recoil duration so the step-back is not fought (and visually
        // cancelled on some hits) by the patrol coroutine restarting and driving its own
        // MovePosition. Each hit re-arms the window, so rapid combos keep the recoil clean.
        _recoilPatrolSuppressUntil = Time.time + recoilPatrolSuppressSeconds;
        yield return StartCoroutine(base.SmoothStepBack(positif));
    }
}
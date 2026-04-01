using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Services;
using UnityEngine;

public class CrawlingMonster : Enemy
{
    [Header("Edge patrol (moving platform safe)")]
    [SerializeField] private float edgeSafeMargin = 0.45f;
    [SerializeField] private float edgeTolerance = 0.05f;

    private bool _goRight;
    private bool _lastGoRight;

    protected override void Start()
    {
        base.Start();

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
        if (hitReaction == null) return incoming;

        float v = incoming * hitReaction.stepBackMultiplier;
        return Mathf.Clamp(v, hitReaction.min, hitReaction.max * 0.6f);
    }
}
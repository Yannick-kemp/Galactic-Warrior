using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Services;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class RakaMonster : Enemy
{
    [Header("Ranges")]
    [SerializeField] private float meleeRange = 4f;
    [SerializeField] private float rangedRange = 6f;

    [Header("Cooldowns")]
    [SerializeField] private float meleeCooldown = 0.6f;
    [SerializeField] private float rangedCooldown = 2f;

    [Header("Flamethrower VFX")]
    [SerializeField] private GameObject flamethrowerVfx;

    [Header("Projectile")]
    [SerializeField] private GameObject flamethrowerProjectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;
    [SerializeField] private int projectileCount = 2;
    [SerializeField] private float projectileDelay = 0.15f;

    [Header("First Hit Reaction")]
    [SerializeField] private bool enableFirstHitReaction = true;
    [SerializeField] private float firstHitCooldown = 0.12f;
    [SerializeField] private float firstHitStunSeconds = 0.35f;

    [SerializeField] private int hugeRecoilSteps = 2;
    [SerializeField] private float hugeRecoilStepDistance = 0.95f;

    [SerializeField] private float minRangedDistance = 1.8f;

    private float _lastFirstHitAt = -999f;
    private float _lastMeleeTime = -999f;
    private float _lastRangedTime = -999f;

    private bool isPerformingMelee;
    private bool isPerformingRanged;

    private bool _impactResolvedThisSwing;

    protected void Awake()
    {
#if UNITY_ANDROID
        Time.fixedDeltaTime = 0.02f;
        Application.targetFrameRate = 60;
#endif
    }

    protected override void Start()
    {
        totalFramesInAnimation = 18;

        base.Start();

        meleeHitDistance = 2.60f;
        Range = meleeRange;
        attackCooldown = meleeCooldown;
        attackDamage = 18;

        CanMove = true;
        Speed = 3.5f;
        stepBackDistance = 0.2f;

        ApplySpawnOverridesNow();

        if (flamethrowerVfx != null)
            flamethrowerVfx.SetActive(false);

        if (NormalCollider != null && TriggerColliderLeft != null)
            Physics2D.IgnoreCollision(NormalCollider, TriggerColliderLeft, true);
    }

    protected override void Update()
    {
        if (StopMovingWhenWarriorDie) return;

        initDirection();
        base.Update();

        var warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null) return;

        if (isPerformingMelee)
        {
            bool touching = IsWarriorCloseEnoughToHit(warrior);

            if (!touching)
            {
                CancelMelee();
                return;
            }
        }

        if (IsStunned)
        {
            StopMoveTowardCoroutine();
            WaitAnimationDisplay();
            return;
        }

        if (warrior.CurrentplatForm == null || CurrentplatForm == null)
            return;

        var b = IsWarriorOnSamePlatform();

        if (target != null && !isPerformingMelee && !isPerformingRanged && b && warrior.activesJumpCoroutine == null)
        {
            float dist = Mathf.Abs(target.position.x - transform.position.x);

            bool inMeleeRange = dist <= meleeRange;
            bool inRangedRange = dist <= rangedRange && dist >= minRangedDistance;

            if (inMeleeRange && Time.time >= _lastMeleeTime + meleeCooldown)
            {
                StartMeleeAttack();
                return;
            }

            if (inRangedRange && Time.time >= _lastRangedTime + rangedCooldown)
            {
                float d = Mathf.Abs(GetTargetCenterX(warrior.transform) - GetMyCenterX());

                if (d <= rangedRange && IsWarriorBehind(warrior.transform))
                {
                    Flip();
                }

                StartRangedAttack();
                return;
            }
        }

        if (CurrentplatForm != null)
        {
            float targetX = xEdge;

            if (target != null && b && warrior.CountGroundPoints() > 0)
                targetX = target.position.x;
            else
            {
                xEdge = 0f;
                initDirection();

                if (activesMoveCoroutine == null)
                {
                    RunAnimationDisplay();
                    activesMoveCoroutine = MoveTowardPostionAction(targetX);
                    StartCoroutine(activesMoveCoroutine);
                }
                return;
            }

            SetDirectionVariables(targetX);

            if (CanMove && activesMoveCoroutine == null && !isPerformingMelee && !isPerformingRanged)
            {
                RunAnimationDisplay();
                activesMoveCoroutine = MoveTowardPostionAction(targetX);
                StartCoroutine(activesMoveCoroutine);
            }
        }
    }

    // ------------------ MELEE ------------------

    private void StartMeleeAttack()
    {
        StopMoveTowardCoroutine();

        CanMove = false;
        isPerformingMelee = true;
        _impactResolvedThisSwing = false;

        _lastMeleeTime = Time.time;

        AttackAnimationDisplay();
    }

    private void CancelMelee()
    {
        isPerformingMelee = false;
        CanMove = true;
        _impactResolvedThisSwing = false;

        if (animator != null)
            animator.SetBool("isAttacking", false);
    }

    public void AE_MeleeHit()
    {
        if (_impactResolvedThisSwing) return;
        if (!isPerformingMelee) return;
        if (IsStunned) return;
        if (!CanDealDamageNow()) return;

        var warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null) return;
        if (!IsWarriorOnSamePlatform()) return;
        if (!IsWarriorCloseEnoughToHit(warrior)) return;

        _impactResolvedThisSwing = true;

        if (warrior.TryBlockEnemyHit(this))
            return;

        warrior.TakeDamage(attackDamage);
        warrior.SpawnBloodshedEffectFromEnemy(this);
    }

    public void AE_EndMelee()
    {
        isPerformingMelee = false;
        CanMove = true;
    }

    // ------------------ RANGED ------------------

    private void StartRangedAttack()
    {
        StopMoveTowardCoroutine();

        FaceTarget();

        CanMove = false;
        isPerformingRanged = true;

        StartCoroutine(RangedAttackRoutine());
    }

    private IEnumerator RangedAttackRoutine()
    {
        AttackAnimationDisplay();

        yield return new WaitForSeconds(0.2f);

        if (flamethrowerVfx != null)
            flamethrowerVfx.SetActive(true);

        yield return StartCoroutine(FireProjectiles());

        yield return new WaitForSeconds(1.1f);

        if (flamethrowerVfx != null)
            flamethrowerVfx.SetActive(false);

        _lastRangedTime = Time.time;

        yield return new WaitForSeconds(0.3f);

        isPerformingRanged = false;
        CanMove = true;
    }

    private IEnumerator FireProjectiles()
    {
        for (int i = 0; i < projectileCount; i++)
        {
            SpawnProjectile();
            yield return new WaitForSeconds(projectileDelay);
        }
    }

    private void SpawnProjectile()
    {
        FaceTarget();

        if (flamethrowerProjectilePrefab == null || projectileSpawnPoint == null)
            return;

        GameObject flame = Instantiate(
            flamethrowerProjectilePrefab,
            projectileSpawnPoint.position,
            Quaternion.identity);

        RakaFlameProjectile proj = flame.GetComponent<RakaFlameProjectile>();
        if (proj != null)
            proj.Initialize(target);

        RakaFlameDamage dmg = flame.GetComponent<RakaFlameDamage>();
        if (dmg != null)
            dmg.Initialize(this);

        Destroy(flame, 3f);
    }

    // ------------------ FIRST HIT REACTION ------------------

    public override void TakeDamage(float damage)
    {
        bool triggerFirstHit = ShouldTriggerFirstHitReaction();

        if (triggerFirstHit)
            TriggerFirstHitReaction();

        base.TakeDamage(damage);
    }

    private bool ShouldTriggerFirstHitReaction()
    {
        if (!enableFirstHitReaction) return false;
        if (Time.time - _lastFirstHitAt < firstHitCooldown) return false;
        if (_impactResolvedThisSwing) return false;
        if (!IsAttackWindowActive()) return false;

        return true;
    }

    private void TriggerFirstHitReaction()
    {
        _lastFirstHitAt = Time.time;
        _impactResolvedThisSwing = true;

        StopMoveTowardCoroutine();
        CanMove = false;

        ApplyStun(firstHitStunSeconds);

        Warrior warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior != null)
            StartCoroutine(HugeRecoilAwayFromWarrior(warrior));
    }

    private IEnumerator HugeRecoilAwayFromWarrior(Warrior warrior)
    {
        if (warrior == null) yield break;

        bool stepRight = transform.position.x >= warrior.transform.position.x;

        float oldStepBack = stepBackDistance;
        stepBackDistance = hugeRecoilStepDistance;

        for (int i = 0; i < hugeRecoilSteps; i++)
        {
            yield return StartCoroutine(SmoothStepBack(stepRight));
            yield return null;
        }

        stepBackDistance = oldStepBack;
    }

    // ------------------ UTILITIES ------------------

    private bool IsAttackWindowActive()
    {
        if (animator == null) return false;

        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
        return st.IsName(targetAnimationName) || animator.GetBool("isAttacking");
    }

    private void initDirection()
    {
        if (CurrentplatForm != null && xEdge == 0f)
        {
            if (OddValue)
                xEdge = CurrentplatForm.platformCollider.bounds.max.x;
            else
                xEdge = CurrentplatForm.platformCollider.bounds.min.x;
        }
    }

    protected override void OnDeath()
    {
        base.OnDeath();

        isPerformingMelee = false;
        isPerformingRanged = false;
    }

    private bool IsWarriorOnSamePlatform()
    {
        var warrior = GameMgr.Instance?.WarriorInstance;

        if (warrior == null) return false;
        if (warrior.CurrentplatForm == null) return false;
        if (CurrentplatForm == null) return false;

        return warrior.CurrentplatForm == CurrentplatForm;
    }

    private void FaceTarget()
    {
        if (target == null) return;

        float myX = GetMyCenterX();
        float targetX = GetTargetCenterX(target);

        bool targetOnRight = targetX > myX;
        bool currentlyFacingRight = transform.localScale.x > 0f;

        if (targetOnRight != currentlyFacingRight)
            FlipWithFlame();
    }

    private void FlipWithFlame()
    {
        Flip();

        if (flamethrowerVfx != null)
        {
            Vector3 s = flamethrowerVfx.transform.localScale;
            s.x *= -1f;
            flamethrowerVfx.transform.localScale = s;
        }
    }
}

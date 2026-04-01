using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Services;
using System.Collections;
using UnityEngine;

public class HashagarMonster : Enemy
{
    [Header("Projectile Settings")]
    [SerializeField] private GameObject fireballPrefab;
    [SerializeField] private Transform firePoint; // assign HandSocket
    [SerializeField] private float projectileSpeed = 5f;
    [SerializeField] private float projectileLifetime = 3f;

    [Header("Attack Ranges")]
    [SerializeField] private float meleeRange = 3f;
    [SerializeField] private float throwRange = 10.0f;
    [SerializeField] private float minDistanceToThrow = 3.0f;

    [Header("Cooldowns")]
    [SerializeField] private float meleeCooldown = 1.0f;
    [SerializeField] private float fireballCooldown = 2.0f;

    [Header("Facing Rules")]
    [SerializeField] private bool meleeRequiresFront = true;
    [SerializeField] private bool fireballRequiresFront = true;

    [Header("Melee Hit Timing")]
    [SerializeField] private int meleeHitFrameThreshold = 25;

    private float lastMeleeTime = -Mathf.Infinity;
    private float lastFireballTime = -Mathf.Infinity;

    private bool isPerformingMelee = false;
    private bool isPerformingRanged = false;

    private GameObject heldFireball;

    [Header("Ranged Fail Safe")]
    [SerializeField] private float rangedFailSafeSeconds = 2.5f;
    private float rangedStartedAt;

    [Header("Attack2 Hold Loop")]
    [SerializeField] private string attack2ClipName = "Attack2Animation"; // exact clip name
    [SerializeField] private int holdStartFrame = 10;
    [SerializeField] private bool holdRequiresTouch = false;
    [SerializeField] private bool nudgeWarriorOnRelease = true;
    [SerializeField] private float releaseNudgePadding = 0.06f;

    [Header("Melee Cancel")]
    [SerializeField] private float meleeOutOfRangePadding = 0.25f;
    [SerializeField] private float meleeOutOfRangeGrace = 0.08f;
    [SerializeField] private float meleeMinCommitSeconds = 0.12f;
    [SerializeField] private float meleeStartTimeout = 1.0f; // if Animator uses Exit Time, increase

    private Coroutine _meleeLockRoutine;
    private int _meleeToken;
    private float _meleeStartedAt;
    private float _meleeOutOfRangeSince = -1f;

    private AnimationClip _attack2Clip;
    private int _attack2TotalFrames;

    private Warrior _warrior;
    private bool _warriorDisabledByHold;

    //  NEW: hide Warrior visuals during hold (without disabling the GO)
    private SpriteRenderer[] _warriorSprites;
    private int _cachedWarriorId = -1;

    private bool IsWarriorShieldUp()
    {
        CacheWarriorRefs(); // you already have this
        return _warrior != null && _warrior.ShieldIsUp;
    }

    [Header("Shield Counter")]
    [SerializeField] private bool preventAttack2WhenWarriorShieldUp = true;
    [SerializeField] private bool cancelAttack2IfShieldGoesUpMidAttack = true;


    private bool _isDying;
    private bool IsDyingOrDead => _isDying || currentHealth <= 0f;
    protected override void Start()
    {
        base.Start();

        CacheAttack2Clip();

        // Hashagar defaults
        Range = throwRange;
        attackCooldown = meleeCooldown;
        attackDamage = 25;
        CanMove = true;
        Speed = 2f;
        stepBackDistance = 0.75f;

        ApplySpawnOverridesNow();

        if (NormalCollider != null && TriggerColliderLeft != null)
            Physics2D.IgnoreCollision(NormalCollider, TriggerColliderLeft, true);

        if (NormalCollider != null && TriggerColliderRight != null)
            Physics2D.IgnoreCollision(NormalCollider, TriggerColliderRight, true);
    }

    protected override void Update()
    {
        if (StopMovingWhenWarriorDie) return;

        // NEW: never run gameplay logic while dying/dead
        if (IsDyingOrDead)
        {
            EnableWarriorIfWeDisabled(force: true);
            return;
        }

        initDirection();
        base.Update();

        // In case death started during base.Update() side effects
        if (IsDyingOrDead)
        {
            EnableWarriorIfWeDisabled(force: true);
            return;
        }

        HandleAttack2HoldByFrame();

        //  ranged failsafe (prevents being stuck)
        if (isPerformingRanged && Time.time - rangedStartedAt > rangedFailSafeSeconds)
        {
            ClearHeldFireball();
            ResetRangedFlag();
        }

        // Decide attacks only when free
        if (target != null && !isPerformingMelee && !isPerformingRanged)
        {
            SetDirectionVariables(target.position.x);
            float dist = Vector2.Distance(transform.position, target.position);

            bool inAnyRange = IsWarriorInAnyAttackRange(dist);

            if (!inAnyRange)
            {
                ResumePatrolIfSafe();
            }
            else if (CanMelee(dist))
            {
                if (meleeRequiresFront && !IsWarriorInFront(target))
                    Flip();

                ClearHeldFireball();
                StartMeleeAttack();
            }
            else if (preventAttack2WhenWarriorShieldUp && dist <= meleeRange && IsWarriorShieldUp())
            {
                // Warrior is close but shielded -> skip Attack2.
                // If he can throw, do ranged; otherwise resume patrol/reposition.
                if (dist > minDistanceToThrow && dist <= throwRange && CanThrowFireball(dist))
                {
                    StopMoveTowardCoroutine();
                    StartRangedAttack();
                }
                else
                {
                    ResumePatrolIfSafe();
                }
            }
            else if (dist > minDistanceToThrow && dist <= throwRange && CanThrowFireball(dist))
            {
                StopMoveTowardCoroutine();
                StartRangedAttack();
            }
        }

        // Patrol
        if (CurrentplatForm != null)
        {
            SetDirectionVariables(xEdge);

            if (CanMove && activesMoveCoroutine == null && !_isJumping && !isPerformingMelee && !isPerformingRanged)
            {
                ClearHeldFireball();
                WalkAnimationDisplay();
                activesMoveCoroutine = MoveTowardPostionAction(xEdge);
                StartCoroutine(activesMoveCoroutine);
            }
        }
    }

    // -------------------- MELEE --------------------

    private bool CanMelee(float dist)
    {
        if (Time.time - lastMeleeTime < meleeCooldown) return false;
        if (target == null) return false;
        if (dist > meleeRange) return false;

        // NEW: do not allow Attack2 melee while warrior shield is active
        if (preventAttack2WhenWarriorShieldUp && IsWarriorShieldUp()) return false;

        // NOTE: no front check here on purpose
        return true;
    }
    private void StartMeleeAttack()
    {
        if (IsDyingOrDead) return;

        if (isPerformingRanged) return;


        // NEW hard guard
        if (preventAttack2WhenWarriorShieldUp && IsWarriorShieldUp())
            return;

        StopMoveTowardCoroutine();
        CanMove = false;
        if (rigidbody2 != null) rigidbody2.linearVelocity = Vector2.zero;

        isPerformingMelee = true;
        _meleeStartedAt = Time.time;

        AttackAnimation2Display();
        lastMeleeTime = Time.time;

        _meleeToken++;
        if (_meleeLockRoutine != null) StopCoroutine(_meleeLockRoutine);
        _meleeLockRoutine = StartCoroutine(MeleeLockRoutine(_meleeToken));
    }

    private IEnumerator MeleeLockRoutine(int token)
    {
        _meleeOutOfRangeSince = -1f;

        float startTimeout = Time.time + meleeStartTimeout;
        while (this != null && token == _meleeToken && !IsAttack2Active())
        {
            if (Time.time >= startTimeout)
            {
                Debug.LogWarning($"{name}: Attack2 never became active. Check Animator transitions / conditions.");
                EndMeleeAndResume();
                yield break;
            }
            yield return null;
        }

        while (this != null && token == _meleeToken)
        {
            if (cancelAttack2IfShieldGoesUpMidAttack && IsWarriorShieldUp())
                break;
            if (currentHealth <= 0f) break;

            var w = (GameMgr.Instance != null) ? GameMgr.Instance.WarriorInstance : null;
            if (w == null) break;

            // IMPORTANT: don't end melee just because YOU hid him
            if (!w.gameObject.activeInHierarchy && !_warriorDisabledByHold) break;

            if (!IsAttack2Active())
                break;

            float dx = GetHorizontalDistanceToWarrior(w);
            bool outOfMelee = dx > meleeRange + meleeOutOfRangePadding;

            if (Time.time - _meleeStartedAt >= meleeMinCommitSeconds && outOfMelee)
            {
                if (_meleeOutOfRangeSince < 0f) _meleeOutOfRangeSince = Time.time;
                if (Time.time - _meleeOutOfRangeSince >= meleeOutOfRangeGrace)
                    break;
            }
            else
            {
                _meleeOutOfRangeSince = -1f;
            }

            yield return null;
        }

        if (this != null && token == _meleeToken)
            EndMeleeAndResume();
    }

    private void EndMeleeAndResume()
    {
        if (!isPerformingMelee) return;

        isPerformingMelee = false;
        CanMove = true;

        EnableWarriorIfWeDisabled();

        if (animator != null)
            animator.SetBool("isAttacking2", false);

        _meleeToken++;
        if (_meleeLockRoutine != null)
        {
            StopCoroutine(_meleeLockRoutine);
            _meleeLockRoutine = null;
        }

        _meleeOutOfRangeSince = -1f;

        // ResetImpactFlag();
    }


    // Animation Event at end of Attack2 clip (optional but perfect)
    public void AE_EndMelee()
    {
        EndMeleeAndResume();
    }

    public override void OnAttackPerformed(IAttacker attacker, Transform attackedTarget)
    {
        var warrior = GameMgr.Instance.WarriorInstance;
        if (warrior == null) return;

        if (!warrior.gameObject.activeInHierarchy) return;

        float dist = Vector2.Distance(transform.position, warrior.transform.position);
        if (dist > meleeRange) return;

        if (meleeRequiresFront && !IsWarriorInFront(warrior.transform)) return;

        if (warrior.CurrentplatForm == CurrentplatForm && warrior.collider2.IsTouching(NormalCollider))
        {
            if (frameIndex >= meleeHitFrameThreshold)
            {
                if (warrior.TryBlockEnemyHit(this))
                    return;

                warrior.TakeDamage(attackDamage);
                warrior.SpawnBloodshedEffectFromEnemy(this);
            }
        }
    }

    private float GetHorizontalDistanceToWarrior(Warrior w)
    {
        if (w == null) return Mathf.Infinity;

        float myX = (NormalCollider != null) ? NormalCollider.bounds.center.x : transform.position.x;
        float wX = (w.collider2 != null) ? w.collider2.bounds.center.x : w.transform.position.x;

        return Mathf.Abs(wX - myX);
    }

    // -------------------- FIREBALL --------------------

    private bool CanThrowFireball(float dist)
    {
        if (Time.time - lastFireballTime < fireballCooldown) return false;
        if (target == null) return false;
        if (dist > throwRange) return false;
        if (dist < minDistanceToThrow) return false;
        if (fireballRequiresFront && !IsWarriorInFront(target)) return false;
        if (fireballPrefab == null) return false;
        if (firePoint == null) return false;
        return true;
    }

    private void StartRangedAttack()
    {
        isPerformingRanged = true;
        rangedStartedAt = Time.time;
        AttackAnimationDisplay();
    }

    public void SpawnFireballInHand()
    {
        if (!isPerformingRanged) return;

        if (fireballPrefab == null)
        {
            Debug.LogWarning($"{name}: Fireball prefab is not assigned!");
            return;
        }

        if (firePoint == null)
        {
            Debug.LogWarning($"{name}: FirePoint/HandSocket is not assigned!");
            return;
        }

        ClearHeldFireball();

        heldFireball = Instantiate(fireballPrefab, firePoint.position, Quaternion.identity, firePoint);
        heldFireball.transform.localPosition = Vector3.zero;
        heldFireball.transform.localRotation = Quaternion.identity;

        var rb = heldFireball.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.simulated = false;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    public void LaunchHeldFireball()
    {
        if (heldFireball == null) return;

        if (target == null)
        {
            ClearHeldFireball();
            ResetRangedFlag();
            return;
        }

        FaceTargetIfNeeded();

        heldFireball.transform.SetParent(null);

        var projectile = heldFireball.GetComponent<LaunchProjectile>();
        if (projectile != null)
            projectile.SetOwnerAndDamage(this, attackDamage);

        Vector3 spawnPosition = heldFireball.transform.position;
        Vector2 direction = (target.position - spawnPosition).normalized;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        heldFireball.transform.rotation = Quaternion.Euler(0, 0, angle);

        LaunchProjectile projectileScript = heldFireball.GetComponent<LaunchProjectile>();
        if (projectileScript != null)
        {
            projectileScript.Initialize(projectileSpeed, projectileLifetime);

            var rb2 = heldFireball.GetComponent<Rigidbody2D>();
            if (rb2 != null)
            {
                rb2.simulated = true;
                rb2.linearVelocity = direction * projectileSpeed;
            }
        }
        else
        {
            Rigidbody2D rb = heldFireball.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.simulated = true;
                rb.linearVelocity = direction * projectileSpeed;
                Destroy(heldFireball, projectileLifetime);
            }
            else
            {
                Debug.LogWarning($"{name}: Held fireball has no LaunchProjectile and no Rigidbody2D. Destroying.");
                Destroy(heldFireball);
            }
        }

        heldFireball = null;

        lastFireballTime = Time.time;

        Invoke(nameof(ResetRangedFlag), fireballCooldown * 0.5f);
    }

    private void ResetRangedFlag()
    {
        isPerformingRanged = false;
    }

    private void ClearHeldFireball()
    {
        if (heldFireball != null)
        {
            Destroy(heldFireball);
            heldFireball = null;
        }

        ResetRangedFlag();
    }

    // -------------------- HOLD (ATTACK2) --------------------

    private void CacheWarriorRefs()
    {
        if (_warrior == null)
            _warrior = (GameMgr.Instance != null) ? GameMgr.Instance.WarriorInstance : null;

        //  if warrior instance changed (scene reload etc.) refresh cached renderers
        if (_warrior != null)
        {
            int id = _warrior.GetInstanceID();
            if (id != _cachedWarriorId)
            {
                _cachedWarriorId = id;
                _warriorSprites = null;
            }
        }
    }

    private void CacheWarriorVisuals()
    {
        CacheWarriorRefs();
        if (_warrior == null) return;

        if (_warriorSprites == null || _warriorSprites.Length == 0)
            _warriorSprites = _warrior.GetComponentsInChildren<SpriteRenderer>(true);
    }

    private void SetWarriorVisible(bool visible)
    {
        CacheWarriorVisuals();
        if (_warriorSprites == null) return;

        for (int i = 0; i < _warriorSprites.Length; i++)
        {
            if (_warriorSprites[i] != null)
                _warriorSprites[i].enabled = visible;
        }
    }

    private void CacheAttack2Clip()
    {
        if (_attack2Clip != null || animator == null) return;

        var ac = animator.runtimeAnimatorController;
        if (ac == null) return;

        foreach (var c in ac.animationClips)
        {
            if (c != null && c.name == attack2ClipName)
            {
                _attack2Clip = c;
                _attack2TotalFrames = Mathf.CeilToInt(_attack2Clip.length * _attack2Clip.frameRate);
                _attack2TotalFrames = Mathf.Max(1, _attack2TotalFrames);
                break;
            }
        }

        if (_attack2Clip == null)
            Debug.LogWarning($"{name}: Attack2 clip '{attack2ClipName}' not found. Check the clip name exactly.");
    }

    private AnimationClip GetDominantClipOnLayer0()
    {
        if (animator == null) return null;

        var infos = animator.GetCurrentAnimatorClipInfo(0);
        if (infos == null || infos.Length == 0) return null;

        AnimationClip best = infos[0].clip;
        float bestW = infos[0].weight;

        for (int i = 1; i < infos.Length; i++)
        {
            if (infos[i].weight > bestW)
            {
                bestW = infos[i].weight;
                best = infos[i].clip;
            }
        }
        return best;
    }

    private int GetCurrentFrameIndexForClip(AnimationClip clip)
    {
        if (clip == null || animator == null) return -1;

        var state = animator.GetCurrentAnimatorStateInfo(0);
        float normalized = state.normalizedTime % 1f;
        int totalFrames = Mathf.CeilToInt(clip.length * clip.frameRate);
        totalFrames = Mathf.Max(1, totalFrames);

        int frame = Mathf.FloorToInt(normalized * totalFrames);
        return Mathf.Clamp(frame, 0, totalFrames - 1);
    }

    private bool CanHoldWarriorNow(Warrior w)
    {
        if (w == null) return false;
        if (w.CanDie) return false;

        float dist = Vector2.Distance(transform.position, w.transform.position);
        if (dist > meleeRange) return false;

        if (meleeRequiresFront && !IsWarriorInFront(w.transform)) return false;
        if (w.CurrentplatForm != CurrentplatForm) return false;

        if (holdRequiresTouch)
        {
            if (w.collider2 == null || NormalCollider == null) return false;
            if (!w.collider2.IsTouching(NormalCollider)) return false;
        }

        return true;
    }

    private void DisableWarriorForce()
    {
        CacheWarriorRefs();
        if (_warrior == null) return;
        if (_warriorDisabledByHold) return;

        _warrior.CanMove = false;
        _warrior.CanAttackWarrior = false;
        _warrior.ForceCancelCurrentAttack();
        _warrior.NotifyUIConsumedInput(0.20f);

        var rb = _warrior.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        SetWarriorVisible(false);
        _warriorDisabledByHold = true;
    }

    private void DisableWarriorIfPossible()
    {
        CacheWarriorRefs();
        if (_warrior == null) return;
        if (_warriorDisabledByHold) return;
        if (!CanHoldWarriorNow(_warrior)) return;

        _warrior.CanMove = false;
        _warrior.CanAttackWarrior = false;
        _warrior.NotifyUIConsumedInput(0.20f);

        var rb = _warrior.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        SetWarriorVisible(false);
        _warriorDisabledByHold = true;
    }

    [Header("StepBack Block After Release")]
    [SerializeField] private float noStepBackAfterReleaseSeconds = 0.25f;
    private float _noStepBackUntil = -1f;

    private void EnableWarriorIfWeDisabled(bool force = false)
    {
        CacheWarriorRefs();
        if (_warrior == null) return;

        if (!force && !_warriorDisabledByHold) return;

        _warrior.CanMove = true;
        _warrior.CanAttackWarrior = true;
        //  Show visuals again
        SetWarriorVisible(true);

        if (_warrior.collider2 != null && NormalCollider != null)
            Physics2D.IgnoreCollision(_warrior.collider2, NormalCollider, false);

        if (nudgeWarriorOnRelease && NormalCollider != null && _warrior.collider2 != null)
        {
            float dir = (transform.localScale.x >= 0f) ? 1f : -1f;

            float warriorHalfX = _warrior.collider2.bounds.extents.x;
            float myEdge = (dir > 0f) ? NormalCollider.bounds.max.x : NormalCollider.bounds.min.x;

            // push warrior fully OUTSIDE our collider
            float targetX = myEdge + (dir > 0f ? +(warriorHalfX + releaseNudgePadding)
                                              : -(warriorHalfX + releaseNudgePadding));

            var p = _warrior.transform.position;
            p.x = targetX;
            _warrior.transform.position = p;
        }


        _warriorDisabledByHold = false;
    }

    private void HandleAttack2HoldByFrame()
    {
        // NEW: hard stop during dying/dead
        if (IsDyingOrDead)
        {
            EnableWarriorIfWeDisabled(force: true);
            return;
        }

        // existing code...
        if (cancelAttack2IfShieldGoesUpMidAttack && isPerformingMelee && IsWarriorShieldUp())
        {
            EndMeleeAndResume();
            return;
        }

        if (_attack2Clip == null) return;

        var current = GetDominantClipOnLayer0();

        if (animator != null && animator.IsInTransition(0))
            return;

        bool playingAttack2 = (current != null && current.name == _attack2Clip.name);

        if (!playingAttack2)
        {
            EnableWarriorIfWeDisabled();
            return;
        }

        int frame = GetCurrentFrameIndexForClip(_attack2Clip);
        if (frame < 0) return;

        if (frame >= holdStartFrame)
            DisableWarriorForce();
        else
            EnableWarriorIfWeDisabled();
    }

    private bool IsPlayingAttack2Animation(float minWeight = 0.45f)
    {
        if (animator == null) return false;

        var infos = animator.GetCurrentAnimatorClipInfo(0);
        for (int i = 0; i < infos.Length; i++)
        {
            var clip = infos[i].clip;
            if (clip != null && clip.name == attack2ClipName && infos[i].weight >= minWeight)
                return true;
        }

        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorClipInfo(0);
            for (int i = 0; i < next.Length; i++)
            {
                var clip = next[i].clip;
                if (clip != null && clip.name == attack2ClipName && next[i].weight > 0.05f)
                    return true;
            }
        }

        return false;
    }

    private bool IsWarriorInAnyAttackRange(float dist)
    {
        bool inMelee = dist <= meleeRange;
        bool inThrow = dist > minDistanceToThrow && dist <= throwRange;
        return inMelee || inThrow;
    }

    private void ResumePatrolIfSafe()
    {
        if (IsPlayingAttack2Animation())
            return;

        CanMove = true;

        if (animator != null && !animator.GetBool("isWalking"))
            WalkAnimationDisplay();
    }

    private bool IsAttack2Active()
    {
        return IsPlayingAttack2Animation(0.35f);
    }

    public override bool CanStepBack(bool positif)
    {
        // block right after release
        if (Time.time < _noStepBackUntil)
            return false;

        if (IsPlayingAttack2Animation())
            return false;

        return base.CanStepBack(positif);
    }


    protected override void OnDisable()
    {
        EnableWarriorIfWeDisabled(force: true);
        base.OnDisable();
    }

    [Header("Attack2 Impact FX")]
    [SerializeField] private GameObject impact02Prefab;
    [SerializeField] private Transform impactSocket;
    [SerializeField] private Vector3 impactLocalOffset = Vector3.zero;
    [SerializeField] private bool impactFollowSocket = false;
    [SerializeField] private float impactDestroyAfter = 1.25f;

    // prevents spam if events are close together
    [SerializeField] private float impactMinInterval = 0.05f;

    private float _lastImpactTime = -999f;

  //  private bool _impact02HitDone;
    public void AE_SpawnImpact02()
    {
        // only during Attack2 melee
        if (!isPerformingMelee) return;
        if (!IsAttack2Active()) return;

        if (impact02Prefab == null || impactSocket == null) return;

        // throttle
        if (Time.time - _lastImpactTime < impactMinInterval) return;
        _lastImpactTime = Time.time;

        Vector3 pos = impactSocket.TransformPoint(impactLocalOffset);
        Quaternion rot = impactSocket.rotation;

        var fx = Instantiate(impact02Prefab, pos, rot);

        if (impactFollowSocket)
            fx.transform.SetParent(impactSocket, worldPositionStays: true);

        if (impactDestroyAfter > 0f)
            Destroy(fx, impactDestroyAfter);

        // ---------------- DAMAGE LOGIC ----------------

        var warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null) return;

        if (!warrior.gameObject.activeInHierarchy) return;

        float dist = Vector2.Distance(transform.position, warrior.transform.position);
        if (dist > meleeRange) return;

        if (meleeRequiresFront && !IsWarriorInFront(warrior.transform)) return;

        if (warrior.CurrentplatForm == CurrentplatForm)
        {
            if (warrior.TryBlockEnemyHit(this))
                return;

            warrior.TakeDamage(50);
            warrior.SpawnBloodshedEffectFromEnemy(this);
        }
    }



    // -------------------- PATROL EDGE INIT --------------------

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

    private void FaceTargetIfNeeded()
    {
        if (target == null) return;

        if (!IsWarriorInFront(target))
            Flip();
    }

    protected override void OnDeath()
    {
        if (_isDying) return;   // prevent double-entry
        _isDying = true;

        // stop melee/ranged state immediately
        isPerformingMelee = false;
        isPerformingRanged = false;

        _meleeToken++;
        if (_meleeLockRoutine != null)
        {
            StopCoroutine(_meleeLockRoutine);
            _meleeLockRoutine = null;
        }

        ClearHeldFireball();
        EnableWarriorIfWeDisabled(force: true);

        if (animator != null)
            animator.SetBool("isAttacking2", false);

        base.OnDeath();
    }
    protected override void ConfigureAttack()
    {
        // custom numbers for this enemy type
        Range = 4.5f;
        attackCooldown = 0.9f;
        attackDamage = 25;

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
}

using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using Assets.Scripts.Relics.Events;
using Assets.Scripts.Services;
using Assets.Scripts.Tools;
using Assets.Scripts.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    [RequireComponent(typeof(EnemyRangeService))]
    public class Enemy : CharacterController, IStepable, IAttacker
    {
        [Header("Attack Configuration")]
        [SerializeField] public float Range = 3f;
        [SerializeField] protected float attackCooldown = 1.5f;
        [SerializeField] protected int attackDamage = 10;

        [Header("Target & Movement")]
        public Transform target;
        public Transform groundCheckPoint;

        [Header("Colliders")]
        public BoxCollider2D TriggerColliderLeft;
        public BoxCollider2D TriggerColliderRight;
        public BoxCollider2D NormalCollider;

        [Header("Services")]
        [SerializeField] public EnemyRangeService EnemyRangeService;

        [Header("Health Bar Configuration")]
        [SerializeField] protected WorldSpaceHealthBar worldHealthBar;
        [SerializeField] protected GameObject healthBarPrefab;
        [SerializeField] protected bool autoCreateHealthBar = true;
        [SerializeField] protected Vector3 healthBarOffset = new Vector3(0, 1.5f, 0);

        [Header("Hit Effects")]
        [SerializeField] private float blinkDuration = 0.08f;
        [SerializeField] private int blinkCount = 3;

        [Header("Death Effects")]

        [SerializeField] private float deathDelay = 1.5f;    // Time to wait before destroying enemy
        [SerializeField] private bool hideOnDeath = true;    // Hide sprite/colliders during explosion

        // ---------------------- DISSOLVE (NEW) ----------------------
        [Header("Dissolve Death")]
        [SerializeField] private bool useDissolveOnDeath = true;
        [SerializeField] private Material dissolveMaterial;     // Assign Mat_Dissolve.mat in Inspector
        [SerializeField] private float dissolveDuration = 0.8f; // Time for dissolve 0->1

        // NOTE: these names match your ShaderGraph properties exactly (including spelling)
        private static readonly int DissolveAmountID = Shader.PropertyToID("_DissovleAmount");
        private static readonly int MainTexID = Shader.PropertyToID("_MainTex");

        // runtime instance so we don't modify shared material
        private Material runtimeDissolveMat;
        // ------------------------------------------------------------

        private Color originalColor;
        private Coroutine blinkCoroutine;

        public float stepBackDistance = 0.5f;
        public float rayLength = 0.5f;
        public float xEdge;
        protected bool OddValue;
        protected bool StopMovingWhenWarriorDie = false;



        [Header("Death SFX")]
        [SerializeField] private AudioClip deathSfxClip;                 // assign fire.mp3 here
        [SerializeField, Range(0f, 1f)] private float deathSfxVolume = 1f;
        [SerializeField] private Vector2 deathSfxPitchRange = new Vector2(0.95f, 1.05f);
        [SerializeField, Range(0f, 1f)] private float deathSfxSpatialBlend = 0f; // 0=2D
        [SerializeField] private float deathSfxMaxDistance = 20f;

        private bool _deathSfxPlayed;

        [Header("Stun")]
        [SerializeField] private bool canBeStunned = true;

        [SerializeField] protected float meleeHitDistance = 0.65f;

        private bool _isStunned;
        private Coroutine _stunRoutine;
        public bool IsStunned => _isStunned;

        public bool IsGroundedOnPlatform => CurrentplatForm != null;
        public Transform Transform => transform;
        public Animator Animator => animator;
        public AudioSource AudioSource => GetComponent<AudioSource>();
        public string Name => gameObject.name;

        public bool IsAttacked { get; set; } = false;

        [SerializeField]
        public string targetAnimationName = "AttackAnimation";
        private AnimatorStateInfo stateInfo;
        private int lastFrameIndex = -1;
        protected AnimationClip currentClip;
        [SerializeField]
        public int totalFramesInAnimation = 16;
        public int frameIndex = -1;

        protected bool _deathStarted;

        public static readonly List<Enemy> ActiveEnemies = new List<Enemy>();

        [SerializeField] protected float disableAttackWhenHitSeconds = 0.2f;
        protected float _attackDisabledUntil = -999f;

        public bool IsAttackTemporarilyDisabled => Time.time < _attackDisabledUntil;

        public virtual bool HardAnchorToMovingPlatforms => true;

        public virtual void DisableAttackTemporarily(float seconds = -1f)
        {
            float d = (seconds > 0f) ? seconds : disableAttackWhenHitSeconds;
            _attackDisabledUntil = Mathf.Max(_attackDisabledUntil, Time.time + d);

        }

        // Inside Assets.Scripts.Characteres.EnemyContoller.Enemy.cs

        // Inside Assets.Scripts.Characteres.EnemyContoller.Enemy.cs

        protected virtual void FixedUpdate()
        {
            if (groundCheckPoint == null) return;

            // Perform the raycast in FixedUpdate to stay synced with Physics/Platform movement
            RaycastHit2D hit = Physics2D.Raycast(
                groundCheckPoint.position,
                Vector2.down,
                rayLength,
                PlatformLayer
            );

            if (hit.collider != null)
            {
                // Update our platform reference immediately
                var platform = hit.collider.GetComponent<PlatFormPlfColliderTrigger>();
                if (platform != null)
                {
                    CurrentplatForm = platform;
                }
            }
            else
            {
                // If we truly left the platform, handle it
                if (CurrentplatForm != null)
                {
                    // Optional: Add a small grace period or double check before nulling
                    CurrentplatForm = null;
                }
            }
        }
        protected override void Start()
        {
            base.Start();
            OddValue = initDirection();
            currentClip = GetAnimationClip(targetAnimationName);

            if (spriteRenderer != null)
            {
                originalColor = spriteRenderer.color;
            }
            else
            {
                Debug.LogError($"{gameObject.name}: No SpriteRenderer found!");
            }

            if (target == null)
            {
                GameObject warrior = GameObject.Find("Warrior");
                if (warrior != null)
                {
                    target = warrior.transform;
                }
            }

            if (EnemyRangeService == null)
            {
                EnemyRangeService = GetComponent<EnemyRangeService>();

                if (EnemyRangeService == null)
                {
                    EnemyRangeService = gameObject.AddComponent<EnemyRangeService>();
                }
            }

            if (EnemyRangeService != null)
            {
                EnemyRangeService.Initialize(this);
                ConfigureAttack();
            }

            // Initialize Health Bar
            InitializeHealthBar();
        }

        /// <summary>
        /// Initialize health bar with multiple fallback options
        /// </summary>
        protected virtual void InitializeHealthBar()
        {
            // Try to find existing health bar in children
            if (worldHealthBar == null)
            {
                worldHealthBar = GetComponentInChildren<WorldSpaceHealthBar>();
            }

            // If still not found, try to create one
            if (worldHealthBar == null)
            {
                if (autoCreateHealthBar)
                {
                    worldHealthBar = CreateHealthBar();
                }
                else
                {
                    Debug.LogWarning($"{gameObject.name}: No health bar assigned and autoCreateHealthBar is false.");
                    return;
                }
            }

            // Configure the health bar
            if (worldHealthBar != null)
            {
                worldHealthBar.SetTarget(this.transform);
                worldHealthBar.SetOffset(healthBarOffset);
                worldHealthBar.ForceUpdate(currentHealth, maxHealth);

            }
        }

        /// <summary>
        /// Create a health bar programmatically
        /// </summary>
        protected virtual WorldSpaceHealthBar CreateHealthBar()
        {
            WorldSpaceHealthBar healthBar;

            // Try to use prefab first
            if (healthBarPrefab != null)
            {
                healthBar = HealthBarFactory.CreateHealthBarFromPrefab(healthBarPrefab, transform, healthBarOffset);
                Debug.Log($"{gameObject.name}: Health bar created from prefab");
            }
            else
            {
                // Create default health bar
                healthBar = HealthBarFactory.CreateHealthBar(transform, healthBarOffset);
                Debug.Log($"{gameObject.name}: Default health bar created");
            }

            return healthBar;
        }

        protected virtual void Update()
        {
            // Keep committed patrol target stable on moving vertical platforms
            CommitPatrolEdgeForMovingVerticalPlatform();

            RaycastHit2D hit = Physics2D.Raycast(
                groundCheckPoint.position,
                Vector2.down,
                rayLength,
                PlatformLayer
            );

            if (!hit.collider && CurrentplatForm != null)
            {
                StopMoveTowardCoroutine();

                if (CurrentplatForm != null)
                {
                    Vector3 safePos = transform.position;
                    safePos.x = ClampToCurrentPlatform(safePos.x);
                    transform.position = safePos;

                    // IMPORTANT:
                    // On moving vertical platforms, keep the already committed target.
                    // On normal platforms, keep old behavior.
                    if (CurrentplatForm is Assets.Scripts.Platforms.MovingVerticalPlatform)
                    {
                        CommitPatrolEdgeForMovingVerticalPlatform();
                    }
                    else
                    {
                        xEdge = GetOppositeEdgeX();
                    }
                }

                if (IsGroundedOnPlatform)
                {
                    StickToPlatform();
                }
            }

            stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            if (stateInfo.IsName(targetAnimationName))
            {
                frameIndex = GetCurrentFrameIndex();

                if (frameIndex != lastFrameIndex && frameIndex >= 0)
                {
                    lastFrameIndex = frameIndex;
                }
            }
        }

        protected void ClampEnemyToPlatformTop()
        {
            if (CurrentplatForm == null) return;
            if (collider2 == null) return;

            Bounds pb = CurrentplatForm.platformCollider.bounds;
            Bounds eb = collider2.bounds;

            Vector3 pos = transform.position;
            pos.y = pb.max.y + eb.extents.y;

            transform.position = pos;
        }

        protected virtual void ConfigureAttack()
        {
            if (EnemyRangeService != null)
            {
                EnemyRangeService.SetAttackRange(Range);
                EnemyRangeService.SetAttackCooldown(attackCooldown);
                EnemyRangeService.SetAttackDamage(attackDamage);
            }
        }

        public virtual void PlayAttackAnimation()
        {
            AttackAnimationDisplay();
        }

        //public override void StopMoveTowardCoroutine()
        //{
        //    base.StopMoveTowardCoroutine();
        //    var w = GameMgr.Instance.WarriorInstance;
        //    if (w.CanDie)
        //        StopMovingWhenWarriorDie = true;
        //}
        public override void StopMoveTowardCoroutine()
        {
            base.StopMoveTowardCoroutine();
        }
        public void OnRangeExecuted(Transform target, int damage)
        {
        }

        public virtual void OnAttackPerformed(IAttacker attacker, Transform attackedTarget)
        {
            if (IsWarriorInFront(target))   //front-only
            {
                AttackAnimationDisplay();
            }


        }

        private bool _isDead;

        public bool TakeDamageAndReturnKilled(float damage)
        {
            if (_isDead) return false;
            if (damage <= 0f) return false;

            currentHealth -= damage;
            currentHealth = Mathf.Max(0, currentHealth);

            UpdateHealthBarDisplay();

            if (blinkCoroutine != null) StopCoroutine(blinkCoroutine);
            blinkCoroutine = StartCoroutine(BlinkOnHit());

            bool killed = currentHealth <= 0f;

            OnDamaged(damage, killed);

            if (killed)
            {
                _isDead = true;
                OnDeath();
                return true;
            }

            return false;
        }


        public override void TakeDamage(float damage)
        {
            // compat si d'autres scripts appellent TakeDamage()
            TakeDamageAndReturnKilled(damage);
        }


    

        /// <summary>
        /// Update the health bar display
        /// </summary>
        protected virtual void UpdateHealthBarDisplay()
        {
            if (worldHealthBar != null)
            {
                worldHealthBar.UpdateHealth(currentHealth, maxHealth);
            }
        }

        /// <summary>
        /// Called when enemy dies - plays dissolve/explosion and then destroys
        /// </summary>
        protected virtual void OnDeath() // mets "override" si le parent a virtual OnDeath()
        {
            if (_deathStarted) return;
            _deathStarted = true;
            if (!_deathSfxPlayed)
            {
                _deathSfxPlayed = true;
                Assets.Scripts.Tools.OneShotAudio.Play(
                    deathSfxClip,
                    transform.position,
                    deathSfxVolume,
                    deathSfxPitchRange,
                    deathSfxSpatialBlend,
                    deathSfxMaxDistance
                );
            }
            // Hide health bar
            if (worldHealthBar != null)
                worldHealthBar.SetVisibility(false);

            // Raise OnKill (Warrior is assumed to be the only killer)
            var w = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (w != null)
            {
                var hub = w.GetComponent<PlayerEventHub>();
                if (hub != null)
                {
                    hub.RaiseKill(new KillEvent
                    {
                        killer = w.gameObject,
                        victim = gameObject
                    });
                }
            }

            // Keep your existing death effects (dissolve/explosion/delay)
            StartCoroutine(DeathSequence());
        }


        /// <summary>
        /// Death sequence with dissolve + explosion effect
        /// </summary>
        private IEnumerator DeathSequence()
        {
            // Stop all movement and attacks
            if (rigidbody2 != null)
            {
                rigidbody2.linearVelocity = Vector2.zero;
                rigidbody2.simulated = false;
            }

            // Disable colliders to prevent further interactions
            if (NormalCollider != null) NormalCollider.enabled = false;
            if (TriggerColliderLeft != null) TriggerColliderLeft.enabled = false;
            if (TriggerColliderRight != null) TriggerColliderRight.enabled = false;

            // Freeze current pose (optional but usually looks better with dissolve)
            if (animator != null) animator.enabled = false;

            // IMPORTANT:
            // We DO NOT disable the SpriteRenderer before dissolve.
            // If dissolve is enabled, we dissolve first, then optionally hide.
            if (useDissolveOnDeath && spriteRenderer != null && dissolveMaterial != null)
            {
                yield return StartCoroutine(PlayDissolve());
            }
            else
            {
                // Fallback to old behavior if dissolve is not configured
                if (hideOnDeath)
                {
                    if (spriteRenderer != null) spriteRenderer.enabled = false;
                }
            }

            yield return new WaitForSeconds(deathDelay);

            // notify manager
            EnemyMgr.Instance?.OnEnemyDestroyed(this);

            // destroy enemy
            Destroy(gameObject);
        }

        /// <summary>
        /// Plays the dissolve effect by animating _DissovleAmount from 0 -> 1
        /// </summary>
        private IEnumerator PlayDissolve()
        {
            if (spriteRenderer == null || dissolveMaterial == null)
                yield break;

            // Create a runtime material instance so we don't affect other enemies
            if (runtimeDissolveMat == null)
                runtimeDissolveMat = new Material(dissolveMaterial);

            // If your ShaderGraph uses _MainTex, feed it the sprite texture
            if (spriteRenderer.sprite != null)
            {
                runtimeDissolveMat.SetTexture(MainTexID, spriteRenderer.sprite.texture);
            }

            // Apply dissolve material
            spriteRenderer.material = runtimeDissolveMat;

            // Start visible
            runtimeDissolveMat.SetFloat(DissolveAmountID, 0f);

            float t = 0f;
            while (t < dissolveDuration)
            {
                t += Time.deltaTime;
                float v = Mathf.Clamp01(t / dissolveDuration);

                // If it looks inverted, swap to (1f - v)
                runtimeDissolveMat.SetFloat(DissolveAmountID, v);

                yield return null;
            }

            runtimeDissolveMat.SetFloat(DissolveAmountID, 1f);

            // Optionally hide sprite after dissolve completes
            if (hideOnDeath)
            {
                spriteRenderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            // Stop all coroutines
            if (blinkCoroutine != null)
            {
                StopCoroutine(blinkCoroutine);
            }

            // Clean up health bar if it exists
            if (worldHealthBar != null && worldHealthBar.gameObject != null)
            {
                Destroy(worldHealthBar.gameObject);
            }

            // Clean up runtime dissolve material
            if (runtimeDissolveMat != null)
            {
                Destroy(runtimeDissolveMat);
                runtimeDissolveMat = null;
            }
        }

        // BLINK EFFECT COROUTINE
        private IEnumerator BlinkOnHit()
        {
            if (spriteRenderer == null)
            {
                Debug.LogError($"{gameObject.name}: SpriteRenderer is null in BlinkOnHit!");
                yield break;
            }

            for (int i = 0; i < blinkCount; i++)
            {
                spriteRenderer.color = Color.black;
                yield return new WaitForSeconds(blinkDuration);

                spriteRenderer.color = Color.white;
                yield return new WaitForSeconds(blinkDuration);
            }

            spriteRenderer.color = Color.white;
            blinkCoroutine = null;
        }

        private bool initDirection()
        {
            bool v = false;
            var val = UnityEngine.Random.Range(1, 10);
            if (val % 2 == 0)
                v = true;
            else v = false;
            return v;
        }

        float GetOppositeEdgeX()
        {
            Bounds platformBounds = CurrentplatForm.platformCollider.bounds;
            float distanceToLeftEdge = Mathf.Abs(groundCheckPoint.position.x - platformBounds.min.x);
            float distanceToRightEdge = Mathf.Abs(groundCheckPoint.position.x - platformBounds.max.x);

            if (distanceToRightEdge < distanceToLeftEdge)
            {
                return platformBounds.min.x;
            }
            else
            {
                return platformBounds.max.x;
            }
        }

        #region Trigger Colliders for Warrior Overlap Resolution
        protected virtual void OnTriggerEnter2D(Collider2D collision)
        {
            if (collision.gameObject.name == "Warrior")
            {
                if (CurrentplatForm != null)
                {
                    var w = GameMgr.Instance.WarriorInstance;
                    if (!w.collider2.IsTouching(CurrentplatForm?.platformCollider) && w.activesJumpCoroutine != null)
                    {
                        if (!w.DescendentPhase)
                        {
                            Physics2D.IgnoreCollision(w.collider2, NormalCollider, true);
                        }
                    }
                }
            }
        }

        protected virtual void OnTriggerExit2D(Collider2D collision)
        {
            if (collision.gameObject.name == "Warrior")
            {
                var w = GameMgr.Instance.WarriorInstance;
                w.CanMove = true;
                Physics2D.IgnoreCollision(w.collider2, NormalCollider, false);
            }
        }

        protected virtual void OnTriggerStay2D(Collider2D collision)
        {
            if (collision.gameObject.name == "Warrior")
            {
                var w = GameMgr.Instance.WarriorInstance;
                if (w.activesJumpCoroutine == null && !w.DescendentPhase)
                {
                    bool tmin = w.GoRight && collision.bounds.max.x > NormalCollider.bounds.min.x && collision.bounds.max.x.GetDistanceXAxis(NormalCollider.bounds.min.x) >= 0.2f;
                    bool tmax = w.GoLeft && collision.bounds.min.x < NormalCollider.bounds.max.x && collision.bounds.min.x.GetDistanceXAxis(NormalCollider.bounds.max.x) >= 0.2f;
                    if (tmin)
                    {
                        w.CanMove = false;
                        w.ResolveOverlap(collision, TriggerColliderLeft);
                    }
                    else if (tmax)
                    {
                        w.CanMove = false;
                        w.ResolveOverlap(collision, TriggerColliderRight);
                    }
                }
            }
        }
        #endregion

        public IEnumerator SmoothStepBack(bool positif)
        {
            //central rule: if you can't step back, do nothing
            if (!CanStepBack(positif))
                yield break;

            Vector3 startPos = transform.position;
            Vector3 targetPos = startPos;

            if (positif)
                targetPos.x += stepBackDistance;
            else
                targetPos.x -= stepBackDistance;

            float clampedX = ClampToCurrentPlatform(targetPos.x);

            if (Mathf.Abs(clampedX - startPos.x) < 0.05f)
            {
                Debug.Log($"{gameObject.name}: Cannot step back - at platform edge!");
                yield break;
            }

            targetPos.x = clampedX;

            float elapsed = 0f;
            float duration = 0.1f;

            while (elapsed < duration)
            {
                if (this == null) yield break;

                Vector3 newPos = Vector3.Lerp(startPos, targetPos, elapsed / duration);
                newPos.x = ClampToCurrentPlatform(newPos.x);
                transform.position = newPos;
                elapsed += Time.deltaTime;
                yield return null;
            }

            targetPos.x = ClampToCurrentPlatform(targetPos.x);
            transform.position = targetPos;
        }

        // make it virtual so Hashagar can override
        public virtual bool CanStepBack(bool positif)
        {
            if (CurrentplatForm == null)
                return false;

            float targetX = transform.position.x + (positif ? stepBackDistance : -stepBackDistance);
            return CanMoveToPosition(targetX);
        }


        public virtual float StepRightMaxamize() => NormalCollider.bounds.max.x;
        public virtual float StepLeftMaxamize() => NormalCollider.bounds.min.x;
        float tolerance = 1f;

        public bool IsGroundPointCloserToEdge()
        {
            if (Mathf.Abs(groundCheckPoint.position.x - xEdge) < tolerance)
            {
                return true;
            }
            return false;
        }

        public virtual void OnWarriorDetectedInLaser() { }
        public virtual void OnWarriorLeftLaser() { }
        public virtual void EnableRunningMovementAfterAttack() { }
        public virtual void OnLaserDeactivated() { }

        #region Animation Methods
        int GetCurrentFrameIndex()
        {
            if (currentClip != null)
            {
                float normalizedTime = stateInfo.normalizedTime % 1f;
                float clipLength = currentClip.length;
                float frameRate = currentClip.frameRate;
                int totalFrames = Mathf.CeilToInt(clipLength * frameRate);
                int frameIndex = Mathf.FloorToInt(normalizedTime * totalFrames);
                return Mathf.Clamp(frameIndex, 0, totalFrames - 1);
            }
            else
            {
                return GetCurrentFrameIndexManual();
            }
        }

        int GetCurrentFrameIndexManual()
        {
            float normalizedTime = stateInfo.normalizedTime % 1f;
            int frameIndex = Mathf.FloorToInt(normalizedTime * totalFramesInAnimation);
            return Mathf.Clamp(frameIndex, 0, totalFramesInAnimation - 1);
        }

        AnimationClip GetAnimationClip(string clipName)
        {
            if (animator == null) return null;

            RuntimeAnimatorController ac = animator.runtimeAnimatorController;
            if (ac == null) return null;

            AnimationClip[] clips = ac.animationClips;

            foreach (AnimationClip clip in clips)
            {
                if (clip.name == clipName)
                {
                    return clip;
                }
            }
            return null;
        }
        #endregion

        // ============================================================
        // UPDATED FRONT CHECK (center-based like your IsWarriorInFront)
        // ============================================================

        /// <summary>
        /// Returns the best "center X" for any target transform (prefers non-trigger colliders).
        /// </summary>
        protected float GetTargetCenterX(Transform t)
        {
            if (t == null) return float.NaN;

            // Prefer a non-trigger collider (more "body accurate" than trigger hitboxes)
            var cols = t.GetComponentsInChildren<Collider2D>();
            if (cols != null && cols.Length > 0)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null && cols[i].enabled && !cols[i].isTrigger)
                        return cols[i].bounds.center.x;
                }

                // fallback: first enabled collider
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null && cols[i].enabled)
                        return cols[i].bounds.center.x;
                }
            }

            return t.position.x;
        }

        /// <summary>
        /// Returns enemy "center X" (prefers NormalCollider if assigned).
        /// </summary>
        protected float GetMyCenterX()
        {
            if (NormalCollider != null && NormalCollider.enabled)
                return NormalCollider.bounds.center.x;

            // fallback: any collider on self
            var c = GetComponent<Collider2D>();
            if (c != null) return c.bounds.center.x;

            return transform.position.x;
        }

        /// <summary>
        /// Refresh leftFacing/rightFacing from Front/Back markers if available.
        /// </summary>
        protected void RefreshFacingFlags()
        {
            if (Front != null && Back != null)
            {
                leftFacing = Front.position.x < Back.position.x;
                rightFacing = Front.position.x > Back.position.x;
            }
            else
            {
                // fallback: use localScale
                rightFacing = transform.localScale.x >= 0f;
                leftFacing = !rightFacing;
            }
        }

        /// <summary>
        /// Replaced: now uses center X + facing flags (same logic as your IsWarriorInFront).
        /// All Enemy children will benefit automatically.
        /// </summary>
        protected bool IsWarriorInFront(Transform t, float frontEpsilon = 0.02f)
        {
            if (t == null) return false;

            RefreshFacingFlags();

            float myX = GetMyCenterX();
            float tx = GetTargetCenterX(t);
            if (float.IsNaN(tx)) return false;

            // Facing LEFT => target must be on the LEFT side (<=)
            if (leftFacing) return tx <= myX + frontEpsilon;

            // Facing RIGHT => target must be on the RIGHT side (>=)
            if (rightFacing) return tx >= myX - frontEpsilon;

            // fallback: use scale
            return (transform.localScale.x >= 0f) ? (tx >= myX - frontEpsilon) : (tx <= myX + frontEpsilon);
        }
        protected bool IsWarriorBehind(Transform t, float epsilon = 0.02f)
        {
            if (t == null) return false;

            RefreshFacingFlags();

            float myX = GetMyCenterX();
            float tx = GetTargetCenterX(t);
            if (float.IsNaN(tx)) return false;

            // opposite logic of IsWarriorInFront
            if (leftFacing) return tx > myX + epsilon;
            if (rightFacing) return tx < myX - epsilon;

            return (transform.localScale.x >= 0f) ? (tx < myX - epsilon) : (tx > myX + epsilon);
        }
        public bool IsWarriorCloseEnoughToHit(Warrior warrior)
        {
            if (warrior == null) return false;
            if (NormalCollider == null || warrior.collider2 == null) return false;

            float myX = NormalCollider.bounds.center.x;
            float warriorX = warrior.collider2.bounds.center.x;

            float dist = Mathf.Abs(warriorX - myX);

            return dist <= meleeHitDistance;
        }
        protected void Flip()
        {
            Vector3 scale = transform.localScale;
            scale.x *= -1;
            transform.localScale = scale;

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

        public void ApplyStun(float seconds)
        {
            if (!canBeStunned) return;
            if (seconds <= 0f) return;
            if (currentHealth <= 0) return;

            if (_stunRoutine != null) StopCoroutine(_stunRoutine);
            _stunRoutine = StartCoroutine(StunRoutine(seconds));
        }

        private IEnumerator StunRoutine(float seconds)
        {
            _isStunned = true;
            CanMove = false;

            StopMoveTowardCoroutine();
            if (rigidbody2 != null) rigidbody2.linearVelocity = Vector2.zero;

            yield return new WaitForSeconds(seconds);

            if (this != null)
            {
                _isStunned = false;
                CanMove = true;
            }

            _stunRoutine = null;
        }


        protected virtual void OnDamaged(float damage, bool killed)
        {
        }

        protected virtual void OnEnable()
        {
            if (!ActiveEnemies.Contains(this))
                ActiveEnemies.Add(this);
        }

        protected virtual void OnDisable()
        {
            ActiveEnemies.Remove(this);
        }

        [SerializeField] protected EnemyHitReactionProfile hitReaction;
        public EnemyHitReactionProfile HitReaction => hitReaction; // optional read-only access


        public virtual float ComputeStepBackDistance(float incoming)
        {
            if (hitReaction == null) return incoming;

            float mul = hitReaction.stepBackMultiplier;

            if (Animator != null && Animator.GetCurrentAnimatorStateInfo(0).IsTag("Attack"))
                mul *= hitReaction.whileAttackingMultiplier;

            return Mathf.Clamp(incoming * mul, hitReaction.min, hitReaction.max);
        }
        public override void OnDrawGizmos()
        {
            base.OnDrawGizmos();
            Gizmos.color = Color.red;
            if (groundCheckPoint != null)
            {
                Gizmos.DrawLine(groundCheckPoint.position, groundCheckPoint.position + Vector3.down * rayLength);
            }
        }

        #region Viewport Auto-Death
        [Header("Viewport Auto-Death")]
        [SerializeField] private bool dieWhenOutOfViewport = true;
        [SerializeField] private bool requireSeenOnceBeforeAutoDeath = true;
        [SerializeField] private float viewportMargin = 0.08f; // allow a small margin outside screen
        [SerializeField] private float autoDeathCheckInterval = 0.15f;

        private bool _hasBeenSeenInViewport;
        private float _nextViewportCheckTime;


        #endregion

        protected bool CanDealDamageNow()
        {
            if (currentHealth <= 0f) return false;
            if (_deathStarted) return false;
            if (_isStunned) return false;
            if (IsAttackTemporarilyDisabled) return false;
            return true;
        }

        public void ResetCombatState(float healthPercent = 1f)
        {
            if (_deathStarted) return;

            // IMPORTANT
            StopMovingWhenWarriorDie = false;

            float newHealth = maxHealth * healthPercent;
            currentHealth = Mathf.Clamp(newHealth, 1f, maxHealth);

            base.StopMoveTowardCoroutine();

            if (rigidbody2 != null)
                rigidbody2.linearVelocity = Vector2.zero;

            _isStunned = false;

            DisableAttackTemporarily(1.5f);

            if (NormalCollider != null) NormalCollider.enabled = true;
            if (TriggerColliderLeft != null) TriggerColliderLeft.enabled = true;
            if (TriggerColliderRight != null) TriggerColliderRight.enabled = true;

            UpdateHealthBarDisplay();
        }
        protected virtual void StickToPlatform()
        {
            if (CurrentplatForm == null) return;
            if (rigidbody2 == null) return;

            Vector2 v = rigidbody2.linearVelocity;
            v.y = 0f;
            rigidbody2.linearVelocity = v;
        }
        private EnemySpawnOverrides _spawnOverrides;
        protected EnemySpawnOverrides SpawnOverrides => _spawnOverrides;

        public virtual void SetSpawnOverrides(EnemySpawnOverrides overrides)
        {
            _spawnOverrides = overrides;
        }

        protected void ApplySpawnOverridesNow()
        {
            if (_spawnOverrides == null)
                return;

            if (_spawnOverrides.overrideSpeed)
                Speed = _spawnOverrides.speed;

            if (_spawnOverrides.overrideAttackRange)
                Range = _spawnOverrides.attackRange;

            if (_spawnOverrides.overrideAttackCooldown)
                attackCooldown = _spawnOverrides.attackCooldown;

            if (_spawnOverrides.overrideAttackDamage)
                attackDamage = _spawnOverrides.attackDamage;

            if (_spawnOverrides.overrideMaxHealth)
            {
                maxHealth = Mathf.Max(1f, _spawnOverrides.maxHealth);
                currentHealth = maxHealth;
            }

            ApplySpecificSpawnOverrides();

            ConfigureAttack();
            UpdateHealthBarDisplay();
        }

        protected virtual void ApplySpecificSpawnOverrides()
        {
        }
        [Header("Moving Platform Patrol")]
        [SerializeField] protected float patrolEdgeArriveThreshold = 0.12f;

        protected bool _hasCommittedPatrolEdge;
        protected float _committedPatrolEdgeX;
        protected Collider2D _committedPatrolPlatform;

        protected void CommitPatrolEdgeForMovingVerticalPlatform()
        {
            if (!(CurrentplatForm is Assets.Scripts.Platforms.MovingVerticalPlatform))
            {
                _committedPatrolPlatform = null;
                _hasCommittedPatrolEdge = false;
                return;
            }

            if (CurrentplatForm == null || CurrentplatForm.platformCollider == null)
                return;

            Collider2D platformCol = CurrentplatForm.platformCollider;
            Bounds pb = platformCol.bounds;

            float leftEdge = pb.min.x;
            float rightEdge = pb.max.x;

            // New platform => forget previous commitment
            if (_committedPatrolPlatform != platformCol)
            {
                _committedPatrolPlatform = platformCol;
                _hasCommittedPatrolEdge = false;
            }

            // First time on this platform: commit the CURRENT target once
            if (!_hasCommittedPatrolEdge)
            {
                bool xEdgeIsLeft = Mathf.Abs(xEdge - leftEdge) < 0.01f;
                bool xEdgeIsRight = Mathf.Abs(xEdge - rightEdge) < 0.01f;

                if (xEdgeIsLeft)
                    _committedPatrolEdgeX = leftEdge;
                else if (xEdgeIsRight)
                    _committedPatrolEdgeX = rightEdge;
                else
                    _committedPatrolEdgeX = OddValue ? rightEdge : leftEdge;

                _hasCommittedPatrolEdge = true;
            }

            // Force patrol target to remain committed
            xEdge = _committedPatrolEdgeX;

            // Swap only when the committed target edge is actually reached
            if (HasReachedCommittedPatrolEdge(pb))
            {
                _committedPatrolEdgeX =
                    Mathf.Abs(_committedPatrolEdgeX - leftEdge) < 0.01f
                    ? rightEdge
                    : leftEdge;

                xEdge = _committedPatrolEdgeX;

                if (activesMoveCoroutine != null)
                    StopMoveTowardCoroutine();
            }
        }

        protected bool HasReachedCommittedPatrolEdge(Bounds pb)
        {
            Collider2D support = null;

            if (NormalCollider != null && NormalCollider.enabled)
                support = NormalCollider;
            else if (collider2 != null && collider2.enabled)
                support = collider2;

            if (support == null)
                return false;

            Bounds eb = support.bounds;

            bool targetLeft = Mathf.Abs(_committedPatrolEdgeX - pb.min.x) < 0.01f;

            if (targetLeft)
                return Mathf.Abs(eb.min.x - pb.min.x) <= patrolEdgeArriveThreshold;

            return Mathf.Abs(eb.max.x - pb.max.x) <= patrolEdgeArriveThreshold;
        }

    }
}
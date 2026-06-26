using System.Collections;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Tools;
using UnityEngine;
namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Hivernox boss AI.
    /// Inherits from your existing Enemy class, so it keeps health bar, death flow,
    /// platform clamping, stun support, knockback, EnemyMgr notification, and boss flags.
    /// </summary>
    public class HivernoxBoss : Enemy
    {
        // Boss: may intentionally body-ram the Warrior, so the anti-penetration guard stays off here.
        protected override bool AllowWarriorBodyPenetrationGuard => false;

        private const string ShieldLaserLayerName = "Shield Laser";
        [Header("Hivernox State")]
        [SerializeField] private HivernoxState state = HivernoxState.Idle;
        [SerializeField] private bool activateOnStart = false;
        [SerializeField] private bool debugStateChanges = false;
        [Header("Detection")]
        [SerializeField] private float detectionRange = 9f;
        [SerializeField] private bool requireSamePlatformForIceAttack = false;
        [Header("Ice Projectile Attack")]
        [SerializeField] private GameObject iceProjectilePrefab;
        [SerializeField] private Transform projectileOrigin;
        [SerializeField] private float iceAttackRange = 8f;
        [SerializeField] private float minIceAttackDistance = 1.25f;
        [SerializeField] private float iceAttackCooldown = 2.2f;
        [SerializeField] private float iceAttackAnimationSeconds = 0.85f;
        [SerializeField] private float autoFireProjectileDelay = 0.28f;
        [SerializeField] private bool autoFireIfNoAnimationEvent = true;
        [SerializeField] private bool aimProjectileAtWarrior = true;
        [SerializeField] private bool horizontalProjectileOnly = true;
        [SerializeField] private float projectileSpeed = 8f;
        [SerializeField] private float projectileLifetime = 4f;
        [SerializeField] private float warriorFreezeSeconds = 5f;
        [SerializeField] private float iceProjectileDamage = 0f;
        [SerializeField] private LayerMask projectileObstacleMask;
        [Header("Ice Projectile Visual Alignment")]
        [Tooltip("ON = projectileOrigin marks the BACK/TAIL of the visual projectile. OFF = projectileOrigin marks the prefab pivot/center.")]
        [SerializeField] private bool alignProjectileTailToOrigin = true;
        [Tooltip("Distance from the prefab pivot/center to the BACK/TAIL of the projectile visual. Increase this until the bolt tail sits exactly on projectileOrigin.")]
        [SerializeField, Min(0f)] private float projectilePivotToTailDistance = 0.55f;
        [Tooltip("Draws the tail-to-center offset in Scene view when the projectile is fired.")]
        [SerializeField] private bool debugProjectileAlignment = false;
        [Header("Freeze Finisher")]
        [SerializeField] private float moveToFrozenWarriorSpeed = 4.2f;

        [SerializeField] private int iceBreakerDamage = 30;
        [SerializeField] private float iceBreakerKnockbackVelocity = 5.5f;
        [SerializeField] private float iceBreakerStunSeconds = 0.25f;
        [SerializeField] private float iceBreakerAnimationSeconds = 0.95f;
        [Header("Freeze Finisher - IceBreaker Punch Repulse")]
        [Tooltip("ON = IceBreakerAttackRoutine uses the same smooth platform repulse style as FallingStone instead of Rigidbody velocity knockback.")]
        [SerializeField] private bool useIceBreakerPunchRepulse = true;
        [SerializeField] private float iceBreakerPunchRepulseDistance = 2.25f;
        [SerializeField] private float iceBreakerPunchRepulseDuration = 0.18f;
        [SerializeField] private float iceBreakerPunchRepulseControlLock = 0.25f;
        [Header("Freeze Finisher - Warrior Hit Box Contact")]
        [Tooltip("Use the WarriorHitbox child layer only. Leave empty to auto-use the Unity layer named 'Hit Box'.")]
        [SerializeField] private LayerMask frozenWarriorHitBoxLayer;
        [Tooltip("The Hivernox body collider that must be checked against the WarriorHitbox. Leave empty to use collider2 / first non-trigger collider.")]
        [SerializeField] private Collider2D hivernoxBodyColliderForFrozenContact;
        [Tooltip("Small safety distance before the Hivernox body collider touches the WarriorHitbox.")]
        [SerializeField, Min(0f)] private float frozenHitBoxContactSkin = 0.02f;
        [Tooltip("ON = when Hivernox touches the frozen WarriorHitbox during IceBreakerAttack, resolve the hit and retreat immediately.")]
        [SerializeField] private bool retreatOnFrozenWarriorHitBoxContact = true;
        [Header("Freeze Finisher - Warrior Shield Laser Contact")]
        [Tooltip("ON = a Warrior shield collider on the Unity layer named 'Shield Laser' stops Hivernox before WarriorHitbox can be reached.")]
        [SerializeField] private bool shieldLaserBlocksIceBreaker = true;
        [Tooltip("Extra safety distance around the Shield Laser collider when Hivernox is moving toward the frozen Warrior.")]
        [SerializeField, Min(0f)] private float shieldLaserContactSkin = 0.03f;
        [Tooltip("ON = when Hivernox touches Shield Laser during Attack2/IceBreaker, he retreats without damaging or breaking freeze.")]
        [SerializeField] private bool retreatWhenIceBreakerHitsShieldLaser = true;
        [Header("Counterattack / Hand Smash")]
        [SerializeField] private bool enableCounterOnDirectHit = true;
        [SerializeField] private float counterReactionRange = 1.85f;
        [SerializeField] private float counterCooldown = 1.1f;
        [SerializeField] private int handSmashDamage = 16;
        [SerializeField] private float handSmashHitRange = 1.9f;
        [SerializeField] private float handSmashKnockbackVelocity = 6.5f;
        [SerializeField] private float handSmashStunSeconds = 0.22f;
        [SerializeField] private float handSmashAnimationSeconds = 0.72f;
        [SerializeField] private float autoHandSmashHitDelay = 0.25f;
        [SerializeField] private bool autoResolveHandSmashIfNoAnimationEvent = false;
        [SerializeField] private float dashReturnDamage = 5f;
        [Header("Retreat / Cooldown")]
        [SerializeField] private Transform[] safeRetreatPoints;
        [SerializeField] private float retreatSpeed = 4f;
        [SerializeField] private float retreatArriveDistance = 0.12f;
        [SerializeField] private float maxRetreatSeconds = 1.4f;
        [SerializeField] private float recoveryCooldownSeconds = 0.9f;
        [Header("Animation")]
        [Tooltip("Optional. Leave empty if your Animator does not have an int parameter for boss state.")]
        [SerializeField] private string stateIntParameter = "";
        [Header("Impact FX")]
        [Tooltip("Dedicated prefab for AttackAnimation2Display / IceBreaker impact. Assign vfx_Impact_hivernox.prefab here.")]
        [SerializeField] private GameObject attack2ImpactFxPrefab;
        [Tooltip("Optional exact spawn point for the Attack2 impact FX. Leave empty to spawn on the WarriorHitbox center.")]
        [SerializeField] private Transform attack2ImpactFxPoint;
        [Tooltip("ON = Attack2 impact FX spawns at the WarriorHitbox center. OFF = use Warrior collider/transform fallback.")]
        [SerializeField] private bool attack2ImpactFxUseWarriorHitBoxCenter = true;
        [Tooltip("Optional world offset for the Attack2 impact FX. Use Y > 0 if the FX must appear slightly above the hitbox.")]
        [SerializeField] private Vector3 attack2ImpactFxWorldOffset = Vector3.zero;
        [Tooltip("Destroy spawned Attack2 impact FX after this many seconds. Set <= 0 if the prefab destroys itself.")]
        [SerializeField, Min(0f)] private float attack2ImpactFxLifetime = 2f;
        [Tooltip("ON = one Attack2 impact FX per IceBreaker attack.")]
        [SerializeField] private bool attack2ImpactFxOnlyOncePerAttack = true;
        [Tooltip("Old field kept as fallback. Prefer assigning Attack 2 Impact Fx Prefab above.")]
        [SerializeField] private GameObject iceBreakerHitFxPrefab;
        [SerializeField] private GameObject handSmashHitFxPrefab;
        [Header("Hand Smash SFX")]
        [Tooltip("Assign impact-487673.mp3 here. Plays only when AE_HandSmashHit resolves a real hand-smash impact.")]
        [SerializeField] private AudioClip handSmashHitClip;
        [SerializeField, Range(0f, 1f)] private float handSmashHitVolume = 0.95f;
        [SerializeField] private Vector2 handSmashHitPitchRange = new Vector2(0.96f, 1.04f);
        [Tooltip("0 = 2D sound. 1 = 3D sound from Hivernox/Warrior impact position.")]
        [SerializeField, Range(0f, 1f)] private float handSmashHitSpatialBlend = 0f;
        [SerializeField, Min(0f)] private float handSmashHitMaxDistance = 20f;
        [SerializeField] private bool handSmashHitPlayOncePerFrame = true;
        private Coroutine _actionRoutine;
        private bool _bossActivated;
        private bool _projectileFiredThisAttack;
        private bool _iceBreakerHitResolved;
        private bool _attack2ImpactFxPlayed;
        private bool _handSmashHitResolved;
        private int _lastHandSmashHitSfxFrame = -1;
        private float _nextIceAttackTime = -999f;
        private float _nextCounterTime = -999f;
        private Vector3 _homePosition;
        private Warrior _finisherTarget;
        public HivernoxState State => state;
        public bool IsBossActivated => _bossActivated;
        protected override void Start()
        {
            totalFramesInAnimation = 18;
            base.Start();
            // Terrestrial boss: pinned to its platform on both axes. It can never take off
            // (Y-lock) nor be shoved off an extremity (horizontal span clamp), whatever the force.
            groundBound = true;
            SetEnemyType(EnemyType.Hivernox);
            SetBoss(true, "Hivernox");
            _homePosition = transform.position;
            CanMove = true;
            if (activateOnStart)
                ActivateBoss();
            else
                SetState(HivernoxState.Idle);
        }
        protected override void Update()
        {
            if (StopMovingWhenWarriorDie)
                return;
            base.Update();
            if (IsDeadOrDying || state == HivernoxState.Dead)
                return;
            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null || warrior.IsDeadOrDying)
                return;
            if (!_bossActivated)
            {
                if (GetHorizontalDistanceTo(warrior.transform) <= detectionRange)
                    ActivateBoss();
                else
                    return;
            }
            if (_actionRoutine != null)
                return;
            if (IsStunned)
            {
                StopMoveTowardCoroutine();
                WaitAnimationDisplay();
                return;
            }
            if (warrior.IsFrozenByHivernox)
            {
                BeginFreezeFinisher(warrior);
                return;
            }
            SetState(HivernoxState.DetectWarrior);
            FaceWarrior();
            float distance = GetHorizontalDistanceTo(warrior.transform);
            bool samePlatformOk = !requireSamePlatformForIceAttack || IsWarriorOnSamePlatform(warrior);
            if (samePlatformOk &&
               distance <= iceAttackRange &&
               distance >= minIceAttackDistance &&
               Time.time >= _nextIceAttackTime)
            {
                BeginIceAttack();
                return;
            }
            WaitAnimationDisplay();
        }
        public void ActivateBoss()
        {
            if (_bossActivated || IsDeadOrDying)
                return;
            _bossActivated = true;
            SetState(HivernoxState.DetectWarrior);
        }
        private void BeginIceAttack()
        {
            StartExclusiveRoutine(IceAttackRoutine());
        }
        private IEnumerator IceAttackRoutine()
        {
            SetState(HivernoxState.IceAttack);
            StopMoveTowardCoroutine();
            FaceWarrior();
            CanMove = false;
            _projectileFiredThisAttack = false;
            // Uses your existing CharacterController animation-display method.
            // This sets isAttacking = true and clears isAttacking2/isAttacking3/etc.
            AttackAnimationDisplay();
            if (autoFireIfNoAnimationEvent)
            {
                yield return new WaitForSeconds(autoFireProjectileDelay);
                if (!_projectileFiredThisAttack)
                    FireIceProjectile();
                float remaining = Mathf.Max(0f, iceAttackAnimationSeconds - autoFireProjectileDelay);
                yield return new WaitForSeconds(remaining);
            }
            else
            {
                yield return new WaitForSeconds(iceAttackAnimationSeconds);
            }
            WaitAnimationDisplay();
            CanMove = true;
            _nextIceAttackTime = Time.time + iceAttackCooldown;
            _actionRoutine = null;
            if (state != HivernoxState.Dead)
                StartExclusiveRoutine(CooldownRoutine());
        }
        /// <summary>
        /// Put this animation event on the exact frame where the ice/light projectile must leave the hand.
        /// </summary>
        public void AE_FireIceProjectile()
        {
            FireIceProjectile();
        }
        public void FireIceProjectile()
        {
            if (_projectileFiredThisAttack)
                return;
            if (iceProjectilePrefab == null || projectileOrigin == null)
                return;
            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null)
                return;
            // Face the Warrior first, then lock the projectile direction to the final facing state.
            // This prevents the projectileOrigin/hand position from reversing the shot direction.
            FaceWarrior();
            RefreshFacingFlags();
            Vector2 direction = GetProjectileDirection(warrior);
            Vector3 spawnPosition = GetProjectileSpawnPosition(direction);
            GameObject projectileObject = Instantiate(
               iceProjectilePrefab,
               spawnPosition,
               Quaternion.identity);
            PrepareProjectileObject(projectileObject, direction);
            HivernoxIceProjectile projectile = projectileObject.GetComponent<HivernoxIceProjectile>();
            if (projectile == null)
                projectile = projectileObject.AddComponent<HivernoxIceProjectile>();
            projectile.Initialize(
               owner: this,
               direction: direction,
               speed: projectileSpeed,
               freezeSeconds: warriorFreezeSeconds,
               projectileDamage: iceProjectileDamage,
               lifetime: projectileLifetime,
               obstacleMask: projectileObstacleMask);
            IgnoreOwnerCollisions(projectileObject);
            _projectileFiredThisAttack = true;
        }
        private Vector2 GetProjectileDirection(Warrior warrior)
        {
            // The projectile must never be allowed to travel opposite to Hivernox's visual facing.
            // Facing left  => X direction is always negative.
            // Facing right => X direction is always positive.
            Vector2 facingDirection = GetFacingDirection();
            if (horizontalProjectileOnly)
                return facingDirection;
            Vector2 direction = facingDirection;
            // Optional vertical aim: Y may aim toward the Warrior, but X stays locked to facing.
            if (aimProjectileAtWarrior && warrior != null && projectileOrigin != null)
            {
                Vector2 targetPoint = warrior.collider2 != null
                    ? (Vector2)warrior.collider2.bounds.center
                    : (Vector2)warrior.transform.position;
                float yDirection = targetPoint.y - projectileOrigin.position.y;
                direction = new Vector2(facingDirection.x, yDirection);
            }
            // Final safety guard: never permit opposite X direction.
            if (Mathf.Abs(direction.x) < 0.001f ||
                Mathf.Sign(direction.x) != Mathf.Sign(facingDirection.x))
            {
                direction.x = facingDirection.x;
            }
            if (direction.sqrMagnitude < 0.001f)
                direction = facingDirection;
            return direction.normalized;
        }
        private Vector3 GetProjectileSpawnPosition(Vector2 direction)
        {
            if (projectileOrigin == null)
                return transform.position;
            Vector2 safeDirection = direction.sqrMagnitude > 0.001f
               ? direction.normalized
               : GetFacingDirection();
            Vector3 originPosition = projectileOrigin.position;
            // Your FX_LightningBolt_Bullet_Hivernox pivot is in the middle of the bolt.
            // If we spawn the pivot directly on projectileOrigin, the bolt center sits on the hand.
            // Move the pivot forward so the BACK/TAIL of the bolt starts exactly at projectileOrigin.
            if (!alignProjectileTailToOrigin || projectilePivotToTailDistance <= 0f)
                return originPosition;
            Vector3 spawnPosition = originPosition + (Vector3)(safeDirection * projectilePivotToTailDistance);
            if (debugProjectileAlignment)
            {
                Debug.DrawLine(originPosition, spawnPosition, Color.cyan, 1.25f);
                Debug.DrawRay(originPosition, safeDirection * 0.25f, Color.yellow, 1.25f);
            }
            return spawnPosition;
        }
        private void PrepareProjectileObject(GameObject projectileObject, Vector2 direction)
        {
            if (projectileObject == null)
                return;
            projectileObject.transform.right = direction;
            Rigidbody2D rb = projectileObject.GetComponent<Rigidbody2D>();
            if (rb == null)
                rb = projectileObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Collider2D col = projectileObject.GetComponent<Collider2D>();
            if (col == null)
            {
                CircleCollider2D circle = projectileObject.AddComponent<CircleCollider2D>();
                circle.radius = 0.18f;
                circle.isTrigger = true;
            }
            else
            {
                col.isTrigger = true;
            }
        }
        private void IgnoreOwnerCollisions(GameObject projectileObject)
        {
            if (projectileObject == null)
                return;
            Collider2D[] projectileColliders = projectileObject.GetComponentsInChildren<Collider2D>(true);
            Collider2D[] ownerColliders = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < projectileColliders.Length; i++)
            {
                if (projectileColliders[i] == null)
                    continue;
                for (int j = 0; j < ownerColliders.Length; j++)
                {
                    if (ownerColliders[j] == null)
                        continue;
                    Physics2D.IgnoreCollision(projectileColliders[i], ownerColliders[j], true);
                }
            }
        }
        public void NotifyWarriorFrozen(Warrior warrior)
        {
            if (warrior == null || IsDeadOrDying)
                return;
            if (!_bossActivated)
                ActivateBoss();
            BeginFreezeFinisher(warrior);
        }
        private void BeginFreezeFinisher(Warrior warrior)
        {
            if (warrior == null || !warrior.IsFrozenByHivernox)
                return;
            _finisherTarget = warrior;
            StartExclusiveRoutine(FreezeFinisherRoutine(warrior));
        }
        private IEnumerator FreezeFinisherRoutine(Warrior warrior)
        {
            SetState(HivernoxState.FreezeWarrior);
            StopHivernoxMotionNow();
            CanMove = false;
            FaceWarrior();

            yield return new WaitForSecondsRealtime(0.08f);

            if (warrior == null || !warrior.IsFrozenByHivernox)
            {
                FinishIceBreakerSequenceAndRetreat();
                yield break;
            }

            SetState(HivernoxState.MoveToWarrior);

            // No chaseTimer anymore.
            // As long as the Warrior is frozen, Hivernox keeps trying to reach him.
            while (warrior != null && warrior.IsFrozenByHivernox)
            {
                FaceWarrior();

                if (shieldLaserBlocksIceBreaker && IsTouchingWarriorShieldLaserLayer(warrior))
                {
                    AbortIceBreakerBecauseShieldLaserBlocked(warrior);
                    yield break;
                }

                // Attack2 starts only when Hivernox body actually touches the WarriorHitbox layer.
                if (retreatOnFrozenWarriorHitBoxContact && IsTouchingWarriorHitBoxLayer(warrior))
                {
                    StopHivernoxMotionNow();
                    yield return StartCoroutine(IceBreakerAttackRoutine(warrior));
                    yield break;
                }

                float targetX = warrior.transform.position.x;

                float nextX = Mathf.MoveTowards(
                    transform.position.x,
                    targetX,
                    moveToFrozenWarriorSpeed * Time.deltaTime);

                nextX = ClampToCurrentPlatform(nextX);

                if (shieldLaserBlocksIceBreaker && WouldTouchWarriorShieldLaserLayerAtX(warrior, nextX))
                {
                    AbortIceBreakerBecauseShieldLaserBlocked(warrior);
                    yield break;
                }

                if (retreatOnFrozenWarriorHitBoxContact && WouldTouchWarriorHitBoxLayerAtX(warrior, nextX))
                {
                    MoveHivernoxHorizontallyWithLocomotionAnimation(nextX, useRunAnimation: true);
                    StopHivernoxMotionNow();

                    yield return StartCoroutine(IceBreakerAttackRoutine(warrior));
                    yield break;
                }

                MoveHivernoxHorizontallyWithLocomotionAnimation(nextX, useRunAnimation: true);

                yield return null;
            }

            // Warrior is no longer frozen, died, or became invalid before contact.
            // Important: this clears _actionRoutine through the retreat routine.
            FinishIceBreakerSequenceAndRetreat();
        }

        private IEnumerator IceBreakerAttackRoutine(Warrior warrior)
        {
            SetState(HivernoxState.IceBreakerAttack);
            FaceWarrior();
            StopHivernoxMotionNow();
            CanMove = false;

            _iceBreakerHitResolved = false;
            _attack2ImpactFxPlayed = false;

            // AttackAnimation2Display() starts Attack2.
            // AE_IceBreakerHit() must be placed as an Animation Event on the real impact frame.
            AttackAnimation2Display();

            float timer = 0f;

            while (timer < iceBreakerAnimationSeconds)
            {
                if (warrior == null)
                    break;

                if (shieldLaserBlocksIceBreaker && IsTouchingWarriorShieldLaserLayer(warrior))
                {
                    AbortIceBreakerBecauseShieldLaserBlocked(warrior);
                    yield break;
                }

                // The animation event already fired. Stop waiting and retreat immediately.
                if (_iceBreakerHitResolved)
                    break;

                // If the freeze expires before the animation impact frame, cancel the punch result.
                if (!warrior.IsFrozenByHivernox)
                    break;

                timer += Time.deltaTime;
                yield return null;
            }

            FinishIceBreakerSequenceAndRetreat();
        }

        /// <summary>
        /// Animation Event for AttackAnimation2Display's real impact frame.
        /// This is the only place that applies IceBreaker damage, freeze break,
        /// impact FX, and the smooth punch repulse.
        /// </summary>
        public void AE_IceBreakerHit()
        {
            if (_iceBreakerHitResolved)
                return;

            // Ignore late animation events after the attack was aborted and Hivernox started retreating.
            if (state != HivernoxState.IceBreakerAttack)
                return;

            Warrior warrior = _finisherTarget != null
               ? _finisherTarget
               : (GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null);

            if (warrior == null)
                return;

            if (shieldLaserBlocksIceBreaker && warrior.HasActiveShieldLaserProtection())
            {
                _iceBreakerHitResolved = true;
                warrior.TryBlockHivernoxAttackWithShieldLaser((Vector2)transform.position);
                return;
            }

            _iceBreakerHitResolved = true;

            FaceWarrior();

            TryPlayAttack2ImpactFx(warrior, requireTouchOrResolved: false);

            warrior.BreakHivernoxFreeze();

            float stunForDamageCall = useIceBreakerPunchRepulse ? 0f : iceBreakerStunSeconds;
            float velocityKnockbackForDamageCall = useIceBreakerPunchRepulse ? 0f : iceBreakerKnockbackVelocity;

            warrior.TryReceiveHivernoxDamage(
                source: this,
                damage: iceBreakerDamage,
                canBeBlockedByShield: false,
                stunSeconds: stunForDamageCall,
                knockbackVelocity: velocityKnockbackForDamageCall);

            if (useIceBreakerPunchRepulse)
            {
                warrior.TryRepulseFromPlatformStoneImpact(
                    warrior.CurrentplatForm,
                    (Vector2)transform.position,
                    iceBreakerPunchRepulseDistance,
                    iceBreakerPunchRepulseDuration,
                    iceBreakerPunchRepulseControlLock
                );
            }
        }

        private void FinishIceBreakerSequenceAndRetreat()
        {
            StopHivernoxMotionNow();
            WaitAnimationDisplay();
            CanMove = false;
            _actionRoutine = null;
            StartExclusiveRoutine(RetreatRoutine());
        }

        /// <summary>
        /// Optional old visual-only Animation Event.
        /// If AE_IceBreakerHit is already on the impact frame, you do not need this event.
        /// It is kept so old animation clips do not break if they still call it.
        /// </summary>
        public void AE_Attack2ImpactFx()
        {
            // Visual only. AE_IceBreakerHit() now also plays the impact FX.
            // _attack2ImpactFxPlayed prevents duplicate FX if both events still exist.
            Warrior warrior = _finisherTarget != null
                ? _finisherTarget
                : (GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null);
            TryPlayAttack2ImpactFx(warrior, requireTouchOrResolved: false);
        }
        private bool TryPlayAttack2ImpactFx(Warrior warrior, bool requireTouchOrResolved)
        {
            if (attack2ImpactFxOnlyOncePerAttack && _attack2ImpactFxPlayed)
                return false;
            if (state != HivernoxState.IceBreakerAttack)
                return false;
            if (warrior == null)
                return false;
            GameObject prefab = attack2ImpactFxPrefab != null
               ? attack2ImpactFxPrefab
               : iceBreakerHitFxPrefab;
            if (prefab == null)
                return false;
            if (requireTouchOrResolved &&
               !_iceBreakerHitResolved &&
               !IsTouchingWarriorHitBoxLayer(warrior))
            {
                return false;
            }
            _attack2ImpactFxPlayed = true;
            SpawnFx(prefab, GetAttack2ImpactFxPosition(warrior), attack2ImpactFxLifetime);
            PlayHandSmashHitSfx(warrior.transform.position);
            return true;
        }
        private Vector3 GetAttack2ImpactFxPosition(Warrior warrior)
        {
            if (attack2ImpactFxPoint != null)
                return attack2ImpactFxPoint.position + attack2ImpactFxWorldOffset;
            if (warrior == null)
                return transform.position + attack2ImpactFxWorldOffset;
            if (attack2ImpactFxUseWarriorHitBoxCenter)
            {
                Collider2D warriorHitBox = GetFirstWarriorHitBoxCollider(warrior);
                if (warriorHitBox != null)
                    return warriorHitBox.bounds.center + attack2ImpactFxWorldOffset;
            }
            if (warrior.collider2 != null)
                return warrior.collider2.bounds.center + attack2ImpactFxWorldOffset;
            return warrior.transform.position + attack2ImpactFxWorldOffset;
        }
        private Collider2D GetFirstWarriorHitBoxCollider(Warrior warrior)
        {
            if (warrior == null)
                return null;
            int mask = GetFrozenWarriorHitBoxMaskValue();
            if (mask == 0)
                return null;
            Collider2D[] warriorColliders = warrior.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < warriorColliders.Length; i++)
            {
                Collider2D candidate = warriorColliders[i];
                if (IsValidWarriorHitBoxCollider(candidate, warrior, mask))
                    return candidate;
            }
            return null;
        }
        protected override void OnDamaged(float damage, bool killed)
        {
            base.OnDamaged(damage, killed);
            if (killed || IsDeadOrDying || !enableCounterOnDirectHit)
                return;
            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null || warrior.IsDeadOrDying)
                return;
            float distance = GetHorizontalDistanceTo(warrior.transform);
            if (distance > counterReactionRange)
                return;
            if (!IsWarriorOnSamePlatform(warrior))
                return;
            if (dashReturnDamage > 0f && warrior.IsDodging)
                warrior.ApplyHivernoxDashReturnDamage(dashReturnDamage, transform.position);
            if (Time.time < _nextCounterTime)
                return;
            if (state == HivernoxState.FreezeWarrior ||
               state == HivernoxState.MoveToWarrior ||
               state == HivernoxState.IceBreakerAttack ||
               state == HivernoxState.Retreat ||
               state == HivernoxState.Dead)
                return;
            BeginCounterAttack();
        }
        private void BeginCounterAttack()
        {
            _nextCounterTime = Time.time + counterCooldown;
            StartExclusiveRoutine(CounterAttackRoutine());
        }
        private IEnumerator CounterAttackRoutine()
        {
            SetState(HivernoxState.CounterAttack);
            StopMoveTowardCoroutine();
            FaceWarrior();
            CanMove = false;
            yield return null;
            SetState(HivernoxState.HandSmash);
            _handSmashHitResolved = false;
            // Uses your existing CharacterController animation-display method.
            // This sets isAttacking3 = true.
            AttackAnimation3Display();
            if (autoResolveHandSmashIfNoAnimationEvent)
            {
                yield return new WaitForSeconds(autoHandSmashHitDelay);
                if (!_handSmashHitResolved)
                    AE_HandSmashHit();
                float remaining = Mathf.Max(0f, handSmashAnimationSeconds - autoHandSmashHitDelay);
                yield return new WaitForSeconds(remaining);
            }
            else
            {
                yield return new WaitForSeconds(handSmashAnimationSeconds);
            }
            AE_EndHandSmash();
        }
        /// <summary>
        /// Put this animation event on the exact hand-impact frame.
        /// </summary>
        public void AE_HandSmashHit()
        {
            if (_handSmashHitResolved)
                return;
            _handSmashHitResolved = true;
            if (!CanDealDamageNow())
                return;
            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null || warrior.IsDeadOrDying)
                return;
            if (GetHorizontalDistanceTo(warrior.transform) > handSmashHitRange)
                return;
            if (!IsWarriorOnSamePlatform(warrior))
                return;
            if (!IsWarriorInFront(warrior.transform))
                return;
            warrior.TryReceiveHivernoxDamage(
               source: this,
               damage: handSmashDamage,
               canBeBlockedByShield: true,
               stunSeconds: handSmashStunSeconds,
               knockbackVelocity: handSmashKnockbackVelocity);
           
            SpawnFx(handSmashHitFxPrefab, warrior.transform.position);
        }
        private void PlayHandSmashHitSfx(Vector3 position)
        {
            if (handSmashHitClip == null)
                return;

            if (handSmashHitPlayOncePerFrame && _lastHandSmashHitSfxFrame == Time.frameCount)
                return;

            _lastHandSmashHitSfxFrame = Time.frameCount;

            OneShotAudio.Play(
               handSmashHitClip,
               position,
               handSmashHitVolume,
               handSmashHitPitchRange,
               handSmashHitSpatialBlend,
               handSmashHitMaxDistance);
        }
        public void AE_EndHandSmash()
        {
            WaitAnimationDisplay();
            CanMove = true;
            _actionRoutine = null;
            StartExclusiveRoutine(CooldownRoutine());
        }
        private IEnumerator RetreatRoutine()
        {
            SetState(HivernoxState.Retreat);
            CanMove = false;
            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            Vector3 retreatTarget = GetBestRetreatPosition(warrior);
            float timer = 0f;
            while (Mathf.Abs(transform.position.x - retreatTarget.x) > retreatArriveDistance &&
                   timer < maxRetreatSeconds)
            {
                FaceAwayFrom(warrior);
                float nextX = Mathf.MoveTowards(
                   transform.position.x,
                   retreatTarget.x,
                   retreatSpeed * Time.deltaTime);
                // Movement + animation are linked here.
                // If Hivernox moves during retreat, he must walk.
                MoveHivernoxHorizontallyWithLocomotionAnimation(nextX, useRunAnimation: false);
                timer += Time.deltaTime;
                yield return null;
            }
            StopHivernoxMotionNow();
            WaitAnimationDisplay();
            CanMove = true;
            _actionRoutine = null;
            StartExclusiveRoutine(CooldownRoutine());
        }
        private IEnumerator CooldownRoutine()
        {
            SetState(HivernoxState.Cooldown);
            StopMoveTowardCoroutine();
            WaitAnimationDisplay();
            CanMove = false;
            yield return new WaitForSeconds(recoveryCooldownSeconds);
            CanMove = true;
            _finisherTarget = null;
            SetState(HivernoxState.DetectWarrior);
            _actionRoutine = null;
        }
        private Vector3 GetBestRetreatPosition(Warrior warrior)
        {
            float currentX = transform.position.x;
            // If no warrior, go back to home position.
            if (warrior == null)
            {
                return new Vector3(
                    ClampToCurrentPlatform(_homePosition.x),
                    transform.position.y,
                    transform.position.z);
            }
            float warriorX = warrior.transform.position.x;
            // TRUE  = Hivernox is on the left side of the Warrior.
            // FALSE = Hivernox is on the right side of the Warrior.
            bool hivernoxIsLeftOfWarrior = currentX < warriorX;
            // ------------------------------------------------------------
            // 1. First priority: use safe retreat points from the Inspector.
            //    But only choose points on the SAME SIDE as Hivernox.
            // ------------------------------------------------------------
            if (safeRetreatPoints != null && safeRetreatPoints.Length > 0)
            {
                Transform best = null;
                float bestScore = float.MinValue;
                for (int i = 0; i < safeRetreatPoints.Length; i++)
                {
                    Transform candidate = safeRetreatPoints[i];
                    if (candidate == null)
                        continue;
                    float candidateX = candidate.position.x;
                    // Keep only retreat points on Hivernox's side.
                    bool candidateIsLeftOfWarrior = candidateX < warriorX;
                    if (candidateIsLeftOfWarrior != hivernoxIsLeftOfWarrior)
                        continue;
                    // Among same-side points, choose the one farthest from the Warrior.
                    float score = Mathf.Abs(candidateX - warriorX);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
                if (best != null)
                {
                    return new Vector3(
                        ClampToCurrentPlatform(best.position.x),
                        transform.position.y,
                        transform.position.z);
                }
            }
            // ------------------------------------------------------------
            // 2. Second priority: use the platform side.
            //    If Hivernox is left of the Warrior, retreat to left edge.
            //    If Hivernox is right of the Warrior, retreat to right edge.
            // ------------------------------------------------------------
            if (CurrentplatForm != null && CurrentplatForm.platformCollider != null)
            {
                Bounds platformBounds = CurrentplatForm.platformCollider.bounds;
                float leftX = platformBounds.min.x + 0.7f;
                float rightX = platformBounds.max.x - 0.7f;
                float targetX = hivernoxIsLeftOfWarrior ? leftX : rightX;
                return new Vector3(
                   ClampToCurrentPlatform(targetX),
                   transform.position.y,
                   transform.position.z);
            }
            // ------------------------------------------------------------
            // 3. Last fallback: go back to home position.
            // ------------------------------------------------------------
            return new Vector3(
                ClampToCurrentPlatform(_homePosition.x),
                transform.position.y,
                transform.position.z);
        }
        private void StopHivernoxMotionNow()
        {
            StopMoveTowardCoroutine();
            if (rigidbody2 != null)
                rigidbody2.linearVelocity = Vector2.zero;
        }
        private bool MoveHivernoxHorizontallyWithLocomotionAnimation(float nextX, bool useRunAnimation)
        {
            nextX = ClampToCurrentPlatform(nextX);
            float currentX = transform.position.x;
            float deltaX = nextX - currentX;
            // No real X movement. Do NOT call WaitAnimationDisplay() here.
            // This helper is used inside movement loops, so the owning routine
            // decides when Hivernox is truly stopped.
            if (Mathf.Abs(deltaX) <= 0.0005f)
            {
                StopHivernoxMotionNow();
                return false;
            }
            // While the transform moves, force the correct locomotion animation.
            if (useRunAnimation)
                RunAnimationDisplay();
            else
                WalkAnimationDisplay();
            transform.position = new Vector3(nextX, transform.position.y, transform.position.z);
            return true;
        }
        private void AbortIceBreakerBecauseShieldLaserBlocked(Warrior warrior)
        {
            if (warrior != null)
                warrior.TryBlockHivernoxAttackWithShieldLaser((Vector2)transform.position);
            StopHivernoxMotionNow();
            WaitAnimationDisplay();
            CanMove = false;
            _iceBreakerHitResolved = true;
            _actionRoutine = null;
            if (retreatWhenIceBreakerHitsShieldLaser)
                StartExclusiveRoutine(RetreatRoutine());
            else
                StartExclusiveRoutine(CooldownRoutine());
        }
        private bool IsTouchingWarriorShieldLaserLayer(Warrior warrior)
        {
            Collider2D hivernoxCollider = GetHivernoxBodyColliderForFrozenContact();
            if (hivernoxCollider == null || warrior == null || !warrior.ShieldIsUp)
                return false;
            int mask = GetShieldLaserMaskValue();
            if (mask == 0)
                return false;
            Collider2D[] warriorColliders = warrior.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < warriorColliders.Length; i++)
            {
                Collider2D warriorCollider = warriorColliders[i];
                if (!IsValidWarriorShieldLaserCollider(warriorCollider, warrior, mask))
                    continue;
                ColliderDistance2D distance = hivernoxCollider.Distance(warriorCollider);
                if (distance.isOverlapped || distance.distance <= shieldLaserContactSkin)
                    return true;
            }
            return false;
        }
        private bool WouldTouchWarriorShieldLaserLayerAtX(Warrior warrior, float nextX)
        {
            Collider2D hivernoxCollider = GetHivernoxBodyColliderForFrozenContact();
            if (hivernoxCollider == null || warrior == null || !warrior.ShieldIsUp)
                return false;
            int mask = GetShieldLaserMaskValue();
            if (mask == 0)
                return false;
            Bounds movedHivernoxBounds = hivernoxCollider.bounds;
            movedHivernoxBounds.center += new Vector3(nextX - transform.position.x, 0f, 0f);
            movedHivernoxBounds.Expand(shieldLaserContactSkin * 2f);
            Collider2D[] warriorColliders = warrior.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < warriorColliders.Length; i++)
            {
                Collider2D warriorCollider = warriorColliders[i];
                if (!IsValidWarriorShieldLaserCollider(warriorCollider, warrior, mask))
                    continue;
                Bounds shieldBounds = warriorCollider.bounds;
                shieldBounds.Expand(shieldLaserContactSkin * 2f);
                if (movedHivernoxBounds.Intersects(shieldBounds))
                    return true;
            }
            return false;
        }
        private bool IsValidWarriorShieldLaserCollider(Collider2D candidate, Warrior warrior, int mask)
        {
            if (candidate == null || !candidate.enabled)
                return false;
            if ((mask & (1 << candidate.gameObject.layer)) == 0)
                return false;
            return candidate.GetComponentInParent<Warrior>() == warrior;
        }
        private int GetShieldLaserMaskValue()
        {
            int shieldLaserLayer = LayerMask.NameToLayer(ShieldLaserLayerName);
            return shieldLaserLayer >= 0 ? 1 << shieldLaserLayer : 0;
        }
        private bool IsTouchingWarriorHitBoxLayer(Warrior warrior)
        {
            Collider2D hivernoxCollider = GetHivernoxBodyColliderForFrozenContact();
            if (hivernoxCollider == null || warrior == null)
                return false;
            int mask = GetFrozenWarriorHitBoxMaskValue();
            if (mask == 0)
                return false;
            Collider2D[] warriorColliders = warrior.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < warriorColliders.Length; i++)
            {
                Collider2D warriorCollider = warriorColliders[i];
                if (!IsValidWarriorHitBoxCollider(warriorCollider, warrior, mask))
                    continue;
                ColliderDistance2D distance = hivernoxCollider.Distance(warriorCollider);
                if (distance.isOverlapped || distance.distance <= frozenHitBoxContactSkin)
                    return true;
            }
            return false;
        }
        private bool WouldTouchWarriorHitBoxLayerAtX(Warrior warrior, float nextX)
        {
            Collider2D hivernoxCollider = GetHivernoxBodyColliderForFrozenContact();
            if (hivernoxCollider == null || warrior == null)
                return false;
            int mask = GetFrozenWarriorHitBoxMaskValue();
            if (mask == 0)
                return false;
            Bounds movedHivernoxBounds = hivernoxCollider.bounds;
            movedHivernoxBounds.center += new Vector3(nextX - transform.position.x, 0f, 0f);
            movedHivernoxBounds.Expand(frozenHitBoxContactSkin * 2f);
            Collider2D[] warriorColliders = warrior.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < warriorColliders.Length; i++)
            {
                Collider2D warriorCollider = warriorColliders[i];
                if (!IsValidWarriorHitBoxCollider(warriorCollider, warrior, mask))
                    continue;
                Bounds warriorHitBoxBounds = warriorCollider.bounds;
                warriorHitBoxBounds.Expand(frozenHitBoxContactSkin * 2f);
                if (movedHivernoxBounds.Intersects(warriorHitBoxBounds))
                    return true;
            }
            return false;
        }
        private bool IsValidWarriorHitBoxCollider(Collider2D candidate, Warrior warrior, int mask)
        {
            if (candidate == null || !candidate.enabled)
                return false;
            if ((mask & (1 << candidate.gameObject.layer)) == 0)
                return false;
            return candidate.GetComponentInParent<Warrior>() == warrior;
        }
        private int GetFrozenWarriorHitBoxMaskValue()
        {
            if (frozenWarriorHitBoxLayer.value != 0)
                return frozenWarriorHitBoxLayer.value;
            int hitBoxLayer = LayerMask.NameToLayer("Hit Box");
            if (hitBoxLayer < 0)
                hitBoxLayer = LayerMask.NameToLayer("Hit Box");
            return hitBoxLayer >= 0 ? 1 << hitBoxLayer : 0;
        }
        private Collider2D GetHivernoxBodyColliderForFrozenContact()
        {
            if (hivernoxBodyColliderForFrozenContact != null && hivernoxBodyColliderForFrozenContact.enabled)
                return hivernoxBodyColliderForFrozenContact;
            if (collider2 != null && collider2.enabled)
                return collider2;
            Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D candidate = colliders[i];
                if (candidate != null && candidate.enabled && !candidate.isTrigger)
                    return candidate;
            }
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D candidate = colliders[i];
                if (candidate != null && candidate.enabled)
                    return candidate;
            }
            return null;
        }
        private void StartExclusiveRoutine(IEnumerator routine)
        {
            // Do not force WaitAnimationDisplay() when switching to a new action.
            // The new action decides whether it should run, walk, attack, or wait.
            StopActionRoutine(forceWaitAnimation: false);
            _actionRoutine = StartCoroutine(routine);
        }
        private void StopActionRoutine(bool forceWaitAnimation = true)
        {
            if (_actionRoutine != null)
            {
                StopCoroutine(_actionRoutine);
                _actionRoutine = null;
            }
            if (forceWaitAnimation)
                WaitAnimationDisplay();
        }
        private void SetState(HivernoxState nextState)
        {
            if (state == nextState)
                return;
            state = nextState;
            if (debugStateChanges)
                Debug.Log($"[Hivernox] State => {state}", this);
            if (animator != null && !string.IsNullOrWhiteSpace(stateIntParameter))
                animator.SetInteger(stateIntParameter, (int)state);
        }
        private void FaceWarrior()
        {
            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null)
                return;
            FlipCharacter(warrior.transform.position.x);
            RefreshFacingFlags();
        }
        private void FaceAwayFrom(Warrior warrior)
        {
            if (warrior == null)
                return;
            float targetX = transform.position.x < warrior.transform.position.x
               ? transform.position.x - 1f
               : transform.position.x + 1f;
            FlipCharacter(targetX);
            RefreshFacingFlags();
        }
        private Vector2 GetFacingDirection()
        {
            RefreshFacingFlags();
            return rightFacing ? Vector2.right : Vector2.left;
        }
        private float GetHorizontalDistanceTo(Transform other)
        {
            if (other == null)
                return float.MaxValue;
            return Mathf.Abs(GetTargetCenterX(other) - GetMyCenterX());
        }
        private bool IsWarriorOnSamePlatform(Warrior warrior)
        {
            if (warrior == null)
                return false;
            if (CurrentplatForm == null || warrior.CurrentplatForm == null)
                return true;
            return CurrentplatForm == warrior.CurrentplatForm;
        }
        private void SpawnFx(GameObject prefab, Vector3 position, float lifetime = 2f)
        {
            if (prefab == null)
                return;
            GameObject fx = Instantiate(prefab, position, Quaternion.identity);
            fx.SetActive(true);
            // Safety: some FX prefabs have Particle Systems with Play On Awake disabled.
            // This forces the impact to play when it is spawned.
            ParticleSystem[] particles = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] == null)
                    continue;
                particles[i].gameObject.SetActive(true);
                particles[i].Clear(true);
                particles[i].Play(true);
            }
            // Safety for animated sprite/VFX prefabs.
            Animator[] fxAnimators = fx.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < fxAnimators.Length; i++)
            {
                if (fxAnimators[i] == null)
                    continue;
                fxAnimators[i].gameObject.SetActive(true);
                fxAnimators[i].enabled = true;
                fxAnimators[i].speed = 1f;
                fxAnimators[i].Play(0, 0, 0f);
            }
            if (lifetime > 0f)
                Destroy(fx, lifetime);
        }
        protected override void OnDeath()
        {
            StopActionRoutine();
            SetState(HivernoxState.Dead);
            base.OnDeath();
        }
    }
}
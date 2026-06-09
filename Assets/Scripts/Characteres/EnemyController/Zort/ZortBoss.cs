using System.Collections;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Tools;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Zort — The Void-Rendered. Final boss of the LandOfFire / Redemption campaign.
    /// Inherits <see cref="Enemy"/>, so it reuses the shared health bar, hit blink,
    /// dissolve death, EnemyMgr boss-death → level-complete flow, stun support and
    /// platform handling. Boss identity is set via <see cref="Enemy.SetBoss"/> so
    /// EnemyMgr drives the final-death campaign progression automatically.
    ///
    /// Phases
    ///   Phase 1  "The Archivist"   100% → 60% HP
    ///   Phase 2  "The Reaper"       60% → 30% HP
    ///   Phase 3  "The Void"         30% →  0% HP  (desperation below 10%)
    /// </summary>
    public class ZortBoss : Enemy
    {
        public enum Phase { One, Two, Three }

        [Header("Zort - Data")]
        [SerializeField] private ZortAttackData attackData;

        [Header("Zort - Activation")]
        [Tooltip("Zort stays idle until the Warrior gets within this horizontal range, then begins fighting.")]
        [SerializeField] private float activationRange = 11f;
        [SerializeField] private bool activateOnStart = false;

        [Header("Phase thresholds (0-1)")]
        [SerializeField] private float phase2Threshold = 0.60f;
        [SerializeField] private float phase3Threshold = 0.30f;
        [SerializeField] private float desperationThreshold = 0.10f;

        [Header("Attack pacing")]
        [SerializeField] private float baseAttackCooldown = 2.2f;
        [SerializeField] private float openingObservationSeconds = 8f;

        [Header("Repeating mechanic cadence (seconds)")]
        [SerializeField] private float gravityWellInterval = 40f;
        [SerializeField] private float riftPulseInterval = 15f;
        [SerializeField] private float desperationNovaInterval = 20f;

        [Header("Scene references")]
        [SerializeField] private Transform arenaCenter;
        [SerializeField] private Transform projectileOrigin;
        [Tooltip("Phase 2 collapses platforms[0].")]
        [SerializeField] private Transform[] platforms;
        [SerializeField] private GameObject[] riftHazardSlots;
        [SerializeField] private Transform[] wraithSpawnPoints;

        [Header("Phase 3 bark")]
        [Tooltip("Logged on Phase 3 entry. The project has no boss-dialogue UI, so this is a developer-facing bark.")]
        [SerializeField] private string phase3Quote = "You were always in the record.";

        // ─── State ────────────────────────────────────────────────────────────────
        private Phase currentPhase = Phase.One;
        private bool _bossActivated;
        private bool _inTransition;
        private bool _invulnerable;

        private Coroutine _actionRoutine;
        private Coroutine _transitionRoutine;

        private float _nextAttackTime;
        private float _gravityWellTimer;
        private float _riftPulseTimer;
        private float _desperationNovaTimer;

        private float _damageReceivedDuringWell;

        private float HealthRatio => maxHealth <= 0f ? 0f : currentHealth / maxHealth;

        // ─────────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────────

        protected override void Start()
        {
            base.Start();

            SetBoss(true, "Zort");
            SetEnemyType(EnemyType.Zort);

            CanMove = true;
            _nextAttackTime = Time.time + openingObservationSeconds; // opening observation window
            _gravityWellTimer = gravityWellInterval;
            _riftPulseTimer = riftPulseInterval;
            _desperationNovaTimer = desperationNovaInterval;

            WaitAnimationDisplay();

            if (activateOnStart)
                _bossActivated = true;
        }

        // Zort drives all of its own movement, so it does NOT call base.Update():
        // the base platform-clamp/stick logic would fight aerial dives and teleports.
        protected override void Update()
        {
            if (IsDeadOrDying || _inTransition)
                return;

            Warrior warrior = GetWarrior();
            if (warrior == null || warrior.IsDeadOrDying)
                return;

            if (!_bossActivated)
            {
                if (GetHorizontalDistanceTo(warrior.transform) <= activationRange)
                    _bossActivated = true;
                else
                    return;
            }

            HandlePhaseTransitions(warrior);
            if (_inTransition)
                return;

            if (_actionRoutine != null)
                return;

            if (IsStunned)
            {
                StopMoveTowardCoroutine();
                WaitAnimationDisplay();
                return;
            }

            FaceWarrior(warrior);

            TickRepeatingMechanics(warrior);
            if (_actionRoutine != null)
                return;

            if (Time.time >= _nextAttackTime)
                DispatchAttack(warrior);
            else
                WaitAnimationDisplay();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Damage / invulnerability hooks
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Transitions and Spectral Summon make Zort untouchable. TakeDamage() routes here.</summary>
        public override bool TakeDamageAndReturnKilled(float damage)
        {
            if (_invulnerable)
                return false;

            return base.TakeDamageAndReturnKilled(damage);
        }

        protected override void OnDamaged(float damage, bool killed)
        {
            base.OnDamaged(damage, killed);
            _damageReceivedDuringWell += damage; // used by Gravity Well early-cancel
        }

        protected override void OnDeath()
        {
            StopActionRoutine();

            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
                _transitionRoutine = null;
            }

            StopAllCoroutines();      // kill any live beams/wells before the death flow
            _invulnerable = true;
            PlaySfx(attackData != null ? attackData.deathSound : null);

            // Base flow: dissolve VFX, EnemyMgr boss-death slow-mo + final level complete.
            base.OnDeath();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Phase management
        // ─────────────────────────────────────────────────────────────────────────

        private void HandlePhaseTransitions(Warrior warrior)
        {
            if (_inTransition)
                return;

            if (currentPhase == Phase.One && HealthRatio <= phase2Threshold)
                StartTransition(Phase.Two, warrior);
            else if (currentPhase == Phase.Two && HealthRatio <= phase3Threshold)
                StartTransition(Phase.Three, warrior);
        }

        private void StartTransition(Phase next, Warrior warrior)
        {
            StopActionRoutine();
            _transitionRoutine = StartCoroutine(TransitionToPhase(next, warrior));
        }

        private IEnumerator TransitionToPhase(Phase next, Warrior warrior)
        {
            _inTransition = true;
            _invulnerable = true;

            StopMoveTowardCoroutine();
            WaitAnimationDisplay();
            PlaySfx(attackData != null ? attackData.phaseTransition : null);

            yield return new WaitForSeconds(0.5f);

            if (next == Phase.Two)
                yield return Phase2EntranceSequence(warrior);
            else if (next == Phase.Three)
                yield return Phase3EntranceSequence(warrior);

            currentPhase = next;
            _invulnerable = false;
            _inTransition = false;
            _nextAttackTime = Time.time + 0.5f; // resume pressure quickly after a transition
            _transitionRoutine = null;
        }

        private IEnumerator Phase2EntranceSequence(Warrior warrior)
        {
            // Rise off the ground.
            yield return FloatTo(transform.position + Vector3.up * 2f, 1.2f);

            // Collapse the lowest arena platform.
            if (platforms != null && platforms.Length > 0 && platforms[0] != null)
                StartCoroutine(CollapsePlatform(platforms[0]));

            // Fairness checkpoint: restore 20% of the Warrior's max HP.
            if (warrior != null)
                warrior.Heal(Mathf.RoundToInt(warrior.MaxHealth * 0.20f));

            SpawnFx(attackData != null ? attackData.transitionNovaPrefab : null, transform.position);
            yield return new WaitForSeconds(1.0f);
        }

        private IEnumerator Phase3EntranceSequence(Warrior warrior)
        {
            // No boss-dialogue system exists in this project; surface the line for devs.
            Debug.Log($"[Zort] {phase3Quote}", this);

            SpawnFx(attackData != null ? attackData.transitionNovaPrefab : null, transform.position);
            yield return new WaitForSeconds(0.8f);

            ActivateRiftHazards(3);

            _gravityWellTimer = gravityWellInterval;
            _riftPulseTimer = riftPulseInterval;
            _desperationNovaTimer = desperationNovaInterval;

            yield return new WaitForSeconds(1.0f);
        }

        private IEnumerator CollapsePlatform(Transform platform)
        {
            yield return new WaitForSeconds(0.3f);

            Vector3 start = platform.position;
            Vector3 end = start + Vector3.down * 6f;
            float t = 0f;
            const float duration = 0.6f;

            while (t < duration && platform != null)
            {
                t += Time.deltaTime;
                platform.position = Vector3.Lerp(start, end, t / duration);
                yield return null;
            }

            if (platform != null)
                platform.gameObject.SetActive(false);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Repeating phase-timed mechanics
        // ─────────────────────────────────────────────────────────────────────────

        private void TickRepeatingMechanics(Warrior warrior)
        {
            if (currentPhase == Phase.Two || currentPhase == Phase.Three)
            {
                _gravityWellTimer -= Time.deltaTime;
                if (_gravityWellTimer <= 0f)
                {
                    _gravityWellTimer = gravityWellInterval;
                    BeginAction(AttackGravityWell(warrior));
                    return;
                }
            }

            if (currentPhase == Phase.Three)
            {
                _riftPulseTimer -= Time.deltaTime;
                if (_riftPulseTimer <= 0f)
                {
                    _riftPulseTimer = riftPulseInterval;
                    ShuffleRiftHazards();
                }

                if (HealthRatio <= desperationThreshold)
                {
                    _desperationNovaTimer -= Time.deltaTime;
                    if (_desperationNovaTimer <= 0f)
                    {
                        _desperationNovaTimer = desperationNovaInterval;
                        BeginAction(AttackDesperationNova(warrior));
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Exclusive action runner
        // ─────────────────────────────────────────────────────────────────────────

        private void BeginAction(IEnumerator routine)
        {
            StopActionRoutine();
            CanMove = false;
            _actionRoutine = StartCoroutine(routine);
        }

        private void EndAction()
        {
            _actionRoutine = null;
            _nextAttackTime = Time.time + GetCooldownForPhase();

            if (!IsDeadOrDying)
            {
                CanMove = true;
                WaitAnimationDisplay();
            }
        }

        private void StopActionRoutine()
        {
            if (_actionRoutine != null)
            {
                StopCoroutine(_actionRoutine);
                _actionRoutine = null;
            }

            _invulnerable = false; // never leave a cut-short Summon in its invulnerable state
        }

        private float GetCooldownForPhase()
        {
            return currentPhase switch
            {
                Phase.Two => baseAttackCooldown * 0.75f,
                Phase.Three => baseAttackCooldown * 0.50f,
                _ => baseAttackCooldown
            };
        }

        private void DispatchAttack(Warrior warrior)
        {
            int roll = currentPhase switch
            {
                Phase.Three => Random.Range(0, 7),  // + Void Stare
                Phase.Two => Random.Range(0, 6),    // + Aerial Dive, Twin Crescent
                _ => Random.Range(0, 4)             // crescent, shadow step, barrage, summon
            };

            IEnumerator attack = roll switch
            {
                0 => AttackVoidCrescent(false),
                1 => AttackShadowStep(warrior),
                2 => AttackRiftBarrage(),
                3 => AttackSpectralSummon(),
                4 => AttackAerialDive(warrior),
                5 => AttackVoidCrescent(true),
                6 => AttackVoidStare(warrior),
                _ => AttackVoidCrescent(false)
            };

            BeginAction(attack);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Attack coroutines  (each ends by calling EndAction())
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Void Crescent Slash — telegraphed crescent across the arena. Twin (P2+) fires high then low.</summary>
        private IEnumerator AttackVoidCrescent(bool twin)
        {
            if (attackData == null) { EndAction(); yield break; }

            AttackAnimationDisplay();
            PlaySfx(attackData.crescentCharge);

            yield return new WaitForSeconds(attackData.crescentTelegraphDuration);

            Vector2 facing = GetFacingDirection();
            FireVoidProjectile(attackData.crescentPrefab,
                new Vector2(facing.x, 0.12f), attackData.crescentSpeed, attackData.crescentDamage);
            PlaySfx(attackData.crescentFire);

            if (twin)
            {
                yield return new WaitForSeconds(attackData.twinCrescentDelay);
                FireVoidProjectile(attackData.crescentPrefab,
                    new Vector2(facing.x, -0.12f), attackData.crescentSpeed, attackData.crescentDamage);
                PlaySfx(attackData.crescentFire);
            }

            yield return new WaitForSeconds(0.4f);
            EndAction();
        }

        /// <summary>Shadow Step Blitz — dissolve, reappear beside the Warrior, strike 2× (P1) / 3× (P2+).</summary>
        private IEnumerator AttackShadowStep(Warrior warrior)
        {
            if (attackData == null || warrior == null) { EndAction(); yield break; }

            int hitCount = currentPhase == Phase.One ? 2 : 3;

            AttackAnimationDisplay();
            PlaySfx(attackData.teleportOut);
            SetVisible(false);
            yield return new WaitForSeconds(0.25f);

            // Reappear near the Warrior.
            Vector3 side = (Random.value > 0.5f ? Vector3.right : Vector3.left) * 1.5f;
            Vector3 target = warrior.transform.position + side;
            MarkIntentionalEnemyDisplacement(0.3f);
            if (rigidbody2 != null)
                rigidbody2.position = target;
            else
                transform.position = target;

            SpawnFx(attackData.teleportVfxPrefab, target);
            SetVisible(true);
            PlaySfx(attackData.teleportIn);
            FaceWarrior(warrior);
            yield return new WaitForSeconds(0.2f);

            for (int i = 0; i < hitCount; i++)
            {
                yield return new WaitForSeconds(0.3f); // per-strike wind-up
                AttackAnimationDisplay();

                if (GetHorizontalDistanceTo(warrior.transform) <= 1.6f && IsWarriorInFront(warrior.transform))
                {
                    DamageWarrior(warrior, attackData.shadowStepDamage, transform.position, HitKind.Spark);
                    PlaySfx(attackData.meleeHit);
                }
            }

            yield return new WaitForSeconds(0.5f);
            EndAction();
        }

        /// <summary>Rift Barrage — 5 (P1) / 8 (P2+) spread projectiles after a tilt-back telegraph.</summary>
        private IEnumerator AttackRiftBarrage()
        {
            if (attackData == null) { EndAction(); yield break; }

            AttackAnimation2Display();
            PlaySfx(attackData.barrageCharge);

            yield return new WaitForSeconds(attackData.barrageTelegraphDuration);

            int count = currentPhase == Phase.One ? 5 : 8;
            const float spreadAngle = 70f;
            float stepAngle = spreadAngle / (count - 1);
            float startAngle = -spreadAngle / 2f;
            Vector2 facing = GetFacingDirection();

            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + stepAngle * i;
                Vector2 dir = Quaternion.Euler(0f, 0f, angle) * (Vector3)facing;
                FireVoidProjectile(attackData.riftProjectilePrefab, dir,
                    attackData.barrageProjectileSpeed, attackData.riftDamage,
                    ProjectileMode.GroundSlide);
                yield return new WaitForSeconds(0.06f);
            }

            yield return new WaitForSeconds(0.6f);
            EndAction();
        }

        /// <summary>Spectral Summon — float up, become invulnerable, spawn 2 (P1) / 3-4 (P2+) Void Wraiths.</summary>
        private IEnumerator AttackSpectralSummon()
        {
            if (attackData == null) { EndAction(); yield break; }

            AttackAnimation3Display();
            PlaySfx(attackData.summonCharge);

            yield return FloatTo(transform.position + Vector3.up * 3f, 0.7f);
            _invulnerable = true;

            int count = currentPhase == Phase.One ? 2 : Random.Range(3, 5);

            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPos = (wraithSpawnPoints != null && wraithSpawnPoints.Length > 0)
                    ? wraithSpawnPoints[i % wraithSpawnPoints.Length].position
                    : transform.position + Vector3.right * ((i % 2 == 0) ? 1.5f : -1.5f);

                if (attackData.wraithPrefab != null)
                {
                    GameObject obj = Instantiate(attackData.wraithPrefab, spawnPos, Quaternion.identity);
                    VoidWraith wraith = obj.GetComponent<VoidWraith>();
                    if (wraith != null)
                        wraith.Init(OnWraithKilled);
                }

                yield return new WaitForSeconds(0.3f);
            }

            _invulnerable = false;
            yield return new WaitForSeconds(0.5f);
            EndAction();
        }

        /// <summary>Aerial Dive Slash (P2+) — ascend off-screen, plummet to the Warrior's last position, shockwave.</summary>
        private IEnumerator AttackAerialDive(Warrior warrior)
        {
            if (attackData == null || warrior == null) { EndAction(); yield break; }

            JumpAnimationDisplay();
            PlaySfx(attackData.diveAscend);

            Vector3 diveTarget = warrior.transform.position; // last known position

            yield return FloatTo(transform.position + Vector3.up * 12f, 0.5f);
            SetVisible(false);
            yield return new WaitForSeconds(0.6f); // off-screen anticipation

            SetVisible(true);
            MarkIntentionalEnemyDisplacement(0.4f);
            transform.position = new Vector3(diveTarget.x, transform.position.y, transform.position.z);

            yield return FloatTo(new Vector3(diveTarget.x, diveTarget.y, transform.position.z), 0.25f);

            SpawnFx(attackData.diveShockwavePrefab, transform.position);
            PlaySfx(attackData.diveLand);

            // Landing shockwave: must be jumped.
            if (!warrior.IsJumping &&
                Vector2.Distance(warrior.transform.position, transform.position) <= attackData.diveShockwaveRadius)
            {
                DamageWarrior(warrior, attackData.diveDamage, transform.position, HitKind.Spark);
            }

            yield return new WaitForSeconds(0.7f);
            EndAction();
        }

        /// <summary>Void Stare (P3) — slow tracking beam. Turning away from Zort breaks lock-on.</summary>
        private IEnumerator AttackVoidStare(Warrior warrior)
        {
            if (attackData == null || warrior == null || attackData.voidBeamPrefab == null)
            {
                EndAction();
                yield break;
            }

            AttackAnimationDisplay();
            PlaySfx(attackData.stareCharge);
            yield return new WaitForSeconds(1.0f); // charge telegraph

            Vector3 origin = projectileOrigin != null ? projectileOrigin.position : transform.position;
            GameObject beamObj = Instantiate(attackData.voidBeamPrefab, origin, Quaternion.identity, transform);
            beamObj.transform.right = GetFacingDirection();

            VoidBeam beam = beamObj.GetComponent<VoidBeam>();
            if (beam == null)
                beam = beamObj.AddComponent<VoidBeam>();

            beam.Configure(this, attackData.beamDamage, attackData.beamTickInterval,
                attackData.beamMaxLength, attackData.beamTurnSpeed,
                attackData.warriorHitStun, attackData.warriorHitKnockback,
                attackData.projectileObstacleMask);

            // Beam widens as HP approaches zero.
            float lowHp01 = 1f - Mathf.Clamp01(HealthRatio / phase3Threshold);
            beam.SetWidth(Mathf.Lerp(attackData.beamWidthFar, attackData.beamWidthNear, lowHp01));
            beam.TrackTarget(warrior.transform);

            float elapsed = 0f;
            while (elapsed < attackData.beamDuration)
            {
                elapsed += Time.deltaTime;
                if (PlayerFacingAway(warrior)) // dashed behind Zort -> lock broken
                    break;
                yield return null;
            }

            if (beamObj != null)
                Destroy(beamObj);

            yield return new WaitForSeconds(0.5f);
            EndAction();
        }

        /// <summary>Gravity Well (P2+) — pull the Warrior toward arena center for 5s, cancellable by burst damage.</summary>
        private IEnumerator AttackGravityWell(Warrior warrior)
        {
            if (attackData == null) { EndAction(); yield break; }

            AttackAnimation2Display();
            PlaySfx(attackData.gravityWellStart);

            Vector3 center = arenaCenter != null ? arenaCenter.position : transform.position;
            GameObject well = null;
            if (attackData.gravityWellFxPrefab != null)
                well = Instantiate(attackData.gravityWellFxPrefab, center, Quaternion.identity);

            _damageReceivedDuringWell = 0f;
            float cancelThreshold = maxHealth * attackData.gravityWellCancelPercent;
            float elapsed = 0f;

            while (elapsed < attackData.gravityWellDuration)
            {
                elapsed += Time.deltaTime;

                if (warrior != null && warrior.rigidbody2 != null && !warrior.IsDodging)
                {
                    Vector2 pull = ((Vector2)center - (Vector2)warrior.transform.position).normalized
                                   * attackData.gravityPullForce;
                    warrior.rigidbody2.AddForce(pull);
                }

                if (_damageReceivedDuringWell >= cancelThreshold)
                    break;

                yield return null;
            }

            if (well != null)
                Destroy(well);

            _damageReceivedDuringWell = 0f;
            PlaySfx(attackData.gravityWellEnd);
            yield return new WaitForSeconds(0.4f);
            EndAction();
        }

        /// <summary>Desperation Nova (below 10% HP) — full-arena ring. Always survivable if jumped.</summary>
        private IEnumerator AttackDesperationNova(Warrior warrior)
        {
            if (attackData == null) { EndAction(); yield break; }

            AttackAnimation3Display();
            PlaySfx(attackData.novaCharge);
            yield return new WaitForSeconds(attackData.novaChargeDuration);

            Vector3 center = arenaCenter != null ? arenaCenter.position : transform.position;
            SpawnFx(attackData.novaPrefab, center);
            PlaySfx(attackData.novaFire);

            if (warrior != null && !warrior.IsJumping &&
                Vector2.Distance(warrior.transform.position, center) <= attackData.novaRadius)
            {
                DamageWarrior(warrior, attackData.novaDamage, center, HitKind.Spark);
            }

            yield return new WaitForSeconds(1.0f);
            EndAction();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static Warrior GetWarrior()
        {
            return GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
        }

        private void DamageWarrior(Warrior warrior, int damage, Vector2 from, HitKind kind)
        {
            if (warrior == null || warrior.IsDeadOrDying || damage <= 0)
                return;

            if (warrior.IsDodging || warrior.ShieldIsUp)
                return;

            warrior.TakeDamage(damage);
            warrior.ApplyHitReaction(kind, from, attackData.warriorHitStun, attackData.warriorHitKnockback);
        }

        private void FireVoidProjectile(GameObject prefab, Vector2 dir, float speed, int damage,
            ProjectileMode mode = ProjectileMode.Aerial)
        {
            if (prefab == null)
                return;

            Vector3 spawnPos = projectileOrigin != null ? projectileOrigin.position : transform.position;
            GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity);

            Rigidbody2D rb = obj.GetComponent<Rigidbody2D>();
            if (rb == null)
                rb = obj.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            Collider2D col = obj.GetComponent<Collider2D>();
            if (col == null)
            {
                CircleCollider2D circle = obj.AddComponent<CircleCollider2D>();
                circle.radius = 0.18f;
                circle.isTrigger = true;
            }
            else
            {
                col.isTrigger = true;
            }

            VoidProjectile proj = obj.GetComponent<VoidProjectile>();
            if (proj == null)
                proj = obj.AddComponent<VoidProjectile>();

            proj.Initialize(this, dir, speed, damage,
                attackData.warriorHitStun, attackData.warriorHitKnockback,
                attackData.projectileLifetime, attackData.projectileObstacleMask,
                attackData.genericImpactFxPrefab, mode);

            IgnoreOwnerCollisions(obj);

            // VFX prefabs (e.g. Slash_Projectile_VFX_Earth) have playOnAwake off on their
            // sub-systems; drive them explicitly so the fragments/trails appear.
            ParticleSystem ps = obj.GetComponent<ParticleSystem>();
            if (ps != null)
                ps.Play(true);
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

        private IEnumerator FloatTo(Vector3 target, float duration)
        {
            Vector3 start = transform.position;
            float t = 0f;

            while (t < duration)
            {
                t += Time.deltaTime;
                MarkIntentionalEnemyDisplacement(0.1f);
                transform.position = Vector3.Lerp(start, target, duration > 0f ? t / duration : 1f);
                yield return null;
            }

            MarkIntentionalEnemyDisplacement(0.1f);
            transform.position = target;
        }

        private void SetVisible(bool visible)
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = visible;
            }
        }

        private void FaceWarrior(Warrior warrior)
        {
            if (warrior == null)
                return;

            FlipCharacter(warrior.transform.position.x);
            RefreshFacingFlags();
        }

        private Vector2 GetFacingDirection()
        {
            RefreshFacingFlags();
            return rightFacing ? Vector2.right : Vector2.left;
        }

        private bool PlayerFacingAway(Warrior warrior)
        {
            if (warrior == null)
                return false;

            Vector2 warriorFacing = warrior.rightFacing ? Vector2.right : Vector2.left;
            Vector2 toZort = (Vector2)transform.position - (Vector2)warrior.transform.position;
            if (toZort.sqrMagnitude < 0.0001f)
                return false;

            return Vector2.Dot(warriorFacing, toZort.normalized) < -0.5f;
        }

        private float GetHorizontalDistanceTo(Transform other)
        {
            if (other == null)
                return float.MaxValue;

            return Mathf.Abs(GetTargetCenterX(other) - GetMyCenterX());
        }

        private void ActivateRiftHazards(int count)
        {
            if (riftHazardSlots == null || riftHazardSlots.Length == 0)
                return;

            ShuffleArray(riftHazardSlots);
            int n = Mathf.Min(count, riftHazardSlots.Length);
            for (int i = 0; i < riftHazardSlots.Length; i++)
            {
                if (riftHazardSlots[i] != null)
                    riftHazardSlots[i].SetActive(i < n);
            }
        }

        private void ShuffleRiftHazards()
        {
            if (riftHazardSlots == null || riftHazardSlots.Length == 0)
                return;

            // Re-roll which slots are active to keep spatial pressure shifting in Phase 3.
            int active = 0;
            for (int i = 0; i < riftHazardSlots.Length; i++)
                if (riftHazardSlots[i] != null && riftHazardSlots[i].activeSelf)
                    active++;

            ActivateRiftHazards(Mathf.Max(active, 1));
        }

        private void OnWraithKilled(Vector3 position)
        {
            if (attackData != null && attackData.wraithHealthPickupPrefab != null)
                Instantiate(attackData.wraithHealthPickupPrefab, position, Quaternion.identity);
        }

        private void PlaySfx(AudioClip clip)
        {
            if (clip == null)
                return;

            OneShotAudio.Play(clip, transform.position, 1f, new Vector2(0.97f, 1.03f), 0f, 25f);
        }

        private void SpawnFx(GameObject prefab, Vector3 position, float lifetime = 2f)
        {
            if (prefab == null)
                return;

            GameObject fx = Instantiate(prefab, position, Quaternion.identity);

            if (lifetime > 0f)
                Destroy(fx, lifetime);
        }

        private static void ShuffleArray<T>(T[] arr)
        {
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
        }
    }
}

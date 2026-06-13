using System.Collections;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using Assets.Scripts.Tools;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>Coarse platform classification used by Zort's behaviour FSM.</summary>
    public enum PlatformSize { Large, Small }

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

        // Invisibilité (blinks/téléportations) : Zort devient intangible et sa barre est masquée.
        // _invulnerableBeforeHide mémorise l'invulnérabilité d'avant le masquage pour la restaurer à la
        // réapparition (sans écraser une invulnérabilité posée par une transition de phase).
        // _spriteHidden permet à StopActionRoutine de restaurer la visibilité si un blink est coupé net.
        private bool _invulnerableBeforeHide;
        private bool _spriteHidden;

        // Esquive spectrale du dash d'Earth Slash : armé pendant la charge, consommé une seule fois.
        private bool _dashDodgeArmed;
        private bool _dashDodgePending;

        private Coroutine _actionRoutine;
        private Coroutine _transitionRoutine;

        private float _nextAttackTime;
        private float _gravityWellTimer;
        private float _riftPulseTimer;
        private float _desperationNovaTimer;

        private float _damageReceivedDuringWell;

        [Header("Range Zones")]
        [Tooltip("Outer awareness range (documentation/tuning; the FSM boundary is crescentRange).")]
        [SerializeField] private float levitationRange = 12f;
        [SerializeField] private float crescentRange = 6f;
        [SerializeField] private float meleeRange = 2.5f;

        [Header("Movement — FSM")]
        [SerializeField] private float levitationSpeed = 3f;
        [SerializeField] private float hoverHeight = 1.5f;
        [SerializeField] private float smallPlatformTeleportDelay = 2f;

        [Header("Zone 2 — Crescent Behaviour")]
        [Tooltip("Height above the platform surface when Zort fires from the edge.")]
        [SerializeField] private float crescentHoverHeight = 2.5f;
        [Tooltip("Inward margin from the platform edge so the vertical drop lands safely on the surface.")]
        [SerializeField] private float crescentEdgeMargin = 0.3f;
        [Tooltip("Delay between two consecutive crescent shots from the edge.")]
        [SerializeField] private float crescentZoneCooldown = 1.5f;
        [Tooltip("Speed at which Zort moves to the platform edge.")]
        [SerializeField] private float crescentApproachSpeed = 1.2f;
        [Tooltip("Arrival distance — Zort locks when within this of the target edge.")]
        [SerializeField] private float crescentArrivalThreshold = 0.2f;
        [Tooltip("Optional child Transform the crescent spawns from (blade/head). Null = Zort's position.")]
        [SerializeField] private Transform crescentSpawnPoint;

        [Header("Zone 2 — Crescent Animation Sync")]
        [Tooltip("Safety timeout (s) if the OnCrescentReleaseFrame AnimationEvent never fires (clip " +
                 "changed, transition cut). The projectile spawns anyway after this delay so the FSM never blocks.")]
        [SerializeField] private float crescentReleaseTimeout = 1.5f;

        [Header("Earth Slash — VFX Animation Sync")]
        [Tooltip("Animated child Transform marking where the Earth Slash VFX spawns (parent it to the blade " +
                 "so it follows the swing). If null, falls back to projectileOrigin, then Zort's position.")]
        [SerializeField] private Transform earthSlashSpawnPoint;
        [Tooltip("Filet de sécurité (s) si l'AnimationEvent OnEarthSlashReleaseFrame ne part jamais " +
                 "(event pas encore posé / clip changé / transition coupée). Borne AUSSI le wind-up avant " +
                 "les dégâts du slash : le garder COURT (~0.3) sinon Zort reste vulnérable trop longtemps " +
                 "en plein slash et se fait interrompre (téléport défensif / esquive) → le slash ne touche jamais.")]
        [SerializeField] private float earthSlashReleaseTimeout = 0.3f;

        [Header("Earth Slash — Hit Circle")]
        [Tooltip("Layer(s) du Warrior, pour la détection OverlapCircle du hit d'Earth Slash.")]
        [SerializeField] private LayerMask warriorLayerMask;
        [Tooltip("Décalage du centre du cercle vers l'avant (direction du facing de Zort), en unités world.")]
        [SerializeField] private float earthSlashCircleForwardOffset = 0.6f;
        [Tooltip("Décalage vertical du centre du cercle (pour aligner sur le buste/lame), en unités world.")]
        [SerializeField] private float earthSlashCircleVerticalOffset = 0f;

        [Header("Zone 3 — Contact Trigger")]
        [Tooltip("Minimum delay between two contact-triggered Earth Slashes (anti-spam).")]
        [SerializeField] private float contactSlashCooldown = 1f;
        [Tooltip("Centre-to-centre distance at/under which the Warrior counts as 'in contact'. " +
                 "Kept just above the ~1.2f where Zort parks (Earth Slash dash-stop / distance Zone 3), " +
                 "because the BoxCollider2Ds rarely truly overlap there.")]
        [SerializeField] private float contactSlashRange = 1.4f;

        [Header("Earth Slash — Spectral Dodge")]
        [Tooltip("Durée de la dématérialisation pendant l'esquive du dash (s).")]
        [SerializeField] private float dashDodgeBlinkDuration = 0.12f;
        [Tooltip("Distance à laquelle Zort réapparaît, de l'autre côté du Warrior (unités world).")]
        [SerializeField] private float dashDodgeBlinkOffset = 1.6f;

        [Header("Zone 2 — Defensive Teleport")]
        [Tooltip("Hits taken (within the window) before Zort defensively teleports to a Zone-2 spot.")]
        [SerializeField] private int hitsToTriggerDefensive = 2;
        [Tooltip("Sliding window: hits spaced more than this apart restart the count.")]
        [SerializeField] private float defensiveHitWindow = 2.5f;
        [Tooltip("Cooldown after a defensive teleport before it can trigger again.")]
        [SerializeField] private float defensiveTeleportCooldown = 8f;

        [Header("Evasive Blink (rise into Warrior's reach)")]
        [Tooltip("Cooldown entre deux esquives préventives (s).")]
        [SerializeField] private float evasiveBlinkCooldown = 4f;
        [Tooltip("Durée de la dématérialisation (s).")]
        [SerializeField] private float evasiveBlinkDuration = 0.12f;
        [Tooltip("Demi-largeur de la zone de réapparition autour de arenaCenter (unités world).")]
        [SerializeField] private float evasiveBlinkArenaHalfWidth = 6f;
        [Tooltip("Demi-hauteur de la zone de réapparition autour de arenaCenter (unités world).")]
        [SerializeField] private float evasiveBlinkArenaHalfHeight = 3f;
        [Tooltip("Distance minimale au Warrior pour qu'un point de réapparition soit accepté.")]
        [SerializeField] private float evasiveBlinkMinWarriorDistance = 4f;
        [Tooltip("Nombre d'essais de tirage aléatoire avant de basculer sur le repli déterministe.")]
        [SerializeField] private int evasiveBlinkMaxAttempts = 8;
        [Tooltip("Marge verticale pour considérer que Zort 'monte' (anti-jitter).")]
        [SerializeField] private float evasiveRiseEpsilon = 0.02f;

        [Header("Platform Detection")]
        [Tooltip("World-space BoxCollider2D width at or below which the Warrior's platform counts as 'small'.")]
        [SerializeField] private float smallPlatformWidthThreshold = 4.7f;
        [SerializeField] private LayerMask platformLayerMask;
        [SerializeField] private float platformDetectDistance = 2f;

        // FSM runtime state
        private float _crescentZoneTimer;
        private float _smallPlatformTimer;
        private bool _warriorWasAttacking;
        private bool _smallPlatformAttackResponded;

        // Zone 2 sub-state machine: move to the platform's far edge, then fire from it repeatedly.
        private enum CrescentZoneState { MovingToEdge, FiringFromEdge }
        private CrescentZoneState _crescentState = CrescentZoneState.MovingToEdge;
        private Vector3 _lockedFirePosition;
        private Collider2D _lastKnownWarriorPlatform;
        // Sticky Zone-2 engagement: once committed, Zort's own distance (he flies to the far edge)
        // must NOT re-classify the zone, or he'd never reach the edge to fire.
        private bool _crescentEngaged;

        // Zone 3 contact trigger anti-spam.
        private float _nextContactSlashTime;

        // Zone 2 crescent animation-event sync: _awaitingCrescentRelease is true only while a
        // VerticalCrescentRoutine is waiting; OnCrescentReleaseFrame then sets _crescentReleaseFired.
        private bool _crescentReleaseFired;
        private bool _awaitingCrescentRelease;

        // Earth Slash VFX sync: set true by the AttackAnimation2Display clip's AnimationEvent
        // (OnEarthSlashReleaseFrame), consumed by AttackEarthSlash. _awaitingEarthSlashRelease guards it
        // so the same shared "isAttacking2" clip on other attacks has no side effect.
        private bool _earthSlashReleaseFired;
        private bool _awaitingEarthSlashRelease;

        // Earth Slash : contexte partagé entre la coroutine et les AnimationEvents de swing
        // (OnEarthSlashSwingFrame1/2/3), callbacks Animator sans accès aux variables locales.
        private Warrior _earthSlashWarrior;
        private bool _earthSlashHitLanded;

        // Defensive teleport (two hits within a window → reposition to Zone 2).
        private int _hitsTakenCount;
        private float _lastHitTime = -999f;
        private float _nextDefensiveTeleportTime;
        private bool _defensiveTeleportPending;
        private bool _desperationNovaActive; // AttackDesperationNova is protected from defensive interrupt

        // Touché par un IceBulletProjectile : déclenche une téléportation punitive + Earth Slash,
        // consommée dans Update (même pattern sûr que la téléportation défensive).
        private bool _iceBulletPunishPending;

        // Esquive préventive : Zort remonte dans la portée d'attaque du Warrior par en-dessous.
        // _lastPosY est échantillonné en tête d'Update (AVANT le mouvement du frame) : le mouvement
        // de Zort se fait dans Update même, donc une capture en fin d'Update donnerait un delta nul.
        private bool _evasiveBlinkPending;
        private float _nextEvasiveBlinkTime;
        private float _lastPosY;
        private bool _roseSinceLastFrame;

        private float HealthRatio => maxHealth <= 0f ? 0f : currentHealth / maxHealth;

        // ─────────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────────

        protected override void Start()
        {
            base.Start();

            SetBoss(true, "Zort");
            SetEnemyType(EnemyType.Zort);

            // Zort is a levitating boss: no gravity (his position is FSM-driven) and he passes
            // through platforms so he can freely reposition above the Warrior's platform.
            if (rigidbody2 != null)
                rigidbody2.gravityScale = 0f;
            IgnorePlatformCollisions();

            CanMove = true;
            _lastPosY = transform.position.y; // baseline esquive préventive : pas de fausse 'montée' au premier frame
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

            // Échantillonnage 'montée' AVANT tout mouvement du frame : le delta mesure le déplacement
            // effectué par le frame précédent. Mis à jour ici (chaque frame actif) et non en fin
            // d'Update, où les early-returns le laisseraient stale et où la capture post-mouvement
            // rendrait le delta toujours nul au point de détection.
            _roseSinceLastFrame = transform.position.y > _lastPosY + evasiveRiseEpsilon
                || (rigidbody2 != null && rigidbody2.linearVelocity.y > 0.01f);
            _lastPosY = transform.position.y;

            HandlePhaseTransitions(warrior);
            if (_inTransition)
                return;

            // Defensive teleport has top priority: it interrupts the running action (combo break),
            // EXCEPT the desperation nova which is protected. If protected, the pending stays and
            // fires once the nova ends.
            if (_defensiveTeleportPending && !_desperationNovaActive)
            {
                _defensiveTeleportPending = false;
                _hitsTakenCount = 0;
                _nextDefensiveTeleportTime = Time.time + defensiveTeleportCooldown;
                StopActionRoutine();
                BeginAction(DefensiveTeleportToZone2(warrior));
                return;
            }

            // Punition ice bullet : téléportation sur le Warrior + Earth Slash. Priorité haute, comme
            // le défensif, mais protégée pendant la desperation nova (le pending reste et part après).
            if (_iceBulletPunishPending && !_desperationNovaActive)
            {
                _iceBulletPunishPending = false;
                StopActionRoutine();
                BeginAction(TeleportThenEarthSlash(warrior));
                return;
            }

            // Esquive préventive : Zort remonte dans la portée d'attaque du Warrior par en-dessous → blink.
            if (_evasiveBlinkPending && !_desperationNovaActive)
            {
                _evasiveBlinkPending = false;
                _nextEvasiveBlinkTime = Time.time + evasiveBlinkCooldown;
                StopActionRoutine();
                BeginAction(EvasiveBlink(warrior));
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

            FaceWarrior(warrior);

            TickRepeatingMechanics(warrior);
            if (_actionRoutine != null)
                return;

            // Détection esquive préventive — n'évalue qu'en mouvement libre (aucune action en cours).
            // Si elle s'arme, on saute le FSM ce frame pour ne pas monter d'un cran de plus dans la
            // hitbox ; le pending est consommé en tête d'Update au prochain frame.
            TryArmEvasiveBlink(warrior);
            if (_evasiveBlinkPending)
                return;

            UpdateBehaviourFSM(warrior);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Damage / invulnerability hooks
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Transitions and Spectral Summon make Zort untouchable. TakeDamage() routes here.</summary>
        public override bool TakeDamageAndReturnKilled(float damage)
        {
            // Esquive spectrale : pendant le dash d'Earth Slash, le premier coup traverse une
            // image rémanente — aucun dégât, et déclenche un blink (consommé dans la boucle de dash).
            if (_dashDodgeArmed)
            {
                _dashDodgeArmed = false;   // une seule esquive par dash
                _dashDodgePending = true;  // consommé par AttackEarthSlash (blink + reprise de charge)
                return false;              // coup annulé : pas de dégât, OnDamaged n'est pas appelé
            }

            if (_invulnerable)
                return false;

            return base.TakeDamageAndReturnKilled(damage);
        }

        /// <summary>Appelée par IceBulletProjectile quand il touche Zort. Arme une téléportation
        /// punitive sur le Warrior suivie d'un Earth Slash, consommée au prochain Update.</summary>
        public void NotifyIceBulletHit()
        {
            if (IsDeadOrDying || _inTransition)
                return;

            _iceBulletPunishPending = true;
        }

        protected override void OnDamaged(float damage, bool killed)
        {
            base.OnDamaged(damage, killed);
            _damageReceivedDuringWell += damage; // used by Gravity Well early-cancel

            if (killed)
                return;

            // Sliding-window hit count (all damage that reaches here is Warrior-originated; during
            // _invulnerable phases TakeDamageAndReturnKilled drops the hit before OnDamaged).
            _hitsTakenCount = (Time.time - _lastHitTime > defensiveHitWindow) ? 1 : _hitsTakenCount + 1;
            _lastHitTime = Time.time;

            if (_hitsTakenCount >= hitsToTriggerDefensive && Time.time >= _nextDefensiveTeleportTime)
                _defensiveTeleportPending = true; // consumed in Update (safe place to interrupt + teleport)
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
            _iceBulletPunishPending = false; // a punish armed just before death must never fire
            _evasiveBlinkPending = false;    // an evasive blink armed just before death must never fire
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
            _iceBulletPunishPending = false; // une punition armée avant la transition ne part pas à contretemps
            _evasiveBlinkPending = false;    // idem pour une esquive préventive armée avant la transition

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
            _lastPosY = transform.position.y; // les entrées de phase déplacent Zort : pas de fausse 'montée' au frame suivant
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

            // Un blink coupé net entre SetVisible(false) et SetVisible(true) laisserait Zort invisible,
            // intangible et barre cachée durablement : on restaure la visibilité ici. SetVisible(true)
            // réactive sprites/colliders/barre ; l'invulnérabilité est ensuite forcée à false ci-dessous.
            if (_spriteHidden)
                SetVisible(true);

            _invulnerable = false; // never leave a cut-short Summon in its invulnerable state
            _desperationNovaActive = false; // clear the protection flag if the nova was force-stopped
            _dashDodgeArmed = false;   // never leave the spectral dodge armed after a cut-short dash
            _dashDodgePending = false;
            _awaitingEarthSlashRelease = false; // a cut-short slash must not leave the event armed
            _earthSlashReleaseFired = false;
            _earthSlashWarrior = null;          // un AnimationEvent tardif ne doit pas toucher hors attaque
            _earthSlashHitLanded = false;
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

        // ─────────────────────────────────────────────────────────────────────────
        // Behaviour FSM (replaces the old random DispatchAttack). Runs INSIDE each phase;
        // it never touches HandlePhaseTransitions or HandleRepeatingTimers. Only invoked
        // when no exclusive action is already running (gated in Update).
        // ─────────────────────────────────────────────────────────────────────────

        private void UpdateBehaviourFSM(Warrior warrior)
        {
            // Contact trigger (independent of the distance-based Zone 3): if the Warrior is physically
            // touching Zort, Earth Slash immediately — no teleport, since contact is already established.
            // This is naturally gated (UpdateBehaviourFSM only runs when _actionRoutine == null, so it
            // never cuts an action short or doubles with the distance melee) plus an anti-spam cooldown.
            if (Time.time >= _nextContactSlashTime && IsTouchingWarrior(warrior))
            {
                _nextContactSlashTime = Time.time + contactSlashCooldown;
                ExitCrescentEngagement();
                BeginAction(AttackEarthSlash(warrior));
                return;
            }

            float dist = Vector2.Distance(transform.position, warrior.transform.position);
            PlatformSize warriorPlatform = GetWarriorPlatformSize(warrior);

            // Absolute priority: small-platform special behaviours.
            if (warriorPlatform == PlatformSize.Small)
            {
                ExitCrescentEngagement();
                HandleSmallPlatformBehaviour(warrior);
                return;
            }

            // Not on a small platform — reset its trackers.
            _smallPlatformTimer = 0f;
            _smallPlatformAttackResponded = false;
            _warriorWasAttacking = IsWarriorAttacking(warrior);

            // Melee always wins.
            if (dist <= meleeRange)
            {
                ExitCrescentEngagement();
                HandleMeleeZone(warrior);
                return;
            }

            // Zone 2 is STICKY: while engaged, Zort flies to the platform's far edge, which inflates
            // his own distance to the Warrior — so we must NOT re-classify by that distance. Stay
            // engaged until the Warrior enters melee (above) or moves beyond the outer range.
            if (_crescentEngaged)
            {
                if (dist > levitationRange)
                {
                    ExitCrescentEngagement();
                    HandleLevitationZone(warrior);
                }
                else
                {
                    HandleCrescentZone(warrior);
                }
                return;
            }

            // Not engaged yet: commit to Zone 2 when the Warrior is within crescentRange, else levitate.
            if (dist <= crescentRange)
            {
                _crescentEngaged = true;
                HandleCrescentZone(warrior);
            }
            else
            {
                HandleLevitationZone(warrior);
            }
        }

        // Leave Zone 2 and reset its sub-state so the next engagement restarts at MovingToEdge.
        private void ExitCrescentEngagement()
        {
            _crescentEngaged = false;
            _crescentState = CrescentZoneState.MovingToEdge;
            _crescentZoneTimer = 0f;
            _lastKnownWarriorPlatform = null;
        }

        // Zone 1 — free levitation toward the Warrior, no attack.
        private void HandleLevitationZone(Warrior warrior)
        {
            Levitate(warrior, levitationSpeed);
        }

        // Zone 2 — two sub-states:
        //   MovingToEdge   : slow-levitate to the FAR edge of the Warrior's platform, then lock.
        //   FiringFromEdge : stay locked on that edge and drop a vertical crescent every cooldown.
        private void HandleCrescentZone(Warrior warrior)
        {
            Collider2D currentWarriorPlatform = GetWarriorPlatformCollider(warrior);

            // Warrior changed platform (e.g. jumped to another while staying in Zone 2) → go realign
            // on the new platform's far edge. Recompute the locked edge once, here.
            if (currentWarriorPlatform != _lastKnownWarriorPlatform)
            {
                _crescentState = CrescentZoneState.MovingToEdge;
                if (currentWarriorPlatform != null)
                    _lockedFirePosition = GetCrescentFirePosition(currentWarriorPlatform);
                _lastKnownWarriorPlatform = currentWarriorPlatform;
            }

            // Warrior airborne / no platform below — hover over the Warrior until they land.
            if (currentWarriorPlatform == null)
            {
                _crescentState = CrescentZoneState.MovingToEdge;
                Levitate(warrior, crescentApproachSpeed);
                return;
            }

            if (_crescentState == CrescentZoneState.MovingToEdge)
            {
                LevitateTo(_lockedFirePosition, crescentApproachSpeed, warrior);

                if (Vector2.Distance(transform.position, _lockedFirePosition) < crescentArrivalThreshold)
                {
                    // Lock exactly on the edge and become a stationary firing post.
                    transform.position = _lockedFirePosition;
                    if (rigidbody2 != null)
                        rigidbody2.linearVelocity = Vector2.zero;

                    _crescentState = CrescentZoneState.FiringFromEdge;
                    _crescentZoneTimer = 0f; // fire immediately on the first FiringFromEdge frame
                }

                return;
            }

            // FiringFromEdge — no movement at all; stay frozen on the locked edge.
            transform.position = _lockedFirePosition;
            if (rigidbody2 != null)
                rigidbody2.linearVelocity = Vector2.zero;
            FaceWarrior(warrior);
            WaitAnimationDisplay();

            _crescentZoneTimer -= Time.deltaTime;
            if (_crescentZoneTimer <= 0f)
            {
                _crescentZoneTimer = crescentZoneCooldown;
                if (IsCrescentDropSafe(currentWarriorPlatform))
                    BeginAction(VerticalCrescentRoutine(warrior));
            }
        }

        // Fire position above the platform edge FARTHER from Zort (evaluated once, when the
        // MovingToEdge episode starts), so the dropped crescent slides the platform's full length.
        private Vector3 GetCrescentFirePosition(Collider2D platformCollider)
        {
            Bounds b = platformCollider.bounds;

            // Nudged inward by crescentEdgeMargin so the vertical drop lands on the surface, not past the lip.
            bool useLeftEdge = transform.position.x > b.center.x; // Zort on the right → enter via left edge
            float edgeX = useLeftEdge ? b.min.x + crescentEdgeMargin : b.max.x - crescentEdgeMargin;

            return new Vector3(edgeX, b.max.y + crescentHoverHeight, transform.position.z);
        }

        // The crescent drops straight down from the spawn point — only fire when a vertical ray from
        // there hits exactly the Warrior's platform (so it lands and slides across to the Warrior).
        private bool IsCrescentDropSafe(Collider2D platformCollider)
        {
            if (platformCollider == null)
                return false;

            Vector2 spawnPos = GetCrescentSpawnPos();

            if (platformLayerMask.value != 0)
            {
                float rayDistance = Mathf.Max(0.5f, spawnPos.y - platformCollider.bounds.max.y + 1f);
                RaycastHit2D hit = Physics2D.Raycast(spawnPos, Vector2.down, rayDistance, platformLayerMask);
                if (hit.collider == platformCollider)
                    return true;
            }

            // Fallback (platformLayerMask unset): make sure the drop column is over the platform.
            Bounds b = platformCollider.bounds;
            return spawnPos.x >= b.min.x && spawnPos.x <= b.max.x && spawnPos.y >= b.max.y;
        }

        // Spawn point for the Zone-2 crescent: a child Transform if assigned, else Zort's position.
        private Vector2 GetCrescentSpawnPos()
        {
            return crescentSpawnPoint != null
                ? (Vector2)crescentSpawnPoint.position
                : (Vector2)transform.position;
        }

        // Spawn point for the Earth Slash VFX: animated child Transform if assigned (follows the blade
        // and is already mirrored by Zort's flip since it's a child), else projectileOrigin, else Zort.
        private Vector3 GetEarthSlashSpawnPos()
        {
            if (earthSlashSpawnPoint != null)
                return earthSlashSpawnPoint.position;
            if (projectileOrigin != null)
                return projectileOrigin.position;
            return transform.position;
        }

        // Zone 3 — teleport next to the Warrior and Earth Slash.
        private void HandleMeleeZone(Warrior warrior)
        {
            BeginAction(TeleportThenEarthSlash(warrior));
        }

        // Small platform — special cases take priority over the range zones.
        private void HandleSmallPlatformBehaviour(Warrior warrior)
        {
            // Case B — the Warrior attacks from the small platform: respond once per attack.
            bool attackingNow = IsWarriorAttacking(warrior);
            bool attackedThisFrame = attackingNow && !_warriorWasAttacking;
            _warriorWasAttacking = attackingNow;

            if (!attackingNow)
                _smallPlatformAttackResponded = false; // re-arm once the attack ends

            if (attackedThisFrame && !_smallPlatformAttackResponded)
            {
                _smallPlatformAttackResponded = true;
                _smallPlatformTimer = 0f;
                BeginAction(GraceThenTeleportEarthSlash(warrior));
                return;
            }

            // Case A — the Warrior stands still on the small platform for too long.
            if (!warrior.IsMoving)
            {
                _smallPlatformTimer += Time.deltaTime;
                if (_smallPlatformTimer >= smallPlatformTeleportDelay)
                {
                    _smallPlatformTimer = 0f;
                    BeginAction(TeleportThenEarthSlash(warrior));
                    return;
                }
            }
            else
            {
                _smallPlatformTimer = 0f;
            }

            // While waiting, levitate normally (Movement 1).
            Levitate(warrior, levitationSpeed);
        }

        // ─── FSM movement / detection helpers ───────────────────────────────────

        // Hover above the Warrior (Zone 1 / small-platform waiting).
        private void Levitate(Warrior warrior, float speed)
        {
            Vector3 target = new Vector3(
                warrior.transform.position.x,
                GetWarriorPlatformTopY(warrior) + hoverHeight,
                transform.position.z);

            LevitateTo(target, speed, warrior);
        }

        // Levitate toward an explicit world target (used by Zone 2 to sit over the platform centre).
        private void LevitateTo(Vector3 target, float speed, Warrior warrior)
        {
            MarkIntentionalEnemyDisplacement(0.1f);

            if (rigidbody2 != null)
                rigidbody2.linearVelocity = Vector2.zero;

            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);

            FaceWarrior(warrior);
            WaitAnimationDisplay();
        }

        private float GetWarriorPlatformTopY(Warrior warrior)
        {
            if (warrior.CurrentplatForm != null && warrior.CurrentplatForm.platformCollider != null)
                return warrior.CurrentplatForm.platformCollider.bounds.max.y;

            return warrior.transform.position.y;
        }

        private PlatformSize GetWarriorPlatformSize(Warrior warrior)
        {
            Collider2D platformCollider = GetWarriorPlatformCollider(warrior);
            if (platformCollider == null)
                return PlatformSize.Large; // nothing below — treat as large

            BoxCollider2D box = platformCollider as BoxCollider2D;
            if (box == null)
                box = platformCollider.GetComponent<BoxCollider2D>();
            if (box == null)
                return PlatformSize.Large; // not a BoxCollider2D — treat as large

            float worldWidth = box.size.x * Mathf.Abs(box.transform.lossyScale.x);
            return worldWidth <= smallPlatformWidthThreshold
                ? PlatformSize.Small
                : PlatformSize.Large;
        }

        // Collider of the platform directly under the Warrior (downward raycast on platformLayerMask),
        // or null if none. Shared by GetWarriorPlatformSize and the Zone-2 edge logic.
        private Collider2D GetWarriorPlatformCollider(Warrior warrior)
        {
            RaycastHit2D hit = Physics2D.Raycast(
                warrior.transform.position,
                Vector2.down,
                platformDetectDistance,
                platformLayerMask);

            if (hit.collider != null)
                return hit.collider;

            // Fallback: the platform the Warrior is tracked as standing on (works even if the
            // raycast misses because platformLayerMask is unset or platformDetectDistance is short).
            if (warrior != null && warrior.CurrentplatForm != null)
                return warrior.CurrentplatForm.platformCollider;

            return null;
        }

        private bool IsWarriorAttacking(Warrior warrior)
        {
            if (warrior == null || warrior.animator == null)
                return false;

            return warrior.animator.GetBool("isAttacking")
                || warrior.animator.GetBool("isAttacking2")
                || warrior.animator.GetBool("isAttacking3");
        }

        // True when the Warrior is close enough to count as body contact. Uses a PROXIMITY range, not
        // exact collider overlap: Zort keeps ~1.2f (Earth Slash dash-stop / distance Zone 3 park), so
        // the BoxCollider2Ds never actually intersect there and IsTouching() would always be false.
        private bool IsTouchingWarrior(Warrior warrior)
        {
            if (warrior == null)
                return false;

            return Vector2.Distance(transform.position, warrior.transform.position) <= contactSlashRange;
        }

        // ─── FSM attack coroutines (each ends via EndAction) ─────────────────────

        /// <summary>
        /// Zone 2 vertical crescent: spawns above the Warrior and falls straight onto their platform
        /// (GroundSlide). AttackVoidCrescent() itself is intentionally left untouched.
        /// </summary>
        private IEnumerator VerticalCrescentRoutine(Warrior warrior)
        {
            if (attackData == null || warrior == null) { EndAction(); yield break; }

            _crescentReleaseFired = false;     // reset before the slash animation plays
            _awaitingCrescentRelease = true;   // arm OnCrescentReleaseFrame for THIS routine only
            AttackAnimationDisplay();
            PlaySfx(attackData.crescentCharge);

            // Wait for the blade-release frame signalled by the AnimationEvent (OnCrescentReleaseFrame),
            // with a safety timeout so the FSM never blocks if the event never arrives.
            float releaseTimeout = Time.time + crescentReleaseTimeout;
            yield return new WaitUntil(() => _crescentReleaseFired || Time.time >= releaseTimeout);
            _awaitingCrescentRelease = false;

            // Spawn from the animated marker (crescentSpawnPoint follows the blade) or Zort's position.
            Vector2 spawnPos = GetCrescentSpawnPos();

            // Capture la plateforme d'origine du Warrior AU LANCER (même raycast platformLayerMask +
            // fallback CurrentplatForm que partout ailleurs). Si le Warrior saute juste après, le crescent
            // vise quand même cette surface et glisse vers le X où il était — pas en l'air / dans le vide.
            // Repli : aucune plateforme trouvée (Warrior déjà aérien au spawn) → comportement dynamique actuel.
            Collider2D originPlatform = GetWarriorPlatformCollider(warrior);
            bool hasTarget = originPlatform != null;
            float targetSurfaceY = 0f, targetSlideX = 0f;
            float targetMinX = float.NegativeInfinity, targetMaxX = float.PositiveInfinity;
            if (hasTarget)
            {
                Bounds b = originPlatform.bounds;
                targetSurfaceY = b.max.y;
                targetSlideX = warrior.transform.position.x;
                targetMinX = b.min.x;
                targetMaxX = b.max.x;
            }

            FireVoidProjectileAt(spawnPos, attackData.crescentPrefab,
                Vector2.down, attackData.crescentSpeed, attackData.crescentDamage,
                ProjectileMode.GroundSlide,
                hasTarget, targetSurfaceY, targetSlideX, targetMinX, targetMaxX);
            PlaySfx(attackData.crescentFire);

            yield return new WaitForSeconds(0.4f);
            EndAction();
        }

        /// <summary>
        /// Called by an AnimationEvent on the slash clip, at the exact frame Zort swings his blade.
        /// Unblocks VerticalCrescentRoutine so the projectile spawns precisely then. Guarded by
        /// _awaitingCrescentRelease, so the same shared "isAttacking" clip on other attacks (Shadow
        /// Step, Void Stare, …) has no side effect — the flag is only honoured while the crescent waits.
        /// </summary>
        public void OnCrescentReleaseFrame()
        {
            if (_awaitingCrescentRelease)
                _crescentReleaseFired = true;
        }

        /// <summary>Called by an AnimationEvent on the slash clip (AttackAnimation2Display) at the exact
        /// blade-impact frame. Unblocks AttackEarthSlash so the VFX spawns precisely then. Guarded by
        /// _awaitingEarthSlashRelease, so the same shared "isAttacking2" clip on other attacks (Gravity
        /// Well, Desperation Nova, teleport intro) has no side effect.</summary>
        public void OnEarthSlashReleaseFrame()
        {
            if (_awaitingEarthSlashRelease)
                _earthSlashReleaseFired = true;
        }

        /// <summary>Called by AnimationEvents on the 3 swing frames of the attack2 clip. Each plays its
        /// swing sound AND applies the Earth Slash damage (proximity-gated via TryApplyEarthSlashDamage),
        /// instead of the old continuous window. Guarded by _awaitingEarthSlashRelease so the same shared
        /// "isAttacking2" clip on other attacks (Gravity Well, Desperation Nova) stays inert. The swing
        /// frames MUST be placed at/before the release frame, else _awaitingEarthSlashRelease is already
        /// false and neither the sound nor the damage fires.</summary>
        public void OnEarthSlashSwingFrame1()
        {
            if (!_awaitingEarthSlashRelease || attackData == null) return;
            PlaySfx(attackData.earthSlashSwing1);
            TryApplyEarthSlashSwingDamage();
        }

        public void OnEarthSlashSwingFrame2()
        {
            if (!_awaitingEarthSlashRelease || attackData == null) return;
            PlaySfx(attackData.earthSlashSwing2);
            TryApplyEarthSlashSwingDamage();
        }

        public void OnEarthSlashSwingFrame3()
        {
            if (!_awaitingEarthSlashRelease || attackData == null) return;
            PlaySfx(attackData.earthSlashSwing3);
            TryApplyEarthSlashSwingDamage();
        }

        /// <summary>Variante A — un seul hit par slash : le premier des 3 frames de swing qui touche
        /// inflige les dégâts, les suivants ne refont pas mal (mais leurs sons jouent quand même).
        /// Réutilise TryApplyEarthSlashDamage (proximité earthSlashContactRange + gardes + son d'impact).</summary>
        private void TryApplyEarthSlashSwingDamage()
        {
            if (_earthSlashHitLanded)
                return;
            if (TryApplyEarthSlashDamage(_earthSlashWarrior))
                _earthSlashHitLanded = true;
        }

        /// <summary>Teleport next to the Warrior (AttackShadowStep pattern) then chain Earth Slash.</summary>
        private IEnumerator TeleportThenEarthSlash(Warrior warrior)
        {
            if (attackData == null || warrior == null) { EndAction(); yield break; }

            // Pose neutre pour l'intro du téléport (Zort devient invisible juste après, donc la pose
            // importe peu). Surtout : NE PAS pré-armer isAttacking2 ici. Sinon, comme on réapparaît à
            // exactement 1.2u et que la boucle de dash d'AttackEarthSlash casse immédiatement (sans
            // jamais appeler RunAnimationDisplay), le state Attack2 serait déjà actif → le
            // AttackAnimation2Display() de l'étape 2 ne reflanquerait aucune transition → l'AnimationEvent
            // OnEarthSlashReleaseFrame ne se redéclencherait pas. En partant d'une pose neutre, l'étape 2
            // fait un vrai false→true sur isAttacking2 → ré-entrée garantie dans Attack2 → event fiable.
            WaitAnimationDisplay();
            PlaySfx(attackData.teleportOut);
            SetVisible(false);
            yield return new WaitForSeconds(0.25f);

            // Reappear 1.2 units from the Warrior, on the side Zort is approaching from.
            float sideSign = transform.position.x <= warrior.transform.position.x ? -1f : 1f;
            Vector3 target = warrior.transform.position + Vector3.right * (sideSign * 1.2f);
            MarkIntentionalEnemyDisplacement(0.3f);
            if (rigidbody2 != null)
                rigidbody2.position = target;
            else
                transform.position = target;

            SpawnFx(attackData.teleportVfxPrefab, target);
            SetVisible(true);
            PlaySfx(attackData.teleportIn);
            FaceWarrior(warrior);
            yield return new WaitForSeconds(0.15f);

            // Chain into the existing Earth Slash (it calls EndAction at its end).
            yield return AttackEarthSlash(warrior);
        }

        /// <summary>Esquive spectrale : Zort se dématérialise et réapparaît de l'autre côté du Warrior,
        /// puis la charge d'Earth Slash reprend. Réutilise le VFX/SFX de téléportation existants.</summary>
        private IEnumerator SpectralDodgeBlink(Warrior warrior)
        {
            PlaySfx(attackData != null ? attackData.teleportOut : null);
            SetVisible(false);
            _invulnerable = true; // i-frames le temps du blink, comme la téléportation défensive
            yield return new WaitForSeconds(dashDodgeBlinkDuration);

            // Réapparaître de l'AUTRE côté du Warrior par rapport à la position actuelle de Zort.
            float sideSign = transform.position.x <= warrior.transform.position.x ? 1f : -1f; // côté opposé
            Vector3 target = warrior.transform.position + Vector3.right * (sideSign * dashDodgeBlinkOffset);
            target.y = transform.position.y; // conserver la hauteur de charge
            target.z = transform.position.z;

            MarkIntentionalEnemyDisplacement(0.3f);
            if (rigidbody2 != null)
                rigidbody2.position = target;
            else
                transform.position = target;

            SpawnFx(attackData != null ? attackData.teleportVfxPrefab : null, target);
            SetVisible(true);
            PlaySfx(attackData != null ? attackData.teleportIn : null);
            _invulnerable = false;
            FaceWarrior(warrior);
        }

        /// <summary>Case B helper: one frame of grace, then teleport + Earth Slash.</summary>
        private IEnumerator GraceThenTeleportEarthSlash(Warrior warrior)
        {
            yield return null;
            yield return TeleportThenEarthSlash(warrior);
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
                new Vector2(facing.x, 0.12f), attackData.crescentSpeed, attackData.crescentDamage,
                ProjectileMode.GroundSlide);
            PlaySfx(attackData.crescentFire);

            if (twin)
            {
                yield return new WaitForSeconds(attackData.twinCrescentDelay);
                FireVoidProjectile(attackData.crescentPrefab,
                    new Vector2(facing.x, -0.12f), attackData.crescentSpeed, attackData.crescentDamage,
                    ProjectileMode.GroundSlide);
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

        /// <summary>
        /// Earth Slash — Zort sprints into melee range, then delivers a single grounded slash.
        /// Close-range only (DispatchAttack falls back to a crescent when the Warrior is far).
        /// </summary>
        private IEnumerator AttackEarthSlash(Warrior warrior)
        {
            if (attackData == null || warrior == null) { EndAction(); yield break; }

            // Contexte lu par les AnimationEvents de swing (OnEarthSlashSwingFrame1/2/3) qui appliquent
            // désormais les dégâts. Réinitialisé à chaque slash.
            _earthSlashWarrior = warrior;
            _earthSlashHitLanded = false;

            FaceWarrior(warrior);

            // 1. Charge horizontally toward the Warrior until within 1.2 units, after the max-distance
            //    dash, OR after a time cap. The cap is essential: the Warrior's solid collider can stop
            //    Zort before he reaches 1.2f (and the Warrior may keep fleeing), so without it neither
            //    break condition is ever met and the loop spins forever — the slash never fires.
            float startX = transform.position.x;
            float dashElapsed = 0f;
            float maxDashSeconds = attackData.earthSlashMaxDashDistance / Mathf.Max(0.01f, attackData.earthSlashDashSpeed) + 0.5f;

            // Esquive spectrale : armée pour toute la durée de la charge, une seule par dash.
            _dashDodgeArmed = true;
            _dashDodgePending = false;

            while (true)
            {
                float toWarrior = warrior.transform.position.x - transform.position.x;
                if (Mathf.Abs(toWarrior) <= 1.2f)
                    break;
                if (Mathf.Abs(transform.position.x - startX) >= attackData.earthSlashMaxDashDistance)
                    break;
                if (dashElapsed >= maxDashSeconds)
                    break; // blocked by the Warrior's collider, or Warrior fleeing — slash anyway

                // Le Warrior a frappé pendant la charge : le coup a traversé une image rémanente
                // (TakeDamageAndReturnKilled), on exécute le blink puis on reprend la charge.
                if (_dashDodgePending)
                {
                    _dashDodgePending = false;
                    yield return SpectralDodgeBlink(warrior);
                    // Recalibrer le repère de charge après le blink : on repart de la nouvelle position.
                    startX = transform.position.x;
                    dashElapsed = 0f;
                    FaceWarrior(warrior);
                    continue; // reprendre la charge proprement depuis la nouvelle position
                }

                RunAnimationDisplay();

                float step = Mathf.Sign(toWarrior) * attackData.earthSlashDashSpeed * Time.deltaTime;
                float nextX = transform.position.x + step;

                // Never dash past the max-distance budget.
                if (Mathf.Abs(nextX - startX) > attackData.earthSlashMaxDashDistance)
                    nextX = startX + Mathf.Sign(nextX - startX) * attackData.earthSlashMaxDashDistance;

                MarkIntentionalEnemyDisplacement(0.1f);
                Vector3 next = new Vector3(nextX, transform.position.y, transform.position.z);
                if (rigidbody2 != null)
                    // Route through the core wrapper so the implicit MovePosition velocity is
                    // neutralized (NeutralizeImplicitMoveVelocity) and cannot shove the Warrior.
                    MoveCharacterTo(next);
                else
                    transform.position = next;

                FaceWarrior(warrior);

                // Aucun dégât pendant la charge : ils sont désormais appliqués uniquement aux 3 frames
                // de swing (OnEarthSlashSwingFrame1/2/3).

                dashElapsed += Time.deltaTime;
                yield return null;
            }

            // Fin du dash : l'esquive spectrale ne doit jamais rester armée hors charge.
            _dashDodgeArmed = false;
            _dashDodgePending = false;

            // 2. Arrival telegraph + animation. The VFX spawn is driven by an AnimationEvent on the slash
            //    clip (OnEarthSlashReleaseFrame), so it lands on the exact blade-impact frame, not a fixed timer.
            FaceWarrior(warrior);
            _earthSlashReleaseFired = false;     // reset before the slash animation plays
            _awaitingEarthSlashRelease = true;   // arm OnEarthSlashReleaseFrame for THIS routine only
            AttackAnimation2Display();
            // PlaySfx(attackData.meleeHit);

            // Attente du release (Animation Event) ou du timeout. Les dégâts sont appliqués par les
            // AnimationEvents de swing pendant cette fenêtre (tant que _awaitingEarthSlashRelease est true).
            float earthSlashReleaseDeadline = Time.time + earthSlashReleaseTimeout;
            while (!_earthSlashReleaseFired && Time.time < earthSlashReleaseDeadline)
                yield return null;
            _awaitingEarthSlashRelease = false;

            // 3. Slash VFX at the animated, flip-mirrored spawn point. Oriented via transform.right
            //    (same as VoidProjectile) so it lies in the 2D plane facing the slash direction.
            //    Pure VFX prefab — no Rigidbody2D/collider added.
            // Note : earthSlashSwing n'est plus joué ici — il l'est par l'AnimationEvent OnEarthSlashSwingFrame
            //        au frame 0 du clip attack2 (entrée du swing), synchronisé avec l'animation.
            if (attackData.slashEarthVfxPrefab != null)
            {
                Vector3 spawnPos = GetEarthSlashSpawnPos();
                GameObject vfx = Instantiate(attackData.slashEarthVfxPrefab, spawnPos, Quaternion.identity);
                vfx.transform.right = GetFacingDirection();
                ParticleSystem ps = vfx.GetComponent<ParticleSystem>();
                if (ps != null)
                    ps.Play(true);
                Destroy(vfx, 2f);
            }

            // 4-5. Recovery : plus aucun dégât ici (les dégâts ne partent qu'aux 3 frames de swing).
            float recoveryUntil = Time.time + 0.5f;
            while (Time.time < recoveryUntil)
                yield return null;

            _earthSlashWarrior = null; // un AnimationEvent tardif ne doit pas toucher hors attaque
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
                DamageWarrior(warrior, attackData.diveDamage, transform.position, HitKind.Projectile);
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

            _desperationNovaActive = true; // protected from the defensive-teleport interrupt

            AttackAnimation2Display();
            PlaySfx(attackData.novaCharge);
            yield return new WaitForSeconds(attackData.novaChargeDuration);

            Vector3 center = arenaCenter != null ? arenaCenter.position : transform.position;
            SpawnFx(attackData.novaPrefab, center);
            PlaySfx(attackData.novaFire);

            if (warrior != null && !warrior.IsJumping &&
                Vector2.Distance(warrior.transform.position, center) <= attackData.novaRadius)
            {
                DamageWarrior(warrior, attackData.novaDamage, center, HitKind.Projectile);
            }

            yield return new WaitForSeconds(1.0f);
            _desperationNovaActive = false;
            EndAction();
        }

        /// <summary>
        /// Defensive teleport — after taking hitsToTriggerDefensive hits within defensiveHitWindow,
        /// Zort blinks (i-frames) to a Zone-2 spot and resumes the edge-fire behaviour, breaking combos.
        /// </summary>
        private IEnumerator DefensiveTeleportToZone2(Warrior warrior)
        {
            if (warrior == null) { EndAction(); yield break; }

            _invulnerable = true; // i-frames: the Warrior cannot combo through the blink

            SetVisible(false);
            PlaySfx(attackData != null ? attackData.teleportOut : null);
            yield return new WaitForSeconds(0.25f);

            // Target: far edge of the Warrior's platform (reuse Zone 2), else a Zone-1 fallback offset.
            Collider2D platform = GetWarriorPlatformCollider(warrior);
            Vector3 target = platform != null
                ? GetCrescentFirePosition(platform)
                : GetDefensiveFallbackPosition(warrior);

            MarkIntentionalEnemyDisplacement(0.3f);
            if (rigidbody2 != null)
                rigidbody2.position = target;
            else
                transform.position = target;

            SpawnFx(attackData != null ? attackData.teleportVfxPrefab : null, target);
            SetVisible(true);
            PlaySfx(attackData != null ? attackData.teleportIn : null);
            FaceWarrior(warrior);
            yield return new WaitForSeconds(0.15f);

            // Resume Zone 2 ALREADY LOCKED on the edge we just teleported onto. We must set the locked
            // state ourselves: if we left _crescentState = MovingToEdge, HandleCrescentZone would see
            // _lastKnownWarriorPlatform == null (reset by the melee/contact ExitCrescentEngagement that
            // ran while Zort was being hit) and recompute GetCrescentFirePosition from Zort's NEW
            // far-edge position — which flips the chosen edge to the opposite side, sending Zort back
            // across the platform so the arrival test never passes and he never fires.
            _crescentEngaged = true;
            if (platform != null)
            {
                _lockedFirePosition = target;
                _lastKnownWarriorPlatform = platform;     // skip the recompute in HandleCrescentZone
                _crescentState = CrescentZoneState.FiringFromEdge;
                _crescentZoneTimer = 0f;                  // fire immediately next frame
            }
            else
            {
                // Zone-1 fallback target: let the FSM realign once the Warrior is on a platform again.
                _crescentState = CrescentZoneState.MovingToEdge;
                _lastKnownWarriorPlatform = null;
            }

            _invulnerable = false;
            EndAction();
        }

        // Zone-1 fallback when the Warrior has no platform: a point within (meleeRange, crescentRange].
        private Vector3 GetDefensiveFallbackPosition(Warrior warrior)
        {
            float side = transform.position.x <= warrior.transform.position.x ? -1f : 1f;
            float offset = Mathf.Clamp(crescentRange * 0.9f, meleeRange + 1f, crescentRange);
            return new Vector3(
                warrior.transform.position.x + side * offset,
                warrior.transform.position.y + hoverHeight,
                transform.position.z);
        }

        /// <summary>
        /// Esquive préventive — arme le blink quand Zort MONTE vers le Warrior par en-dessous et
        /// pénètre dans sa portée d'attaque réelle. Checks ordonnés du moins cher au plus cher :
        /// cooldown → montée → centres, puis seulement l'OverlapCircle de GetEnemiesInAttackRange().
        /// Consommé en tête d'Update (pattern _defensiveTeleportPending).
        /// </summary>
        private void TryArmEvasiveBlink(Warrior warrior)
        {
            if (_evasiveBlinkPending || Time.time < _nextEvasiveBlinkTime)
                return;

            if (!_roseSinceLastFrame)
                return;

            // Centre de Zort sous celui du Warrior (centres de colliders si disponibles).
            float myCenterY = NormalCollider != null
                ? NormalCollider.bounds.center.y
                : transform.position.y;
            float warriorCenterY = warrior.collider2 != null
                ? warrior.collider2.bounds.center.y
                : warrior.transform.position.y;
            if (myCenterY >= warriorCenterY)
                return;

            // C'est bien CE Zort qui est dans le cercle d'attaque — pas un wraith.
            Enemy[] inRange = warrior.GetEnemiesInAttackRange();
            for (int i = 0; i < inRange.Length; i++)
            {
                if (inRange[i] == this)
                {
                    _evasiveBlinkPending = true;
                    return;
                }
            }
        }

        // Point de réapparition de l'esquive préventive : aléatoire mais borné par l'arène et validé
        // (assez loin du Warrior pour que l'esquive serve), avec repli déterministe côté opposé.
        private Vector3 PickEvasiveBlinkTarget(Warrior warrior)
        {
            Vector3 center = arenaCenter != null ? arenaCenter.position : transform.position;

            for (int attempt = 0; attempt < evasiveBlinkMaxAttempts; attempt++)
            {
                Vector3 candidate = center + new Vector3(
                    Random.Range(-evasiveBlinkArenaHalfWidth, evasiveBlinkArenaHalfWidth),
                    Random.Range(-evasiveBlinkArenaHalfHeight, evasiveBlinkArenaHalfHeight),
                    0f);
                candidate.z = transform.position.z;

                if (Vector2.Distance(candidate, warrior.transform.position) >= evasiveBlinkMinWarriorDistance)
                    return candidate;
            }

            // Repli : côté opposé au Warrior par rapport au centre de l'arène, en hauteur.
            float sideSign = warrior.transform.position.x <= center.x ? 1f : -1f;
            return center + new Vector3(sideSign * evasiveBlinkArenaHalfWidth * 0.8f, evasiveBlinkArenaHalfHeight * 0.5f, 0f);
        }

        /// <summary>Esquive préventive : Zort se dématérialise (i-frames) et réapparaît à un point
        /// aléatoire validé de l'arène, hors de portée du Warrior. Pure fuite — aucune contre-attaque.</summary>
        private IEnumerator EvasiveBlink(Warrior warrior)
        {
            if (warrior == null) { EndAction(); yield break; }

            PlaySfx(attackData != null ? attackData.teleportOut : null);
            SetVisible(false);
            _invulnerable = true; // i-frames le temps du blink, comme les autres téléportations
            yield return new WaitForSeconds(evasiveBlinkDuration);

            Vector3 target = PickEvasiveBlinkTarget(warrior);
            MarkIntentionalEnemyDisplacement(0.3f);
            if (rigidbody2 != null)
                rigidbody2.position = target;
            else
                transform.position = target;

            SpawnFx(attackData != null ? attackData.teleportVfxPrefab : null, target);
            SetVisible(true);
            PlaySfx(attackData != null ? attackData.teleportIn : null);
            _invulnerable = false;
            FaceWarrior(warrior);

            EndAction();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static Warrior GetWarrior()
        {
            return GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
        }

        /// <summary>Inflige les dégâts au Warrior. Renvoie true si le coup a porté, false si annulé
        /// (Warrior mort / esquive / bouclier) — permet à l'appelant de ne jouer un son d'impact que
        /// sur un vrai hit. Les appelants qui ignorent le retour gardent le comportement d'origine.</summary>
        private bool DamageWarrior(Warrior warrior, int damage, Vector2 from, HitKind kind)
        {
            if (warrior == null || warrior.IsDeadOrDying || damage <= 0)
                return false;

            if (warrior.IsDodging || warrior.ShieldIsUp)
                return false;
            warrior.SpawnBloodshedEffectFromEnemy(this);
            warrior.TakeDamage(damage);
            warrior.ApplyHitReaction(kind, from, attackData.warriorHitStun, attackData.warriorHitKnockback);
            return true;
        }

        /// <summary>
        /// Inflige le hit d'Earth Slash si le Warrior est à portée. Distance 2D (proximité « visuelle »,
        /// donc le vertical compte : un Warrior proche mais en l'air est touché, un saut qui l'éloigne
        /// vraiment ne l'est pas). Retourne true seulement si le coup a effectivement porté ; une esquive
        /// (IsDodging) ou un bouclier (ShieldIsUp) renvoie false → l'appelant continue d'essayer dans la
        /// fenêtre, et le slash n'est « consommé » que sur un vrai hit.
        /// </summary>
        private bool TryApplyEarthSlashDamage(Warrior warrior)
        {
            if (warrior == null || warrior.IsDeadOrDying)
                return false;
            if (warrior.IsDodging || warrior.ShieldIsUp)
                return false;
            if (!IsWarriorInEarthSlashCircle(warrior))
                return false;

            // Le coup ne porte que si DamageWarrior n'a pas été annulé in extremis (esquive/bouclier
            // levés pile à ce frame) : earthSlashImpact distinct uniquement sur un vrai hit. Appelé
            // désormais depuis les 3 AnimationEvents de swing via TryApplyEarthSlashSwingDamage.
            if (!DamageWarrior(warrior, attackData.shadowStepDamage, transform.position, HitKind.Projectile))
                return false;

            PlaySfx(attackData.earthSlashImpact);
            return true;
        }

        /// <summary>True si le collider du Warrior chevauche le cercle d'attaque de l'Earth Slash.
        /// Le cercle est centré devant Zort (offset dans la direction du facing) et de rayon
        /// earthSlashContactRange — le Warrior « entre » dans ce cercle quel que soit son point central.</summary>
        private bool IsWarriorInEarthSlashCircle(Warrior warrior)
        {
            if (warrior == null) 
                return false;

            Vector2 circleCenter = GetEarthSlashCircleCenter();

            // Détecte spécifiquement le collider du Warrior (via son layer) dans le cercle.
            Collider2D hit = Physics2D.OverlapCircle(circleCenter, attackData.earthSlashContactRange, warriorLayerMask);
            if (hit == null) 
                return false;

            // Confirme que c'est bien le Warrior (et pas un autre collider sur le même layer).
            Warrior found = hit.GetComponent<Warrior>() ?? hit.GetComponentInParent<Warrior>();
            return found == warrior;
        }

        // Centre du cercle de hit de l'Earth Slash : devant Zort (sens du facing) + offset vertical.
        // Partagé entre la détection et le gizmo pour qu'ils ne divergent jamais.
        private Vector2 GetEarthSlashCircleCenter()
        {
            Vector2 facing = GetFacingDirection();
            return (Vector2)transform.position
                + facing * earthSlashCircleForwardOffset
                + Vector2.up * earthSlashCircleVerticalOffset;
        }

        // Visualise le cercle de hit de l'Earth Slash dans l'éditeur, TOUJOURS (pas seulement quand Zort
        // est sélectionné). En edit-mode on n'appelle pas GetFacingDirection() (qui dépend de l'état
        // runtime via RefreshFacingFlags) → facing = droite.
        private void OnDrawGizmos()
        {
            if (attackData == null) return;

            Vector2 facing = Application.isPlaying ? (Vector2)GetFacingDirection() : Vector2.right;
            Vector3 c = (Vector2)transform.position
                + facing * earthSlashCircleForwardOffset
                + Vector2.up * earthSlashCircleVerticalOffset;

            float r = attackData.earthSlashContactRange;

            // Disque plein translucide pour le repérer d'un coup d'œil, puis le contour net.
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.12f);
            Gizmos.DrawSphere(c, r);
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.9f);
            Gizmos.DrawWireSphere(c, r);
        }

        private void FireVoidProjectile(GameObject prefab, Vector2 dir, float speed, int damage,
            ProjectileMode mode = ProjectileMode.Aerial)
        {
            Vector3 spawnPos = projectileOrigin != null ? projectileOrigin.position : transform.position;
            FireVoidProjectileAt(spawnPos, prefab, dir, speed, damage, mode);
        }

        /// <summary>Same as <see cref="FireVoidProjectile"/> but the spawn position is explicit
        /// (used by the Zone-2 vertical crescent, which spawns above the Warrior).</summary>
        private void FireVoidProjectileAt(Vector2 spawnPos, GameObject prefab, Vector2 dir, float speed, int damage,
            ProjectileMode mode = ProjectileMode.Aerial,
            bool hasGroundSlideTarget = false, float targetSurfaceY = 0f, float targetSlideX = 0f,
            float targetMinX = float.NegativeInfinity, float targetMaxX = float.PositiveInfinity)
        {
            if (prefab == null)
                return;

            GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity);

            Rigidbody2D rb = obj.GetComponent<Rigidbody2D>();
            if (rb == null)
                rb = obj.AddComponent<Rigidbody2D>();
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            // Réglages physiques conditionnels au mode (Approche B) :
            //  • Aerial      : ZortBoss tient le projectile en vol rectiligne → gravité 0 ici.
            //  • GroundSlide : VoidProjectile POSSÈDE la gravité (EnterSeek applique la gravité de chute
            //                  puis la fige à l'atterrissage). Ne pas la mettre à 0 ici — sinon on ne
            //                  comptait que sur Initialize pour la restaurer (dépendance d'ordre fragile).
            // Le collider reste un trigger dans LES DEUX modes : le GroundSlide en a besoin pour traverser
            // toutes les plateformes sauf celle du Warrior (l'atterrissage est logique, pas physique).
            if (mode == ProjectileMode.Aerial)
                rb.gravityScale = 0f;

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
                attackData.genericImpactFxPrefab, mode,
                hasGroundSlideTarget, targetSurfaceY, targetSlideX, targetMinX, targetMaxX);

            IgnoreOwnerCollisions(obj);

            // VFX prefabs (e.g. Slash_Projectile_VFX_Earth) have playOnAwake off on their
            // sub-systems; drive them explicitly so the fragments/trails appear.
            ParticleSystem ps = obj.GetComponent<ParticleSystem>();
            if (ps != null)
                ps.Play(true);
        }

        /// <summary>
        /// Disable physical collision between Zort's colliders and every platform so the levitating
        /// boss can pass through them. Platforms are detected by the "PlatformLayer" layer and the
        /// <see cref="PlatFormColliderTrigger"/> component (inspector-mask independent). Per-collider,
        /// so other enemies keep colliding with platforms normally. <see cref="OnCollisionEnter2D"/>
        /// catches any platform that spawns (or is met) after Start.
        /// </summary>
        private void IgnorePlatformCollisions()
        {
            Collider2D[] myColliders = GetComponentsInChildren<Collider2D>(true);
            if (myColliders == null || myColliders.Length == 0)
                return;

            Collider2D[] sceneColliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
            for (int i = 0; i < sceneColliders.Length; i++)
            {
                Collider2D platformCollider = sceneColliders[i];
                if (platformCollider == null || !IsPlatformCollider(platformCollider))
                    continue;

                for (int j = 0; j < myColliders.Length; j++)
                {
                    if (myColliders[j] != null)
                        Physics2D.IgnoreCollision(myColliders[j], platformCollider, true);
                }
            }
        }

        // Catch platforms spawned (or first met) after Start: phase through them from contact on.
        protected override void OnCollisionEnter2D(Collision2D collision)
        {
            base.OnCollisionEnter2D(collision);

            if (collision == null || collision.collider == null || collision.otherCollider == null)
                return;

            if (IsPlatformCollider(collision.collider))
                Physics2D.IgnoreCollision(collision.otherCollider, collision.collider, true);
        }

        private bool IsPlatformCollider(Collider2D col)
        {
            if (col == null)
                return false;

            int platformLayer = LayerMask.NameToLayer("PlatformLayer");
            if (platformLayer >= 0 && col.gameObject.layer == platformLayer)
                return true;

            if (platformLayerMask.value != 0 && (platformLayerMask.value & (1 << col.gameObject.layer)) != 0)
                return true;

            return col.GetComponentInParent<PlatFormColliderTrigger>() != null;
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

        // Centralise toute la conséquence de l'(in)visibilité : sprites, intangibilité, barre de vie.
        // Tous les blinks/téléportations passent par ici, donc Zort est totalement intouchable et sa
        // barre masquée tant qu'il est invisible, et tout revient à l'état normal à la réapparition.
        private void SetVisible(bool visible)
        {
            // Sprites
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = visible;
            }

            // Intangibilité : pendant l'invisibilité, les colliders sont désactivés → le scan d'attaque
            // du Warrior (OverlapCircleAll sur enemyLayer) ne touche plus Zort à son ancienne position.
            SetCollidersEnabled(visible);

            // Invulnérabilité de secours (au cas où un collider resterait actif via une autre route),
            // sans écraser une invulnérabilité déjà posée (ex. transition de phase) : on la sauvegarde
            // au masquage et on la restaure à la réapparition.
            if (!visible)
            {
                _invulnerableBeforeHide = _invulnerable;
                _invulnerable = true;
            }
            else
            {
                _invulnerable = _invulnerableBeforeHide;
            }

            // Barre de vie (World-Space Canvas) masquée pendant l'invisibilité.
            SetHealthBarVisible(visible);

            _spriteHidden = !visible;
        }

        // Active/désactive tous les colliders de Zort (corps + triggers hérités d'Enemy/CharacterController).
        private void SetCollidersEnabled(bool enabled)
        {
            if (NormalCollider != null) NormalCollider.enabled = enabled;
            if (collider2 != null) collider2.enabled = enabled;
            if (TriggerColliderLeft != null) TriggerColliderLeft.enabled = enabled;
            if (TriggerColliderRight != null) TriggerColliderRight.enabled = enabled;
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
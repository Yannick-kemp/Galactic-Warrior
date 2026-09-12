using Assets.Scripts.Characteres.EnemyContoller;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {

        #region Edge-Fall Destination Platform Anti-Tunneling

        [Header("Edge Fall Destination Platform Anti-Tunneling")]
        [SerializeField] private bool enableEdgeFallDestinationAntiTunnel = true;

        [Tooltip("Predicts the next physics step while Warrior is falling from an edge. This catches the destination platform even when CurrentplatForm is still the source platform.")]
        [SerializeField, Min(0.5f)] private float edgeFallAntiTunnelPredictionMultiplier = 1.35f;

        [Tooltip("Minimum downward speed before the physics-fall anti-tunnel check runs.")]
        [SerializeField, Min(0f)] private float edgeFallAntiTunnelMinDownSpeed = 0.01f;

        [Tooltip("Small extra distance added to the next-step physics fall prediction. Keep small: the exact previous/current sweep is also checked.")]
        [SerializeField, Min(0f)] private float physicsFallAntiTunnelExtraLookAhead = 0.03f;

        private Vector2 _lastPhysicsFallAntiTunnelPosition;
        private bool _hasLastPhysicsFallAntiTunnelPosition;

        [Header("Enemy Top Ping-Pong Escape - Warrior Side")]
        [SerializeField] private bool enableEnemyTopPingPongEscape = true;

        [Tooltip("How long Warrior may stay in post-bounce / controlled jump on top of nearby enemies before we force a natural fall.")]
        [SerializeField, Min(0.05f)] private float enemyTopTrapMinStuckTime = 0.28f;

        [Tooltip("Repeated Warrior-on-enemy-top contacts inside this window count as ping-pong.")]
        [SerializeField, Min(0.1f)] private float enemyTopTrapWindow = 1.20f;

        [Tooltip("Fallback safety threshold. The normal rule below is: bounce each nearby enemy top once, then force final landing.")]
        [SerializeField, Min(2)] private int enemyTopTrapBounceThreshold = 2;

        [Tooltip("ON = Warrior may use each nearby Enemy top-bound once as a bounce pad. After every known nearby enemy top was used once, Warrior performs a final landing instead of bouncing forever.")]
        [SerializeField] private bool enemyTopTrapBounceEachEnemyOnceBeforeLanding = true;

        [Tooltip("ON = after the last allowed enemy-top bounce, temporarily ignore all nearby enemy body colliders for the full ignore window so the final landing cannot be interrupted by another enemy top.")]
        [SerializeField] private bool enemyTopTrapIgnoreAllEnemiesDuringFinalLanding = true;

        [Tooltip("Nearby search width around Warrior. Multiple enemies inside this zone can create the ping-pong bridge.")]
        [SerializeField, Min(0f)] private float enemyTopTrapNearbyRadiusX = 4.0f;

        [Tooltip("Nearby search height around Warrior. Keep this modest so enemies far above/below are ignored.")]
        [SerializeField, Min(0f)] private float enemyTopTrapNearbyRadiusY = 2.5f;

        [Tooltip("How long Warrior ignores nearby enemy colliders after the trap breaks so he can fall through the gap naturally.")]
        [SerializeField, Min(0.05f)] private float enemyTopTrapIgnoreCollisionTime = 0.65f;

        [Tooltip("Extra clearance required before enemy collisions are restored early.")]
        [SerializeField, Min(0f)] private float enemyTopTrapRestoreClearance = 0.03f;

        [Tooltip("Gravity used when forcing Warrior out of the stale top-bounce jump.")]
        [SerializeField, Min(0f)] private float enemyTopTrapFallGravity = 3.5f;

        [Tooltip("Minimum downward velocity applied when the ping-pong trap is broken.")]
        [SerializeField, Min(0f)] private float enemyTopTrapMinDownVelocity = 1.25f;

        [Tooltip("Optional: asks nearby enemies to side-step away via SendMessage if their script exposes ForceAntiPingPongSideStepAwayFromWarrior(Warrior). Safe if the method does not exist.")]
        [SerializeField] private bool notifyEnemiesToSideStepOnTopTrap = true;

        [Tooltip("ON = when the top ping-pong trap breaks with no safe land, try to drop the Warrior straight onto the ground BETWEEN the two bracketing enemies (one on each side) instead of a blind free fall. Falls back to the free fall when there is no bracketing pair or no ground between them.")]
        [SerializeField] private bool enableLandBetweenEnemies = true;

        [Tooltip("Downward raycast distance used to confirm there is real ground between the two bracketing enemies before snapping the Warrior down between them.")]
        [SerializeField, Min(0.5f)] private float landBetweenEnemiesRaycastMaxDistance = 6.0f;

        private float _enemyTopTrapWindowStartedAt = -999f;
        private int _enemyTopTrapBounceCount;
        private readonly HashSet<int> _enemyTopTrapTouchedIds = new HashSet<int>();
        private int _enemyTopTrapLatestRegisteredEnemyId = -1;
        private bool _enemyTopTrapLatestRegisteredEnemyWasUnique;
        private Coroutine _enemyTopTrapIgnoreRoutine;

        [Header("Single-Enemy Top Stuck Escape (includes Zalayty)")]
        [Tooltip("ON = if the Warrior floats on a SINGLE enemy's top bound (no ground support) for too long, force one clean bounce-away. Complements the multi-enemy ping-pong rule.")]
        [SerializeField] private bool enableSingleEnemyTopStuckEscape = true;

        [Tooltip("Continuous time the Warrior may rest on a single enemy top (with zero ground points) before a forced bounce-away. Keep above the platform edge-fall timer (0.20s).")]
        [SerializeField, Min(0.05f)] private float singleEnemyTopStuckTime = 0.30f;

        private Enemy _singleEnemyTopContactEnemy;
        private float _singleEnemyTopContactStartedAt = -999f;

        [Header("Enemy Top Ping-Pong ABSOLUTE Failsafe (anti soft-lock)")]
        [Tooltip("ON = last-resort guaranteed escape that does NOT depend on the enemy cluster geometry, on which enemy was already bounced, or on the enemies cooperating (side-step / repath). It fires when the Warrior has been ping-ponging on enemy tops for too many total contacts OR too long, regardless of identity — the case that made a >2 Zalayty cluster over no reachable ground trap the Warrior forever. Leave ON.")]
        [SerializeField] private bool enableEnemyTopPingPongAbsoluteFailsafe = true;

        [Tooltip("Total enemy-top contacts (ANY enemy, NOT windowed, NOT per-enemy) that force the failsafe. Keep well above the normal 2-enemy resolution (~2-3 contacts) so it only triggers on the pathological perpetual loop.")]
        [SerializeField, Min(2)] private int absoluteFailsafeContactLimit = 6;

        [Tooltip("Continuous seconds the Warrior may stay airborne/pinned on enemy tops (zero own ground points) before the failsafe forces a resolution. Parallel trigger to the contact count.")]
        [SerializeField, Min(0.2f)] private float absoluteFailsafeStuckSeconds = 1.5f;

        [Tooltip("If no enemy-top contact happens for this long, the absolute-failsafe counters self-reset so unrelated later encounters do not accumulate onto a stale count.")]
        [SerializeField, Min(0.2f)] private float absoluteFailsafeIdleResetSeconds = 1.2f;

        [Tooltip("Downward search distance for real ground (PlatformLayer) below the Warrior when the failsafe fires. Ground within this range → phase-through toward it; nothing within range → straight teleport to the last safe position.")]
        [SerializeField, Min(0.5f)] private float absoluteFailsafeGroundSearchDistance = 12f;

        [Tooltip("Maximum duration of the phase-through fall before we give up and teleport to the last safe position (backstop even when a ground raycast said ground existed).")]
        [SerializeField, Min(0.2f)] private float absoluteFailsafePhaseThroughMaxSeconds = 1.5f;

        private int _absoluteFailsafeContactCount;
        private float _absoluteFailsafeStuckSince = -1f;
        private float _absoluteFailsafeLastContactTime = -999f;
        private bool _absoluteFailsafeActive;
        private Coroutine _absoluteFailsafeRoutine;

        [Header("Enemy Top Ping-Pong - Forced Landing BETWEEN Enemies after N bounces")]
        [Tooltip("ON = absolute rule, valid for EVERY enemy type: once the Warrior has performed the configured number of ping-pong bounces on enemy top bounds without having found a landing spot of his own, he stops bouncing and is dropped BETWEEN the enemies (widest gap of the group with real ground under it), never on top of one of them.")]
        [SerializeField] private bool enableEnemyTopMiddleLandingAfterBounces = true;

        [Tooltip("Number of enemy-top ping-pong bounces (ANY enemy, ANY type, no identity or time-window accounting) allowed before the forced landing between the enemies. 3 = the requested rule: bounce, bounce, bounce, then land between them.")]
        [SerializeField, Min(1)] private int enemyTopMiddleLandingBounceLimit = 3;

        [Tooltip("If no enemy-top contact happens for this long, the bounce counter self-resets so an unrelated later encounter does not inherit a stale count.")]
        [SerializeField, Min(0.2f)] private float enemyTopMiddleLandingIdleResetSeconds = 4f;

        [Tooltip("Downward search distance for real ground (PlatformLayer) under a candidate gap between the enemies. A gap only becomes a landing spot when this ray finds ground.")]
        [SerializeField, Min(0.5f)] private float enemyTopMiddleLandingGroundSearchDistance = 14f;

        [Tooltip("Grace period after the forced landing during which no ping-pong / single-enemy escape may launch the Warrior again, so the descent between the enemies cannot be interrupted.")]
        [SerializeField, Min(0f)] private float enemyTopMiddleLandingSettleSeconds = 0.8f;

        [Tooltip("Maximum time the forced descent between the enemies may take before the deeper absolute failsafe (phase-through / last safe position) takes over. Guarantees the rule always ends on the ground.")]
        [SerializeField, Min(0.2f)] private float enemyTopMiddleLandingWatchdogSeconds = 1.5f;

        [Tooltip("Second absolute limit, per enemy: the Warrior may never chain more than this many bounces on the SAME enemy without touching the ground. Reaching it forces the same landing between the enemies as the global bounce limit, even when the global count is not reached yet.")]
        [SerializeField, Min(1)] private int enemyTopMiddleLandingSameEnemyBounceLimit = 2;

        [Tooltip("Extra clearance added to the Warrior's body width before a gap between two enemies is accepted as a real landing spot. A packed cluster has overlapping (negative) gaps whose middle is inside an enemy body: dropping him there gets him ejected sideways by the physics depenetration. Below this width the rule uses the middle of the whole group and goes down through them instead.")]
        [SerializeField, Min(0f)] private float middleLandingGapClearance = 0.2f;

        [Tooltip("ON = during the forced descent between the enemies, the Warrior keeps the X the rule committed to until he is grounded. Without it the enemies of the group push him out mid-fall and he lands beside them instead of among them. Only the fall is held; normal pushes resume on landing.")]
        [SerializeField] private bool holdLandingXWhileFallingBetweenEnemies = true;

        [Tooltip("Plafond d'absurdite sur la distance horizontale du point d'arrivee. Ce n'est PLUS un plafond de teleportation: le point est rejoint en GLISSANT pendant la chute, jamais par un saut de position, donc une distance moyenne ne produit aucune repulsion. Regle a 3 il refusait quasiment tout (mesure: 109 candidats refuses a 3,2 a 6,8 unites, la regle degeneree en traversee + ennemis sonnes 20 fois).")]
        [SerializeField, Min(0.5f)] private float middleLandingMaxSnapDistanceX = 10f;

        [Tooltip("Vitesse horizontale utilisee pour rejoindre l'ecart PENDANT la chute, au lieu d'un saut de position instantane.")]
        [SerializeField, Min(1f)] private float middleLandingGlideSpeedX = 8f;

        [Tooltip("Duree pendant laquelle une nouvelle prise de contact sur le MEME ennemi n'est plus comptee comme un rebond. Protege du double comptage: le callback de collision se declenche par paire de colliders, et le detecteur geometrique alimente maintenant le meme compteur. Un vrai rebond est espace d'au moins 0,2 s.")]
        [SerializeField, Min(0f)] private float middleLandingSameContactDebounce = 0.15f;

        [Tooltip("Nombre de declenchements de la regle dans la fenetre ci-dessous a partir duquel on considere qu'elle tourne en rond et qu'il faut vraiment sortir le Warrior de la zone.")]
        [SerializeField, Min(2)] private int middleLandingRepeatEscalationCount = 3;

        [Tooltip("Fenetre (secondes) sur laquelle les atterrissages forces repetes sont comptes.")]
        [SerializeField, Min(1f)] private float middleLandingRepeatWindowSeconds = 12f;

        [Tooltip("Duree pendant laquelle les ennemis responsables de la boucle sont traverses et sonnes, pour que le Warrior puisse vraiment repartir.")]
        [SerializeField, Min(0.2f)] private float middleLandingLoopEscapeSeconds = 1.5f;

        [Tooltip("ON = a la fin de la descente forcee, ne pas rendre les ennemis solides tant que leur corps chevauche le Warrior: on le degage d'abord.")]
        [SerializeField] private bool clearOverlapBeforeRestoringEnemies = true;

        [Tooltip("Duree maximale accordee au degagement avant de rendre les ennemis solides malgre tout.")]
        [SerializeField, Min(0.1f)] private float middleLandingClearOutSeconds = 0.8f;

        [Tooltip("Duree pendant laquelle un ennemi qui chevauchait le point d'arrivee est sonne, pour qu'il n'y revienne pas immediatement.")]
        [SerializeField, Min(0f)] private float middleLandingClearOutStunSeconds = 0.5f;

        [Tooltip("ON = apres l'atterrissage force, le Warrior est reellement libre pendant un instant: aucun ennemi ne peut le porter ni le relancer, et le groupe fautif est sonne et recule.")]
        [SerializeField] private bool freeWarriorAfterForcedLanding = true;

        [Tooltip("Duree de cette fenetre de liberte. C'est le temps donne au joueur pour repartir a pied avant que les ennemis redeviennent solides.")]
        [SerializeField, Min(0.2f)] private float postLandingFreedomSeconds = 2.5f;

        [Tooltip("Distance de recul appliquee aux ennemis du groupe apres l'atterrissage force (0 = pas de recul). Leur laisser regagner le Warrior en une seconde suffit a relancer la chaine.")]
        [SerializeField, Min(0f)] private float postLandingEnemyStepBack = 0f;

        private Coroutine _postLandingFreedomRoutine;

        private readonly List<float> _recentForcedLandings = new List<float>();

        [Tooltip("Recalage maximal par pas physique entre le Warrior et la trajectoire de l'arc de saut (0 = illimite, comportement d'origine). L'arc garde sa forme; seul le rattrapage d'un ecart cause par la physique est lisse.")]
        [SerializeField, Min(0f)] private float maxJumpArcRejoinPerStep = 0.12f;

        protected override float MaxJumpArcRejoinPerStep => maxJumpArcRejoinPerStep;

        [Tooltip("Diagnostic: journalise toute etape d'arc de saut qui deplace le Warrior d'au moins cette distance en un pas physique (0 = desactive).")]
        [SerializeField, Min(0f)] private float bigJumpArcStepLogThreshold = 0f;

        protected override float BigJumpArcStepLogThreshold => bigJumpArcStepLogThreshold;

        [Tooltip("Vitesse maximale de la phase de vol libre d'un arc de saut (0 = illimite). Empeche l'arc de se transformer en catapulte si un ecart est injecte dans son calcul de vitesse.")]
        [SerializeField, Min(0f)] private float maxJumpArcSpeed = 30f;

        protected override float MaxJumpArcSpeed => maxJumpArcSpeed;

        [Tooltip("Ecart maximal entre le Warrior et la trajectoire de l'arc avant d'abandonner l'arc et de rendre la main a la physique (0 = ne jamais abandonner). Evite le rappel brutal quand quelque chose l'a deplace pendant le saut.")]
        [SerializeField, Min(0f)] private float maxJumpArcDivergenceBeforeAbort = 1.2f;

        protected override float MaxJumpArcDivergenceBeforeAbort => maxJumpArcDivergenceBeforeAbort;

        [Tooltip("TEMP DIAGNOSTIC: logs every counted bounce and the whole decision of the 'N bounces then land between the enemies' rule (cluster, candidates, why each is rejected, outcome). Turn OFF once the rule is validated.")]
        [SerializeField] private bool logMiddleLandingDecisions = false;

        private float _middleLandingTargetX;
        private Coroutine _middleLandingIgnoreRoutine;
        private readonly Dictionary<int, int> _middleLandingSameEnemyBounces = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _middleLandingLastCountedContact = new Dictionary<int, float>();
        private int _middleLandingBounceCount;
        private float _middleLandingLastContactTime = -999f;
        private float _middleLandingSettleUntil = -999f;
        private Coroutine _middleLandingWatchdogRoutine;
        private readonly List<Enemy> _middleLandingChainEnemies = new List<Enemy>();

        #endregion

        #region Enemy Overlap Recovery (Guaranteed Separation Backstop)

        // ─────────────────────────────────────────────────────────────────────────────
        // This is an independent, callback-free safety authority. Every FixedUpdate it
        // verifies the physical invariant "the Warrior must not remain penetrated inside
        // an enemy". It does NOT replace the existing event-driven bounces (Branch 6/7,
        // Stay fallback, ping-pong, Morvex). It only acts when those missed the contact
        // (fast falls, late/threshold-missed callbacks, fall flags cleared by anti-tunnel,
        // null enemy platform, etc.) AND no other system is intentionally controlling the
        // body (sprint dodge, post-bounce ignore window, hard action lock, hit-stun).
        // ─────────────────────────────────────────────────────────────────────────────

        [Header("Enemy Overlap Recovery (Anti-Stuck Backstop)")]
        [Tooltip("Master switch. Continuously enforces separation from enemies when no other system handled the contact. Acts as a guaranteed rebound backstop.")]
        [SerializeField] private bool enableEnemyOverlapRecovery = true;

        [Tooltip("Penetration depth (meters) that must be exceeded before recovery treats the Warrior as genuinely overlapping an enemy. Filters out resting/surface contact such as standing on an enemy top.")]
        [SerializeField, Min(0.001f)] private float overlapRecoveryPenetrationEpsilon = 0.04f;

        [Tooltip("Extra padding (meters) added around the Warrior collider when searching for overlapping enemies via the physics broadphase.")]
        [SerializeField, Min(0f)] private float overlapRecoveryScanPad = 0.05f;

        [Tooltip("Consecutive physics frames of confirmed penetration on the same enemy before a rebound/nudge fires. Debounces one-frame solver transients to avoid false bounces.")]
        [SerializeField, Min(1)] private int overlapRecoveryDebounceFrames = 2;

        [Tooltip("Maximum per-frame depenetration nudge (meters) applied while a clean bounce cannot start yet (e.g. mid jump-arc) so the Warrior is never left visibly embedded. Capped to prevent launch/jitter.")]
        [SerializeField, Min(0f)] private float overlapRecoveryMaxNudgePerFrame = 0.06f;

        [Tooltip("ON = while the Zalayty different-platform arrival absorber is active (and for a short trailing window after) the overlap guardian stands down. The absorber intentionally PINS the Warrior, which prolongs the arrival overlap; without this the guardian would misread that as a stuck penetration and launch a grounded Warrior with a full bounce arc.")]
        [SerializeField] private bool overlapRecoveryDeferToArrivalAbsorber = true;

        [Tooltip("Extra time (seconds) after the arrival absorber ends during which the overlap guardian still stands down, letting Zalayty's body finish separating naturally before the guardian re-arms.")]
        [SerializeField, Min(0f)] private float overlapRecoveryAbsorberTrailingWindow = 0.08f;

        [Tooltip("ON = the full BounceAndLandAway jump arc is reserved for genuinely airborne / falling overlap cases (the situation it was designed for). When the Warrior is firmly grounded and an enemy merely overlaps him (e.g. an arriving Zalayty), the guardian uses the capped horizontal depenetration nudge instead of launching him into a jump arc.")]
        [SerializeField] private bool overlapRecoveryGroundedAware = true;

        [Tooltip("ON = the airborne full bounce only fires when the Warrior is genuinely DESCENDING onto an enemy (falling / past apex / negative vertical velocity). This prevents a fresh jump arc from launching in the brief window where a previous jump's coroutine has just ended while the Warrior is still airborne over an enemy — the 'double jump on ascent' case. Rising / just-peaked airborne overlaps fall through to the capped nudge instead.")]
        [SerializeField] private bool overlapRecoveryAirborneBounceRequiresDescent = true;

        [Tooltip("Vertical velocity (m/s) at or below which the Warrior is treated as descending for the airborne bounce gate. A small positive value tolerates a near-apex frame; keep close to zero so a clearly rising Warrior is never bounced.")]
        [SerializeField] private float overlapRecoveryDownwardVyThreshold = 0.10f;

        [Tooltip("OFF by default. When ON, re-enables the last-resort capped depenetration nudge for overlaps that the full bounce does not handle (grounded side overlap, or mid jump-arc). It uses a velocity-free rigidbody2.position teleport (NOT MovePosition) so it depenetrates without injecting the residual upward velocity that previously read as a 'double jump'. Turn this ON only if you observe the Warrior getting genuinely stuck embedded inside an enemy with no bounce.")]
        [SerializeField] private bool overlapRecoveryUsePositionTeleportNudge = false;

        [Tooltip("After the Warrior's jump is canceled by hitting a boss (e.g. Zort), the overlap-recovery guardian stands down for this long so the Warrior falls under gravity instead of being launched into a scripted bounce arc while sliding off the boss.")]
        [SerializeField, Min(0f)] private float bossJumpCancelOverlapSuppressSeconds = 0.5f;

        private Enemy _overlapRecoveryEnemy;
        private int _overlapRecoveryFrames;
        private readonly Collider2D[] _overlapRecoveryBuffer = new Collider2D[12];
        private ContactFilter2D _overlapRecoveryFilter;
        private bool _overlapRecoveryFilterReady;

        // While Time.time <= this, the overlap-recovery bounce is suppressed (see CancelControlledJumpIntoFall).
        private float _suppressEnemyOverlapBounceUntil = -999f;

        /// <summary>
        /// Frame-driven separation guardian. Detects genuine penetration into an enemy via
        /// a geometric query (independent of collision callbacks and of the WarriorOverlay
        /// scalar threshold), debounces it, then resolves it through the existing
        /// <see cref="BounceAndLandAway"/> path. If a scripted move currently owns the body,
        /// it applies a capped depenetration nudge instead so overlap is never persistent.
        /// </summary>
        private void EnforceEnemyOverlapRecovery()
        {
            if (!enableEnemyOverlapRecovery) { ResetOverlapRecoveryTracking(); return; }
            if (collider2 == null || rigidbody2 == null || enemyLayer.value == 0)
            {
                ResetOverlapRecoveryTracking();
                return;
            }

            // Respect every window where another system intentionally owns the body or
            // intentionally lets the Warrior pass through enemies. Touching these would
            // break sprint dodge, knockback, stone-repulse, hivernox freeze, death, and
            // the in-flight bounce arc.
            if (_postBounceActive) { ResetOverlapRecoveryTracking(); return; }
            if (_sprintActive) { ResetOverlapRecoveryTracking(); return; }
            if (IsHardActionLocked) { ResetOverlapRecoveryTracking(); return; }
            if (_hitReactRoutine != null) { ResetOverlapRecoveryTracking(); return; }

            // Warrior attack has priority over enemy-overlap separation. A grounded melee attack
            // (M97, P39, Crawling, ...) must be allowed to play its full clip — the depenetration
            // nudge / bounce here would otherwise disturb the Attack1 clip before its hit-frame.
            if (IsAnyAttackPlaying()) { ResetOverlapRecoveryTracking(); return; }

            // Stand down while a boss-jump-cancel fall is in progress: the Warrior must drop under
            // gravity (sliding off the boss via the physics solver), not be launched into a bounce.
            if (Time.time <= _suppressEnemyOverlapBounceUntil) { ResetOverlapRecoveryTracking(); return; }

            // Guard (1): stand down while the Zalayty different-platform arrival absorber owns
            // the body. The absorber intentionally PINS the Warrior to his pre-impact X (and
            // zeroes velocity), which keeps the arriving Zalayty overlapping for several frames.
            // That sustained overlap would otherwise satisfy this guardian's debounce and fire a
            // full bounce arc on an otherwise-grounded, intentionally-held Warrior. The trailing
            // window lets Zalayty's body finish separating before the guardian re-arms.
            if (overlapRecoveryDeferToArrivalAbsorber &&
                (_zalaytyBodyImpactAbsorbActive ||
                 Time.time <= _zalaytyBodyImpactAbsorbUntil + overlapRecoveryAbsorberTrailingWindow))
            {
                ResetOverlapRecoveryTracking();
                return;
            }

            if (!_overlapRecoveryFilterReady)
            {
                _overlapRecoveryFilter = new ContactFilter2D
                {
                    useTriggers = false,
                    useLayerMask = true
                };
                _overlapRecoveryFilter.SetLayerMask(enemyLayer);
                _overlapRecoveryFilterReady = true;
            }

            Bounds b = collider2.bounds;
            Vector2 size = (Vector2)b.size + new Vector2(overlapRecoveryScanPad, overlapRecoveryScanPad);

            int count = Physics2D.OverlapBox(b.center, size, 0f, _overlapRecoveryFilter, _overlapRecoveryBuffer);
            if (count <= 0) { ResetOverlapRecoveryTracking(); return; }

            Enemy deepestEnemy = null;
            float deepestDepth = 0f;
            Vector2 deepestNormal = Vector2.up;

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _overlapRecoveryBuffer[i];
                if (hit == null || hit == collider2) continue;

                Enemy enemy = hit.GetComponentInParent<Enemy>();
                if (enemy == null) continue;

                // Jump pass-through: the Warrior is deliberately phasing through this
                // CrawlingMonster until they separate, so the overlap is intentional here and
                // must not be read as a stuck penetration (no bounce, no nudge).
                if (IsCrawlingJumpPassThroughActiveFor(enemy)) continue;

                // Use the actual overlapping body collider for the depth measurement.
                ColliderDistance2D cd = collider2.Distance(hit);
                if (!cd.isValid || !cd.isOverlapped) continue;

                // cd.distance is negative while overlapped; magnitude is penetration depth.
                float depth = -cd.distance;
                if (depth <= overlapRecoveryPenetrationEpsilon) continue;

                if (depth > deepestDepth)
                {
                    deepestDepth = depth;
                    deepestEnemy = enemy;
                    // cd.normal points from this collider toward the other; pushing the
                    // Warrior along -normal separates him from the enemy.
                    deepestNormal = -cd.normal;
                }
            }

            if (deepestEnemy == null) { ResetOverlapRecoveryTracking(); return; }

            // Debounce: require N consecutive confirmed frames on the SAME enemy so a
            // single-frame solver transient never forces a bounce.
            if (_overlapRecoveryEnemy == deepestEnemy)
            {
                _overlapRecoveryFrames++;
            }
            else
            {
                _overlapRecoveryEnemy = deepestEnemy;
                _overlapRecoveryFrames = 1;
            }

            if (_overlapRecoveryFrames < overlapRecoveryDebounceFrames)
                return;

            // Preferred resolution: the existing, fully-featured rebound. plfExitMode is
            // derived from the live fall flags so an edge/platform-exit fall lands the same
            // way Branch 7 would have. BounceAndLandAway is self-guarded against re-entry.
            if (activesJumpCoroutine == null)
            {
                // Guard (2): the full BounceAndLandAway jump arc is reserved for the case it was
                // designed for — the Warrior is genuinely airborne / falling onto (or stuck on)
                // an enemy. When the Warrior is firmly grounded and an enemy merely overlaps him
                // sideways (e.g. an arriving Zalayty pressing in on the same platform), launching
                // him into a 2.2-height arc is the "unexpected takeoff" the user reported. In that
                // grounded case fall through to the capped horizontal depenetration nudge instead.

                bool fallingOrDescending = IsFallingEdge || IsFallingPlfExit || DescendentPhase;
                bool grounded = CountGroundPoints() > 0;
                bool allowFullBounce = !overlapRecoveryGroundedAware || fallingOrDescending || !grounded;

                if (allowFullBounce)
                {
                    bool plfExitMode = IsFallingEdge || IsFallingPlfExit;

                    BounceAndLandAway(deepestEnemy, plfExitMode);

                    IsFallingEdge = false;
                    IsFallingPlfExit = false;
                    IsFallingHitEnemy = false;
                    IsFallingGrazesEdge = false;

                    ResetOverlapRecoveryTracking();
                    return;
                }

                // Last-resort depenetration backstop (OFF by default). The full bounce above did
                // not apply (grounded side overlap), so without this the Warrior could in theory
                // stay embedded in an enemy. When the teleport nudge is enabled we separate with a
                // capped step, biased to the horizontal axis so a resting/standing contact never
                // pushes him upward.
                //
                // It uses a direct rigidbody2.position write (a teleport) instead of MovePosition:
                // MovePosition on a Dynamic body reaches its target by synthesizing an implicit
                // velocity (~nudge/fixedDeltaTime) that LINGERS afterward — an upward component
                // there read as a "double jump". A position write moves the same distance with
                // ZERO injected velocity. Default OFF because the other systems already separate
                // this case; enable only if the Warrior is seen genuinely stuck inside an enemy.
                if (overlapRecoveryUsePositionTeleportNudge)
                {
                    Vector2 groundedSep = deepestNormal;
                    if (Mathf.Abs(groundedSep.x) > 0.0001f)
                        groundedSep = new Vector2(Mathf.Sign(groundedSep.x), 0f);
                    else
                        groundedSep = groundedSep.sqrMagnitude > 0.0001f ? groundedSep.normalized : Vector2.right;

                    float groundedNudge = Mathf.Min(deepestDepth, overlapRecoveryMaxNudgePerFrame);
                    rigidbody2.position += groundedSep * groundedNudge;
                    Physics2D.SyncTransforms();
                }

                return;
            }

            // A scripted move (jump/bounce arc) currently owns the position via MovePosition,
            // so a fresh bounce cannot cleanly start this frame. Last-resort capped separation
            // along the contact normal so the Warrior is never left visibly embedded (OFF by
            // default). Uses a velocity-free rigidbody2.position teleport rather than MovePosition,
            // so it cannot inject the residual upward velocity that previously read as a double
            // jump during ascent, and it does not override the arc's own MovePosition target.
            if (overlapRecoveryUsePositionTeleportNudge)
            {
                Vector2 sep = deepestNormal.sqrMagnitude > 0.0001f ? deepestNormal.normalized : Vector2.up;
                float nudge = Mathf.Min(deepestDepth, overlapRecoveryMaxNudgePerFrame);
                rigidbody2.position += sep * nudge;
                Physics2D.SyncTransforms();
            }
        }


        private void ResetOverlapRecoveryTracking()
        {
            _overlapRecoveryEnemy = null;
            _overlapRecoveryFrames = 0;
        }

        #endregion

        #region Collision / Bounce / Contact Blocking

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var enemy = collision.collider.GetComponentInParent<Enemy>();

            if (enemy != null && TryStopPlatformStoneRepulseOnEnemyContact(enemy))
            {
                NoteRuleSkip(enemy, "contact consomme par l'arret de repulsion de pierre de plateforme");
                return;
            }

            if (_sprintActive && enemy != null && collision.collider != null)
            {
                NoteRuleSkip(enemy, "sprint actif: le contact ennemi est ignore avant la regle");

                if (_ignoredEnemyCollidersDuringSprint.Add(collision.collider))
                    SetIgnoreWithAllWarriorColliders(collision.collider, true);

                if (collision.otherCollider != null)
                    Physics2D.IgnoreCollision(collision.otherCollider, collision.collider, true);

                return;
            }

            if (enemy != null)
            {
                NotifyZalaytyIfWarriorPressingOnTop(enemy, collision);

                if (TryBreakEnemyTopPingPongTrap(enemy, collision, registerBounceContact: true))
                    return;

                // Hitting a boss (e.g. Zort) while rising in a jump must immediately cancel the
                // jump and drop the Warrior under gravity — never continue the upward arc, hover,
                // or stay stuck in a jump state. Covers both the scripted jump arc and any residual
                // upward velocity; descent (stomp/bounce) is intentionally left to the logic below.
                if (enemy.IsBoss && CountGroundPoints() == 0)
                {
                    bool risingControlledJump =
                        (activesJumpCoroutine != null || IsJumping) && !DescendentPhase;
                    bool risingByVelocity =
                        rigidbody2 != null && rigidbody2.linearVelocity.y > 0.01f;

                    if (risingControlledJump || risingByVelocity)
                    {
                        CancelControlledJumpIntoFall(enemy);
                        return;
                    }
                }

                if (DescendentPhase && CountGroundPoints() == 0)
                {
                    if (ShieldIsUp) DoShieldStomp(enemy);
                    Debug.Log("Warrior hit enemy while descending from edge with no ground points. ShieldIsUp=" + ShieldIsUp);
                    StopJumpTowardCoroutine();
                    BounceAndLandAway(enemy);
                    return;
                }

                if ((IsFallingEdge || IsFallingPlfExit) && WarriorOverlay(enemy))
                {
                    Debug.Log("Warrior hit enemy while falling from edge or platform exit and is overlapping the enemy. IsFallingEdge=" + IsFallingEdge + ", IsFallingPlfExit=" + IsFallingPlfExit);
                    BounceAndLandAway(enemy, plfExitMode: true);
                    IsFallingEdge = false;
                    IsFallingPlfExit = false;
                    IsFallingHitEnemy = false;
                    return;
                }

                if (CountGroundPoints() == 0 && OnCollisionHitBottom(collision))
                {
                    IsFallingHitEnemy = true;
                    StopJumpTowardCoroutine();
                    WaitAnimationDisplay();
                }

                if (!_postBounceActive && !IsFalling && activesJumpCoroutine == null)
                {
                    StopRunningOnEnemyContact(enemy);
                }

                return;
            }

            if (collision.gameObject.layer == LayerMask.NameToLayer("PlatformLayer"))
                _cmp = 0;
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            var enemy = collision.collider.GetComponentInParent<Enemy>();

            if (enemy != null && TryStopPlatformStoneRepulseOnEnemyContact(enemy))
                return;

            if (_sprintActive && collision.collider != null)
            {
                if (enemy != null && collider2 != null)
                {
                    if (_ignoredEnemyCollidersDuringSprint.Add(collision.collider))
                        Physics2D.IgnoreCollision(collider2, collision.collider, true);
                    return;
                }
            }

            // Continuously refresh Zalayty's press grace while the Warrior rests on his top,
            // so the one-way platform never reads the downward push as an edge loss.
            if (enemy != null)
                NotifyZalaytyIfWarriorPressingOnTop(enemy, collision);

            // Important:
            // The generic ping-pong guard must run BEFORE enemy.CurrentplatForm == null return.
            // Some enemies may temporarily lose CurrentplatForm during moving-platform / edge / jump transitions,
            // but their top collider can still catch Warrior and create the ping-pong loop.
            if (enemy != null && TryBreakEnemyTopPingPongTrap(enemy, collision, registerBounceContact: false))
                return;

            // ── Morvex-top stuck guard ────────────────────────────────────────────────
            // Morvex flies with gravityScale = 0 and moves via transform.position, so
            // the warrior can land on its NormalCollider top and get stuck there if none
            // of the normal DescendentPhase / IsFallingEdge conditions were set. We allow
            // at most 2 consecutive physics frames of contact before forcing a bounce.
            if (enemy != null && enemy is MorvexMonster && WarriorSitsOnEnemyTop(enemy))
            {
                if (_morvexTopContactEnemy != enemy)
                {
                    _morvexTopContactEnemy = enemy;
                    _morvexTopContactFrames = 0;
                }

                _morvexTopContactFrames++;

                if (_morvexTopContactFrames >= 2)
                {
                    _morvexTopContactFrames = 0;
                    _morvexTopContactEnemy = null;

                    BounceAndLandAway(enemy);
                    return;
                }

                // Frame 1: not yet at threshold — do nothing else this frame.
                return;
            }
            else if (_morvexTopContactEnemy != null && _morvexTopContactEnemy == enemy)
            {
                // Contact has shifted away from yMax — reset counter.
                _morvexTopContactFrames = 0;
                _morvexTopContactEnemy = null;
            }
            // ─────────────────────────────────────────────────────────────────────────

            if (enemy == null || enemy.CurrentplatForm == null) return;

            if (!_postBounceActive && !IsFalling && activesJumpCoroutine == null)
            {
                StopRunningOnEnemyContact(enemy);
                if (_blockedByEnemyContact) return;
            }

            if (_postBounceActive) return;

            if (WarriorOverlay(enemy) && CountGroundPoints() == 0)
                BounceAndLandAway(enemy);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            var enemy = collision.collider.GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                _blockAction = false;
                ClearEnemyContactBlock(enemy);

                if (enemy == _singleEnemyTopContactEnemy)
                    ResetSingleEnemyTopStuckTracking();

                // ── Morvex-top counter reset ──────────────────────────────────────────
                if (enemy == _morvexTopContactEnemy)
                {
                    _morvexTopContactFrames = 0;
                    _morvexTopContactEnemy = null;
                }
                // ─────────────────────────────────────────────────────────────────────

                return;
            }

            if (collision.gameObject.layer == LayerMask.NameToLayer("PlatformLayer"))
                _cmp = 0;
        }

        private bool TryBreakEnemyTopPingPongTrap(Enemy enemy, Collision2D collision, bool registerBounceContact)
        {
            if (!enableEnemyTopPingPongEscape)
            {
                if (registerBounceContact)
                    NoteRuleSkip(enemy, "echappatoire de ping-pong desactivee sur le Warrior");

                return false;
            }

            if (enemy == null || collider2 == null)
                return false;

            if (!CanEnemyParticipateInTopPingPong(enemy))
            {
                if (registerBounceContact)
                    NoteRuleSkip(enemy, "ennemi non eligible: " + DescribeEnemyParticipation(enemy));

                return false;
            }

            bool warriorOnEnemyTop =
                WarriorSitsOnEnemyTop(enemy) ||
                WarriorOverlay(enemy) ||
                IsWarriorLandingOnEnemyTopByCollision(collision);

            if (!warriorOnEnemyTop)
            {
                if (registerBounceContact)
                    NoteRuleSkip(enemy, "pas reconnu sur le haut de l'ennemi (assis=" +
                                        WarriorSitsOnEnemyTop(enemy) + ", chevauchement=" +
                                        WarriorOverlay(enemy) + ", contact=" +
                                        IsWarriorLandingOnEnemyTopByCollision(collision) + ")");

                return false;
            }

            // A forced middle landing was just committed: hold it. No escape may relaunch the
            // Warrior from the surface he was placed on before the settle window expires — and,
            // above all, for as long as the forced descent is still running. The fixed 0.8 s
            // window alone was shorter than some descents: an enemy OUTSIDE the ignored group
            // caught him mid-fall, started a new chain, and the pass-through was then restored
            // in mid-air on timeout (measured 3 restorations out of 6 with groundPoints=0).
            if (Time.time < _middleLandingSettleUntil || _middleLandingIgnoreRoutine != null)
            {
                if (registerBounceContact)
                    NoteRuleSkip(enemy, "garde anti-relance de l'atterrissage force (descente en cours=" +
                                        (_middleLandingIgnoreRoutine != null) + ")");

                return true;
            }

            // ── Règle absolue "N rebonds puis atterrissage au milieu" ────────────────────
            // Independent of the enemy type, of the enemy identity and of the 1.2s window:
            // after N ping-pong bounces on enemy tops without a safe landing spot of his own,
            // the Warrior is forced down in the MIDDLE of that enemy group instead of bouncing
            // again. Skipped while the deeper absolute failsafe already owns the resolution.
            if (_absoluteFailsafeActive && registerBounceContact)
                NoteRuleSkip(enemy, "failsafe absolu actif: la regle des rebonds est court-circuitee");

            if (!_absoluteFailsafeActive &&
                UpdateAndCheckEnemyTopMiddleLanding(enemy, registerBounceContact))
                return true;

            // ── Couche 1 + résolution absolue ─────────────────────────────────────────────
            // Absolute, geometry-independent, identity-independent backstop. Runs BEFORE the
            // windowed per-enemy chain logic below. It only takes over when that logic has
            // provably failed to resolve the situation (perpetual >2-enemy ping-pong over no
            // reachable ground — the Zalayty soft-lock). In normal 1-2 enemy cases the chain
            // resolves long before these thresholds, so this is a no-op there.
            if (UpdateAndCheckEnemyTopPingPongAbsoluteFailsafe(registerBounceContact))
                return true;

            List<Enemy> nearbyEnemies = GetNearbyEnemiesForTopTrap(enemy);

            // The multi-enemy chain rule below exists only for the top-bounce bridge.
            // For a single enemy (including a single Zalayty), the Warrior can still get
            // pinned/stuck on one enemy top — handle that with the single-enemy escape.
            if (nearbyEnemies.Count < 2)
                return TryEscapeSingleEnemyTopStuck(enemy);

            RegisterEnemyTopTrapBounce(enemy, registerBounceContact);

            bool touchedAtLeastTwoDifferentEnemies =
                _enemyTopTrapTouchedIds.Count >= 2;

            bool currentEnemyWasAlreadyUsed =
                _enemyTopTrapTouchedIds.Contains(enemy.GetInstanceID()) &&
                !IsLatestRegisteredEnemyUnique(enemy, nearbyEnemies);

            bool hasUnbouncedNearbyEnemy =
                HasUnbouncedEnemyTopCandidate(nearbyEnemies);

            bool stalePostBounce =
                _postBounceActive &&
                Time.time >= _postBounceStartTime + enemyTopTrapMinStuckTime;

            bool staleControlledJump =
                activesJumpCoroutine != null &&
                CountGroundPoints() == 0 &&
                Time.time >= _postBounceStartTime + enemyTopTrapMinStuckTime;

            if (enemyTopTrapBounceEachEnemyOnceBeforeLanding)
            {
                // Rule:
                // Each nearby enemy top-bound may be used once as a bounce pad.
                // If there is still another nearby enemy that was not used, bounce away from this one.
                if (IsLatestRegisteredEnemyUnique(enemy, nearbyEnemies) && hasUnbouncedNearbyEnemy)
                {
                    ContinueEnemyTopBounceChainFrom(enemy, nearbyEnemies);
                    return true;
                }

                // Chain exhausted: each nearby enemy top has been used once as a bounce pad and
                // there is no safe land left. New rule — prefer dropping the Warrior straight onto
                // the ground BETWEEN the two bracketing enemies instead of one more final bounce.
                // Falls back to the final bounce (which uses the current enemy top as a landing
                // aid) only when there is no two-sided bracket or a hole sits between the enemies.
                if (IsLatestRegisteredEnemyUnique(enemy, nearbyEnemies) &&
                    touchedAtLeastTwoDifferentEnemies &&
                    !hasUnbouncedNearbyEnemy)
                {
                    if (!TryLandBetweenBracketingEnemies(nearbyEnemies))
                        FinishEnemyTopBounceChainWithFinalBounce(enemy, nearbyEnemies);
                    return true;
                }

                // If Warrior touches an enemy top already used in the same chain, do not bounce again.
                // This is the hard anti-loop rule: one enemy top = one bounce maximum per chain.
                if (currentEnemyWasAlreadyUsed &&
                    touchedAtLeastTwoDifferentEnemies &&
                    (_postBounceActive || stalePostBounce || staleControlledJump || registerBounceContact))
                {
                    BreakEnemyTopPingPongTrap(nearbyEnemies);
                    return true;
                }
            }

            // Fallback safety:
            // If for any reason the chain mode above does not finish the situation,
            // keep the old threshold behavior as a last resort.
            bool repeatedBounce =
                _enemyTopTrapBounceCount >= enemyTopTrapBounceThreshold;

            bool confirmedMultiEnemyTopPingPong =
                repeatedBounce &&
                touchedAtLeastTwoDifferentEnemies;

            bool staleMultiEnemyTrap =
                touchedAtLeastTwoDifferentEnemies &&
                (stalePostBounce || staleControlledJump);

            if (!confirmedMultiEnemyTopPingPong && !staleMultiEnemyTrap)
                return false;

            BreakEnemyTopPingPongTrap(nearbyEnemies);
            return true;
        }

        /// <summary>
        /// Single-enemy counterpart to the multi-enemy ping-pong escape. If the Warrior
        /// floats on ONE enemy's top bound (no ground support of his own) for longer than
        /// <see cref="singleEnemyTopStuckTime"/>, force a single clean bounce-away so he
        /// cannot stay pinned on top of that enemy (e.g. a single Zalayty whose
        /// CurrentplatForm is momentarily null, or a stale post-bounce contact).
        /// Returns true only when it actually performs the bounce.
        /// Caller guarantees the Warrior is already on this enemy's top bound.
        /// </summary>
        private bool TryEscapeSingleEnemyTopStuck(Enemy enemy)
        {
            if (!enableSingleEnemyTopStuckEscape)
                return false;

            if (enemy == null || collider2 == null)
                return false;

            if (!CanEnemyParticipateInTopPingPong(enemy))
            {
                ResetSingleEnemyTopStuckTracking();
                return false;
            }

            // With his own ground support the Warrior is not floating-stuck; he can walk off.
            // Only treat a true no-ground rest on the enemy top as a stuck candidate.
            if (CountGroundPoints() > 0)
            {
                ResetSingleEnemyTopStuckTracking();
                return false;
            }

            // Begin / continue timing continuous top contact with THIS single enemy.
            if (_singleEnemyTopContactEnemy != enemy)
            {
                _singleEnemyTopContactEnemy = enemy;
                _singleEnemyTopContactStartedAt = Time.time;
                return false;
            }

            if (Time.time < _singleEnemyTopContactStartedAt + singleEnemyTopStuckTime)
                return false;

            // Stuck on a single enemy top for too long → force one clean bounce-away.
            ResetSingleEnemyTopStuckTracking();

            if (_postBounceActive)
                EndPostBounce();

            BounceAndLandAway(enemy);
            return true;
        }

        private void ResetSingleEnemyTopStuckTracking()
        {
            _singleEnemyTopContactEnemy = null;
            _singleEnemyTopContactStartedAt = -999f;
        }

        // ── Enemy Top Ping-Pong ABSOLUTE Failsafe ────────────────────────────────────────
        // Couche 1: absolute, non-windowed, identity-independent accounting. Returns true
        // once it has taken over the situation (caller must stop all other bounce handling).
        private bool UpdateAndCheckEnemyTopPingPongAbsoluteFailsafe(bool registerBounceContact)
        {
            if (!enableEnemyTopPingPongAbsoluteFailsafe)
                return false;

            // Already resolving: swallow every further top contact until the routine ends.
            // Stale-guard: if the routine reference is gone (e.g. the Warrior was
            // disabled/re-enabled by a retry mid-resolution) the flag would otherwise stay
            // stuck and swallow every bounce forever — self-heal instead of freezing.
            if (_absoluteFailsafeActive)
            {
                if (_absoluteFailsafeRoutine != null)
                    return true;

                _absoluteFailsafeActive = false;
            }

            // Genuine own-ground support ⇒ not trapped. Reset and let the normal logic run.
            if (CountGroundPoints() > 0)
            {
                ResetEnemyTopPingPongAbsoluteFailsafe();
                return false;
            }

            // Self-clean: a long gap since the last top contact means any partial count
            // belonged to an unrelated, already-resolved encounter.
            if (Time.time - _absoluteFailsafeLastContactTime > absoluteFailsafeIdleResetSeconds)
                ResetEnemyTopPingPongAbsoluteFailsafe();

            _absoluteFailsafeLastContactTime = Time.time;

            // Continuous "pinned on tops with no own ground" timer.
            if (_absoluteFailsafeStuckSince < 0f)
                _absoluteFailsafeStuckSince = Time.time;

            // Count only real new contacts (OnCollisionEnter), never per-frame Stay callbacks.
            if (registerBounceContact)
                _absoluteFailsafeContactCount++;

            bool tooManyContacts = _absoluteFailsafeContactCount >= absoluteFailsafeContactLimit;

            // The stuck timer is wall-clock and starts on the FIRST top contact, while the
            // "N bounces then land between the enemies" rule needs one bounce arc (~0.6 s) per
            // bounce. At 1.5 s it therefore fired between the 2nd and the 3rd bounce and stole
            // the resolution from the rule (measured on AgeOfIce: bounces=2, failsafe active).
            // The rule owns the first N bounces; the timer only backs it up once it has had its
            // chance. The contact limit stays unconditional — it sits above the bounce limit.
            bool middleLandingRuleStillOwed =
                enableEnemyTopMiddleLandingAfterBounces &&
                _middleLandingBounceCount < enemyTopMiddleLandingBounceLimit;

            bool stuckTooLong =
                !middleLandingRuleStillOwed &&
                Time.time - _absoluteFailsafeStuckSince >= absoluteFailsafeStuckSeconds;

            if (!tooManyContacts && !stuckTooLong)
                return false;

            TriggerEnemyTopPingPongAbsoluteFailsafe();
            return true;
        }

        private void ResetEnemyTopPingPongAbsoluteFailsafe()
        {
            _absoluteFailsafeContactCount = 0;
            _absoluteFailsafeStuckSince = -1f;
        }

        private void TriggerEnemyTopPingPongAbsoluteFailsafe()
        {
            _absoluteFailsafeActive = true;
            ResetEnemyTopPingPongAbsoluteFailsafe();
            ResetEnemyTopTrapTracking();

            // Tear down any scripted motion / post-bounce / jump state that owns the body.
            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();
            if (_postBounceActive)
                EndPostBounce();

            _postBounceActive = false;
            _lastBouncedEnemy = null;
            _blockAction = false;

            IsFallingEdge = false;
            IsFallingPlfExit = false;
            IsFallingHitEnemy = false;
            IsFallingGrazesEdge = false;

            if (_absoluteFailsafeRoutine != null)
                StopCoroutine(_absoluteFailsafeRoutine);

            _absoluteFailsafeRoutine =
                StartCoroutine(EnemyTopPingPongAbsoluteFailsafeRoutine());
        }

        private IEnumerator EnemyTopPingPongAbsoluteFailsafeRoutine()
        {
            List<Enemy> allEnemies = GetAllActiveTopPingPongEnemies();

            // Phase 1: ignore EVERY enemy collider (absolute, not a snapshot) so nothing can
            // re-catch a top while we resolve.
            SetIgnoreNearbyEnemies(allEnemies, true);

            // Type 2 hybride: real ground within reach directly below the Warrior?
            bool groundBelow = HasGroundBelowWithin(absoluteFailsafeGroundSearchDistance);

            bool landedNaturally = false;

            if (groundBelow)
            {
                // Phase-through: force a clean vertical fall and wait for a genuine ground landing.
                DescendentPhase = true;
                CanMove = true;
                CanAttackWarrior = true;

                if (rigidbody2 != null)
                {
                    RigidbodyConstraints2D constraints = rigidbody2.constraints;
                    constraints &= ~RigidbodyConstraints2D.FreezePositionY;
                    constraints |= RigidbodyConstraints2D.FreezeRotation;
                    rigidbody2.constraints = constraints;

                    rigidbody2.gravityScale =
                        Mathf.Max(rigidbody2.gravityScale, enemyTopTrapFallGravity);

                    Vector2 velocity = rigidbody2.linearVelocity;
                    velocity.x = 0f;
                    if (velocity.y > -enemyTopTrapMinDownVelocity)
                        velocity.y = -enemyTopTrapMinDownVelocity;
                    rigidbody2.linearVelocity = velocity;
                    rigidbody2.WakeUp();
                }

                JumpAnimationDisplay();

                float timeoutAt = Time.time + absoluteFailsafePhaseThroughMaxSeconds;
                while (Time.time < timeoutAt)
                {
                    if (CountGroundPoints() > 0)
                    {
                        landedNaturally = true;
                        break;
                    }
                    yield return new WaitForFixedUpdate();
                }
            }

            // Phase 2 (fallback): no reachable ground, or phase-through timed out → teleport
            // to the last known safe standing position.
            if (!landedNaturally)
                TeleportToLastSafePositionForFailsafe();

            // Keep enemies ignored a short grace so the landing/teleport cannot be re-trapped,
            // then restore collisions once the Warrior is actually clear.
            float restoreTimeoutAt = Time.time + enemyTopTrapIgnoreCollisionTime;
            while (Time.time < restoreTimeoutAt)
            {
                if (AreWarriorAndNearbyEnemiesClear(allEnemies))
                    break;
                yield return new WaitForFixedUpdate();
            }

            SetIgnoreNearbyEnemies(allEnemies, false);

            ResetEnemyTopPingPongAbsoluteFailsafe();
            _absoluteFailsafeLastContactTime = -999f;
            _absoluteFailsafeActive = false;
            _absoluteFailsafeRoutine = null;
        }

        private List<Enemy> GetAllActiveTopPingPongEnemies()
        {
            List<Enemy> result = new List<Enemy>();
            Enemy[] all = FindObjectsByType<Enemy>(FindObjectsSortMode.None);

            for (int i = 0; i < all.Length; i++)
            {
                Enemy e = all[i];
                if (e == null || !e.gameObject.activeInHierarchy)
                    continue;

                result.Add(e);
            }

            return result;
        }

        private bool HasGroundBelowWithin(float distance)
        {
            if (collider2 == null || PlatformLayer.value == 0)
                return false;

            Vector2 origin = new Vector2(collider2.bounds.center.x, collider2.bounds.min.y);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, distance, PlatformLayer);
            return hit.collider != null;
        }

        private void TeleportToLastSafePositionForFailsafe()
        {
            // Only teleport to a position we trust as a real, seated platform spot.
            if (LastSafePlatform == null)
            {
                // No trusted safe spot: fall back to a plain downward break so the Warrior at
                // least leaves the cluster under gravity instead of freezing.
                if (rigidbody2 != null)
                {
                    RigidbodyConstraints2D constraints = rigidbody2.constraints;
                    constraints &= ~RigidbodyConstraints2D.FreezePositionY;
                    constraints |= RigidbodyConstraints2D.FreezeRotation;
                    rigidbody2.constraints = constraints;

                    rigidbody2.gravityScale =
                        Mathf.Max(rigidbody2.gravityScale, enemyTopTrapFallGravity);

                    Vector2 v = rigidbody2.linearVelocity;
                    v.x = 0f;
                    if (v.y > -enemyTopTrapMinDownVelocity)
                        v.y = -enemyTopTrapMinDownVelocity;
                    rigidbody2.linearVelocity = v;
                    rigidbody2.WakeUp();
                }

                return;
            }

            if (rigidbody2 != null)
            {
                rigidbody2.position = LastSafePosition;
                Physics2D.SyncTransforms();
            }
            else
            {
                transform.position = LastSafePosition;
            }

            // Canonical safe-state reset (zeroes velocity, restores gravity/constraints, idle anim).
            PrepareForSafeRespawn();

            CurrentplatForm = LastSafePlatform;
        }

        // Enemy Top Ping-Pong: forced landing BETWEEN the enemies after N bounces.
        // Absolute rule requested for every enemy type: the Warrior may chain at most
        // <see cref="enemyTopMiddleLandingBounceLimit"/> top-bound bounces. If he still has no
        // landing spot of his own at that point (zero ground points), he stops bouncing and is
        // dropped between the enemies. Returns true once the landing was committed (the caller
        // must stop every other bounce handling for this contact).
        private bool UpdateAndCheckEnemyTopMiddleLanding(Enemy enemy, bool registerBounceContact)
        {
            if (!enableEnemyTopMiddleLandingAfterBounces)
            {
                if (registerBounceContact)
                    NoteRuleSkip(enemy, "regle des rebonds desactivee dans l'Inspector");

                return false;
            }

            if (enemy == null || collider2 == null)
                return false;

            // Real ground under his own feet = he found a safe landing: rule not applicable.
            if (CountGroundPoints() > 0)
            {
                ResetEnemyTopMiddleLandingTracking();
                return false;
            }

            // NO idle wipe while airborne. The rule counts bounces "sans toucher le sol", so the
            // ONLY thing that ends a chain is the real ground contact handled just above.
            // The previous time window (1.2 s serialized) wiped the counter between two bounces —
            // measured gaps of 1.50 s, 3.06 s and 3.70 s with groundPoints=0 the whole time — so
            // the Warrior could ping-pong forever without ever reaching the 3rd bounce. That is
            // exactly the time-windowed accounting flaw already fixed once on the absolute
            // failsafe; a longer window would only make it rarer, not correct.

            _middleLandingLastContactTime = Time.time;

            if (!_middleLandingChainEnemies.Contains(enemy))
                _middleLandingChainEnemies.Add(enemy);

            // Only genuine new contacts (OnCollisionEnter) count as a bounce; per-frame Stay
            // callbacks must never inflate the count.
            if (!registerBounceContact)
                return false;

            int enemyId = enemy.GetInstanceID();

            // Anti double-comptage d'une MEME prise de contact. Deux sources l'alimentent
            // maintenant (le callback de collision et le detecteur geometrique), et le callback
            // lui-meme se declenche par PAIRE de colliders: mesure en jeu, deux contacts sur le
            // meme ennemi au meme instant (t=0,00s) et a la meme position, donc un rebond compte
            // double. Une vraie chaine de rebonds est espacee d'au moins 0,2 s (arc de saut).
            if (_middleLandingLastCountedContact.TryGetValue(enemyId, out float lastCounted) &&
                Time.time - lastCounted < middleLandingSameContactDebounce)
            {
                if (logMiddleLandingDecisions)
                    Debug.Log($"[MIDLAND] contact ignore sur {enemy.name} : meme prise de contact " +
                              $"deja comptee il y a {(Time.time - lastCounted):F3}s", this);

                return false;
            }

            _middleLandingLastCountedContact[enemyId] = Time.time;

            _middleLandingBounceCount++;

            _middleLandingSameEnemyBounces.TryGetValue(enemyId, out int sameEnemyBounces);
            sameEnemyBounces++;
            _middleLandingSameEnemyBounces[enemyId] = sameEnemyBounces;

            NotePingPongContact(enemy);

            if (logMiddleLandingDecisions)
                Debug.Log($"[MIDLAND] rebond #{_middleLandingBounceCount}/{enemyTopMiddleLandingBounceLimit} sur {enemy.name} " +
                          $"(sur cet ennemi: {sameEnemyBounces}/{enemyTopMiddleLandingSameEnemyBounceLimit}) " +
                          $"| pos=({transform.position.x:F2}, {transform.position.y:F2}) groundPoints={CountGroundPoints()} " +
                          $"failsafeActif={_absoluteFailsafeActive}", this);

            // Seconde règle absolue, par ennemi: jamais plus de N rebonds sur le MÊME ennemi sans
            // toucher le sol. Elle peut se déclencher avant la limite globale (un Warrior qui
            // retombe deux fois de suite sur la même tête n'a visiblement pas de porte de sortie),
            // et elle se résout exactement comme elle: atterrissage forcé entre les ennemis.
            if (sameEnemyBounces >= enemyTopMiddleLandingSameEnemyBounceLimit)
            {
                if (logMiddleLandingDecisions)
                    Debug.Log($"[MIDLAND] LIMITE MEME ENNEMI atteinte sur {enemy.name} " +
                              $"({sameEnemyBounces}/{enemyTopMiddleLandingSameEnemyBounceLimit}) " +
                              $"| compteur global={_middleLandingBounceCount}/{enemyTopMiddleLandingBounceLimit}", this);

                return ForceLandBetweenEnemies(enemy);
            }

            if (_middleLandingBounceCount < enemyTopMiddleLandingBounceLimit)
                return false;

            return ForceLandBetweenEnemies(enemy);
        }

        private void ResetEnemyTopMiddleLandingTracking()
        {
            if (logMiddleLandingDecisions && _middleLandingBounceCount > 0)
                Debug.Log($"[MIDLAND] compteur remis a 0 (etait {_middleLandingBounceCount}) " +
                          $"| groundPoints={CountGroundPoints()} depuisDernierContact={(Time.time - _middleLandingLastContactTime):F2}s", this);

            _middleLandingBounceCount = 0;
            _middleLandingChainEnemies.Clear();
            _middleLandingSameEnemyBounces.Clear();
        }

        /// <summary>
        /// Drops the Warrior BETWEEN the enemies of the group he is ping-ponging on: the gap
        /// with real ground under it (widest first) is chosen, the Warrior is aligned with it
        /// horizontally and falls there under gravity. The descent stays a normal physics fall —
        /// never a vertical teleport — so the platform landing pipeline (one-way plf triggers +
        /// destination anti-tunneling) seats him on top instead of letting him cross the
        /// platform downwards. When no gap of the group has ground under it, the deeper absolute
        /// failsafe resolves it; the Warrior is never parked on an enemy top.
        /// </summary>
        private bool ForceLandBetweenEnemies(Enemy touchedEnemy)
        {
            // La regle peut tourner en rond: le Warrior se pose entre les ennemis, le meme P39 le
            // renvoie aussitot en l'air, la chaine repart, et comme le compteur se remet a zero au
            // contact du sol le failsafe absolu — qui existe pour ce cas — n'est jamais atteint.
            // Observe en jeu: cycles consecutifs "rebond #1/3 -> #2/3 -> #3/3 -> descente forcee"
            // tous sur P39, le Warrior bloque au meme endroit sans jamais pouvoir repartir.
            if (HasForcedLandingLoopedTooOften())
            {
                if (logMiddleLandingDecisions)
                    Debug.Log($"[MIDLAND] {_recentForcedLandings.Count} declenchements de la regle en " +
                              $"{middleLandingRepeatWindowSeconds:F0}s : la regle tourne en rond " +
                              $"(toujours {(touchedEnemy != null ? touchedEnemy.name : "?")}), " +
                              $"passage en traversee + ennemis sonnes.", this);

                _recentForcedLandings.Clear();
                ResetEnemyTopMiddleLandingTracking();

                // Le failsafe absolu seul ne suffit pas ici: il repose le Warrior au meme endroit
                // et l'ennemi le relance dans la seconde (mesure: bascule sur le failsafe puis
                // "rebond #1/3" immediatement, failsafeActif deja retombe a False). Il faut retirer
                // la CAUSE pendant un instant: traverser les corps fautifs et les sonner, le temps
                // que le Warrior reprenne la main.
                NotePingPongLoopEscape();

                StartCoroutine(EscapeMiddleLandingLoop(BuildMiddleLandingCluster(touchedEnemy)));
                return true;
            }

            // Compter les DECLENCHEMENTS de la regle, pas seulement les descentes engagees: avec le
            // plafond de distance, beaucoup de chaines finissent en refus de candidat et ne
            // produisent aucune descente, alors que du point de vue du joueur la regle a bien
            // tourne une fois de plus sans rien resoudre.
            _recentForcedLandings.Add(Time.time);

            List<Enemy> clusterEnemies = BuildMiddleLandingCluster(touchedEnemy);

            NotePingPongRuleTriggered(touchedEnemy, clusterEnemies.Count);

            if (clusterEnemies.Count == 0)
            {
                if (logMiddleLandingDecisions)
                    Debug.Log("[MIDLAND] LIMITE ATTEINTE mais cluster VIDE -> regle abandonnee, le ping-pong continue.", this);

                ResetEnemyTopMiddleLandingTracking();
                return false;
            }

            List<float> candidateXs = BuildBetweenEnemiesLandingCandidates(clusterEnemies);

            if (logMiddleLandingDecisions)
                Debug.Log($"[MIDLAND] LIMITE ATTEINTE: cluster={clusterEnemies.Count} ennemis, {candidateXs.Count} candidat(s), " +
                          $"ecart minimal requis={RequiredGapWidthForMiddleLanding():F2}", this);

            for (int i = 0; i < candidateXs.Count; i++)
            {
                if (logMiddleLandingDecisions)
                    Debug.Log($"[MIDLAND]   candidat x={candidateXs[i]:F2} solEnDessous={HasGroundBelowAt(candidateXs[i])} " +
                              $"snapLibre={IsBetweenEnemiesSnapClear(candidateXs[i])}", this);

                // A gap is usable only when real ground waits under it AND the horizontal snap
                // does not drop the Warrior inside a platform collider (that would make the
                // physics depenetration eject him through it).
                if (!HasGroundBelowAt(candidateXs[i]) ||
                    !IsBetweenEnemiesSnapClear(candidateXs[i]))
                    continue;

                float warriorX = rigidbody2 != null ? rigidbody2.position.x : transform.position.x;
                float travelX = Mathf.Abs(candidateXs[i] - warriorX);

                if (travelX > middleLandingMaxSnapDistanceX)
                {
                    NotePingPongCandidateRefused();

                    if (logMiddleLandingDecisions)
                        Debug.Log($"[MIDLAND]   candidat x={candidateXs[i]:F2} REFUSE : {travelX:F2} unites " +
                                  $"a parcourir, au-dela du plafond d'absurdite " +
                                  $"({middleLandingMaxSnapDistanceX:F2}).", this);

                    continue;
                }

                if (logMiddleLandingDecisions)
                    Debug.Log($"[MIDLAND]   -> RETENU x={candidateXs[i]:F2} : descente forcee entre les ennemis.", this);

                NotePingPongForcedLanding(candidateXs[i], touchedEnemy);

                CommitLandingBetweenEnemies(candidateXs[i], clusterEnemies);
                return true;
            }

            // Aucun point d'arrivee utilisable. Le failsafe absolu SEUL ne resout pas ce cas: il
            // repose le Warrior au meme endroit et l'ennemi le reprend dans la seconde (mesure:
            // episode avec 3 candidats refuses, 0 descente, et le ping-pong qui continue). On
            // applique donc la vraie sortie: traversee des corps fautifs + ennemis sonnes.
            if (logMiddleLandingDecisions)
                Debug.Log("[MIDLAND]   -> aucun candidat valable : traversee + ennemis sonnes.", this);

            ResetEnemyTopMiddleLandingTracking();
            NotePingPongLoopEscape();
            StartCoroutine(EscapeMiddleLandingLoop(clusterEnemies));
            return true;
        }

        /// <summary>
        /// Sortie de boucle: les ennemis responsables sont traverses ET sonnes pendant un court
        /// instant, de sorte que le Warrior retombe au sol et puisse repartir au lieu d'etre
        /// renvoye en l'air par le meme corps a chaque cycle.
        /// </summary>
        private IEnumerator EscapeMiddleLandingLoop(List<Enemy> loopEnemies)
        {
            if (loopEnemies == null || loopEnemies.Count == 0)
                yield break;

            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();

            if (_postBounceActive)
                EndPostBounce();

            _postBounceActive = false;
            _lastBouncedEnemy = null;
            _blockAction = false;

            SetIgnoreNearbyEnemies(loopEnemies, true);

            for (int i = 0; i < loopEnemies.Count; i++)
            {
                Enemy enemy = loopEnemies[i];

                if (enemy == null)
                    continue;

                enemy.ApplyStun(middleLandingLoopEscapeSeconds);
                enemy.StopMoveTowardCoroutine();
            }

            NotifyNearbyEnemiesToSideStep(loopEnemies);

            if (rigidbody2 != null)
            {
                rigidbody2.gravityScale = normalGravityScale;

                Vector2 velocity = rigidbody2.linearVelocity;
                if (velocity.y > 0f)
                    velocity.y = 0f;
                rigidbody2.linearVelocity = velocity;
            }

            yield return new WaitForSeconds(middleLandingLoopEscapeSeconds);

            SetIgnoreNearbyEnemies(loopEnemies, false);

            if (logMiddleLandingDecisions)
                Debug.Log($"[MIDLAND]   sortie de boucle terminee | pos=" +
                          $"({transform.position.x:F2}, {transform.position.y:F2}) " +
                          $"pointsSol={CountGroundPoints()}", this);
        }

        /// <summary>
        /// True quand des atterrissages forces s'enchainent dans la fenetre: la regle ne resout
        /// plus rien, elle repose simplement le Warrior a portee du meme ennemi.
        /// </summary>
        private bool HasForcedLandingLoopedTooOften()
        {
            float since = Time.time - middleLandingRepeatWindowSeconds;

            for (int i = _recentForcedLandings.Count - 1; i >= 0; i--)
            {
                if (_recentForcedLandings[i] < since)
                    _recentForcedLandings.RemoveAt(i);
            }

            return _recentForcedLandings.Count >= middleLandingRepeatEscalationCount;
        }

        /// <summary>
        /// Horizontal landing candidates between the enemies, best first: the middle of each gap
        /// separating two adjacent enemies (widest gap first, so the Warrior is dropped where he
        /// really fits), then the middle of the whole group as a last resort.
        /// </summary>
        private List<float> BuildBetweenEnemiesLandingCandidates(List<Enemy> clusterEnemies)
        {
            List<float> candidates = new List<float>();

            List<Collider2D> colliders = new List<Collider2D>();
            for (int i = 0; i < clusterEnemies.Count; i++)
            {
                Collider2D candidateCollider = GetEnemyTopTrapCollider(clusterEnemies[i]);
                if (candidateCollider != null)
                    colliders.Add(candidateCollider);
            }

            if (colliders.Count == 0)
                return candidates;

            colliders.Sort((a, b) => a.bounds.center.x.CompareTo(b.bounds.center.x));

            List<float> gapCenters = new List<float>();
            List<float> gapWidths = new List<float>();

            for (int i = 0; i < colliders.Count - 1; i++)
            {
                float leftEdge = colliders[i].bounds.max.x;
                float rightEdge = colliders[i + 1].bounds.min.x;

                float gapWidth = rightEdge - leftEdge;

                // A packed cluster has NEGATIVE gaps (the enemies overlap): measured -1.56 and
                // -0.05 on AgeOfIce for a 1.90 wide Warrior. Their "middle" sits inside an enemy
                // body, the X snap dropped him in there, and the physics depenetration ejected
                // him 4.5 units sideways — he landed BESIDE the group instead of among it. Only
                // a gap he actually fits in is a real between-enemies spot; everything else must
                // fall through to the group middle below.
                if (gapWidth < RequiredGapWidthForMiddleLanding())
                    continue;

                gapCenters.Add((leftEdge + rightEdge) * 0.5f);
                gapWidths.Add(gapWidth);
            }

            // Le plus PROCHE d'abord. Le classement par largeur envoyait le Warrior sur l'ecart le
            // plus large du groupe meme s'il etait a 9 unites: mesure, x=-943.32 -> x=-934.32, soit
            // 8.97 unites parcourues en un seul pas physique, ce que le joueur voit comme une
            // repulsion violente. La largeur reste un filtre dur (il doit tenir dans l'ecart), elle
            // n'a pas a decider de la distance parcourue.
            float warriorX = rigidbody2 != null ? rigidbody2.position.x : transform.position.x;

            while (gapCenters.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < gapCenters.Count; i++)
                {
                    if (Mathf.Abs(gapCenters[i] - warriorX) < Mathf.Abs(gapCenters[best] - warriorX))
                        best = i;
                }

                candidates.Add(gapCenters[best]);
                gapCenters.RemoveAt(best);
                gapWidths.RemoveAt(best);
            }

            // Repli: A COTE du groupe, jamais en son milieu. Le milieu du groupe est le centre de
            // l'ennemi lui-meme des que le cluster n'en compte qu'UN (mesure en jeu:
            // "LIMITE ATTEINTE: cluster=1 ennemis" puis descente forcee sur x = centre du P39):
            // le Warrior etait repose pile sur lui et renvoye en l'air dans la foulee, cycle sans
            // fin dont il ne pouvait plus sortir. On vise donc le sol libre juste a cote du corps,
            // du cote le plus proche d'abord.
            float bodyHalfWidth = collider2 != null ? collider2.bounds.extents.x : 0.95f;
            float sideOffset = bodyHalfWidth + Mathf.Max(0f, middleLandingGapClearance);

            float leftOfGroup = colliders[0].bounds.min.x - sideOffset;
            float rightOfGroup = colliders[colliders.Count - 1].bounds.max.x + sideOffset;

            float currentX = rigidbody2 != null ? rigidbody2.position.x : transform.position.x;

            if (Mathf.Abs(leftOfGroup - currentX) <= Mathf.Abs(rightOfGroup - currentX))
            {
                candidates.Add(leftOfGroup);
                candidates.Add(rightOfGroup);
            }
            else
            {
                candidates.Add(rightOfGroup);
                candidates.Add(leftOfGroup);
            }

            return candidates;
        }

        /// <summary>
        /// Minimum clear width between two enemies for the Warrior to be dropped there as a real
        /// "between the enemies" landing rather than squeezed into their bodies.
        /// </summary>
        private float RequiredGapWidthForMiddleLanding()
        {
            float bodyWidth = collider2 != null ? collider2.bounds.size.x : 1f;
            return bodyWidth + Mathf.Max(0f, middleLandingGapClearance);
        }

        /// <summary>
        /// True when the Warrior's body would sit in free space at this X (same height as now).
        /// Guards the horizontal snap against landing him inside a wall/platform collider.
        /// </summary>
        private bool IsBetweenEnemiesSnapClear(float x)
        {
            if (collider2 == null || PlatformLayer.value == 0)
                return true;

            Bounds bounds = collider2.bounds;

            return Physics2D.OverlapBox(
                new Vector2(x, bounds.center.y),
                new Vector2(bounds.size.x * 0.9f, bounds.size.y * 0.9f),
                0f,
                PlatformLayer) == null;
        }

        /// <summary>
        /// Diagnostic: everything the Warrior's body currently touches or overlaps, so a stalled
        /// forced descent names what holds him instead of leaving it to guesswork.
        /// </summary>
        private string DescribeWhatHoldsTheWarrior()
        {
            if (collider2 == null)
                return "pas de collider";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            ContactPoint2D[] contacts = new ContactPoint2D[16];
            int contactCount = collider2.GetContacts(contacts);
            sb.Append("contacts=").Append(contactCount);

            for (int i = 0; i < contactCount; i++)
            {
                Collider2D other = contacts[i].collider;
                if (other == null)
                    continue;

                sb.Append(" [").Append(other.name)
                  .Append(" couche=").Append(LayerMask.LayerToName(other.gameObject.layer))
                  .Append(" normale=").Append(contacts[i].normal.ToString("F2"))
                  .Append("]");
            }

            Collider2D[] overlaps = Physics2D.OverlapBoxAll(
                collider2.bounds.center, collider2.bounds.size, 0f);

            sb.Append(" | chevauchements=");
            for (int i = 0; i < overlaps.Length; i++)
            {
                Collider2D o = overlaps[i];
                if (o == null || o.transform.IsChildOf(transform))
                    continue;

                sb.Append(" [").Append(o.name)
                  .Append(o.isTrigger ? " trigger" : " solide")
                  .Append(" couche=").Append(LayerMask.LayerToName(o.gameObject.layer))
                  .Append("]");
            }

            sb.Append(" | corpsKinematique=").Append(rigidbody2 != null ? rigidbody2.bodyType.ToString() : "n/a")
              .Append(" masseEchelle=").Append(rigidbody2 != null ? rigidbody2.gravityScale.ToString("F2") : "n/a")
              .Append(" phaseDescente=").Append(DescendentPhase)
              .Append(" chuteBord=").Append(IsFallingEdge)
              .Append(" postRebond=").Append(_postBounceActive);

            return sb.ToString();
        }

        /// <summary>
        /// Diagnostic: vertical distance from the Warrior's feet to the ground straight below,
        /// or -1 when no ground is found within the middle-landing search distance.
        /// </summary>
        private float MeasureGroundDistanceBelow()
        {
            if (collider2 == null || PlatformLayer.value == 0)
                return -1f;

            RaycastHit2D hit = Physics2D.Raycast(
                new Vector2(collider2.bounds.center.x, collider2.bounds.min.y),
                Vector2.down,
                enemyTopMiddleLandingGroundSearchDistance,
                PlatformLayer);

            return hit.collider != null ? hit.distance : -1f;
        }

        private bool HasGroundBelowAt(float x)
        {
            if (collider2 == null || PlatformLayer.value == 0)
                return false;

            RaycastHit2D hit = Physics2D.Raycast(
                new Vector2(x, collider2.bounds.min.y),
                Vector2.down,
                enemyTopMiddleLandingGroundSearchDistance,
                PlatformLayer);

            return hit.collider != null;
        }

        /// <summary>
        /// Commits the forced landing: horizontal snap onto the gap between the enemies, then a
        /// real gravity fall. Only X is moved — the Warrior's Y is left to the physics so the
        /// platform he lands on is entered from above like any normal fall.
        /// </summary>
        private void CommitLandingBetweenEnemies(float landingX, List<Enemy> clusterEnemies)
        {
            ResetEnemyTopMiddleLandingTracking();
            ResetEnemyTopTrapTracking();
            ResetSingleEnemyTopStuckTracking();
            ResetEnemyTopPingPongAbsoluteFailsafe();

            _middleLandingSettleUntil = Time.time + enemyTopMiddleLandingSettleSeconds;

            // Hand the body back from every scripted motion before the fall.
            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();

            if (_postBounceActive)
                EndPostBounce();

            _postBounceActive = false;
            _lastBouncedEnemy = null;
            _blockAction = false;

            IsFallingEdge = false;
            IsFallingPlfExit = false;
            IsFallingHitEnemy = false;
            IsFallingGrazesEdge = false;

            DescendentPhase = true;
            CanMove = true;
            CanAttackWarrior = true;

            if (rigidbody2 != null)
            {
                // Aucun deplacement instantane: le Warrior REJOINT l'ecart pendant sa chute, a
                // vitesse horizontale bornee (voir le watchdog). Le saut de position d'origine
                // etait ressenti comme une repulsion violente — mesure: 8.97 unites en un pas.
                // La chute elle-meme reste une vraie chute gravite: un teleport vertical le
                // ferait apparaitre dans un collider plf one-way et il le traverserait.
                _middleLandingTargetX = landingX;

                RigidbodyConstraints2D constraints = rigidbody2.constraints;
                constraints &= ~RigidbodyConstraints2D.FreezePositionY;
                constraints |= RigidbodyConstraints2D.FreezeRotation;
                rigidbody2.constraints = constraints;

                rigidbody2.gravityScale = normalGravityScale;

                Vector2 velocity = rigidbody2.linearVelocity;
                velocity.x = 0f;
                if (velocity.y > 0f)
                    velocity.y = 0f;
                rigidbody2.linearVelocity = velocity;
                rigidbody2.WakeUp();
            }

            JumpAnimationDisplay();

            // Ignore the enemies of the group while he goes down between them, so none of them
            // can catch a top again during the descent.
            if (_enemyTopTrapIgnoreRoutine != null)
                StopCoroutine(_enemyTopTrapIgnoreRoutine);

            // Règle produit: au 3e rebond la collision avec les ennemis concernés est ignorée
            // pour laisser le Warrior descendre ENTRE eux, et elle n'est restaurée que lorsqu'il
            // est vraiment au sol. Ni le test "les corps ne se touchent plus" (il libère dès le
            // début de la chute et les ennemis le repoussent hors du groupe), ni un simple délai
            // fixe (il l'a laissé figé en l'air) ne conviennent: la sortie est le contact du sol.
            if (_middleLandingIgnoreRoutine != null)
                StopCoroutine(_middleLandingIgnoreRoutine);

            // L'ignore couvre TOUS les ennemis actifs, pas seulement ceux retenus pour choisir le
            // point d'atterrissage: un ennemi hors du groupe interceptait la descente et le
            // Warrior restait posé sur sa tête jusqu'au timeout (restauration en l'air mesurée).
            _middleLandingIgnoreRoutine =
                StartCoroutine(IgnoreClusterUntilWarriorReallyGrounded(GetAllActiveTopPingPongEnemies()));

            NotifyNearbyEnemiesToSideStep(clusterEnemies);

            // Guarantee: if the descent has not produced a real landing in time, the absolute
            // failsafe finishes the job instead of leaving the Warrior in the air.
            if (_middleLandingWatchdogRoutine != null)
                StopCoroutine(_middleLandingWatchdogRoutine);

            _middleLandingWatchdogRoutine = StartCoroutine(EnsureLandingBetweenEnemiesCompletes());
        }

        /// <summary>
        /// Keeps the enemies of the group non-solid for the Warrior for the whole forced descent
        /// and restores them the moment he really stands on the ground. The watchdog delay caps
        /// it so a descent that never lands can never leave the pass-through applied.
        /// </summary>
        private IEnumerator IgnoreClusterUntilWarriorReallyGrounded(List<Enemy> clusterEnemies)
        {
            // Tous les ennemis ignores au moins une fois pendant la descente: la restauration
            // doit couvrir exactement ce qui a ete ignore, pas seulement l'instantane de depart.
            List<Enemy> ignoredDuringDescent = new List<Enemy>();

            RefreshDescentIgnore(clusterEnemies, ignoredDuringDescent);

            float timeoutAt = Time.time + Mathf.Max(0.2f, enemyTopMiddleLandingWatchdogSeconds);

            // One step before testing: he leaves an enemy top with no ground point yet, so the
            // very first frame would otherwise exit immediately.
            yield return new WaitForFixedUpdate();

            int solidGroundSteps = 0;

            // "Parfaitement par terre" et pas "un point de contact une frame": un premier appui
            // fugace pendant la chute rendait les ennemis solides trop tot.
            while (Time.time < timeoutAt && solidGroundSteps < 2)
            {
                bool solidNow = CountGroundPoints() > 0 &&
                                (rigidbody2 == null || Mathf.Abs(rigidbody2.linearVelocity.y) <= 0.1f);

                solidGroundSteps = solidNow ? solidGroundSteps + 1 : 0;

                // L'ignore pose une seule fois au commit ne tient pas: mesure en jeu, le Warrior
                // s'arretait a 1.98 du sol, pose sur P39 (contact solide, normale (0,1)), et
                // etait meme repousse vers le haut de 70.94 a 71.94. Unity remet a zero
                // IgnoreCollision des qu'un collider est desactive puis reactive, et un ennemi
                // active APRES le commit (sortie de culling, spawn) n'etait dans aucune liste.
                // La descente doit donc reaffirmer l'ignore a chaque pas physique.
                RefreshDescentIgnore(GetAllActiveTopPingPongEnemies(), ignoredDuringDescent);

                yield return new WaitForFixedUpdate();
            }

            // Ne PAS rendre les corps solides tant qu'ils chevauchent le Warrior. Mesure en jeu:
            // a la pose, "ennemi le plus proche: P39 a -1.70 (ecartX=0.02)" — pendant la chute les
            // collisions sont ignorees, l'ennemi vient donc occuper le point d'arrivee et le
            // Warrior se retrouve DANS son corps au retablissement, donc aussitot considere comme
            // pose sur sa tete: c'est ce qui relance la chaine indefiniment.
            if (clearOverlapBeforeRestoringEnemies)
                yield return ClearOutOfOverlappingEnemies(ignoredDuringDescent);

            SetIgnoreNearbyEnemies(ignoredDuringDescent, false);

            if (freeWarriorAfterForcedLanding && CountGroundPoints() > 0)
            {
                if (_postLandingFreedomRoutine != null)
                    StopCoroutine(_postLandingFreedomRoutine);

                _postLandingFreedomRoutine =
                    StartCoroutine(KeepWarriorFreeAfterForcedLanding(clusterEnemies));
            }

            // La garde repart AU CONTACT DU SOL, pas au moment de la décision. Sinon elle est
            // déjà écoulée quand il se pose (la descente dure plus longtemps qu'elle), un ennemi
            // le relance dans la foulée et le ping-pong reprend aussitôt: vu du joueur, la règle
            // "ne marche pas" alors que le compteur, lui, a bien plafonné la chaîne.
            bool landed = CountGroundPoints() > 0;
            if (landed)
            {
                _middleLandingSettleUntil = Time.time + enemyTopMiddleLandingSettleSeconds;
                NotePingPongLanded();
            }

            if (logMiddleLandingDecisions)
                Debug.Log($"[MIDLAND]   collisions restaurees | groundPoints={CountGroundPoints()} " +
                          $"pos=({transform.position.x:F2}, {transform.position.y:F2})" +
                          (landed ? $" | garde anti-relance {enemyTopMiddleLandingSettleSeconds:F2}s a partir du sol" : " | PAS AU SOL (timeout)"), this);

            _middleLandingIgnoreRoutine = null;
        }

        /// <summary>
        /// Re-applies the descent pass-through to <paramref name="enemies"/> and records them in
        /// <paramref name="ignoredSoFar"/> so the restore at the end of the fall covers every
        /// enemy that was ever ignored.
        /// </summary>
        private void RefreshDescentIgnore(List<Enemy> enemies, List<Enemy> ignoredSoFar)
        {
            if (enemies == null)
                return;

            SetIgnoreNearbyEnemies(enemies, true);

            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];

                if (enemy != null && !ignoredSoFar.Contains(enemy))
                    ignoredSoFar.Add(enemy);
            }
        }

        /// <summary>
        /// Fenetre de liberte apres un atterrissage force: pendant cette duree AUCUN ennemi ne
        /// peut porter le Warrior ni le relancer, et le groupe fautif est sonne et recule.
        ///
        /// Sans elle, la regle "fonctionne" a chaque fois et ne change pourtant rien pour le
        /// joueur: le Warrior se pose, l'ennemi qui le poursuit se remet sous lui dans la seconde
        /// et le renvoie en l'air. Vu de l'ecran, il rebondit sans fin. La regle ne vaut que si
        /// elle rend REELLEMENT la main au joueur.
        /// </summary>
        private IEnumerator KeepWarriorFreeAfterForcedLanding(List<Enemy> clusterEnemies)
        {
            _middleLandingSettleUntil = Time.time + postLandingFreedomSeconds;

            if (clusterEnemies != null)
            {
                for (int i = 0; i < clusterEnemies.Count; i++)
                {
                    Enemy enemy = clusterEnemies[i];

                    if (enemy == null)
                        continue;

                    // Aucune projection ni etourdissement ici. Un ennemi qui recule tout seul de
                    // 2.5 unites se voit a l'ecran comme un bug: rien dans le jeu ne l'explique.
                    // La regle demandee est plus simple et suffit: la collision reste desactivee
                    // tant que le Warrior n'est pas franchement au sol.
                    if (postLandingEnemyStepBack > 0f)
                    {
                        enemy.stepBackDistance = postLandingEnemyStepBack;
                        bool towardRight = enemy.transform.position.x >= transform.position.x;
                        enemy.StartCoroutine(enemy.SmoothStepBack(towardRight));
                    }
                }
            }

            List<Enemy> freed = new List<Enemy>();
            float endsAt = Time.time + postLandingFreedomSeconds;
            int solidSteps = 0;

            if (logMiddleLandingDecisions)
                Debug.Log($"[MIDLAND]   collision desactivee jusqu'a ce qu'il soit franchement au sol " +
                          $"(securite {postLandingFreedomSeconds:F2}s).", this);

            // Condition de sortie = etre VRAIMENT pose, pas un chrono: pieds au sol et plus de
            // mouvement vertical, sur deux pas physiques consecutifs. Le chrono ne sert plus que
            // de securite si un cas imprevu l'empeche de se poser.
            while (Time.time < endsAt && solidSteps < 2)
            {
                // Reaffirmee a chaque pas: un ennemi qui sort du culling ou dont le collider a ete
                // reactive n'est plus couvert par un ignore pose une seule fois.
                RefreshDescentIgnore(GetAllActiveTopPingPongEnemies(), freed);

                yield return new WaitForFixedUpdate();

                bool solid = CountGroundPoints() > 0 &&
                             (rigidbody2 == null || Mathf.Abs(rigidbody2.linearVelocity.y) <= 0.1f);

                solidSteps = solid ? solidSteps + 1 : 0;
            }

            SetIgnoreNearbyEnemies(freed, false);

            if (logMiddleLandingDecisions)
                Debug.Log($"[MIDLAND]   collisions rendues, il est au sol | pos=" +
                          $"({transform.position.x:F2}, {transform.position.y:F2}) " +
                          $"pointsSol={CountGroundPoints()}", this);

            _postLandingFreedomRoutine = null;
        }

        /// <summary>
        /// Ecarte le Warrior des corps qui le chevauchent avant de leur rendre leur solidite, et
        /// sonne brievement ceux-la pour qu'ils ne reviennent pas dans la seconde. Sans cela le
        /// retablissement des collisions le laisse a l'interieur d'un ennemi.
        /// </summary>
        private IEnumerator ClearOutOfOverlappingEnemies(List<Enemy> enemies)
        {
            Enemy deepest = FindDeepestOverlappingEnemy(enemies, out float deepestOverlap);

            if (deepest == null)
                yield break;

            float escapeSign = deepest.collider2.bounds.center.x >= collider2.bounds.center.x ? -1f : 1f;

            // Ne jamais degager vers le vide: si ce cote n'a pas de sol, sortir de l'autre.
            if (!HasGroundBelowAt(collider2.bounds.center.x + escapeSign * 1.5f))
                escapeSign = -escapeSign;

            if (logMiddleLandingDecisions)
                Debug.Log($"[MIDLAND]   degagement avant restauration: {deepest.name} chevauche de " +
                          $"{deepestOverlap:F2} | sortie vers {(escapeSign < 0f ? "la gauche" : "la droite")}", this);

            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];

                if (enemy == null || enemy.collider2 == null || collider2 == null)
                    continue;

                if (Physics2D.Distance(collider2, enemy.collider2).isOverlapped &&
                    middleLandingClearOutStunSeconds > 0f)
                {
                    enemy.ApplyStun(middleLandingClearOutStunSeconds);
                    enemy.StopMoveTowardCoroutine();
                }
            }

            float timeoutAt = Time.time + middleLandingClearOutSeconds;

            while (Time.time < timeoutAt)
            {
                if (rigidbody2 != null)
                {
                    Vector2 velocity = rigidbody2.linearVelocity;
                    velocity.x = escapeSign * Speed;
                    rigidbody2.linearVelocity = velocity;
                }

                yield return new WaitForFixedUpdate();

                if (FindDeepestOverlappingEnemy(enemies, out _) == null)
                    break;
            }

            if (rigidbody2 != null)
            {
                Vector2 velocity = rigidbody2.linearVelocity;
                velocity.x = 0f;
                rigidbody2.linearVelocity = velocity;
            }
        }

        private Enemy FindDeepestOverlappingEnemy(List<Enemy> enemies, out float overlap)
        {
            overlap = 0f;

            if (enemies == null || collider2 == null)
                return null;

            Enemy deepest = null;

            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];

                if (enemy == null || enemy.collider2 == null || !enemy.gameObject.activeInHierarchy)
                    continue;

                ColliderDistance2D separation = Physics2D.Distance(collider2, enemy.collider2);

                if (!separation.isOverlapped)
                    continue;

                float depth = Mathf.Abs(separation.distance);

                if (depth > overlap)
                {
                    overlap = depth;
                    deepest = enemy;
                }
            }

            return deepest;
        }

        private IEnumerator EnsureLandingBetweenEnemiesCompletes()
        {
            float startedAt = Time.time;
            float timeoutAt = startedAt + enemyTopMiddleLandingWatchdogSeconds;
            float nextTraceAt = startedAt;

            if (logMiddleLandingDecisions)
                Debug.Log($"[MIDLAND-TRACE] depart chute | y={transform.position.y:F2} " +
                          $"distanceSol={MeasureGroundDistanceBelow():F2} " +
                          $"gravite={(rigidbody2 != null ? rigidbody2.gravityScale : -1f):F2}", this);

            while (Time.time < timeoutAt)
            {
                if (CountGroundPoints() > 0)
                {
                    if (logMiddleLandingDecisions)
                        Debug.Log($"[MIDLAND-TRACE] pose apres {(Time.time - startedAt):F2}s " +
                                  $"| y={transform.position.y:F2}", this);

                    _middleLandingWatchdogRoutine = null;
                    yield break;
                }

                if (rigidbody2 != null &&
                    Time.time - startedAt > 0.15f &&
                    Mathf.Abs(rigidbody2.linearVelocity.y) < 0.05f)
                {
                    Assets.Scripts.Tools.GwLog.Verbose(
                        $"[MIDLAND-TRACE] chute bloquee t={(Time.time - startedAt):F2} " +
                        $"| {DescribeWhatHoldsTheWarrior()}", this);
                }

                if (Time.time >= nextTraceAt)
                {
                    nextTraceAt = Time.time + 0.1f;
                    Assets.Scripts.Tools.GwLog.Verbose($"[MIDLAND-TRACE] t={(Time.time - startedAt):F2} " +
                              $"y={transform.position.y:F2} " +
                              $"v=({(rigidbody2 != null ? rigidbody2.linearVelocity.x : 0f):F2}, " +
                              $"{(rigidbody2 != null ? rigidbody2.linearVelocity.y : 0f):F2}) " +
                              $"gravite={(rigidbody2 != null ? rigidbody2.gravityScale : -1f):F2} " +
                              $"contraintes={(rigidbody2 != null ? rigidbody2.constraints.ToString() : "n/a")} " +
                              $"simule={(rigidbody2 != null && rigidbody2.simulated)} " +
                              $"distanceSol={MeasureGroundDistanceBelow():F2} " +
                              $"absorbeurZalayty={_zalaytyBodyImpactAbsorbActive} " +
                              $"coroutineSaut={(activesJumpCoroutine != null)} " +
                              $"coroutineDeplacement={(activesMoveCoroutine != null)} " +
                        $"plateforme={(CurrentplatForm != null ? CurrentplatForm.name : "null")}", this);
                }

                // The descent must END between the enemies, so the committed X is held all the
                // way down. Without it the enemies of the group simply shove him out while he
                // falls — measured drifts of 2.4 to 4.5 units, landing him BESIDE the cluster
                // (their push moves him even when he is idle with zero velocity, so zeroing the
                // velocity once at commit time is not enough). Only the fall is constrained:
                // as soon as he is grounded the loop exits and the enemies may push him again.
                // Horizontal velocity only. Re-assigning rigidbody2.position every FixedUpdate
                // teleports the body and cancels the gravity integration of the step: the descent
                // stalled (0.3 unit in 1.5 s, measured) until the watchdog gave up. The collisions
                // with the group are already ignored until he is grounded, so nothing pushes him
                // sideways any more and zeroing vx is enough to keep the committed X.
                if (holdLandingXWhileFallingBetweenEnemies && rigidbody2 != null)
                {
                    Vector2 velocity = rigidbody2.linearVelocity;
                    float remainingX = _middleLandingTargetX - rigidbody2.position.x;

                    // Rejoindre l'ecart en glissant, jamais en sautant de position. Arrive sur la
                    // cible, vx repasse a 0 et l'ancien comportement (maintien du X) reprend.
                    if (Mathf.Abs(remainingX) <= 0.05f)
                    {
                        velocity.x = 0f;
                    }
                    else
                    {
                        float stepSpeed = Mathf.Abs(remainingX) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
                        velocity.x = Mathf.Sign(remainingX) * Mathf.Min(middleLandingGlideSpeedX, stepSpeed);
                    }

                    rigidbody2.linearVelocity = velocity;
                }

                yield return new WaitForFixedUpdate();
            }

            _middleLandingWatchdogRoutine = null;

            if (!_absoluteFailsafeActive)
                TriggerEnemyTopPingPongAbsoluteFailsafe();
        }

        /// <summary>
        /// Enemies that define the group: every enemy whose top was actually used during the
        /// bounce chain, plus the ones currently around the Warrior (the chain list alone can miss
        /// an enemy that just moved in, the nearby list alone forgets the ones already left).
        /// </summary>
        private List<Enemy> BuildMiddleLandingCluster(Enemy touchedEnemy)
        {
            List<Enemy> result = new List<Enemy>();

            for (int i = 0; i < _middleLandingChainEnemies.Count; i++)
            {
                Enemy chained = _middleLandingChainEnemies[i];

                if (CanEnemyParticipateInTopPingPong(chained) && !result.Contains(chained))
                    result.Add(chained);
            }

            List<Enemy> nearbyEnemies = GetNearbyEnemiesForTopTrap(touchedEnemy);
            for (int i = 0; i < nearbyEnemies.Count; i++)
            {
                Enemy nearby = nearbyEnemies[i];

                if (CanEnemyParticipateInTopPingPong(nearby) && !result.Contains(nearby))
                    result.Add(nearby);
            }

            if (touchedEnemy != null && !result.Contains(touchedEnemy))
                result.Add(touchedEnemy);

            return result;
        }

        private void RegisterEnemyTopTrapBounce(Enemy enemy, bool forceCountBounce)
        {
            _enemyTopTrapLatestRegisteredEnemyId = -1;
            _enemyTopTrapLatestRegisteredEnemyWasUnique = false;

            if (enemy == null)
                return;

            if (Time.time > _enemyTopTrapWindowStartedAt + enemyTopTrapWindow)
                ResetEnemyTopTrapTracking();

            if (_enemyTopTrapWindowStartedAt < 0f)
                _enemyTopTrapWindowStartedAt = Time.time;

            int id = enemy.GetInstanceID();
            bool firstTimeThisEnemyInWindow = _enemyTopTrapTouchedIds.Add(id);

            _enemyTopTrapLatestRegisteredEnemyId = id;
            _enemyTopTrapLatestRegisteredEnemyWasUnique = firstTimeThisEnemyInWindow;

            // OnCollisionEnter counts as a bounce.
            // OnCollisionStay counts only when it discovers a new enemy top that Enter missed.
            if (forceCountBounce || firstTimeThisEnemyInWindow)
                _enemyTopTrapBounceCount++;
        }

        private bool IsLatestRegisteredEnemyUnique(Enemy enemy, List<Enemy> nearbyEnemies)
        {
            if (enemy == null)
                return false;

            return _enemyTopTrapLatestRegisteredEnemyWasUnique &&
                   _enemyTopTrapLatestRegisteredEnemyId == enemy.GetInstanceID();
        }

        private bool HasUnbouncedEnemyTopCandidate(List<Enemy> nearbyEnemies)
        {
            if (nearbyEnemies == null)
                return false;

            for (int i = 0; i < nearbyEnemies.Count; i++)
            {
                Enemy candidate = nearbyEnemies[i];

                if (!CanEnemyParticipateInTopPingPong(candidate))
                    continue;

                if (!_enemyTopTrapTouchedIds.Contains(candidate.GetInstanceID()))
                    return true;
            }

            return false;
        }

        private void ResetEnemyTopTrapTracking()
        {
            _enemyTopTrapWindowStartedAt = Time.time;
            _enemyTopTrapBounceCount = 0;
            _enemyTopTrapTouchedIds.Clear();
            _enemyTopTrapLatestRegisteredEnemyId = -1;
            _enemyTopTrapLatestRegisteredEnemyWasUnique = false;
        }

        private List<Enemy> GetNearbyEnemiesForTopTrap(Enemy touchedEnemy)
        {
            List<Enemy> result = new List<Enemy>();

            if (collider2 == null)
                return result;

            Vector2 warriorCenter = collider2.bounds.center;
            Enemy[] allEnemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);

            for (int i = 0; i < allEnemies.Length; i++)
            {
                Enemy enemy = allEnemies[i];

                if (!CanEnemyParticipateInTopPingPong(enemy))
                    continue;

                Collider2D enemyCollider = GetEnemyTopTrapCollider(enemy);
                if (enemyCollider == null || !enemyCollider.enabled)
                    continue;

                Vector2 enemyCenter = enemyCollider.bounds.center;
                float dx = Mathf.Abs(enemyCenter.x - warriorCenter.x);
                float dy = Mathf.Abs(enemyCenter.y - warriorCenter.y);

                if (dx <= enemyTopTrapNearbyRadiusX &&
                    dy <= enemyTopTrapNearbyRadiusY)
                {
                    result.Add(enemy);
                }
            }

            // Safety: make sure the touched enemy is included even if FindObjectsByType
            // misses it during a frame where the object was just enabled/spawned.
            if (touchedEnemy != null && !result.Contains(touchedEnemy))
                result.Add(touchedEnemy);

            return result;
        }

        private bool CanEnemyParticipateInTopPingPong(Enemy enemy)
        {
            if (enemy == null)
                return false;

            if (!enemy.gameObject.activeInHierarchy)
                return false;

            if (!enemy.CanCauseWarriorTopPingPongTrap)
                return false;

            Collider2D enemyCollider = GetEnemyTopTrapCollider(enemy);
            return enemyCollider != null && enemyCollider.enabled && !enemyCollider.isTrigger;
        }

        private Collider2D GetEnemyTopTrapCollider(Enemy enemy)
        {
            if (enemy == null)
                return null;

            if (enemy.WarriorTopPingPongCollider != null)
                return enemy.WarriorTopPingPongCollider;

            if (enemy.NormalCollider != null)
                return enemy.NormalCollider;

            if (enemy.collider2 != null)
                return enemy.collider2;

            return enemy.GetComponentInChildren<Collider2D>();
        }

        private bool IsWarriorLandingOnEnemyTopByCollision(Collision2D collision)
        {
            if (collision == null || collision.contactCount <= 0)
                return false;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);

                // In Warrior's collision callback, upward normal means the enemy top
                // surface is pushing Warrior upward.
                if (contact.normal.y >= 0.55f)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// When the Warrior is bearing down on Zalayty's top bound, signal Zalayty so his
        /// one-way platform does not misread the transient downward push as an edge loss
        /// (which would turn the platform pass-through and tunnel him through it).
        /// Purely a notification: never alters Warrior or collision control flow.
        /// </summary>
        private void NotifyZalaytyIfWarriorPressingOnTop(Enemy enemy, Collision2D collision)
        {
            if (enemy is not ZalaytyMonster zalayty)
                return;

            if (!IsWarriorLandingOnEnemyTopByCollision(collision))
                return;

            zalayty.NotifyWarriorPressingOnTop();
        }

        private void ContinueEnemyTopBounceChainFrom(Enemy enemy, List<Enemy> nearbyEnemies)
        {
            if (enemy == null)
                return;

            if (DescendentPhase && CountGroundPoints() == 0 && ShieldIsUp)
                DoShieldStomp(enemy);

            if (_postBounceActive)
                EndPostBounce();

            BounceAndLandAway(enemy);
        }

        private void FinishEnemyTopBounceChainWithFinalBounce(Enemy enemy, List<Enemy> nearbyEnemies)
        {
            if (enemy == null)
            {
                BreakEnemyTopPingPongTrap(nearbyEnemies);
                return;
            }

            if (DescendentPhase && CountGroundPoints() == 0 && ShieldIsUp)
                DoShieldStomp(enemy);

            if (_postBounceActive)
                EndPostBounce();

            // This is the last allowed enemy-top bounce in the current cluster.
            // Warrior still visually bounces from this enemy once, then all nearby
            // enemy colliders are ignored long enough for the landing to complete.
            BounceAndLandAway(enemy);

            if (_enemyTopTrapIgnoreRoutine != null)
                StopCoroutine(_enemyTopTrapIgnoreRoutine);

            _enemyTopTrapIgnoreRoutine =
                StartCoroutine(TemporarilyIgnoreNearbyEnemiesForTopTrap(
                    nearbyEnemies,
                    waitFullDuration: enemyTopTrapIgnoreAllEnemiesDuringFinalLanding));

            NotifyNearbyEnemiesToSideStep(nearbyEnemies);
            ResetEnemyTopTrapTracking();
        }

        // When the Warrior collides with a boss (e.g. Zort) during the RISING phase of a
        // controlled jump, the scripted jump arc — which is driven by Rigidbody2D.MovePosition
        // and therefore overrides gravity every FixedUpdate — must stop instantly. We cancel any
        // leftover upward momentum and hand the body back to gravity. From there the per-frame
        // HandleFallingAndDeath() keeps JumpAnimationDisplay() playing and forces a falling
        // gravityScale until the Warrior lands on a valid surface, where the normal landing
        // handlers (ResolvePredictedPhysicsFallLanding / BounceAndLandAway / CheckIfStopRunDisplay)
        // clear the fall state and restore the idle animation.
        public void CancelControlledJumpIntoFall(Enemy enemy)
        {
            // Stop the scripted parabola (and any move arc) so MovePosition stops driving us.
            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();

            // Keep the overlap-recovery guardian from launching a scripted bounce arc while the
            // Warrior slides off the boss; we want a pure gravity fall.
            _suppressEnemyOverlapBounceUntil = Time.time + bossJumpCancelOverlapSuppressSeconds;

            // We are now descending under gravity, not rising.
            DescendentPhase = true;

            // Mark "fell after hitting an enemy" so IsFalling is immediately true; this makes
            // HandleFallingAndDeath() drive the jump/fall animation this very frame.
            IsFallingHitEnemy = true;
            IsFallingEdge = false;
            IsFallingPlfExit = false;
            IsFallingGrazesEdge = false;

            _blockAction = false;

            if (rigidbody2 != null)
            {
                rigidbody2.simulated = true;

                // Allow vertical motion and keep upright so gravity can pull the Warrior down.
                RigidbodyConstraints2D constraints = rigidbody2.constraints;
                constraints &= ~RigidbodyConstraints2D.FreezePositionY;
                constraints |= RigidbodyConstraints2D.FreezeRotation;
                rigidbody2.constraints = constraints;

                rigidbody2.gravityScale = Mathf.Max(rigidbody2.gravityScale, normalGravityScale);

                // Cancel remaining upward momentum (and the scripted horizontal carry): from the
                // moment of impact the Warrior is affected only by gravity.
                Vector2 velocity = rigidbody2.linearVelocity;
                velocity.x = 0f;
                if (velocity.y > 0f)
                    velocity.y = 0f;
                rigidbody2.linearVelocity = velocity;

                rigidbody2.WakeUp();
            }

            // Immediate, smooth switch to the airborne (jump) animation. HandleFallingAndDeath()
            // re-asserts this every frame while airborne, so there is no flicker before landing.
            JumpAnimationDisplay();

            Debug.Log($"[Warrior] Jump canceled into fall after hitting boss '{(enemy != null ? enemy.name : "?")}'.");
        }

        /// <summary>
        /// Last-resort landing for the multi-enemy top ping-pong: when the bounce chain is
        /// exhausted and no safe land was found, drop the Warrior straight down onto the ground
        /// in the gap BETWEEN the two bracketing enemies (nearest enemy on each side of him).
        /// Snap-X + vertical fall (no scripted parabola): we move the Warrior's X to the mid
        /// point, kill horizontal velocity, force a downward gravity fall, and briefly ignore the
        /// nearby enemy colliders so he cannot re-catch a top on the way down.
        /// Returns false (→ caller keeps the existing blind free fall) when:
        ///   - the feature is disabled,
        ///   - there is no genuine two-sided bracket (both enemies on the same side), or
        ///   - there is no real ground between the two enemies (a hole).
        /// </summary>
        private bool TryLandBetweenBracketingEnemies(List<Enemy> nearbyEnemies)
        {
            if (!enableLandBetweenEnemies)
                return false;

            if (nearbyEnemies == null || nearbyEnemies.Count < 2 || collider2 == null)
                return false;

            if (PlatformLayer.value == 0)
                return false;

            float warriorX = collider2.bounds.center.x;

            // Find the nearest bracketing enemy on each side (one left, one right).
            Enemy leftEnemy = null;
            Enemy rightEnemy = null;
            float leftX = 0f;
            float rightX = 0f;
            float bestLeftDist = float.PositiveInfinity;
            float bestRightDist = float.PositiveInfinity;

            for (int i = 0; i < nearbyEnemies.Count; i++)
            {
                Enemy candidate = nearbyEnemies[i];
                if (!CanEnemyParticipateInTopPingPong(candidate))
                    continue;

                Collider2D candidateCollider = GetEnemyTopTrapCollider(candidate);
                if (candidateCollider == null)
                    continue;

                float candidateX = candidateCollider.bounds.center.x;
                float dist = Mathf.Abs(candidateX - warriorX);

                if (candidateX < warriorX)
                {
                    if (dist < bestLeftDist)
                    {
                        bestLeftDist = dist;
                        leftEnemy = candidate;
                        leftX = candidateX;
                    }
                }
                else if (candidateX > warriorX)
                {
                    if (dist < bestRightDist)
                    {
                        bestRightDist = dist;
                        rightEnemy = candidate;
                        rightX = candidateX;
                    }
                }
            }

            // No genuine two-sided bracket (both on the same side, or only one usable) → fallback.
            if (leftEnemy == null || rightEnemy == null)
                return false;

            float midX = (leftX + rightX) * 0.5f;

            // Confirm there is real ground between the two enemies before snapping down.
            Vector2 rayOrigin = new Vector2(midX, collider2.bounds.min.y);
            RaycastHit2D groundHit = Physics2D.Raycast(
                rayOrigin,
                Vector2.down,
                landBetweenEnemiesRaycastMaxDistance,
                PlatformLayer);

            // No ground in the gap (a hole) → fallback to the blind free fall.
            if (groundHit.collider == null)
                return false;

            // ── Commit the between-enemies landing ────────────────────────────────────────
            ResetEnemyTopTrapTracking();

            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();

            if (_postBounceActive)
                EndPostBounce();

            _postBounceActive = false;
            _lastBouncedEnemy = null;
            _blockAction = false;

            IsFallingHitEnemy = false;
            IsFallingEdge = false;
            IsFallingPlfExit = false;
            IsFallingGrazesEdge = false;

            DescendentPhase = true;
            CanMove = true;
            CanAttackWarrior = true;

            if (rigidbody2 != null)
            {
                // Snap X only: align the Warrior horizontally with the gap mid-point, keep Y so
                // the fall stays vertical. The ignore-collision window below prevents the snap
                // from producing an unwanted enemy hit even if midX is close to a body collider.
                Vector2 pos = rigidbody2.position;
                pos.x = midX;
                rigidbody2.position = pos;
                Physics2D.SyncTransforms();

                RigidbodyConstraints2D constraints = rigidbody2.constraints;
                constraints &= ~RigidbodyConstraints2D.FreezePositionY;
                constraints |= RigidbodyConstraints2D.FreezeRotation;
                rigidbody2.constraints = constraints;

                rigidbody2.gravityScale =
                    Mathf.Max(rigidbody2.gravityScale, enemyTopTrapFallGravity);

                Vector2 velocity = rigidbody2.linearVelocity;
                velocity.x = 0f;
                if (velocity.y > -enemyTopTrapMinDownVelocity)
                    velocity.y = -enemyTopTrapMinDownVelocity;

                rigidbody2.linearVelocity = velocity;
                rigidbody2.WakeUp();
            }

            JumpAnimationDisplay();

            if (_enemyTopTrapIgnoreRoutine != null)
                StopCoroutine(_enemyTopTrapIgnoreRoutine);

            _enemyTopTrapIgnoreRoutine =
                StartCoroutine(TemporarilyIgnoreNearbyEnemiesForTopTrap(
                    nearbyEnemies,
                    waitFullDuration: false));

            NotifyNearbyEnemiesToSideStep(nearbyEnemies);

            return true;
        }

        private void BreakEnemyTopPingPongTrap(List<Enemy> nearbyEnemies)
        {
            // New rule: once the bounce chain is exhausted with no safe land, prefer dropping
            // the Warrior straight onto the ground BETWEEN the two bracketing enemies rather
            // than the blind free fall below. Only applies to a genuine two-sided bracket with
            // real ground between the enemies; otherwise we fall through to the free fall.
            if (TryLandBetweenBracketingEnemies(nearbyEnemies))
                return;

            ResetEnemyTopTrapTracking();

            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();

            if (_postBounceActive)
                EndPostBounce();

            _postBounceActive = false;
            _lastBouncedEnemy = null;
            _blockAction = false;

            IsFallingHitEnemy = false;
            IsFallingEdge = false;
            IsFallingPlfExit = false;
            IsFallingGrazesEdge = false;

            DescendentPhase = true;
            CanMove = true;
            CanAttackWarrior = true;

            if (rigidbody2 != null)
            {
                RigidbodyConstraints2D constraints = rigidbody2.constraints;
                constraints &= ~RigidbodyConstraints2D.FreezePositionY;
                constraints |= RigidbodyConstraints2D.FreezeRotation;
                rigidbody2.constraints = constraints;

                rigidbody2.gravityScale =
                    Mathf.Max(rigidbody2.gravityScale, enemyTopTrapFallGravity);

                Vector2 velocity = rigidbody2.linearVelocity;

                // Stop horizontal bridge ping-pong.
                velocity.x = 0f;

                if (velocity.y > -enemyTopTrapMinDownVelocity)
                    velocity.y = -enemyTopTrapMinDownVelocity;

                rigidbody2.linearVelocity = velocity;
                rigidbody2.WakeUp();
            }

            JumpAnimationDisplay();

            if (_enemyTopTrapIgnoreRoutine != null)
                StopCoroutine(_enemyTopTrapIgnoreRoutine);

            _enemyTopTrapIgnoreRoutine =
                StartCoroutine(TemporarilyIgnoreNearbyEnemiesForTopTrap(
                    nearbyEnemies,
                    waitFullDuration: false));

            NotifyNearbyEnemiesToSideStep(nearbyEnemies);
        }

        private IEnumerator TemporarilyIgnoreNearbyEnemiesForTopTrap(List<Enemy> enemies, bool waitFullDuration)
        {
            SetIgnoreNearbyEnemies(enemies, true);

            float timeoutAt = Time.time + enemyTopTrapIgnoreCollisionTime;

            while (Time.time < timeoutAt)
            {
                if (!waitFullDuration && AreWarriorAndNearbyEnemiesClear(enemies))
                    break;

                yield return new WaitForFixedUpdate();
            }

            SetIgnoreNearbyEnemies(enemies, false);
            _enemyTopTrapIgnoreRoutine = null;
        }

        private void SetIgnoreNearbyEnemies(List<Enemy> enemies, bool ignore)
        {
            if (enemies == null)
                return;

            Collider2D[] warriorColliders = GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null)
                    continue;

                Collider2D[] enemyColliders = enemy.GetComponentsInChildren<Collider2D>(true);

                for (int w = 0; w < warriorColliders.Length; w++)
                {
                    Collider2D warriorCollider = warriorColliders[w];

                    if (warriorCollider == null ||
                        !warriorCollider.enabled ||
                        warriorCollider.isTrigger)
                        continue;

                    for (int e = 0; e < enemyColliders.Length; e++)
                    {
                        Collider2D enemyCollider = enemyColliders[e];

                        if (enemyCollider == null ||
                            !enemyCollider.enabled ||
                            enemyCollider.isTrigger)
                            continue;

                        Physics2D.IgnoreCollision(warriorCollider, enemyCollider, ignore);
                    }
                }
            }
        }

        private bool AreWarriorAndNearbyEnemiesClear(List<Enemy> enemies)
        {
            if (enemies == null)
                return true;

            Collider2D[] warriorColliders = GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null)
                    continue;

                Collider2D[] enemyColliders = enemy.GetComponentsInChildren<Collider2D>(true);

                for (int w = 0; w < warriorColliders.Length; w++)
                {
                    Collider2D warriorCollider = warriorColliders[w];

                    if (warriorCollider == null ||
                        !warriorCollider.enabled ||
                        warriorCollider.isTrigger)
                        continue;

                    for (int e = 0; e < enemyColliders.Length; e++)
                    {
                        Collider2D enemyCollider = enemyColliders[e];

                        if (enemyCollider == null ||
                            !enemyCollider.enabled ||
                            enemyCollider.isTrigger)
                            continue;

                        ColliderDistance2D distance =
                            Physics2D.Distance(warriorCollider, enemyCollider);

                        if (distance.isOverlapped ||
                            distance.distance < enemyTopTrapRestoreClearance)
                            return false;
                    }
                }
            }

            return true;
        }

        private void NotifyNearbyEnemiesToSideStep(List<Enemy> enemies)
        {
            if (!notifyEnemiesToSideStepOnTopTrap || enemies == null)
                return;

            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];

                if (enemy == null)
                    continue;

                enemy.OnWarriorTopPingPongTrapBroken(this);

                // Safe optional hook.
                // Existing Zalayty can implement this.
                // Other enemies ignore it without compile/runtime errors.
                enemy.SendMessage(
                    "ForceAntiPingPongSideStepAwayFromWarrior",
                    this,
                    SendMessageOptions.DontRequireReceiver);
            }
        }

        private void BounceAndLandAway(Enemy enemy, bool plfExitMode = false)
        {
            if (enemy == null) return;
            if (_postBounceActive) return;

            // Descente forcee "entre les ennemis" en cours: le rebond scripte demarre un saut
            // (TriggerJump) qui reprend la main sur la gravite. Mesure en jeu: la vitesse
            // verticale se fige a -1.2/-1.8, le Warrior REMONTE (71.52 -> 72.26) puis redescend
            // au rythme de l'arc, et la chute n'atteint pas le sol avant le watchdog -> "PAS AU
            // SOL (timeout)". Les chutes qui aboutissent (0.34 s, vy -2.99 -> -5.94 -> -8.88)
            // sont exactement celles sans coroutine de saut. La regle des 3 rebonds exige que
            // cette descente aille jusqu'au sol: aucun rebond ne peut la reprendre.
            // Pendant la descente forcee ET pendant la fenetre de recuperation qui suit la pose:
            // sans ce second cas, l'ennemi qui poursuit le Warrior se remet sous lui des la
            // premiere seconde, le rebond scripte repart, et la chaine recommence — vu du joueur
            // "la regle ne marche pas". La fenetre lui laisse le temps de repartir a pied.
            if (_middleLandingIgnoreRoutine != null || Time.time < _middleLandingSettleUntil)
            {
                if (logMiddleLandingDecisions)
                    Debug.Log($"[MIDLAND] rebond scripte refuse " +
                              $"({(_middleLandingIgnoreRoutine != null ? "descente forcee" : "fenetre de recuperation")}, " +
                              $"ennemi={enemy.name}) : le Warrior garde la main.", this);

                return;
            }

            _blockAction = true;
            _postBounceActive = true;
            _postBounceStartTime = Time.time;
            _lastBouncedEnemy = enemy;

            Collider2D enemyCollider = GetEnemyTopTrapCollider(enemy);

            if (enemyCollider != null)
                _requiredClearanceX = enemyCollider.bounds.extents.x + collider2.bounds.extents.x + clearancePad;
            else
                _requiredClearanceX = collider2.bounds.extents.x + 0.2f;

            IgnoreAllEnemyColliders(enemy, true);

            Vector2 landing = CalculateLandingAwayFromEnemy(enemy, plfExitMode);
            TriggerJump(landing);
        }

        private void EndPostBounce()
        {
            if (_lastBouncedEnemy != null)
                IgnoreAllEnemyColliders(_lastBouncedEnemy, false);

            _postBounceActive = false;
            _lastBouncedEnemy = null;
            _blockAction = false;
        }

        private Vector2 CalculateLandingAwayFromEnemy(Enemy enemy, bool plfExitMode)
        {
            Collider2D enemyCollider = GetEnemyTopTrapCollider(enemy);
            float enemyX = enemyCollider != null ? enemyCollider.bounds.center.x : enemy.transform.position.x;
            float dir = (transform.position.x < enemyX) ? -1f : 1f;

            float desiredSeparation = _requiredClearanceX + Mathf.Max(0f, landAwayDistance);
            if (plfExitMode) desiredSeparation += 0.08f;

            float candidate1 = enemyX + dir * desiredSeparation;
            float candidate2 = enemyX - dir * desiredSeparation;

            var platform = enemy.CurrentplatForm != null ? enemy.CurrentplatForm : CurrentplatForm;

            float y = transform.position.y;
            float minX = float.NegativeInfinity;
            float maxX = float.PositiveInfinity;

            if (platform != null && platform.platformCollider != null)
            {
                float platformTop = platform.platformCollider.bounds.max.y;
                y = platformTop + collider2.bounds.extents.y * 0.95f;

                minX = platform.platformCollider.bounds.min.x + collider2.bounds.extents.x;
                maxX = platform.platformCollider.bounds.max.x - collider2.bounds.extents.x;
            }

            float c1 = Mathf.Clamp(candidate1, minX, maxX);
            float c2 = Mathf.Clamp(candidate2, minX, maxX);

            bool c1Ok = Mathf.Abs(c1 - enemyX) >= _requiredClearanceX;
            bool c2Ok = Mathf.Abs(c2 - enemyX) >= _requiredClearanceX;

            float myX = collider2.bounds.center.x;

            if (c1Ok && c2Ok)
                return new Vector2(Mathf.Abs(c1 - myX) <= Mathf.Abs(c2 - myX) ? c1 : c2, y);

            if (c1Ok) return new Vector2(c1, y);
            if (c2Ok) return new Vector2(c2, y);

            float closest = Mathf.Abs(c1 - myX) <= Mathf.Abs(c2 - myX) ? c1 : c2;
            return new Vector2(closest, y);
        }

        public void CheckIfAvoidCollider(bool enableCollision)
        {
            var touching = new List<Collider2D>();

            var filter = new ContactFilter2D
            {
                useTriggers = true,
                useLayerMask = true
            };
            filter.SetLayerMask(LayerMask.GetMask("Enemy"));

            int count = Physics2D.GetContacts(collider2, filter, touching);
            if (count <= 0) return;

            foreach (Collider2D c in touching)
            {
                if (!c.isTrigger) continue;

                var enemyNormalCollider = touching.FirstOrDefault(x => x.isTrigger == false);
                if (enemyNormalCollider == null) continue;

                ResolveOverlap(collider2, enemyNormalCollider);
                Physics2D.IgnoreCollision(collider2, enemyNormalCollider, enableCollision);
            }
        }

        public void ResolveOverlap(Collider2D colA, Collider2D colB, bool resolveXAxisOnly = true)
        {
            Vector3 separation = (colA.transform.position - colB.transform.position).normalized * 0.1f;

            if (resolveXAxisOnly) separation.y = 0;
            else separation.x = 0;

            colA.transform.position += separation;
        }

        private void TriggerJump(Vector2 targetPosition, float height = 2.2f, float duration = 0.6f)
        {
            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();

            MarkJumpStarted();

            JumpAnimationDisplay();
            activesJumpCoroutine = JumpTowardPositionAction(targetPosition, height, duration);
            StartCoroutine(activesJumpCoroutine);
        }

        private void IgnoreAllEnemyColliders(Enemy enemy, bool ignore)
        {
            if (enemy == null) return;

            var cols = enemy.GetComponentsInChildren<Collider2D>(true);
            foreach (var c in cols)
            {
                if (c == null) continue;
                Physics2D.IgnoreCollision(collider2, c, ignore);
            }
        }

        private bool WarriorOverlay(Enemy enemy)
        {
            if (enemy == null || collider2 == null)
                return false;

            Collider2D enemyCollider = GetEnemyTopTrapCollider(enemy);
            if (enemyCollider == null)
                return false;

            float warriorBottom = collider2.bounds.min.y;
            float enemyTop = enemyCollider.bounds.max.y;

            return warriorBottom >= enemyTop - 0.01f;
        }

        /// <summary>
        /// Returns true when the warrior's collider bottom is within CONTACT_Y_TOLERANCE
        /// of the enemy's top collider — i.e. he is resting on top of it.
        /// </summary>
        private bool WarriorSitsOnEnemyTop(Enemy enemy)
        {
            if (enemy == null || collider2 == null)
                return false;

            Collider2D enemyCollider = GetEnemyTopTrapCollider(enemy);
            if (enemyCollider == null)
                return false;

            float warriorBottom = collider2.bounds.min.y;
            float enemyTop = enemyCollider.bounds.max.y;

            return Mathf.Abs(warriorBottom - enemyTop) <= CONTACT_Y_TOLERANCE;
        }

        private bool IsSamePlatformAs(Enemy e)
        {
            if (e == null) return false;

            var myPlf = CurrentplatForm != null ? CurrentplatForm.platformCollider : null;
            var enPlf = e.CurrentplatForm != null ? e.CurrentplatForm.platformCollider : null;

            if (myPlf == null || enPlf == null)
                return true;

            return myPlf == enPlf;
        }

        private void StopRunningOnEnemyContact(Enemy enemy)
        {
            if (enemy == null) return;

            // Warrior attack has priority over the enemy-contact stop (M97, P39, Crawling, ...).
            // A pressing melee enemy keeps imparting horizontal velocity to the Warrior, so without
            // this guard StopRunningOnEnemyContact runs every physics frame, calls Wait/LosingBalance,
            // and resets isAttacking=false -> the in-progress Attack1 clip is killed before its
            // hit-frame AnimationEvent ever fires. Attack2/Attack3 survive only because their damage
            // is already resolved at button press. Let the attack clip play to completion here.
            if (IsAnyAttackPlaying()) return;

            if (!IsSamePlatformAs(enemy)) return;
            if (CountGroundPoints() <= 0) return;

            bool wasMoving = activesMoveCoroutine != null || Mathf.Abs(rigidbody2.linearVelocity.x) > 0.15f;
            if (!wasMoving) return;

            _blockedByEnemyContact = true;
            _blockingEnemy = enemy;

            StopMoveTowardCoroutine();

            if (rigidbody2 != null)
            {
                var v = rigidbody2.linearVelocity;
                v.x = 0f;
                rigidbody2.linearVelocity = v;
            }

            if (CountGroundPoints() <= 1)
                ShowLosingBalance();
            else
                WaitAnimationDisplay();
        }

        private void ClearEnemyContactBlock(Enemy enemy)
        {
            if (!_blockedByEnemyContact) return;

            if (_blockingEnemy == null || enemy == null || enemy == _blockingEnemy)
            {
                _blockedByEnemyContact = false;
                _blockingEnemy = null;
            }
        }

        private bool OnCollisionHitBottom(Collision2D collision)
        {
            Vector2 avg = Vector2.zero;
            foreach (ContactPoint2D contact in collision.contacts)
                avg += contact.normal;

            avg /= collision.contactCount;
            return avg.y < -0.7f;
        }

        // ── Physics-driven fall anti-tunneling ─────────────────────────────────────────
        // This is the missing case for PerformWarriorEdgeFall().
        // After an edge fall, Warrior is not moved by JumpTowardPositionAction anymore;
        // he falls by Rigidbody2D velocity/gravity. The destination platform can be
        // different from the source platform, so CurrentplatForm is deliberately ignored.
        private void ApplyDestinationPlatformAntiTunnelDuringPhysicsFall()
        {
            if (!enableEdgeFallDestinationAntiTunnel)
            {
                _hasLastPhysicsFallAntiTunnelPosition = false;
                return;
            }

            if (collider2 == null || rigidbody2 == null || PlatformLayer.value == 0)
            {
                _hasLastPhysicsFallAntiTunnelPosition = false;
                return;
            }

            if (CanDie || _deathStarted)
            {
                _hasLastPhysicsFallAntiTunnelPosition = false;
                return;
            }

            Vector2 currentPosition = rigidbody2.position;

            if (!_hasLastPhysicsFallAntiTunnelPosition)
            {
                _lastPhysicsFallAntiTunnelPosition = currentPosition;
                _hasLastPhysicsFallAntiTunnelPosition = true;
            }

            // JumpTowardPositionAction already has its own destination-platform sweep.
            // This method is only for real Rigidbody2D / gravity-driven falling.
            if (activesJumpCoroutine != null)
            {
                _lastPhysicsFallAntiTunnelPosition = currentPosition;
                return;
            }

            Vector2 velocity = rigidbody2.linearVelocity;

            bool physicallyDescending = velocity.y < -edgeFallAntiTunnelMinDownSpeed;
            bool fallStateKnown =
                IsFallingEdge ||
                IsFallingPlfExit ||
                IsFallingGrazesEdge ||
                CountGroundPoints() == 0;

            // Robust rule:
            // If Warrior is descending fast enough, run the sweep even if a ground point
            // is stale for one frame. The destination-platform crossing test will decide
            // whether a landing is valid.
            if (!physicallyDescending && !fallStateKnown)
            {
                _lastPhysicsFallAntiTunnelPosition = currentPosition;
                return;
            }

            Vector2 resolvedLandingPosition;
            PlatFormColliderTrigger destinationPlatform;

            // 1) Previous physics position -> current physics position.
            // This catches cases where Unity already moved Warrior through the destination
            // platform during the last physics simulation step because that platform was
            // still ignored by trigger-first logic.
            if (currentPosition.y < _lastPhysicsFallAntiTunnelPosition.y - 0.0001f &&
                TryResolveDestinationPlatformTopLanding(
                    _lastPhysicsFallAntiTunnelPosition,
                    currentPosition,
                    out resolvedLandingPosition,
                    out destinationPlatform,
                    ignoreCurrentPlatform: true))
            {
                ResolvePredictedPhysicsFallLanding(resolvedLandingPosition, destinationPlatform);
                _lastPhysicsFallAntiTunnelPosition = resolvedLandingPosition;
                return;
            }

            if (!physicallyDescending)
            {
                _lastPhysicsFallAntiTunnelPosition = currentPosition;
                return;
            }

            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : Time.deltaTime;

            // 2) Current physics position -> next predicted physics position.
            // Use the actual integration formula instead of a very large multiplier:
            // pNext = p + v*dt + 0.5*g*dt^2. Then add a tiny downward cushion.
            Vector2 gravity = Physics2D.gravity * rigidbody2.gravityScale;
            Vector2 predictedDelta = velocity * dt + 0.5f * gravity * dt * dt;

            if (physicsFallAntiTunnelExtraLookAhead > 0f)
                predictedDelta += Vector2.down * physicsFallAntiTunnelExtraLookAhead;

            // Keep your existing tuning as an optional extra guard, but apply it only to
            // the predicted delta, not to the previous/current correction.
            if (edgeFallAntiTunnelPredictionMultiplier > 1f)
                predictedDelta *= edgeFallAntiTunnelPredictionMultiplier;

            Vector2 predictedNextPosition = currentPosition + predictedDelta;

            if (predictedNextPosition.y < currentPosition.y - 0.0001f &&
                TryResolveDestinationPlatformTopLanding(
                    currentPosition,
                    predictedNextPosition,
                    out resolvedLandingPosition,
                    out destinationPlatform,
                    ignoreCurrentPlatform: true))
            {
                ResolvePredictedPhysicsFallLanding(resolvedLandingPosition, destinationPlatform);
                _lastPhysicsFallAntiTunnelPosition = resolvedLandingPosition;
                return;
            }

            _lastPhysicsFallAntiTunnelPosition = currentPosition;
        }

        private void ResolvePredictedPhysicsFallLanding(Vector2 landingPosition, PlatFormColliderTrigger destinationPlatform)
        {
            MoveCharacterTo(landingPosition);
            CompletePredictedTopLanding(destinationPlatform);

            IsFallingEdge = false;
            IsFallingPlfExit = false;
            IsFallingGrazesEdge = false;
            IsFallingHitEnemy = false;
            CanMove = true;
            _blockAction = false;

            StopJumpTowardCoroutine();
            StopMoveTowardCoroutine();

            if (rigidbody2 != null)
            {
                Vector2 v = rigidbody2.linearVelocity;
                if (v.y < 0f)
                    v.y = 0f;
                rigidbody2.linearVelocity = v;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────

        #endregion
    }
}
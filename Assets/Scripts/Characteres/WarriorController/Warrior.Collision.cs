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
                return;

            if (_sprintActive && enemy != null && collision.collider != null)
            {
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
                return false;

            if (enemy == null || collider2 == null)
                return false;

            if (!CanEnemyParticipateInTopPingPong(enemy))
                return false;

            bool warriorOnEnemyTop =
                WarriorSitsOnEnemyTop(enemy) ||
                WarriorOverlay(enemy) ||
                IsWarriorLandingOnEnemyTopByCollision(collision);

            if (!warriorOnEnemyTop)
                return false;

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

                // If this was the last known nearby enemy top, let Warrior bounce from it once,
                // then ignore all nearby enemies so the jump can finish as a safe landing.
                if (IsLatestRegisteredEnemyUnique(enemy, nearbyEnemies) &&
                    touchedAtLeastTwoDifferentEnemies &&
                    !hasUnbouncedNearbyEnemy)
                {
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

        private void BreakEnemyTopPingPongTrap(List<Enemy> nearbyEnemies)
        {
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
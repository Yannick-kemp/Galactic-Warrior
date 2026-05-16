using Assets.Scripts.Characteres.WarriorController;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Timer = Assets.GalaticfFileSys.TimerManager.Timer;

namespace Assets.Scripts.Platforms
{
    public class PlatFormPlfColliderTrigger : PlatFormColliderTrigger
    {
        protected Timer warriorEdgeTimer;
        protected Timer zalaytyEdgeTimer;

        private Warrior _pendingWarriorFall;
        private ZalaytyMonster _pendingZalaytyJump;

        private enum FirstPlatformContact
        {
            None = 0,
            TriggerFirst = 1,
            BodyFirst = 2
        }

        private readonly Dictionary<int, FirstPlatformContact> _firstContactByCharacter =
            new Dictionary<int, FirstPlatformContact>();

        // Stores the frame where the first contact was registered.
        // This lets trigger contact win when Unity reports trigger/collision
        // callbacks in the same physics frame.
        private readonly Dictionary<int, int> _firstContactFrameByCharacter =
            new Dictionary<int, int>();

        // Tracks exactly which Warrior/Zalayty colliders are still inside this
        // platformTrigger. This is stronger than an int counter because Warrior/Zalayty
        // can have several child colliders and Unity can report repeated Stay/Exit patterns.
        private readonly Dictionary<int, HashSet<Collider2D>> _triggerContactsByCharacter =
            new Dictionary<int, HashSet<Collider2D>>();

        // Set by CharacterController's predictive sweep when the destination platform
        // is the one that will be crossed from above during this physics step.
        // While locked, trigger-first logic is not allowed to make this platform pass-through.
        private readonly HashSet<int> _predictedTopLandingLockedCharacters =
            new HashSet<int>();

        // Source-platform fall-through lock.
        // When Warrior loses balance from this platform, this source platform must stay
        // ignored until the Warrior has fully left this platformTrigger and no body collider
        // still overlaps platformCollider. This lock has priority over predictive landing,
        // BodyFirst, OnCollisionStay, and trigger restoration.
        private readonly HashSet<int> _sourceFallThroughLockedCharacters =
            new HashSet<int>();

        private readonly Dictionary<int, Coroutine> _sourceFallRestoreCoroutines =
            new Dictionary<int, Coroutine>();

        // Zalayty-only jump-down source-platform lock.
        // This is intentionally separate from the Warrior source fall-through lock.
        private readonly HashSet<int> _zalaytyJumpDownLockedCharacters =
            new HashSet<int>();

        // After the source body is clear, collision is restored even if Zalayty is
        // still inside this platformTrigger. This prevents OnTriggerStay from
        // immediately making the source platform pass-through again.
        private readonly HashSet<int> _zalaytyJumpDownRestoredInsideTriggerCharacters =
            new HashSet<int>();

        private readonly Dictionary<int, Coroutine> _zalaytyJumpDownRestoreCoroutines =
            new Dictionary<int, Coroutine>();

        [Header("Anti-jitter")]
        [SerializeField] private float maxWarriorSpeed = 20f;

        [Tooltip("Same pass-through buffer used when the character comes from below.")]
        [SerializeField, Min(0f)] private float passThroughBuffer = 0.08f;

        [Tooltip("How close the character bottom must be to the platform top before collision can be restored while inside the trigger.")]
        [SerializeField, Min(0f)] private float landingBand = 0.14f;

        [Tooltip("Small horizontal skin used to decide if the character is really above the platform top.")]
        [SerializeField, Min(0f)] private float horizontalLandingSkin = 0.03f;

        [Tooltip("Safety timeout used if the character exits the trigger but Unity still reports a body overlap.")]
        [SerializeField, Min(0f)] private float restoreAfterTriggerExitTimeout = 0.75f;

        [Tooltip("Zalayty-only: how close to the source platform body still counts as physical contact during jump-down restore.")]
        [SerializeField, Min(0f)] private float zalaytyJumpDownRestoreContactSkin = 0.01f;

        [SerializeField] private float edgeZoneWidth = 0.35f;

        [Header("Warrior Anti Ledge Snag")]
        [SerializeField] private bool enableWarriorAntiLedgeSnag = true;

        [Tooltip("Warrior must be moving upward faster than this to treat a side/corner hit as ledge snag.")]
        [SerializeField, Min(0f)] private float warriorAntiSnagMinUpVelocity = 0.05f;

        [Tooltip("If the Warrior bottom is still below the platform top by this amount, the hit is considered a side/lip hit, not a landing.")]
        [SerializeField, Min(0f)] private float warriorAntiSnagBelowTopSkin = 0.04f;

        [Tooltip("Contact normal Y required to count as a real top landing. Smaller values are treated as side/corner hits.")]
        [SerializeField, Range(0f, 1f)] private float warriorAntiSnagTopNormalY = 0.55f;

        [Tooltip("Horizontal contact normal required to identify side/corner collision.")]
        [SerializeField, Range(0f, 1f)] private float warriorAntiSnagSideNormalX = 0.35f;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
#if UNITY_ANDROID
            Time.fixedDeltaTime = 0.01667f;
            Application.targetFrameRate = 60;
#endif
        }

        protected override void Start()
        {
            base.Start();

            warriorEdgeTimer = new Timer(0.75f);
            warriorEdgeTimer.OnTimerComplete += PerformWarriorEdgeFall;

            zalaytyEdgeTimer = new Timer(0.20f);
            zalaytyEdgeTimer.OnTimerComplete += PerformZalaytyEdgeJumpOrDrop;

            var warrior = GameMgr.Instance?.WarriorInstance;
            if (warrior != null && warrior.rigidbody2 != null)
            {
                warrior.rigidbody2.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                warrior.rigidbody2.interpolation = RigidbodyInterpolation2D.Interpolate;
            }
        }

        protected virtual void FixedUpdate()
        {
            warriorEdgeTimer?.Update(Time.fixedDeltaTime);
            zalaytyEdgeTimer?.Update(Time.fixedDeltaTime);

            var warrior = GameMgr.Instance?.WarriorInstance;

            if (warrior != null &&
                warrior.rigidbody2 != null &&
                warrior.rigidbody2.linearVelocity.magnitude > maxWarriorSpeed)
            {
                warrior.rigidbody2.linearVelocity =
                    warrior.rigidbody2.linearVelocity.normalized * maxWarriorSpeed;
            }
        }

        // ─── Trigger events ───────────────────────────────────────────────────

        private void OnTriggerEnter2D(Collider2D other)
        {
            var character = other.GetComponentInParent<CharacterController>();

            if (!IsValidPlatformCharacter(character))
                return;
            //if (character is M97Monster) 

            //{
            //    int t = 0;

            //}
            AddTriggerContact(character, other);

            if (IsZalaytyJumpDownRestoredInsideTrigger(character))
            {
                SetIgnoreForCharacter(character, false);
                return;
            }

            if (IsZalaytyJumpDownLocked(character))
            {
                Debug
                    .Log("Zalayty jump-down source platform still locked on trigger enter for " + character.name);
                SetIgnoreForCharacter(character, true);
                StartRestoreZalaytyJumpDownWhenBodyClear((ZalaytyMonster)character);
                return;
            }

            if (IsSourceFallThroughLocked(character))
            {
                Debug.Log("Source fall-through still locked on trigger enter for " + character.name);
                // This is the platform source of the edge fall. It must remain
                // pass-through until Warrior fully leaves this trigger/body area.
                SetIgnoreForCharacter(character, true);
                return;
            }

            if (ShouldKeepPredictedTopLandingSolid(character))
            {
                RememberFirstContact(character, FirstPlatformContact.BodyFirst);
                SetIgnoreForCharacter(character, false);
                return;
            }

            // Strong rule:
            // If Warrior/Zalayty enters this platformTrigger first, this same
            // platform must immediately ignore its platformCollider.
            // Direction does not matter.
            RememberTriggerContactWithPriority(character);

            if (EnteredTriggerFirst(character))
            {
                SetIgnoreForCharacter(character, true);
            }

        }

        private void OnTriggerStay2D(Collider2D other)
        {
            var character = other.GetComponentInParent<CharacterController>();

            if (!IsValidPlatformCharacter(character))
                return;


            if (IsZalaytyJumpDownRestoredInsideTrigger(character))
            {
                SetIgnoreForCharacter(character, false);
                return;
            }

            if (IsZalaytyJumpDownLocked(character))
            {
                Debug.Log("Zalayty jump-down source platform still locked on trigger stay for " + character.name);
                SetIgnoreForCharacter(character, true);
                StartRestoreZalaytyJumpDownWhenBodyClear((ZalaytyMonster)character);
                return;
            }

            if (IsSourceFallThroughLocked(character))
            {
                Debug.Log("Source fall-through still locked on trigger stay for " + character.name);
                SetIgnoreForCharacter(character, true);
                return;
            }

            if (ShouldKeepPredictedTopLandingSolid(character))
            {
                RememberFirstContact(character, FirstPlatformContact.BodyFirst);
                SetIgnoreForCharacter(character, false);
                return;
            }

            // Safety for missed Enter events: if Unity gives us Stay first, treat it
            // as trigger-first unless the normal body collider was already recorded first
            // in an older physics frame.
            RememberTriggerContactWithPriority(character);

            // If the normal platformCollider was touched first, never let trigger /
            // edge / going-up logic turn this platform pass-through for this character.
            if (EnteredBodyFirst(character))
            {
                SetIgnoreForCharacter(character, false);
                return;
            }

            // Natural jump path rule:
            // If any pass-through condition is true, do not restore the body collider
            // and do not let the platform artificially catch the character.
            if (ShouldKeepNaturalJumpPath(character))
            {
                SetIgnoreForCharacter(character, true);
                return;
            }

            // Main anti-jitter rule:
            // If this platform is already ignored, do NOT restore it just because one
            // child collider changed trigger state. Restore only when the character is
            // really landing on the top surface.
            if (IsCharacterCurrentlyIgnoringPlatform(character))
            {
                if (IsCharacterLandingOnTopNow(character))
                    SetIgnoreForCharacter(character, false);
                else
                {
                    Debug.Log("Not landing on top yet for " + character.name + ", keeping platform ignored");
                    SetIgnoreForCharacter(character, true);
                }

            }
        }

        private void OnTriggerExit2D(Collider2D collision)
        {
            var character = collision.GetComponentInParent<CharacterController>();

            if (!IsValidPlatformCharacter(character))
                return;

            RemoveTriggerContact(character, collision);

            if (IsZalaytyJumpDownRestoredInsideTrigger(character))
            {
                SetIgnoreForCharacter(character, false);

                if (!IsCharacterStillInsideThisTrigger(character))
                {
                    ClearZalaytyJumpDownRestoredInsideTrigger(character);
                    ClearFirstContact(character);
                }

                return;
            }

            if (IsZalaytyJumpDownLocked(character))
            {
               
                SetIgnoreForCharacter(character, true);
                StartRestoreZalaytyJumpDownWhenBodyClear((ZalaytyMonster)character);
                return;
            }

            if (IsSourceFallThroughLocked(character))
            {
               // Debug.Log("Source fall-through still locked on trigger exit for " + character.name);
                // Do not restore on the first child-collider exit. Restore only after
                // all Warrior colliders have left this trigger and no body overlap remains.
                SetIgnoreForCharacter(character, true);

                if (!IsCharacterStillInsideThisTrigger(character))
                    StartRestoreSourceFallThroughWhenFullyClear(character);

                return;
            }

            if (ShouldKeepPredictedTopLandingSolid(character))
            {
                SetIgnoreForCharacter(character, false);
                return;
            }

            // Do not restore platformCollider when only one child collider exited
            // while another Warrior/Zalayty body collider is still inside this trigger.
            if (IsCharacterStillInsideThisTrigger(character))
            {
                if (EnteredTriggerFirst(character))
                {
                    SetIgnoreForCharacter(character, true);
                }


                return;
            }

            // Now the character has really exited this platformTrigger.
            // This same platform can restore its own platformCollider.
            SetIgnoreForCharacter(character, false);
            ClearFirstContact(character);
        }

        // ─── Collision events ─────────────────────────────────────────────────

        protected override void OnCollisionEnter2D(Collision2D collision)
        {
            CharacterController character = collision.collider.GetComponentInParent<CharacterController>();

            if (character is M97Monster)

            {
                int t = 0;


            }
            if (IsValidPlatformCharacter(character))
            {


                if (IsZalaytyJumpDownLocked(character))
                {
                    Debug.Log("Zalayty jump-down source platform still locked on collision enter for " + character.name);
                    SetIgnoreForCharacter(character, true);
                    StartRestoreZalaytyJumpDownWhenBodyClear((ZalaytyMonster)character);
                    return;
                }

                if (IsSourceFallThroughLocked(character))
                {
                    Debug.Log("Source fall-through still locked on collision enter for " + character.name);
                    SetIgnoreForCharacter(character, true);
                    StartRestoreSourceFallThroughWhenFullyClear(character);
                    return;
                }

                if (ShouldTreatAsWarriorLedgeSnag(character, collision))
                {
                    ForceFirstContact(character, FirstPlatformContact.TriggerFirst);
                    SetIgnoreForCharacter(character, true);
                    return;
                }

                RememberFirstContact(character, FirstPlatformContact.BodyFirst);

                // If platformCollider was the first contact, or this platform was
                // preselected by the predictive destination-platform sweep, keep it solid.
                if (EnteredBodyFirst(character) || ShouldKeepPredictedTopLandingSolid(character))
                    SetIgnoreForCharacter(character, false);
            }

            base.OnCollisionEnter2D(collision);
        }

        protected override void OnCollisionStay2D(Collision2D collision)
        {
            CharacterController character = collision.collider.GetComponentInParent<CharacterController>();
            if (character == null)
                return;

            if (IsValidPlatformCharacter(character))
            {
                if (IsZalaytyJumpDownLocked(character))
                {
                    Debug.Log("Zalayty jump-down source platform still locked on collision stay for " + character.name);
                    SetIgnoreForCharacter(character, true);
                    StartRestoreZalaytyJumpDownWhenBodyClear((ZalaytyMonster)character);
                    return;
                }

                if (IsSourceFallThroughLocked(character))
                {
                    Debug.Log("Source fall-through still locked on collision stay for " + character.name);
                    SetIgnoreForCharacter(character, true);
                    StartRestoreSourceFallThroughWhenFullyClear(character);
                    return;
                }

                if (ShouldTreatAsWarriorLedgeSnag(character, collision))
                {
                    ForceFirstContact(character, FirstPlatformContact.TriggerFirst);
                    SetIgnoreForCharacter(character, true);
                    return;
                }

                RememberFirstContact(character, FirstPlatformContact.BodyFirst);

                if (EnteredBodyFirst(character) || ShouldKeepPredictedTopLandingSolid(character))
                    SetIgnoreForCharacter(character, false);
                else
                    HandleEdgeJumpPassThrough(character);
            }
            else
            {
                // Preserve previous behavior for any other CharacterController-derived enemy.
                HandleEdgeJumpPassThrough(character);
            }

            base.OnCollisionStay2D(collision);

            if (character is Warrior warrior)
            {
                warrior.CurrentplatForm = this;
                warrior.IsFallingPlfExit = false;
                warrior.IsFallingGrazesEdge = false;

                int c = warrior.CountGroundPoints();

                if (c >= 2)
                {
                    if (warriorEdgeTimer.IsRunning)
                        warriorEdgeTimer.Stop();

                    _pendingWarriorFall = null;
                    return;
                }

                if (c == 1 && !warriorEdgeTimer.IsRunning)
                {
                    if (warrior.activesMoveCoroutine != null)
                        return;

                    _pendingWarriorFall = warrior;
                    warrior.ShowLosingBalance();
                    warriorEdgeTimer.Start();
                    return;
                }

                if (c == 0)
                {
                    if (warriorEdgeTimer.IsRunning)
                        warriorEdgeTimer.Stop();

                    bool notMovingUp =
                        warrior.rigidbody2 == null ||
                        warrior.rigidbody2.linearVelocity.y <= 0.05f;

                    if (notMovingUp)
                    {
                        warrior.IsFallingGrazesEdge = true;
                        warrior.IsFallingEdge = true;
                        warrior.IsFallingPlfExit = false;
                        warrior.IsFallingHitEnemy = false;
                        warrior.CanMove = false;

                        SetIgnoreForCharacter(warrior, true);

                        if (warrior.rigidbody2 != null)
                        {
                            warrior.rigidbody2.gravityScale =
                                Mathf.Max(warrior.rigidbody2.gravityScale, 2.5f);

                            Vector2 v = warrior.rigidbody2.linearVelocity;

                            if (v.y > -0.05f)
                                v.y = -0.05f;

                            warrior.rigidbody2.linearVelocity = v;
                        }

                        warrior.JumpAnimationDisplay();
                    }
                    else
                    {
                        warrior.IsFallingGrazesEdge = false;
                    }

                    _pendingWarriorFall = null;
                }
            }
            else if (character is ZalaytyMonster z)
            {
                z.CurrentplatForm = this;

                // Zalayty edge fall must be based on THIS platform only.
                // CountGroundPoints() can be polluted by nearby/overlapping platforms.
                // When only one or zero ground points remain on this platform, let him
                // fall through the source platform, then restore collision after clear.
                if (ShouldLetZalaytyFallFromThisPlatform(z))
                {
                    _pendingZalaytyJump = z;

                    if (!zalaytyEdgeTimer.IsRunning)
                        zalaytyEdgeTimer.Start();
                }
                else if (_pendingZalaytyJump == z)
                {
                    _pendingZalaytyJump = null;

                    if (zalaytyEdgeTimer.IsRunning)
                        zalaytyEdgeTimer.Stop();
                }
            }
        }

        protected override void OnCollisionExit2D(Collision2D collision)
        {
            base.OnCollisionExit2D(collision);

            var character = collision.collider.GetComponentInParent<CharacterController>();
            if (character == null)
                return;
            if (IsValidPlatformCharacter(character) && IsZalaytyJumpDownLocked(character))
            {
                Debug.Log("Zalayty jump-down source platform still locked on collision exit for " + character.name);
                SetIgnoreForCharacter(character, true);
                StartRestoreZalaytyJumpDownWhenBodyClear((ZalaytyMonster)character);
                return;
            }

            if (IsValidPlatformCharacter(character) && IsSourceFallThroughLocked(character))
            {
                Debug.Log("Source fall-through still locked on collision exit for " + character.name);
                SetIgnoreForCharacter(character, true);
                StartRestoreSourceFallThroughWhenFullyClear(character);
            }

            if (IsValidPlatformCharacter(character) && !IsSourceFallThroughLocked(character))
                StartCoroutine(ClearPlatformMemoryWhenCharacterFullyLeft(character));

            if (character is Warrior w)
            {
                // Do NOT restore platform collision here.
                // Restore belongs to trigger exit / real landing only.
                if (!w.IsJumping)
                    w.IsFallingPlfExit = w.activesMoveCoroutine != null;

                if (!w.IsJumping && w.activesMoveCoroutine is null)
                    warriorEdgeTimer.Stop();

                if (warriorEdgeTimer.IsRunning)
                {
                    warriorEdgeTimer.Stop();
                    w.IsFallingGrazesEdge = false;
                    _pendingWarriorFall = null;
                }
            }
            else if (character is ZalaytyMonster)
            {
                if (zalaytyEdgeTimer.IsRunning)
                {
                    zalaytyEdgeTimer.Stop();
                    _pendingZalaytyJump = null;
                }
            }
        }

        // ─── Zalayty-only jump-down source-platform lock ─────────────────────

        public override bool RequestZalaytyJumpDownThroughSourcePlatform(ZalaytyMonster zalayty)
        {
            if (zalayty == null || platformCollider == null)
                return false;

            int id = GetCharacterKey(zalayty);
            if (id == 0)
                return false;

            _zalaytyJumpDownLockedCharacters.Add(id);
            _zalaytyJumpDownRestoredInsideTriggerCharacters.Remove(id);

            // The source platform is being intentionally left downward. It must not
            // become a predicted top landing platform while Zalayty is still clearing it.
            _predictedTopLandingLockedCharacters.Remove(id);

            _firstContactByCharacter[id] = FirstPlatformContact.TriggerFirst;
            _firstContactFrameByCharacter[id] = Time.frameCount;

            SetIgnoreForCharacter(zalayty, true);
            StartRestoreZalaytyJumpDownWhenBodyClear(zalayty);
            return true;
        }

        public override bool ForceRestoreZalaytyJumpDownSourcePlatform(ZalaytyMonster zalayty)
        {
            if (zalayty == null || platformCollider == null)
                return false;

            ClearZalaytyJumpDownLock(zalayty, restoreCollision: true);
            return true;
        }

        private bool IsZalaytyJumpDownLocked(CharacterController character)
        {
            if (character is not ZalaytyMonster)
                return false;

            int id = GetCharacterKey(character);
            return id != 0 && _zalaytyJumpDownLockedCharacters.Contains(id);
        }

        private bool IsZalaytyJumpDownRestoredInsideTrigger(CharacterController character)
        {
            if (character is not ZalaytyMonster)
                return false;

            int id = GetCharacterKey(character);
            return id != 0 && _zalaytyJumpDownRestoredInsideTriggerCharacters.Contains(id);
        }

        private void ClearZalaytyJumpDownRestoredInsideTrigger(CharacterController character)
        {
            int id = GetCharacterKey(character);
            if (id != 0)
                _zalaytyJumpDownRestoredInsideTriggerCharacters.Remove(id);
        }

        private void StartRestoreZalaytyJumpDownWhenBodyClear(ZalaytyMonster zalayty)
        {
            int id = GetCharacterKey(zalayty);
            if (id == 0)
                return;

            if (_zalaytyJumpDownRestoreCoroutines.ContainsKey(id))
                return;

            _zalaytyJumpDownRestoreCoroutines[id] =
                StartCoroutine(RestoreZalaytyJumpDownWhenBodyClear(zalayty, id));
        }

        private IEnumerator RestoreZalaytyJumpDownWhenBodyClear(ZalaytyMonster zalayty, int id)
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();

            // Let the IgnoreCollision call affect the physics step first.
            yield return wait;

            while (zalayty != null && platformCollider != null)
            {
                if (!IsAnyCharacterColliderTouchingOrOverlappingPlatformBody(zalayty, zalaytyJumpDownRestoreContactSkin))
                    break;
                Debug.Log("Zalayty jump-down still unsafe because of body contact for " + zalayty.name);
                SetIgnoreForCharacter(zalayty, true);
                yield return wait;
            }

            if (zalayty != null && platformCollider != null)
                ClearZalaytyJumpDownLock(zalayty, restoreCollision: true);
            else
                ClearZalaytyJumpDownLock(zalayty, restoreCollision: false);
        }

        private void ClearZalaytyJumpDownLock(ZalaytyMonster zalayty, bool restoreCollision)
        {
            int id = GetCharacterKey(zalayty);
            if (id == 0)
                return;

            _zalaytyJumpDownLockedCharacters.Remove(id);

            if (_zalaytyJumpDownRestoreCoroutines.TryGetValue(id, out Coroutine coroutine) && coroutine != null)
                StopCoroutine(coroutine);

            _zalaytyJumpDownRestoreCoroutines.Remove(id);
            ClearFirstContact(zalayty);

            if (restoreCollision && zalayty != null && platformCollider != null)
            {
                if (IsCharacterStillInsideThisTrigger(zalayty))
                    _zalaytyJumpDownRestoredInsideTriggerCharacters.Add(id);
                else
                    _zalaytyJumpDownRestoredInsideTriggerCharacters.Remove(id);

                SetIgnoreForCharacter(zalayty, false);
            }
            else
            {
                _zalaytyJumpDownRestoredInsideTriggerCharacters.Remove(id);
            }
        }

        private bool ShouldLetZalaytyFallFromThisPlatform(ZalaytyMonster zalayty)
        {
            if (zalayty == null || platformCollider == null)
                return false;

            // Already pass-through; the restore coroutine owns the collider state now.
            if (IsZalaytyJumpDownLocked(zalayty))
                return false;

            // Do not turn a predicted destination landing into a fall-through.
            if (ShouldKeepPredictedTopLandingSolid(zalayty))
                return false;

            return zalayty.CountGroundPointsOnSpecificPlatform(this) <= 1;
        }

        // ─── Anti-jitter helpers ──────────────────────────────────────────────

        public override bool TryPrepareForPredictedTopLanding(CharacterController character, Collider2D predictedPlatformCollider)
        {
            if (!IsValidPlatformCharacter(character))
                return false;

            if (predictedPlatformCollider != platformCollider)
                return false;

            int id = GetCharacterKey(character);
            if (id == 0)
                return false;

            if (IsZalaytyJumpDownLocked(character))
            {
                Debug.Log("Zalayty jump-down still locked for " + character.name);
                SetIgnoreForCharacter(character, true);
                StartRestoreZalaytyJumpDownWhenBodyClear((ZalaytyMonster)character);
                return false;
            }

            if (IsSourceFallThroughLocked(character))
            {
                // This platform is still the source platform being left after
                // LosingBalance/PerformWarriorEdgeFall. It must NOT be converted
                // into a predicted destination landing while any of its trigger/body
                // contacts are still active; otherwise SetIgnore(false) cancels the
                // physical fall.
                if (IsSourceFallThroughStillUnsafe(character))
                {
                    Debug.Log("Source fall-through still unsafe for " + character.name);
                    SetIgnoreForCharacter(character, true);
                    StartRestoreSourceFallThroughWhenFullyClear(character);
                    return false;
                }

                ClearSourceFallThroughLock(character, restoreCollision: false);
            }

            _predictedTopLandingLockedCharacters.Add(id);

            // The predictive sweep has proven that this destination platform is
            // crossed from above during the current physics step. Therefore, this
            // platform must behave as BodyFirst for this character, even if its
            // trigger callback ran earlier in the same frame.
            _firstContactByCharacter[id] = FirstPlatformContact.BodyFirst;
            _firstContactFrameByCharacter[id] = Time.frameCount;

            SetIgnoreForCharacter(character, false);
            return true;
        }

        private bool ShouldKeepPredictedTopLandingSolid(CharacterController character)
        {
            int id = GetCharacterKey(character);
            return id != 0 && _predictedTopLandingLockedCharacters.Contains(id);
        }

        private void LockSourceFallThrough(CharacterController character)
        {
            int id = GetCharacterKey(character);
            if (id == 0)
                return;

            _sourceFallThroughLockedCharacters.Add(id);

            // Source fall-through must behave like TriggerFirst until the source
            // trigger/body is fully clear.
            _firstContactByCharacter[id] = FirstPlatformContact.TriggerFirst;
            _firstContactFrameByCharacter[id] = Time.frameCount;

            // The source platform is not a destination landing candidate anymore.
            _predictedTopLandingLockedCharacters.Remove(id);

            Debug.Log("Source fall-through locked for " + character.name);
            SetIgnoreForCharacter(character, true);
        }

        private bool IsSourceFallThroughLocked(CharacterController character)
        {
            int id = GetCharacterKey(character);
            return id != 0 && _sourceFallThroughLockedCharacters.Contains(id);
        }

        private bool IsSourceFallThroughStillUnsafe(CharacterController character)
        {
            return IsCharacterStillInsideThisTrigger(character) ||
                   IsAnyBodyColliderOverlappingPlatformBody(character);
        }

        private void StartRestoreSourceFallThroughWhenFullyClear(CharacterController character)
        {
            int id = GetCharacterKey(character);
            if (id == 0)
                return;

            if (_sourceFallRestoreCoroutines.ContainsKey(id))
                return;

            _sourceFallRestoreCoroutines[id] =
                StartCoroutine(RestoreSourceFallThroughWhenFullyClear(character, id));
        }

        private IEnumerator RestoreSourceFallThroughWhenFullyClear(CharacterController character, int id)
        {
            while (character != null && platformCollider != null)
            {
                if (!IsSourceFallThroughStillUnsafe(character))
                    break;

                Debug.Log("Source fall-through still unsafe for " + character.name);
                SetIgnoreForCharacter(character, true);
                yield return new WaitForFixedUpdate();
            }

            if (character != null && platformCollider != null)
                ClearSourceFallThroughLock(character, restoreCollision: true);
            else
                ClearSourceFallThroughLock(character, restoreCollision: false);
        }

        private void ClearSourceFallThroughLock(CharacterController character, bool restoreCollision)
        {
            int id = GetCharacterKey(character);
            if (id == 0)
                return;

            _sourceFallThroughLockedCharacters.Remove(id);

            if (_sourceFallRestoreCoroutines.TryGetValue(id, out Coroutine coroutine) && coroutine != null)
                StopCoroutine(coroutine);

            _sourceFallRestoreCoroutines.Remove(id);
            ClearFirstContact(character);

            if (restoreCollision && character != null && platformCollider != null)
                SetIgnoreForCharacter(character, false);
        }

        private IEnumerator ClearPlatformMemoryWhenCharacterFullyLeft(CharacterController character)
        {
            yield return new WaitForFixedUpdate();

            if (character == null)
                yield break;

            if (IsCharacterStillInsideThisTrigger(character))
                yield break;

            if (IsAnyBodyColliderOverlappingPlatformBody(character))
                yield break;

            ClearFirstContact(character);
        }

        private bool IsValidPlatformCharacter(CharacterController character)
        {
            return character != null &&
                   character.collider2 != null &&
                   platformTrigger != null &&
                   platformCollider != null &&
                   (character is Warrior || character is ZalaytyMonster);
        }

        //private IEnumerator ReEnableCollisionWhenWholeCharacterIsClear(CharacterController character)
        //{
        //    yield return new WaitForFixedUpdate();

        //    if (character == null || platformCollider == null)
        //        yield break;

        //    if (!platformCollider.enabled)
        //        platformCollider.enabled = true;

        //    float timeoutAt = Time.time + restoreAfterTriggerExitTimeout;

        //    while (character != null && platformCollider != null)
        //    {
        //        bool stillInsideTrigger = IsAnyBodyColliderInsideTrigger(character);

        //        bool unsafeBodyOverlap =
        //            IsAnyBodyColliderOverlappingPlatformBody(character) &&
        //            !IsCharacterLandingOnTopNow(character);

        //        if (!stillInsideTrigger && !unsafeBodyOverlap)
        //            break;

        //        // Safety: if the character is no longer inside the trigger but Unity still
        //        // reports overlap for too long, restore anyway to avoid permanent ignore.
        //        if (!stillInsideTrigger && Time.time >= timeoutAt)
        //            break;

        //        yield return new WaitForFixedUpdate();
        //    }

        //    if (character == null || platformCollider == null)
        //        yield break;

        //    SetIgnoreForCharacter(character, false);
        //    ClearFirstContact(character);
        //}

        private int GetCharacterKey(CharacterController character)
        {
            return character != null ? character.GetInstanceID() : 0;
        }

        private void RememberFirstContact(CharacterController character, FirstPlatformContact contact)
        {
            int id = GetCharacterKey(character);

            if (id == 0 || contact == FirstPlatformContact.None)
                return;

            if (_firstContactByCharacter.TryGetValue(id, out FirstPlatformContact existing) &&
                existing != FirstPlatformContact.None)
            {
                return;
            }

            _firstContactByCharacter[id] = contact;
            _firstContactFrameByCharacter[id] = Time.frameCount;
        }

        private void ForceFirstContact(CharacterController character, FirstPlatformContact contact)
        {
            int id = GetCharacterKey(character);

            if (id == 0 || contact == FirstPlatformContact.None)
                return;

            _firstContactByCharacter[id] = contact;
            _firstContactFrameByCharacter[id] = Time.frameCount;
        }

        private void RememberTriggerContactWithPriority(CharacterController character)
        {
            int id = GetCharacterKey(character);

            if (id == 0)
                return;

            if (_sourceFallThroughLockedCharacters.Contains(id))
            {
                _firstContactByCharacter[id] = FirstPlatformContact.TriggerFirst;
                _firstContactFrameByCharacter[id] = Time.frameCount;
                return;
            }

            if (_predictedTopLandingLockedCharacters.Contains(id))
            {
                _firstContactByCharacter[id] = FirstPlatformContact.BodyFirst;
                _firstContactFrameByCharacter[id] = Time.frameCount;
                return;
            }

            if (!_firstContactByCharacter.TryGetValue(id, out FirstPlatformContact existing) ||
                existing == FirstPlatformContact.None)
            {
                _firstContactByCharacter[id] = FirstPlatformContact.TriggerFirst;
                _firstContactFrameByCharacter[id] = Time.frameCount;
                return;
            }

            // Unity can report OnCollisionEnter2D and OnTriggerEnter2D in the same
            // physics frame. For one-way/pass-through platforms, prefer trigger-first
            // during that same frame so the character is not caught by platformCollider.
            if (existing == FirstPlatformContact.BodyFirst &&
                _firstContactFrameByCharacter.TryGetValue(id, out int recordedFrame) &&
                recordedFrame == Time.frameCount)
            {
                _firstContactByCharacter[id] = FirstPlatformContact.TriggerFirst;
                _firstContactFrameByCharacter[id] = Time.frameCount;
            }
        }

        private void ClearFirstContact(CharacterController character)
        {
            int id = GetCharacterKey(character);

            if (id == 0)
                return;

            _firstContactByCharacter.Remove(id);
            _firstContactFrameByCharacter.Remove(id);
            _triggerContactsByCharacter.Remove(id);
            _predictedTopLandingLockedCharacters.Remove(id);

            if (character is ZalaytyMonster)
                _zalaytyJumpDownRestoredInsideTriggerCharacters.Remove(id);
        }

        private void AddTriggerContact(CharacterController character, Collider2D collider)
        {
            int id = GetCharacterKey(character);

            if (id == 0 || collider == null)
                return;

            if (!_triggerContactsByCharacter.TryGetValue(id, out HashSet<Collider2D> contacts))
            {
                contacts = new HashSet<Collider2D>();
                _triggerContactsByCharacter[id] = contacts;
            }

            contacts.Add(collider);
        }

        private void RemoveTriggerContact(CharacterController character, Collider2D collider)
        {
            int id = GetCharacterKey(character);

            if (id == 0 || collider == null)
                return;

            if (!_triggerContactsByCharacter.TryGetValue(id, out HashSet<Collider2D> contacts))
                return;

            contacts.Remove(collider);

            if (contacts.Count <= 0)
                _triggerContactsByCharacter.Remove(id);
        }

        private bool IsCharacterStillInsideThisTrigger(CharacterController character)
        {
            int id = GetCharacterKey(character);

            if (id != 0 &&
                _triggerContactsByCharacter.TryGetValue(id, out HashSet<Collider2D> contacts))
            {
                contacts.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);

                if (contacts.Count > 0)
                    return true;

                _triggerContactsByCharacter.Remove(id);
            }

            // Fallback for cases where Unity missed one enter/exit callback.
            return IsAnyBodyColliderInsideTrigger(character);
        }

        private FirstPlatformContact GetFirstContact(CharacterController character)
        {
            if (character == null)
                return FirstPlatformContact.None;

            if (_firstContactByCharacter.TryGetValue(character.GetInstanceID(), out FirstPlatformContact contact))
                return contact;

            return FirstPlatformContact.None;
        }

        private bool EnteredTriggerFirst(CharacterController character)
        {
            return GetFirstContact(character) == FirstPlatformContact.TriggerFirst;
        }

        private bool EnteredBodyFirst(CharacterController character)
        {
            return GetFirstContact(character) == FirstPlatformContact.BodyFirst;
        }

        private void SetIgnoreForCharacter(CharacterController ch, bool ignore)
        {
            if (ch == null || platformCollider == null)
                return;

            Collider2D[] cols = ch.GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D c = cols[i];

                if (c == null)
                    continue;

                Physics2D.IgnoreCollision(platformCollider, c, ignore);
            }
        }

        private bool IsCharacterCurrentlyIgnoringPlatform(CharacterController character)
        {
            if (character == null || platformCollider == null)
                return false;

            Collider2D[] cols = character.GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D col = cols[i];

                if (col == null)
                    continue;

                if (Physics2D.GetIgnoreCollision(platformCollider, col))
                    return true;
            }

            return false;
        }

        private bool IsAnyBodyColliderInsideTrigger(CharacterController character)
        {
            if (character == null || platformTrigger == null)
                return false;

            Collider2D support = GetStandingCollider(character);

            if (support != null && platformTrigger.IsTouching(support))
                return true;

            Collider2D[] cols = character.GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D col = cols[i];

                if (col == null || col.isTrigger)
                    continue;

                if (platformTrigger.IsTouching(col))
                    return true;
            }

            return false;
        }

        private bool IsAnyBodyColliderOverlappingPlatformBody(CharacterController character)
        {
            if (character == null || platformCollider == null)
                return false;

            Collider2D support = GetStandingCollider(character);

            if (support != null)
            {
                ColliderDistance2D supportDistance =
                    Physics2D.Distance(support, platformCollider);

                if (supportDistance.isOverlapped)
                    return true;
            }

            Collider2D[] cols = character.GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D col = cols[i];

                if (col == null || col.isTrigger)
                    continue;

                ColliderDistance2D distance = Physics2D.Distance(col, platformCollider);

                if (distance.isOverlapped)
                    return true;
            }

            return false;
        }

        private bool IsInsideEdgeZone(Collider2D characterCollider)
        {
            if (characterCollider == null || platformCollider == null)
                return false;

            Bounds pb = platformCollider.bounds;
            Bounds cb = characterCollider.bounds;

            float leftEdge = pb.min.x + edgeZoneWidth;
            float rightEdge = pb.max.x - edgeZoneWidth;
            float charX = cb.center.x;

            return charX < leftEdge || charX > rightEdge;
        }

        private bool ShouldTreatAsWarriorLedgeSnag(CharacterController character, Collision2D collision)
        {
            if (!enableWarriorAntiLedgeSnag)
                return false;

            Warrior warrior = character as Warrior;

            if (warrior == null)
                return false;

            if (collision == null || warrior.rigidbody2 == null || platformCollider == null)
                return false;

            // Only protect upward jump / upward movement.
            if (!warrior.IsJumping && warrior.rigidbody2.linearVelocity.y <= warriorAntiSnagMinUpVelocity)
                return false;

            Collider2D support = GetStandingCollider(warrior);

            if (support == null)
                return false;

            Bounds pb = platformCollider.bounds;
            Bounds wb = support.bounds;

            // If Warrior is already in a valid landing position, do not make the platform pass-through.
            if (IsCharacterLandingOnTopNow(warrior))
                return false;

            // If the Warrior bottom is clearly below the platform top, this is a lip/side/corner hit.
            bool bottomStillBelowPlatformTop =
                wb.min.y < pb.max.y - warriorAntiSnagBelowTopSkin;

            if (!bottomStillBelowPlatformTop)
                return false;

            bool hasSideOrCornerContact = false;
            bool hasRealTopContact = false;

            int contactCount = collision.contactCount;

            for (int i = 0; i < contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);
                Vector2 n = contact.normal;

                // On the platform side/corner, the normal usually has strong X and weak Y.
                if (Mathf.Abs(n.x) >= warriorAntiSnagSideNormalX && n.y < warriorAntiSnagTopNormalY)
                    hasSideOrCornerContact = true;

                // A real top landing has a strong upward normal.
                if (n.y >= warriorAntiSnagTopNormalY)
                    hasRealTopContact = true;
            }

            return hasSideOrCornerContact && !hasRealTopContact;
        }

        private bool IsCharacterLandingOnTopNow(CharacterController character)
        {
            if (character == null || platformCollider == null)
                return false;

            Collider2D support = GetStandingCollider(character);

            if (support == null)
                return false;

            Bounds pb = platformCollider.bounds;
            Bounds cb = support.bounds;

            bool movingDownOrStable =
                character.rigidbody2 == null ||
                character.rigidbody2.linearVelocity.y <= 0.05f;

            bool horizontallyOverTop =
                cb.max.x > pb.min.x + horizontalLandingSkin &&
                cb.min.x < pb.max.x - horizontalLandingSkin;

            bool closeEnoughToTopFromAbove =
                cb.min.y >= pb.max.y - landingBand;

            return movingDownOrStable &&
                   horizontallyOverTop &&
                   closeEnoughToTopFromAbove;
        }

        private bool ShouldPassThroughPlatformNow(CharacterController character)
        {
            if (character == null || platformCollider == null || platformTrigger == null)
                return false;

            // Predictive destination-platform top landing wins over trigger-first.
            // This is what prevents high-speed tunneling on a destination platform
            // different from CurrentplatForm.
            if (ShouldKeepPredictedTopLandingSolid(character))
                return false;

            // Priority 1: trigger-first. Direction does not matter.
            // The character must naturally continue the jump path until the trigger exits.
            if (EnteredTriggerFirst(character))
                return true;

            // Priority 2: body-first. Never pass through because of trigger / edge /
            // going-up logic after the normal collider was the first contact.
            if (EnteredBodyFirst(character))
                return false;

            // Existing fallback rules are preserved.
            Collider2D support = GetStandingCollider(character);

            if (support == null)
                return false;

            bool goingUp = IsCharacterGoingUpOrBeingLifted(character);

            bool isBelowTrigger =
                support.bounds.max.y < platformTrigger.bounds.min.y - passThroughBuffer;

            bool inEdgeZone = IsInsideEdgeZone(support);

            return goingUp && (isBelowTrigger || inEdgeZone);
        }

        private void HandleEdgeJumpPassThrough(CharacterController character)
        {
            if (character == null ||
                character.collider2 == null ||
                character.rigidbody2 == null ||
                platformCollider == null ||
                platformTrigger == null)
                return;

            if (EnteredBodyFirst(character))
            {
                SetIgnoreForCharacter(character, false);
                return;
            }

            if (ShouldKeepNaturalJumpPath(character))
            {
                Debug.Log("Keeping natural jump path on collision stay for " + character.name);
                SetIgnoreForCharacter(character, true);
                return;
            }

            // Important:
            // Do not blindly call SetIgnoreForCharacter(false) here.
            // Restoring while still inside the trigger is the jitter source.
            if (IsCharacterCurrentlyIgnoringPlatform(character))
            {
                if (IsCharacterLandingOnTopNow(character))
                    SetIgnoreForCharacter(character, false);
                else
                {
                    Debug.Log("Edge jump pass-through for " + character.name);
                    SetIgnoreForCharacter(character, true);
                }

            }
        }

        protected override bool ShouldSkipArtificialPlatformLanding(CharacterController character, Collision2D collision)
        {
            if (!IsValidPlatformCharacter(character))
                return false;

            // If this platform is currently pass-through for Warrior/Zalayty,
            // the base class must not call SeatCharacterOnTop, must not zero Y velocity,
            // and must not stop jump/move coroutines.
            return ShouldKeepNaturalJumpPath(character);
        }

        private bool ShouldKeepNaturalJumpPath(CharacterController character)
        {
            if (!IsValidPlatformCharacter(character))
                return false;

            if (IsZalaytyJumpDownRestoredInsideTrigger(character))
                return false;

            if (IsZalaytyJumpDownLocked(character))
                return true;

            if (ShouldKeepPredictedTopLandingSolid(character))
                return false;

            if (EnteredBodyFirst(character))
                return false;

            return EnteredTriggerFirst(character) ||
                   ShouldPassThroughBecauseLiftedFromBelow(character) ||
                   ShouldPassThroughPlatformNow(character);
        }

        private bool IsCharacterGoingUpOrBeingLifted(CharacterController character)
        {
            if (character == null)
                return false;

            bool goingUp = character.IsJumping;

            if (character.rigidbody2 != null)
                goingUp |= character.rigidbody2.linearVelocity.y > 0.05f;

            if (!goingUp && character.CurrentplatForm is MovingVerticalPlatform movingPlf)
                goingUp = movingPlf.IsMovingUpNow;

            return goingUp;
        }

        // ─── Timer callbacks ──────────────────────────────────────────────────

        private void PerformWarriorEdgeFall()
        {
            if (_pendingWarriorFall == null)
                return;

            var w = _pendingWarriorFall;

            if (w.CountGroundPoints() <= 1)
            {
                w.LastSafePlatform = this;

                Bounds pb = platformCollider.bounds;
                float yOffset = w.collider2 != null ? w.collider2.bounds.extents.y : 0.5f;

                w.LastSafePosition = new Vector3(
                    w.transform.position.x,
                    pb.max.y + yOffset + 0.05f,
                    w.transform.position.z
                );

                w.IsFallingEdge = true;
                w.CanMove = false;

                LockSourceFallThrough(w);
                SetIgnoreForCharacter(w, true);

                if (w.rigidbody2 != null)
                {
                    w.rigidbody2.gravityScale =
                        Mathf.Max(w.rigidbody2.gravityScale, 2.5f);

                    Vector2 v = w.rigidbody2.linearVelocity;

                    if (v.y > -0.05f)
                        v.y = -0.05f;

                    w.rigidbody2.linearVelocity = v;
                }
            }

            _pendingWarriorFall = null;
        }

        private void PerformZalaytyEdgeJumpOrDrop()
        {
            if (_pendingZalaytyJump == null)
                return;

            var z = _pendingZalaytyJump;

            if (ShouldLetZalaytyFallFromThisPlatform(z))
            {
                // This is the important part: do not just mark Zalayty as jumping.
                // The source platform must ignore only Zalayty, then restore itself
                // when Zalayty is no longer touching/overlapping its body.
                RequestZalaytyJumpDownThroughSourcePlatform(z);

                z.CurrentplatForm = null;

                if (z.rigidbody2 != null)
                {
                    z.rigidbody2.constraints = RigidbodyConstraints2D.FreezeRotation;
                    z.rigidbody2.gravityScale = 2.5f;

                    Vector2 v = z.rigidbody2.linearVelocity;
                    if (v.y > -0.05f)
                        v.y = -0.05f;

                    z.rigidbody2.linearVelocity = v;
                }

                z.SetJumping(true);
            }

            _pendingZalaytyJump = null;
        }

        private bool ShouldPassThroughBecauseLiftedFromBelow(CharacterController character)
        {
            if (character == null || character.collider2 == null || platformCollider == null)
                return false;

            if (character.CurrentplatForm is not MovingVerticalPlatform lowerMovingPlatform)
                return false;

            if (lowerMovingPlatform == this)
                return false;

            if (!lowerMovingPlatform.IsMovingUpNow)
                return false;

            if (character.collider2.bounds.center.y >= platformCollider.bounds.max.y)
                return false;

            return true;
        }
    }
}
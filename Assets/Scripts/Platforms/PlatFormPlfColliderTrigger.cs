using Assets.Scripts.Characteres.WarriorController;

using System.Collections;

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

        private float maxWarriorSpeed = 20f;



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
            if (warrior != null)
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

            if (warrior != null

                && warrior.rigidbody2 != null

                && warrior.rigidbody2.linearVelocity.magnitude > maxWarriorSpeed)

            {

                warrior.rigidbody2.linearVelocity =

                    warrior.rigidbody2.linearVelocity.normalized * maxWarriorSpeed;

            }

        }



        // ─── Trigger events ───────────────────────────────────────────────────



        private void OnTriggerEnter2D(Collider2D other)

        {

            var character = other.GetComponentInParent<CharacterController>();

            const float buffer = 0.08f;



            if (character == null

                || character.collider2 == null

                || platformTrigger == null

                || platformCollider == null)

                return;



            if (character is ZalaytyMonster z)

            {

                bool isBelowPlatform = z.collider2.bounds.max.y

                                       < platformTrigger.bounds.min.y - buffer;

                bool goingUp = z.IsJumping

                               || (z.rigidbody2 != null

                                   && z.rigidbody2.linearVelocity.y > 0.05f);



                if (isBelowPlatform && goingUp) SetIgnoreForCharacter(z, true);

                return;

            }



            bool isBelow = character.collider2.bounds.max.y

                           < platformTrigger.bounds.min.y - buffer;

            if (!isBelow) return;



            if (IsCharacterGoingUpOrBeingLifted(character))

                SetIgnoreForCharacter(character, true);

        }



        private void OnTriggerStay2D(Collider2D other)

        {

            var character = other.GetComponentInParent<CharacterController>();



            if (character == null ||

                character.collider2 == null ||

                platformTrigger == null ||

                platformCollider == null)

                return;



            // Special case: character is being pushed upward by another moving vertical platform.

            if (ShouldPassThroughBecauseLiftedFromBelow(character))

            {

                SetIgnoreForCharacter(character, true);

                return;

            }



            const float buffer = 0.08f;



            bool isBelow =

                character.collider2.bounds.max.y < platformTrigger.bounds.min.y - buffer;



            bool goingUp = IsCharacterGoingUpOrBeingLifted(character);



            if (isBelow && goingUp)

                SetIgnoreForCharacter(character, true);



            HandleEdgeJumpPassThrough(character);



            if (character is ZalaytyMonster z)

            {

                if (z.NormalCollider == null || !z.NormalCollider.IsTouching(platformTrigger))

                    return;



                bool zBelow =

                    z.collider2.bounds.max.y < platformTrigger.bounds.min.y - buffer;



                bool zGoingUp =

                    z.IsJumping ||

                    (z.rigidbody2 != null && z.rigidbody2.linearVelocity.y > 0.05f);



                if (zBelow && zGoingUp)

                    SetIgnoreForCharacter(z, true);

            }

        }



        private void OnTriggerExit2D(Collider2D collision)

        {

            var character = collision.GetComponentInParent<CharacterController>();

            // Important: the same platform pass-through rule is used by Warrior and Zalayty.
            // On trigger enter/stay we may ignore the solid platform collider.
            // On trigger exit we must restore it for BOTH characters, otherwise the
            // normal platform collider can remain ignored and cause edge/jump jitter later.
            if (character is Warrior || character is ZalaytyMonster)

                StartCoroutine(ReEnableCollisionDelayed(character));

        }



        // ─── Collision events ─────────────────────────────────────────────────



        protected override void OnCollisionStay2D(Collision2D collision)

        {

            GameObject collidedObject = collision.collider.gameObject;

            CharacterController character = collision.collider.GetComponentInParent<CharacterController>();

            if (character == null) return;



            HandleEdgeJumpPassThrough(character);

            base.OnCollisionStay2D(collision);



            if (character is Warrior warrior)

            {

                warrior.CurrentplatForm = this;

                warrior.IsFallingPlfExit = false;

                warrior.IsFallingGrazesEdge = false;



                int c = warrior.CountGroundPoints();



                if (c >= 2)

                {

                    if (warriorEdgeTimer.IsRunning) warriorEdgeTimer.Stop();

                    _pendingWarriorFall = null;

                    return;

                }



                if (c == 1 && !warriorEdgeTimer.IsRunning)

                {

                    if (warrior.activesMoveCoroutine != null) return;

                    _pendingWarriorFall = warrior;

                    warrior.ShowLosingBalance();

                    warriorEdgeTimer.Start();

                    return;

                }



                if (c == 0)

                {

                    if (warriorEdgeTimer.IsRunning) warriorEdgeTimer.Stop();

                    bool notMovingUp = warrior.rigidbody2 == null || warrior.rigidbody2.linearVelocity.y <= 0.05f;

                    if (notMovingUp)

                    {

                        // No ground point left: do not let a side/contact jitter keep the Warrior
                        // attached to the moving platform edge. Ignore this platform until the
                        // trigger is exited; OnTriggerExit2D restores the collision.
                        warrior.IsFallingGrazesEdge = true;
                        warrior.IsFallingEdge = true;
                        warrior.IsFallingPlfExit = false;
                        warrior.IsFallingHitEnemy = false;
                        warrior.CanMove = false;

                        SetIgnoreForCharacter(warrior, true);

                        if (warrior.rigidbody2 != null)
                        {
                            warrior.rigidbody2.gravityScale = Mathf.Max(warrior.rigidbody2.gravityScale, 2.5f);

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

                var w = GameMgr.Instance.WarriorInstance;

                if (w.CurrentplatForm != z.CurrentplatForm) return;



                if (z.CountGroundPoints() <= 1 && !zalaytyEdgeTimer.IsRunning)

                {

                    _pendingZalaytyJump = z;

                    zalaytyEdgeTimer.Start();

                }

            }

        }



        protected override void OnCollisionExit2D(Collision2D collision)

        {

            base.OnCollisionExit2D(collision);



            var collidedObject = collision.collider.gameObject;

            var character = collision.collider.GetComponentInParent<CharacterController>();

            if (character == null) return;





            if (character is Warrior w)

            {

                // Do NOT restore platform collision here.
                // If the Warrior is still inside this platform trigger while jumping up
                // or passing through an edge zone, restoring on collision-exit can make
                // him land on a platform from the bottom. The restore belongs to
                // OnTriggerExit2D -> ReEnableCollisionDelayed().
                if (!w.IsJumping)

                {

                    w.IsFallingPlfExit = w.activesMoveCoroutine != null;

                }



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



        // ─── Helpers ──────────────────────────────────────────────────────────



        private IEnumerator ReEnableCollisionDelayed(CharacterController character)

        {

            // Wait one physics step so Unity updates trigger-touching state for all child colliders.
            yield return new WaitForFixedUpdate();

            if (character == null || platformCollider == null) yield break;

            if (!platformCollider.enabled) platformCollider.enabled = true;

            var cols = character.GetComponentsInChildren<Collider2D>(true);

            foreach (var col in cols)

            {

                if (col == null) continue;

                // Restore only the colliders that are really outside the trigger.
                // Any child collider still inside keeps passing through until its own exit.
                if (platformTrigger != null && platformTrigger.IsTouching(col)) continue;

                Physics2D.IgnoreCollision(platformCollider, col, false);

            }

        }



        private void SetIgnoreForCharacter(CharacterController ch, bool ignore)

        {

            if (ch == null || platformCollider == null) return;

            var cols = ch.GetComponentsInChildren<Collider2D>(true);

            foreach (var c in cols)

            {

                if (c != null) Physics2D.IgnoreCollision(platformCollider, c, ignore);

            }

        }



        [SerializeField] private float edgeZoneWidth = 0.35f;



        private bool IsInsideEdgeZone(Collider2D characterCollider)

        {

            Bounds pb = platformCollider.bounds;

            Bounds cb = characterCollider.bounds;



            float leftEdge = pb.min.x + edgeZoneWidth;

            float rightEdge = pb.max.x - edgeZoneWidth;

            float charX = cb.center.x;



            return charX < leftEdge || charX > rightEdge;

        }



        private void HandleEdgeJumpPassThrough(CharacterController character)

        {

            if (character == null ||

                character.collider2 == null ||

                character.rigidbody2 == null ||

                platformCollider == null ||

                platformTrigger == null)

                return;



            // Important:

            // if the character is being lifted by another moving vertical platform,

            // do NOT re-enable collision on this platform while inside the trigger.

            if (ShouldPassThroughBecauseLiftedFromBelow(character))

            {

                SetIgnoreForCharacter(character, true);

                return;

            }



            const float buffer = 0.08f;



            bool inEdge = IsInsideEdgeZone(character.collider2);

            bool goingUp = IsCharacterGoingUpOrBeingLifted(character);

            bool isBelowPlatform =

                character.collider2.bounds.max.y < platformTrigger.bounds.min.y - buffer;



            if ((inEdge && goingUp) || (isBelowPlatform && goingUp))

            {

                SetIgnoreForCharacter(character, true);

            }

            else

            {

                if (character is Warrior w && (w.IsFallingEdge || w.IsFallingGrazesEdge))

                    return;



                SetIgnoreForCharacter(character, false);

            }

        }



        private bool IsCharacterGoingUpOrBeingLifted(CharacterController character)

        {

            if (character == null) return false;



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

            if (_pendingWarriorFall == null) return;

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

                SetIgnoreForCharacter(w, true);

                if (w.rigidbody2 != null)
                {
                    w.rigidbody2.gravityScale = Mathf.Max(w.rigidbody2.gravityScale, 2.5f);

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

            if (_pendingZalaytyJump == null) return;

            var z = _pendingZalaytyJump;



            if (z.CountGroundPoints() <= 1)

            {

                if (z.rigidbody2 != null)

                {

                    z.rigidbody2.constraints = RigidbodyConstraints2D.FreezeRotation;

                    z.rigidbody2.gravityScale = 2.5f;

                }

                z.SetJumping(true);

            }



            _pendingZalaytyJump = null;

        }

        private bool ShouldPassThroughBecauseLiftedFromBelow(CharacterController character)

        {

            if (character == null || character.collider2 == null || platformCollider == null)

                return false;



            // Character must currently belong to another moving vertical platform

            if (character.CurrentplatForm is not MovingVerticalPlatform lowerMovingPlatform)

                return false;



            // Not this same platform

            if (lowerMovingPlatform == this)

                return false;



            // Lower platform must be moving upward now

            if (!lowerMovingPlatform.IsMovingUpNow)

                return false;



            // Character is still below the top surface of this platform

            if (character.collider2.bounds.center.y >= platformCollider.bounds.max.y)

                return false;



            return true;

        }

    }

}
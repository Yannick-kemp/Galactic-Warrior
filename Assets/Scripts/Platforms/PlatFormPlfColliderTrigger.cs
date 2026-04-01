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



            var warrior = GameMgr.Instance.WarriorInstance;

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

            var character = other.GetComponent<CharacterController>();

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

            var character = other.GetComponent<CharacterController>();



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

            var character = collision.GetComponent<CharacterController>();

            if (character is Warrior w)

                StartCoroutine(ReEnableCollisionDelayed(w));

        }



        // ─── Collision events ─────────────────────────────────────────────────



        protected override void OnCollisionStay2D(Collision2D collision)

        {

            GameObject collidedObject = collision.collider.gameObject;

            CharacterController character = collidedObject.GetComponent<CharacterController>();

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

                    warrior.IsFallingGrazesEdge = warrior.IsFallingDueToGravity();

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

            var character = collidedObject.GetComponent<CharacterController>();

            if (character == null) return;



            var characterColliders = character.GetComponentsInChildren<Collider2D>(true);



            if (character is Warrior w)

            {

                if (w.IsJumping)

                {

                    foreach (var col in characterColliders)

                    {

                        if (col == null) continue;

                        Physics2D.IgnoreCollision(platformCollider, col, false);

                    }

                }

                else

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

            yield return new WaitForFixedUpdate();

            if (character == null) yield break;



            if (character is Warrior w)

            {

                if (w.IsFallingEdge || w.IsFallingGrazesEdge) yield break;

            }



            if (!platformCollider.enabled) platformCollider.enabled = true;



            var cols = character.GetComponentsInChildren<Collider2D>(true);

            foreach (var col in cols)

            {

                if (col == null) continue;

                if (platformTrigger != null && platformTrigger.IsTouching(col)) continue;

                Physics2D.IgnoreCollision(platformCollider, col, false);

            }

        }



        private void SetIgnoreForCharacter(CharacterController ch, bool ignore)

        {

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

                Physics2D.IgnoreCollision(platformCollider, w.collider2, true);

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
using Assets.Scripts.Characteres.EnemyContoller;

using Assets.Scripts.Characteres.WarriorController;

using System.Collections;

using UnityEngine;

public class PlatFormColliderTrigger : MonoBehaviour

{

    public BoxCollider2D platformCollider;



    public BoxCollider2D platformTrigger;



    protected virtual void Start()

    {

        platformCollider.edgeRadius = 0.02f;

    }

    // Update is called once per frame

    protected virtual void Update()

    {



    }

    protected virtual void OnCollisionExit2D(Collision2D collision)

    {

        GameObject collidedObject = collision.collider.gameObject;

        CharacterController character = collidedObject.GetComponent<CharacterController>();

        if (character == null) return;



        // Only clear if THIS platform is the one currently stored

        if (character.CurrentplatForm != this) return;



        StartCoroutine(ClearPlatformIfReallyLeft(character));



    }

    protected virtual void OnCollisionEnter2D(Collision2D collision)

    {

        GameObject collidedObject = collision.collider.gameObject;

        CharacterController character = collidedObject.GetComponent<CharacterController>();



        if (character != null)

        {

            character.CurrentplatForm = this;

            if (character is Warrior w) //matching pattern

            {

                if (collision.transform.position.y > platformCollider.bounds.min.y)

                {

                    if (!Physics2D.GetIgnoreCollision(collision.collider, platformCollider))

                    {



                        w.CanMove = true;

                        w.IsFallingEdge = false;

                        w.IsFallingPlfExit = false;

                        w.IsFallingHitEnemy = false;

                        w.IsFallingGrazesEdge = false;

                        w.StopJumpTowardCoroutine();

                        w.StopMoveTowardCoroutine();

                        w._blockAction = false;

                        w.LastSafePlatform = this;

                        //Debug.Log($"Warrior landed on platform {this.gameObject.name}.");

                    }

                }

                w._blockAction = false;

            }



            else if (character is Enemy enemy)

            {

                if (!Physics2D.GetIgnoreCollision(collision.collider, platformCollider))

                {

                    SeatCharacterOnTop(enemy);



                    enemy.StopJumpTowardCoroutine();



                    if (enemy is ZalaytyMonster zalayty)

                        zalayty.SetJumping(false);



                    if (enemy.rigidbody2 != null)

                    {

                        Vector2 v = enemy.rigidbody2.linearVelocity;

                        v.y = 0f;

                        enemy.rigidbody2.linearVelocity = v;



                        // IMPORTANT:

                        // keep only FreezeRotation

                        // DO NOT use FreezePositionY for moving vertical platforms

                        enemy.rigidbody2.constraints = RigidbodyConstraints2D.FreezeRotation;

                    }



                    enemy.WaitAnimationDisplay();

                }

            }

        }

    }



    protected virtual void OnCollisionStay2D(Collision2D collision)

    {

        GameObject collidedObject = collision.collider.gameObject;

        CharacterController character = collidedObject.GetComponent<CharacterController>();

        if (character == null) return;



        character.CurrentplatForm = this;



        if (character is Warrior) return;

        if (character is ZalaytyMonster) return;



        if (character is Enemy enemy && enemy.rigidbody2 != null)

        {

            var c = enemy.rigidbody2.constraints;

            c |= RigidbodyConstraints2D.FreezeRotation;

            c &= ~RigidbodyConstraints2D.FreezePositionY;

            enemy.rigidbody2.constraints = c;

        }

    }



    private System.Collections.IEnumerator ClearPlatformIfReallyLeft(CharacterController ch)

    {

        yield return new WaitForFixedUpdate();



        if (ch == null) yield break;



        if (ch.collider2 != null && ch.collider2.IsTouching(platformCollider))

            yield break;



        if (ch is Warrior w && w.transform.parent == transform)

            yield break;



        if (ch.CurrentplatForm == this)

            ch.CurrentplatForm = null;

    }



    protected Collider2D GetStandingCollider(CharacterController character)

    {

        if (character is Enemy e && e.NormalCollider != null && e.NormalCollider.enabled)

            return e.NormalCollider;



        return character.collider2;

    }



    protected void SeatCharacterOnTop(CharacterController character, float safeMargin = 0.08f, float seatOffset = 0.02f)

    {

        if (character == null || platformCollider == null) return;



        Collider2D support = GetStandingCollider(character);

        if (support == null) return;



        Bounds pb = platformCollider.bounds;

        Bounds cb = support.bounds;



        float dx = 0f;



        float leftLimit = pb.min.x + safeMargin;

        float rightLimit = pb.max.x - safeMargin;



        if (cb.min.x < leftLimit)

            dx = leftLimit - cb.min.x;

        else if (cb.max.x > rightLimit)

            dx = rightLimit - cb.max.x;



        float dy = (pb.max.y + seatOffset) - cb.min.y;



        Vector2 delta = new Vector2(dx, dy);



        if (character.rigidbody2 != null)

            character.rigidbody2.MovePosition(character.rigidbody2.position + delta);

        else

            character.transform.position += (Vector3)delta;

    }





}
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using System.Collections;
using UnityEngine;

public class RotatingPlatform : PlatFormPlfColliderTrigger
{
    [SerializeField] public float Radius = 0.2f;
    [SerializeField] public float Speed = 1.0f;

    private Vector2 center;
    private float angle = 0.0f;

    private Warrior _attachedWarrior;

    protected override void Start()
    {
        base.Start();
        center = transform.position;
    }

    protected override void Update()
    {
        PerformRotation();
        base.Update();
    }

    protected override void OnCollisionEnter2D(Collision2D collision)
    {
        base.OnCollisionEnter2D(collision);
        TryAttachWarrior(collision);
    }

    protected override void OnCollisionStay2D(Collision2D collision)
    {
        base.OnCollisionStay2D(collision);
        TryAttachWarrior(collision);
    }

    protected override void OnCollisionExit2D(Collision2D collision)
    {
        base.OnCollisionExit2D(collision);

        var warrior = collision.rigidbody != null
            ? collision.rigidbody.GetComponent<Warrior>()
            : collision.collider.GetComponentInParent<Warrior>();

        if (warrior != null && warrior == _attachedWarrior)
        {
            StartCoroutine(DetachIfReallyLeft(warrior));
        }
    }

    private void TryAttachWarrior(Collision2D collision)
    {
        var warrior = collision.rigidbody != null
            ? collision.rigidbody.GetComponent<Warrior>()
            : collision.collider.GetComponentInParent<Warrior>();

        if (warrior == null) return;

        if (collision.transform.position.y > platformCollider.bounds.min.y)
        {
            _attachedWarrior = warrior;
        }
    }

    private IEnumerator DetachIfReallyLeft(Warrior warrior)
    {
        yield return new WaitForFixedUpdate();

        if (warrior == null)
        {
            _attachedWarrior = null;
            yield break;
        }

        if (warrior.collider2 != null && warrior.collider2.IsTouching(platformCollider))
            yield break;

        _attachedWarrior = null;
    }

    private void PerformRotation()
    {
        Vector2 oldPosition = transform.position;

        angle += Speed * Time.deltaTime;
        Vector2 newPosition = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius;
        transform.position = newPosition;

        Vector2 delta = newPosition - oldPosition;

        if (_attachedWarrior != null)
        {
            _attachedWarrior.transform.position += (Vector3)delta;
        }
    }
}
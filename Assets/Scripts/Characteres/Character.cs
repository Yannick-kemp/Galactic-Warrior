using UnityEngine;
public class Character : MonoBehaviour
{
    public LayerMask PlatformLayer;
    public Animator animator;
    public BoxCollider2D collider2;
    public SpriteRenderer spriteRenderer;

    public Rigidbody2D rigidbody2;
    protected virtual void Start()
    {
        animator = GetComponent<Animator>();
        collider2 = GetComponent<BoxCollider2D>();
        rigidbody2 = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public virtual void OnDrawGizmos()
    {

        if (collider2 == null) return;

        // Set Gizmo color
        Gizmos.color = Color.green;

        // Get the center and size of the collider in world space
        Vector3 colliderCenter = transform.position + (Vector3)collider2.offset;
        Vector3 colliderSize = new Vector3(collider2.bounds.size.x * transform.localScale.x,
                                           collider2.bounds.size.y * transform.localScale.y,
                                           0f);

        // Draw the wireframe box
        Gizmos.DrawWireCube(colliderCenter, colliderSize);
    }
}

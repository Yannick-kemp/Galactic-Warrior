using UnityEngine;

public sealed class FrontEdgeMarkerGizmo : MonoBehaviour
{
    [SerializeField] private bool showGizmo = true;
    [SerializeField] private float lineHalfHeight = 4f;
    [SerializeField] private float sphereRadius = 0.18f;
    [SerializeField] private float rightTickLength = 0.6f;

    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        float h = Mathf.Max(0.5f, lineHalfHeight);
        Vector3 p = transform.position;

        Gizmos.color = Color.magenta;

        Gizmos.DrawWireSphere(p, sphereRadius);

        Gizmos.DrawLine(
            new Vector3(p.x, p.y - h, p.z),
            new Vector3(p.x, p.y + h, p.z)
        );

        // Small tick pointing to the front/right
        Gizmos.DrawLine(
            p,
            p + Vector3.right * rightTickLength
        );
    }
}
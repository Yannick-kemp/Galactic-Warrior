using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    private bool activated = false;

    [SerializeField] private GameObject activateEffect;

    [Header("Gizmo")]
    [SerializeField] private bool showGizmo = true;
    [SerializeField] private float gizmoRadius = 0.25f;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (activated) return;

        if (other.CompareTag("Player") || other.name == "Warrior")
        {
            activated = true;

            GameMgr.Instance?.SetCheckpoint(transform);

            if (activateEffect != null)
                Instantiate(activateEffect, transform.position, Quaternion.identity);
        }
    }

    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        Gizmos.color = Color.white;
        Gizmos.DrawSphere(transform.position, gizmoRadius);

        Gizmos.DrawWireSphere(transform.position, gizmoRadius + 0.05f);
    }
}
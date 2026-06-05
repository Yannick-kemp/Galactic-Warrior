using UnityEngine;
using Assets.Scripts.Characteres.WarriorController;

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

        if (!IsWarrior(other)) return;

        activated = true;

        if (GameMgr.Instance != null)
        {
            GameMgr.Instance.SetCheckpoint(transform);
        }
        else
        {
            Debug.LogWarning($"[Checkpoint] '{name}' reached but GameMgr.Instance is null — checkpoint NOT saved.");
        }

        Debug.Log($"[Checkpoint] Activated '{name}' at {transform.position} (collider '{other.name}').");

        if (activateEffect != null)
            Instantiate(activateEffect, transform.position, Quaternion.identity);
    }

    // Robust warrior detection: works regardless of which child collider triggers the volume
    // or how the GameObject is tagged/named. Falls back to the legacy tag/name checks.
    private static bool IsWarrior(Collider2D other)
    {
        if (other == null) return false;

        if (other.GetComponentInParent<Warrior>() != null)
            return true;

        return other.CompareTag("Player") || other.name == "Warrior";
    }

    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        Gizmos.color = Color.white;
        Gizmos.DrawSphere(transform.position, gizmoRadius);

        Gizmos.DrawWireSphere(transform.position, gizmoRadius + 0.05f);
    }
}
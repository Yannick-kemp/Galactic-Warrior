using UnityEngine;

/// <summary>
/// Marks a "zone" parent (e.g. Zone1, Zone2) as cullable for the ZoneCullingManager.
///
/// Bounds come from a BoxCollider2D on this GameObject and are CACHED at Awake, so they survive the
/// zone being deactivated (a disabled GameObject's collider can no longer be read). The collider is
/// purely an authoring/visualization tool and is disabled at runtime so it never takes part in
/// physics or triggers.
///
/// Strategy A: culling = SetActive on this whole GameObject. All child runtime state (spawn points'
/// permanentlyDefeated/currentEnemy, KeyRelicLocks, etc.) is preserved across deactivation/reactivation.
///
/// IMPORTANT: a zone must START ACTIVE in the scene so Awake runs and caches its bounds. The manager
/// then deactivates it on the first cull pass if it is off-screen.
/// </summary>
[DisallowMultipleComponent]
public class ZoneCullable : MonoBehaviour
{
    [Header("Bounds")]
    [Tooltip("Bounding box used for visibility tests. If left empty, a BoxCollider2D on this object is used.")]
    [SerializeField] private BoxCollider2D boundsCollider;

    [Tooltip("Disable the bounds collider at runtime so it never participates in physics/triggers (bounds are cached).")]
    [SerializeField] private bool disableColliderAtRuntime = true;

    [Header("Options")]
    [Tooltip("Never let the manager deactivate this zone (use for a global / always-on zone).")]
    [SerializeField] private bool neverCull = false;

    private Rect _worldRect;
    private bool _hasBounds;

    public bool NeverCull => neverCull;
    public bool IsZoneActive => gameObject.activeSelf;

    private void Awake()
    {
        if (boundsCollider == null)
            boundsCollider = GetComponent<BoxCollider2D>();

        if (boundsCollider != null)
        {
            // World-space bounds, valid here because the zone starts active + the collider enabled.
            Bounds b = boundsCollider.bounds;
            _worldRect = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
            _hasBounds = true;

            if (disableColliderAtRuntime)
                boundsCollider.enabled = false;
        }
        else
        {
            _hasBounds = false;
            Debug.LogError($"[ZoneCullable] '{name}' has no BoxCollider2D for bounds; it will be kept always active.");
        }
    }

    public bool TryGetWorldRect(out Rect rect)
    {
        rect = _worldRect;
        return _hasBounds;
    }

    /// <summary>Bounds for gizmo drawing: cached rect once Awake ran (works while the zone is culled),
    /// otherwise the live BoxCollider2D bounds (edit mode / before Awake).</summary>
    public bool TryGetGizmoRect(out Rect rect)
    {
        if (_hasBounds)
        {
            rect = _worldRect;
            return true;
        }

        BoxCollider2D col = boundsCollider != null ? boundsCollider : GetComponent<BoxCollider2D>();
        if (col != null)
        {
            Bounds b = col.bounds;
            rect = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
            return true;
        }

        rect = default;
        return false;
    }

    public bool ContainsPoint(Vector2 worldPoint)
    {
        return _hasBounds && _worldRect.Contains(worldPoint);
    }

    /// <summary>Strategy A: show/hide the whole zone, preserving child runtime state. No-op if unchanged.</summary>
    public void SetZoneActive(bool active)
    {
        if (gameObject.activeSelf == active)
            return;

        gameObject.SetActive(active);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        BoxCollider2D col = boundsCollider != null ? boundsCollider : GetComponent<BoxCollider2D>();
        if (col == null) return;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Bounds b = col.bounds;
        Gizmos.DrawCube(b.center, b.size);
    }
#endif
}

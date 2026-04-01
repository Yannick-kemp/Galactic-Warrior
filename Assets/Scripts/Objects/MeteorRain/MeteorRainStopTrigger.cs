using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class MeteorRainStopTrigger : MonoBehaviour
{
    [Header("Meteor")]
    [SerializeField] private MeteorRainFollowWarrior meteorRain;
    [SerializeField] private bool oneShot = true;

    [Header("Gizmo")]
    [SerializeField] private bool showGizmo = true;
    [SerializeField] private Color gizmoColor = new Color(1f, 0f, 0f, 0.25f);
    [SerializeField] private Color gizmoWireColor = new Color(0.8f, 0f, 0f, 1f);

    private bool _used;
    private BoxCollider2D _box;

    private void Awake()
    {
        _box = GetComponent<BoxCollider2D>();
        if (_box != null)
            _box.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Warrior warrior = other.GetComponentInParent<Warrior>();
        if (warrior == null) return;

        // Always clear forced retry override when the warrior reaches the stop point.
        GameMgr.Instance?.ExitForcedRetryZone();

        if (_used && oneShot) return;
        if (meteorRain == null) return;

        meteorRain.StopRainCompletely();

        if (oneShot)
            _used = true;
    }

    public void ResetTriggerState()
    {
        _used = false;
    }

    private void OnDrawGizmos()
    {
        if (!showGizmo) return;
        DrawBoxGizmo();
    }

    private void DrawBoxGizmo()
    {
        BoxCollider2D box = _box != null ? _box : GetComponent<BoxCollider2D>();
        if (box == null) return;

        Matrix4x4 oldMatrix = Gizmos.matrix;

        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(box.offset, box.size);

        Gizmos.color = gizmoWireColor;
        Gizmos.DrawWireCube(box.offset, box.size);

        Gizmos.matrix = oldMatrix;
    }
}
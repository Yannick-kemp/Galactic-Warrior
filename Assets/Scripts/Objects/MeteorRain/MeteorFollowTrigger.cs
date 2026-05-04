using Assets.Scripts.Characteres.WarriorController;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class MeteorFollowTrigger : MonoBehaviour
{
    [Header("Meteor")]
    [SerializeField] private MeteorRainFollowWarrior meteorRain;
    [SerializeField] private bool oneShot = true;
    [SerializeField] private float startDelay = 0.75f;

    [Header("Optional Direction Override")]
    [SerializeField] private bool overrideRainDirection = false;
    [SerializeField] private MeteorRainTravelDirection rainDirection = MeteorRainTravelDirection.LeftToRight;

    [Header("Retry Respawn Override")]
    [SerializeField] private bool registerRetryZoneOnEnter = true;

    [Tooltip("Recommended: empty transform placed BEFORE MeteorStartTrigger.")]
    [SerializeField] private Transform retryRespawnBeforeStart;

    [Tooltip("Optional checkpoint that should beat this meteor retry override once reached.")]
    [SerializeField] private Transform checkpointThatBeatsRetryOverride;

    [SerializeField] private float fallbackRespawnOffsetX = -1.5f;
    [SerializeField] private float fallbackRespawnOffsetY = 0f;

    [Header("Gizmo")]
    [SerializeField] private bool showGizmo = true;
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 0f, 0.25f);
    [SerializeField] private Color gizmoWireColor = new Color(0f, 0.7f, 0f, 1f);

    private bool _used;
    private Coroutine _startRoutine;
    private BoxCollider2D _box;

    private void Awake()
    {
        _box = GetComponent<BoxCollider2D>();
        if (_box != null)
            _box.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_used && oneShot) return;
        if (meteorRain == null) return;
        if (GameMgr.Instance?.IsRestarting == true) return;

        Warrior warrior = other.GetComponentInParent<Warrior>();
        if (warrior == null) return;

        if (registerRetryZoneOnEnter)
        {
            bool checkpointAlreadyReached =
                checkpointThatBeatsRetryOverride != null &&
                GameMgr.Instance != null &&
                GameMgr.Instance.CurrentCheckpoint == checkpointThatBeatsRetryOverride;

            if (!checkpointAlreadyReached)
                GameMgr.Instance?.EnterForcedRetryZone(GetRetryRespawnPosition());
        }

        if (_startRoutine != null)
            StopCoroutine(_startRoutine);

        _startRoutine = StartCoroutine(StartRainDelayed(warrior));

        if (oneShot)
            _used = true;
    }

    private IEnumerator StartRainDelayed(Warrior warrior)
    {
        yield return new WaitForSeconds(startDelay);
        _startRoutine = null;

        if (meteorRain == null) yield break;
        if (warrior == null) yield break;
        if (!warrior.gameObject.activeInHierarchy) yield break;
        if (GameMgr.Instance?.IsRestarting == true) yield break;

        if (overrideRainDirection)
            meteorRain.SetTravelDirection(rainDirection);

        meteorRain.StartRainAndFollow(warrior);
    }

    private Vector3 GetRetryRespawnPosition()
    {
        if (retryRespawnBeforeStart != null)
            return retryRespawnBeforeStart.position;

        BoxCollider2D box = _box != null ? _box : GetComponent<BoxCollider2D>();
        Bounds b = box.bounds;

        return new Vector3(
            b.min.x + fallbackRespawnOffsetX,
            transform.position.y + fallbackRespawnOffsetY,
            transform.position.z
        );
    }

    public void ResetTriggerState()
    {
        if (_startRoutine != null)
        {
            StopCoroutine(_startRoutine);
            _startRoutine = null;
        }

        _used = false;
    }

    private void OnDisable()
    {
        if (_startRoutine != null)
        {
            StopCoroutine(_startRoutine);
            _startRoutine = null;
        }
    }

    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        DrawBoxGizmo();

        if (retryRespawnBeforeStart != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(retryRespawnBeforeStart.position, 0.15f);
            Gizmos.DrawWireSphere(retryRespawnBeforeStart.position, 0.22f);
            Gizmos.DrawLine(transform.position, retryRespawnBeforeStart.position);
        }
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
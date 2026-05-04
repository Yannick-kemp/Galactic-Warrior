using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

public enum MeteorRainTravelDirection
{
    LeftToRight = 1,
    RightToLeft = -1
}

public sealed class MeteorRainFollowWarrior : MonoBehaviour
{
    [Header("Start Behind Warrior")]
    [SerializeField] private bool startBehindWarrior = true;
    [SerializeField] private float behindPadding = 1.0f;
    [SerializeField] private float extraStartBehindDistance = 2.0f;

    [Header("Direction")]
    [SerializeField] private MeteorRainTravelDirection travelDirection = MeteorRainTravelDirection.LeftToRight;

    [Tooltip("If true, direction is decided once from the rain position toward the warrior position at start, then never updated again.")]
    [SerializeField] private bool chooseDirectionFromWarriorAtStart = false;

    [Header("Front / Leading Edge")]
    [Tooltip("Legacy marker. If leftEdgeMarker/rightEdgeMarker are not assigned, this marker is used as the visible leading edge.")]
    [SerializeField] private Transform frontEdgeMarker;

    [Tooltip("Recommended for right-to-left rain. Place this at the visible LEFT/front edge of the rain.")]
    [SerializeField] private Transform leftEdgeMarker;

    [Tooltip("Recommended for left-to-right rain. Place this at the visible RIGHT/front edge of the rain.")]
    [SerializeField] private Transform rightEdgeMarker;

    [Tooltip("Used only if no marker is assigned and no particle shape could be estimated.")]
    [SerializeField] private float fallbackFrontEdgeDistance = 25f;

    [Header("Independent Movement")]
    [SerializeField] private float moveSpeed = 18.5f;

    [Header("Vertical Behaviour")]
    [SerializeField] private bool keepInitialY = true;
    [SerializeField] private float yOffset = 0f;

    [Header("Particles")]
    [SerializeField] private bool forceParticleSimulationSpaceLocal = true;

    [Header("Start State")]
    [SerializeField] private bool startInactive = true;

    [Header("Optional Clamp")]
    [SerializeField] private bool clampX = false;
    [SerializeField] private float minX = -999f;
    [SerializeField] private float maxX = 999f;

    [Header("Stop On Death")]
    [SerializeField] private bool stopFollowingWhenWarriorDies = true;
    [SerializeField] private bool stopEmissionWhenWarriorDies = true;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    [Header("Gizmo")]
    [SerializeField] private bool showGizmo = true;
    [SerializeField] private float gizmoLineHalfHeight = 4f;

    private Collider2D _warriorCollider;
    private Transform _target;
    private Warrior _warrior;
    private bool _isFollowing;
    private bool _rainStarted;
    private float _baseY;
    private float _resolvedMoveDirectionX = 1f;

    private Vector3 _initialPosition;
    private Quaternion _initialRotation;

    private ParticleSystem[] _systems;

    public bool RainStarted => _rainStarted;
    public bool IsFollowing => _isFollowing;
    public float CurrentMoveDirectionX => _resolvedMoveDirectionX;

    private void Awake()
    {
        _initialPosition = transform.position;
        _initialRotation = transform.rotation;
        _baseY = _initialPosition.y;

        CacheSystems();

        if (forceParticleSimulationSpaceLocal)
            ForceSystemsToLocalSimulationSpace();

        if (startInactive)
            StopRainImmediate();
    }

    private void OnDisable()
    {
        UnbindWarriorEvents();
    }

    private void CacheSystems()
    {
        if (_systems == null || _systems.Length == 0)
            _systems = GetComponentsInChildren<ParticleSystem>(true);
    }

    private void ForceSystemsToLocalSimulationSpace()
    {
        if (_systems == null) return;

        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem ps = _systems[i];
            if (ps == null) continue;

            var main = ps.main;
            if (main.simulationSpace != ParticleSystemSimulationSpace.Local)
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
        }
    }

    public void SetTarget(Warrior warrior)
    {
        if (warrior == null) return;
        if (warrior.transform == null) return;

        UnbindWarriorEvents();

        _warrior = warrior;
        _target = warrior.transform;
        _warriorCollider = warrior.GetComponentInChildren<Collider2D>();

        BindWarriorEvents();
    }

    public void SetTravelDirection(MeteorRainTravelDirection direction)
    {
        travelDirection = direction;
        _resolvedMoveDirectionX = GetConfiguredDirectionSign();
    }

    public void SetLeftToRight()
    {
        SetTravelDirection(MeteorRainTravelDirection.LeftToRight);
    }

    public void SetRightToLeft()
    {
        SetTravelDirection(MeteorRainTravelDirection.RightToLeft);
    }

    public void StartRainAndFollow(Warrior warrior = null)
    {
        if (warrior != null)
            SetTarget(warrior);

        CacheSystems();

        if (forceParticleSimulationSpaceLocal)
            ForceSystemsToLocalSimulationSpace();

        ResolveMoveDirectionOnce();

        if (startBehindWarrior && _warriorCollider != null)
            SnapAtStartBehindWarrior();
        else
            RestoreInitialTransform();

        PlayRain();
        _isFollowing = true;
    }

    public void StopFollowing()
    {
        _isFollowing = false;
    }

    public void StopRain()
    {
        _rainStarted = false;

        if (_systems == null) return;

        for (int i = 0; i < _systems.Length; i++)
        {
            if (_systems[i] == null) continue;
            _systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    public void StopRainImmediate()
    {
        _rainStarted = false;
        _isFollowing = false;

        CacheSystems();

        for (int i = 0; i < _systems.Length; i++)
        {
            if (_systems[i] == null) continue;
            _systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _systems[i].Clear(true);
        }
    }

    private void PlayRain()
    {
        _rainStarted = true;

        CacheSystems();

        if (forceParticleSimulationSpaceLocal)
            ForceSystemsToLocalSimulationSpace();

        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem ps = _systems[i];
            if (ps == null) continue;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);
            ps.Play(true);
        }
    }

    private void Update()
    {
        if (!_isFollowing)
            return;

        Vector3 pos = transform.position;

        pos.x += _resolvedMoveDirectionX * Mathf.Max(0f, moveSpeed) * Time.deltaTime;

        if (clampX)
            pos.x = Mathf.Clamp(pos.x, minX, maxX);

        if (keepInitialY)
            pos.y = _baseY + yOffset;

        transform.position = pos;

        if (showDebugLogs)
        {
            Debug.Log(
                $"[MeteorRain] rootX={transform.position.x:F2} " +
                $"leadingEdgeX={GetCurrentLeadingEdgeWorldX():F2} " +
                $"moveSpeed={moveSpeed:F2} dir={_resolvedMoveDirectionX:F1}"
            );
        }
    }

    private float GetConfiguredDirectionSign()
    {
        return travelDirection == MeteorRainTravelDirection.RightToLeft ? -1f : 1f;
    }

    private void ResolveMoveDirectionOnce()
    {
        if (!chooseDirectionFromWarriorAtStart || _target == null)
        {
            _resolvedMoveDirectionX = GetConfiguredDirectionSign();
            return;
        }

        float dx = _target.position.x - transform.position.x;

        if (Mathf.Abs(dx) < 0.001f)
            _resolvedMoveDirectionX = GetConfiguredDirectionSign();
        else
            _resolvedMoveDirectionX = Mathf.Sign(dx);
    }

    private void SnapAtStartBehindWarrior()
    {
        Vector3 pos = transform.position;
        pos.x = GetStartBehindX();

        if (clampX)
            pos.x = Mathf.Clamp(pos.x, minX, maxX);

        if (keepInitialY)
            pos.y = _baseY + yOffset;

        transform.position = pos;
    }

    private void RestoreInitialTransform()
    {
        transform.position = _initialPosition;
        transform.rotation = _initialRotation;

        if (keepInitialY)
            _baseY = _initialPosition.y;
    }

    private Transform GetLeadingEdgeMarker(float directionX)
    {
        if (directionX < 0f)
        {
            if (leftEdgeMarker != null)
                return leftEdgeMarker;
        }
        else
        {
            if (rightEdgeMarker != null)
                return rightEdgeMarker;
        }

        return frontEdgeMarker;
    }

    private float GetEstimatedLeadingEdgeWorldX(float directionX)
    {
        float dir = directionX < 0f ? -1f : 1f;

        Transform marker = GetLeadingEdgeMarker(dir);
        if (marker != null)
            return marker.position.x;

        CacheSystems();

        float bestWorldX = dir > 0f ? float.NegativeInfinity : float.PositiveInfinity;
        bool foundAny = false;

        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem ps = _systems[i];
            if (ps == null) continue;

            foundAny = true;
            var shape = ps.shape;
            float candidateX;

            if (shape.enabled && shape.shapeType == ParticleSystemShapeType.Box)
            {
                float halfWidth = Mathf.Abs(shape.scale.x) * 0.5f;
                float localEdgeX = shape.position.x + halfWidth * dir;
                Vector3 worldEdge = ps.transform.TransformPoint(new Vector3(localEdgeX, 0f, 0f));
                candidateX = worldEdge.x;
            }
            else
            {
                candidateX = ps.transform.position.x;
            }

            if (dir > 0f)
            {
                if (candidateX > bestWorldX)
                    bestWorldX = candidateX;
            }
            else
            {
                if (candidateX < bestWorldX)
                    bestWorldX = candidateX;
            }
        }

        if (foundAny)
            return bestWorldX;

        return transform.position.x + dir * Mathf.Abs(fallbackFrontEdgeDistance);
    }

    private float GetLeadingEdgeOffsetFromRoot(float directionX)
    {
        return GetEstimatedLeadingEdgeWorldX(directionX) - transform.position.x;
    }

    private float GetCurrentLeadingEdgeWorldX()
    {
        return GetEstimatedLeadingEdgeWorldX(_resolvedMoveDirectionX);
    }

    private float GetStartBehindX()
    {
        if (_warriorCollider == null)
            return transform.position.x;

        float dir = _resolvedMoveDirectionX < 0f ? -1f : 1f;
        float totalPadding = behindPadding + extraStartBehindDistance;
        float leadingEdgeOffset = GetLeadingEdgeOffsetFromRoot(dir);

        float wantedLeadingEdgeX;

        if (dir > 0f)
        {
            // Rain moves left -> right, so it starts behind the warrior's left side.
            wantedLeadingEdgeX = _warriorCollider.bounds.min.x - totalPadding;
        }
        else
        {
            // Rain moves right -> left, so it starts behind the warrior's right side.
            wantedLeadingEdgeX = _warriorCollider.bounds.max.x + totalPadding;
        }

        return wantedLeadingEdgeX - leadingEdgeOffset;
    }

    private void BindWarriorEvents()
    {
        if (_warrior == null) return;
        if (!stopFollowingWhenWarriorDies) return;

        _warrior.OnDeathStarted += HandleWarriorDeathStarted;
    }

    private void UnbindWarriorEvents()
    {
        if (_warrior == null) return;
        if (!stopFollowingWhenWarriorDies) return;

        _warrior.OnDeathStarted -= HandleWarriorDeathStarted;
    }

    private void HandleWarriorDeathStarted()
    {
        if (stopFollowingWhenWarriorDies)
            _isFollowing = false;

        if (stopEmissionWhenWarriorDies)
            StopRainImmediate();
    }

    public void ResetMeteorState(bool clearTarget = true)
    {
        StopRainImmediate();
        RestoreInitialTransform();

        if (clearTarget)
        {
            UnbindWarriorEvents();
            _warrior = null;
            _target = null;
            _warriorCollider = null;
        }
    }

    public void StopRainCompletely()
    {
        StopRainImmediate();
        RestoreInitialTransform();
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGizmo) return;

        float y = transform.position.y;
        float h = Mathf.Max(0.5f, gizmoLineHalfHeight);
        float dir = Application.isPlaying ? _resolvedMoveDirectionX : GetConfiguredDirectionSign();

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.15f);

        float currentLeadingEdgeX = Application.isPlaying
            ? GetCurrentLeadingEdgeWorldX()
            : GetEditorLeadingEdgeWorldX(dir);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(
            new Vector3(currentLeadingEdgeX, y - h, 0f),
            new Vector3(currentLeadingEdgeX, y + h, 0f)
        );

        Transform leadingMarker = GetLeadingEdgeMarker(dir);
        if (leadingMarker != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(leadingMarker.position, 0.18f);
            Gizmos.DrawLine(transform.position, leadingMarker.position);

            Gizmos.DrawLine(
                new Vector3(leadingMarker.position.x, y - h, 0f),
                new Vector3(leadingMarker.position.x, y + h, 0f)
            );
        }

        if (leftEdgeMarker != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(leftEdgeMarker.position, 0.12f);
        }

        if (rightEdgeMarker != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(rightEdgeMarker.position, 0.12f);
        }
    }

    private float GetEditorLeadingEdgeWorldX(float directionX)
    {
        Transform marker = GetLeadingEdgeMarker(directionX);
        if (marker != null)
            return marker.position.x;

        return transform.position.x + Mathf.Sign(directionX) * Mathf.Abs(fallbackFrontEdgeDistance);
    }
}

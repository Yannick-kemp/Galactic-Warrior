using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

public sealed class MeteorRainFollowWarrior : MonoBehaviour
{
    [Header("Start Behind Warrior")]
    [SerializeField] private bool startBehindWarrior = true;
    [SerializeField] private float behindPadding = 1.0f;
    [SerializeField] private float extraStartBehindDistance = 2.0f;

    [Tooltip("Optional marker placed at the visible FRONT edge of the rain. Highly recommended.")]
    [SerializeField] private Transform frontEdgeMarker;

    [Tooltip("Used only if no frontEdgeMarker is assigned and no particle shape could be estimated.")]
    [SerializeField] private float fallbackFrontEdgeDistance = 25f;

    [Header("Independent Movement")]
    [SerializeField] private float moveSpeed = 18.5f;
    [SerializeField] private float moveDirectionX = 1f;

    [Tooltip("If true, direction is decided once from warrior position at start, then never updated again.")]
    [SerializeField] private bool chooseDirectionFromWarriorAtStart = false;

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
                $"frontEdgeX={GetCurrentFrontEdgeWorldX():F2} " +
                $"moveSpeed={moveSpeed:F2} dir={_resolvedMoveDirectionX:F1}"
            );
        }
    }

    private void ResolveMoveDirectionOnce()
    {
        if (!chooseDirectionFromWarriorAtStart || _target == null)
        {
            _resolvedMoveDirectionX = Mathf.Approximately(moveDirectionX, 0f) ? 1f : Mathf.Sign(moveDirectionX);
            return;
        }

        float dx = _target.position.x - transform.position.x;
        _resolvedMoveDirectionX = Mathf.Abs(dx) < 0.001f ? 1f : Mathf.Sign(dx);
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

    private float GetEstimatedFrontEdgeWorldX()
    {
        if (frontEdgeMarker != null)
            return frontEdgeMarker.position.x;

        CacheSystems();

        float maxWorldX = float.NegativeInfinity;
        bool foundAny = false;

        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem ps = _systems[i];
            if (ps == null) continue;

            foundAny = true;
            var shape = ps.shape;

            if (shape.enabled && shape.shapeType == ParticleSystemShapeType.Box)
            {
                float halfWidth = Mathf.Abs(shape.scale.x) * 0.5f;
                float localFrontX = shape.position.x + halfWidth;

                Vector3 worldFront = ps.transform.TransformPoint(new Vector3(localFrontX, 0f, 0f));
                if (worldFront.x > maxWorldX)
                    maxWorldX = worldFront.x;
            }
            else
            {
                if (ps.transform.position.x > maxWorldX)
                    maxWorldX = ps.transform.position.x;
            }
        }

        if (foundAny && maxWorldX > float.NegativeInfinity)
            return maxWorldX;

        return transform.position.x + Mathf.Abs(fallbackFrontEdgeDistance);
    }

    private float GetFrontEdgeDistanceFromRoot()
    {
        float dist = GetEstimatedFrontEdgeWorldX() - transform.position.x;
        return Mathf.Max(0f, dist);
    }

    private float GetCurrentFrontEdgeWorldX()
    {
        return transform.position.x + GetFrontEdgeDistanceFromRoot();
    }

    private float GetStartBehindX()
    {
        if (_warriorCollider == null)
            return transform.position.x;

        float warriorMinX = _warriorCollider.bounds.min.x;
        float totalPadding = behindPadding + extraStartBehindDistance;
        float frontEdgeDistance = GetFrontEdgeDistanceFromRoot();

        return warriorMinX - totalPadding - frontEdgeDistance;
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

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.15f);

        float currentFrontEdgeX = Application.isPlaying
            ? GetCurrentFrontEdgeWorldX()
            : GetEditorFrontEdgeWorldX();

        Gizmos.color = Color.red;
        Gizmos.DrawLine(
            new Vector3(currentFrontEdgeX, y - h, 0f),
            new Vector3(currentFrontEdgeX, y + h, 0f)
        );

        if (frontEdgeMarker != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(frontEdgeMarker.position, 0.18f);
            Gizmos.DrawLine(transform.position, frontEdgeMarker.position);

            Gizmos.DrawLine(
                new Vector3(frontEdgeMarker.position.x, y - h, 0f),
                new Vector3(frontEdgeMarker.position.x, y + h, 0f)
            );
        }
    }

    private float GetEditorFrontEdgeWorldX()
    {
        if (frontEdgeMarker != null)
            return frontEdgeMarker.position.x;

        return transform.position.x + Mathf.Abs(fallbackFrontEdgeDistance);
    }
}
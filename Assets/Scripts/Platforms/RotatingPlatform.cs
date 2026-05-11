using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(40)]
[RequireComponent(typeof(Rigidbody2D))]
public class RotatingPlatform : PlatFormPlfColliderTrigger
{
    [Header("Orbit Motion")]
    [SerializeField] public float Radius = 0.2f;

    [Tooltip("Radians per second. Positive = counter-clockwise, negative = clockwise.")]
    [SerializeField] public float Speed = 1.0f;

    [Tooltip("Initial angle on the orbit circle, in degrees.")]
    [SerializeField] private float startAngleDegrees = 0f;

    [Tooltip("Optional world-space center. If empty, the platform keeps its current scene position as the start position.")]
    [SerializeField] private Transform centerOverride;

    [Header("Lift / Passenger Carry")]
    [SerializeField] private bool carryWarriorAndZalayty = true;

    [Tooltip("How far below the platform top the passenger body may be and still count as seated.")]
    [SerializeField, Min(0f)] private float topPenetrationTolerance = 0.08f;

    [Tooltip("How far above the platform top the passenger body may be and still count as seated.")]
    [SerializeField, Min(0f)] private float topMaxGap = 0.16f;

    [Tooltip("Horizontal tolerance used to check if the passenger is really above the platform.")]
    [SerializeField, Min(0f)] private float horizontalSupportSkin = 0.04f;

    [Tooltip("If true, the platform Rigidbody2D is forced to Kinematic on Start.")]
    [SerializeField] private bool forceKinematicBody = true;

    private Rigidbody2D _platformBody;

    private Vector2 _center;
    private Vector2 _platformPosition;
    private float _angle;

    private Vector2 _lastCarryDelta;
    private float _lastCarryFixedTime = -999f;

    private readonly Dictionary<int, CharacterController> _passengers =
        new Dictionary<int, CharacterController>();

    private readonly List<int> _removeBuffer = new List<int>();

    private const float TinyDeltaSqr = 0.00000001f;

    protected override void Start()
    {
        base.Start();

        _platformBody = GetComponent<Rigidbody2D>();

        if (_platformBody != null)
        {
            if (forceKinematicBody)
                _platformBody.bodyType = RigidbodyType2D.Kinematic;

            _platformBody.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        _angle = startAngleDegrees * Mathf.Deg2Rad;

        _platformPosition = _platformBody != null
            ? _platformBody.position
            : (Vector2)transform.position;

        if (centerOverride != null)
        {
            _center = centerOverride.position;
        }
        else
        {
            // Important:
            // This prevents the platform from jumping by Radius on the first frame.
            // The current scene position becomes the initial orbit position.
            _center = _platformPosition - GetOrbitDirection(_angle) * Radius;
        }
    }

    protected override void Update()
    {
        // Do not move the platform in Update.
        // Physics lift/carry must happen in FixedUpdate.
        base.Update();
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        if (centerOverride != null)
            _center = centerOverride.position;

        RemoveInvalidPassengersBeforeMove();

        Vector2 oldPosition = _platformPosition;

        _angle += Speed * Time.fixedDeltaTime;

        Vector2 newPosition = _center + GetOrbitDirection(_angle) * Radius;
        Vector2 delta = newPosition - oldPosition;

        _platformPosition = newPosition;
        _lastCarryDelta = delta;
        _lastCarryFixedTime = Time.fixedTime;

        MovePlatformBody(newPosition);

        if (carryWarriorAndZalayty)
            CarryPassengers(delta);
    }

    protected override void OnCollisionEnter2D(Collision2D collision)
    {
        base.OnCollisionEnter2D(collision);
        TryRegisterPassenger(collision);
    }

    protected override void OnCollisionStay2D(Collision2D collision)
    {
        base.OnCollisionStay2D(collision);
        TryRegisterPassenger(collision);
    }

    protected override void OnCollisionExit2D(Collision2D collision)
    {
        base.OnCollisionExit2D(collision);

        CharacterController character =
            collision.collider.GetComponentInParent<CharacterController>();

        if (character != null)
            StartCoroutine(RemovePassengerIfReallyLeft(character));
    }

    /// <summary>
    /// Used by ZalaytyMonster while he is doing his own independent MovePosition.
    /// This prevents the platform and Zalayty from overwriting each other.
    /// </summary>
    public bool TryGetCarryDeltaForCurrentFixedStep(
        CharacterController character,
        out Vector2 carryDelta)
    {
        carryDelta = Vector2.zero;

        if (character == null)
            return false;

        if (Mathf.Abs(_lastCarryFixedTime - Time.fixedTime) > 0.0001f)
            return false;

        int id = character.GetInstanceID();

        if (!_passengers.ContainsKey(id))
            return false;

        carryDelta = _lastCarryDelta;
        return carryDelta.sqrMagnitude > TinyDeltaSqr;
    }

    private void MovePlatformBody(Vector2 newPosition)
    {
        if (_platformBody != null)
            _platformBody.MovePosition(newPosition);
        else
            transform.position = new Vector3(newPosition.x, newPosition.y, transform.position.z);
    }

    private void CarryPassengers(Vector2 delta)
    {
        _removeBuffer.Clear();

        foreach (KeyValuePair<int, CharacterController> pair in _passengers)
        {
            CharacterController character = pair.Value;

            if (!IsValidLiftPassenger(character) || !IsTopSurfacePassenger(character))
            {
                _removeBuffer.Add(pair.Key);
                continue;
            }

            RefreshPassengerPlatformState(character);

            if (delta.sqrMagnitude <= TinyDeltaSqr)
                continue;

            // Zalayty may be running his own independent MovePosition.
            // In that case, do not directly move him here.
            // He will add this platform delta inside MoveZalaytyBody().
            if (ShouldLetZalaytyConsumeDeltaInsideOwnMove(character))
                continue;

            ApplyCarryDelta(character, delta);
            RefreshPassengerPlatformState(character);
        }

        for (int i = 0; i < _removeBuffer.Count; i++)
            _passengers.Remove(_removeBuffer[i]);
    }

    private void TryRegisterPassenger(Collision2D collision)
    {
        if (!carryWarriorAndZalayty)
            return;

        CharacterController character =
            collision.collider.GetComponentInParent<CharacterController>();

        if (!IsValidLiftPassenger(character))
            return;

        if (!IsTopSurfacePassenger(character))
            return;

        int id = character.GetInstanceID();
        _passengers[id] = character;

        RefreshPassengerPlatformState(character);
    }

    private IEnumerator RemovePassengerIfReallyLeft(CharacterController character)
    {
        yield return new WaitForFixedUpdate();

        if (character == null)
            yield break;

        if (IsTopSurfacePassenger(character))
            yield break;

        _passengers.Remove(character.GetInstanceID());
    }

    private void RemoveInvalidPassengersBeforeMove()
    {
        _removeBuffer.Clear();

        foreach (KeyValuePair<int, CharacterController> pair in _passengers)
        {
            CharacterController character = pair.Value;

            if (!IsValidLiftPassenger(character) || !IsTopSurfacePassenger(character))
                _removeBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _removeBuffer.Count; i++)
            _passengers.Remove(_removeBuffer[i]);
    }

    private bool IsValidLiftPassenger(CharacterController character)
    {
        if (character == null)
            return false;

        if (platformCollider == null)
            return false;

        if (character.collider2 == null)
            return false;

        return character is Warrior || character is ZalaytyMonster;
    }

    private bool IsTopSurfacePassenger(CharacterController character)
    {
        if (!IsValidLiftPassenger(character))
            return false;

        // If the character intentionally jumps, do not keep carrying him.
        if (character.IsJumping || character.activesJumpCoroutine != null)
            return false;

        if (character is Warrior warrior)
        {
            if (warrior.IsFallingEdge ||
                warrior.IsFallingPlfExit ||
                warrior.IsFallingHitEnemy)
            {
                return false;
            }
        }

        Collider2D support = GetStandingCollider(character);

        if (support == null || !support.enabled || !support.gameObject.activeInHierarchy)
            return false;

        if (Physics2D.GetIgnoreCollision(platformCollider, support))
            return false;

        Bounds pb = platformCollider.bounds;
        Bounds cb = support.bounds;

        float left = pb.min.x + horizontalSupportSkin;
        float right = pb.max.x - horizontalSupportSkin;

        if (left > right)
        {
            left = pb.min.x;
            right = pb.max.x;
        }

        bool horizontallyOverPlatform =
            cb.max.x > left &&
            cb.min.x < right;

        if (!horizontallyOverPlatform)
            return false;

        float bottomToPlatformTop = cb.min.y - pb.max.y;

        bool closeToTop =
            bottomToPlatformTop >= -topPenetrationTolerance &&
            bottomToPlatformTop <= topMaxGap;

        if (!closeToTop)
            return false;

        ColliderDistance2D distance = Physics2D.Distance(support, platformCollider);

        return support.IsTouching(platformCollider) ||
               distance.isOverlapped ||
               distance.distance <= topMaxGap + 0.02f;
    }

    private void ApplyCarryDelta(CharacterController character, Vector2 delta)
    {
        if (character == null)
            return;

        if (character.rigidbody2 != null)
        {
            Vector2 target = character.rigidbody2.position + delta;
            character.rigidbody2.MovePosition(target);

            // Prevent the passenger from sinking into an upward-moving platform.
            if (delta.y >= 0f && character.rigidbody2.linearVelocity.y < 0f)
            {
                Vector2 velocity = character.rigidbody2.linearVelocity;
                velocity.y = 0f;
                character.rigidbody2.linearVelocity = velocity;
            }
        }
        else
        {
            character.transform.position += (Vector3)delta;
        }
    }

    private void RefreshPassengerPlatformState(CharacterController character)
    {
        if (character == null)
            return;

        character.CurrentplatForm = this;

        if (character is Warrior warrior)
        {
            warrior.LastSafePlatform = this;
        }
        else if (character is ZalaytyMonster zalayty)
        {
            zalayty.NotifyMovingPlatformTopSupport(this);
        }
    }

    private bool ShouldLetZalaytyConsumeDeltaInsideOwnMove(CharacterController character)
    {
        if (character is not ZalaytyMonster zalayty)
            return false;

        return zalayty.ShouldApplyHorizontalMovingPlatformCarryInsideOwnMove();
    }

    private static Vector2 GetOrbitDirection(float angleRadians)
    {
        return new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians));
    }
}
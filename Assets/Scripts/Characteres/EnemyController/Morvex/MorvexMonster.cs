using System.Collections.Generic;
using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class MorvexMonster : Enemy
    {
        private enum State
        {
            Idle,
            FlyToReserve,
            GrabStone,
            FlyAboveWarrior,
            DropStone,
            Retreat,
            SearchNewReserve,
            NoReserveMode
        }

        [Header("Morvex References")]
        [SerializeField] private Warrior warrior;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform holdPoint;
        [SerializeField] private GameObject carriedStoneVisual;
        [SerializeField] private FallingStone fallingStonePrefab;

        [Header("Reserve Search")]
        [SerializeField] private bool autoDiscoverReserves = true;
        [SerializeField] private List<StoneReserve> reserves = new List<StoneReserve>();

        [Header("Detection")]
        [SerializeField] private float detectRange = 14f;
        [SerializeField] private float forgetRange = 20f;

        [Header("Flight")]
        [SerializeField] private float flySpeed = 4f;
        [SerializeField] private float carryFlySpeed = 3.2f;
        [SerializeField] private float retreatFlySpeed = 5.5f;
        [SerializeField] private float arriveDistance = 0.15f;

        [Header("Idle Hover")]
        [SerializeField] private float idlePatrolDistance = 1.4f;
        [SerializeField] private float idlePatrolSpeed = 1.2f;
        [SerializeField] private float idleHoverAmplitude = 0.25f;
        [SerializeField] private float idleHoverFrequency = 2f;

        [Header("Stone Attack")]
        [SerializeField] private float preferredDropHeight = 4.5f;
        [SerializeField] private float minimumDropHeight = 3.5f;
        [SerializeField] private float horizontalDropTolerance = 0.75f;
        [SerializeField] private float grabDuration = 0.35f;
        [SerializeField] private float dropWindup = 0.25f;

        [Header("Retreat")]
        [SerializeField] private float retreatDuration = 1f;
        [SerializeField] private Vector2 retreatOffset = new Vector2(2.5f, 1.75f);

        [Header("Hit Reaction")]
        [SerializeField] private float hitStunDuration = 0.35f;

        [Header("No Reserve Mode")]
        [SerializeField] private float noReserveRecheckInterval = 1.5f;
        [SerializeField] private float noReserveHoverRadius = 1.5f;

        private State currentState;
        private StoneReserve currentReserve;
        private bool hasStone;
        private float stateTimer;
        private float noReserveRecheckTimer;
        private float stunTimer;
        private Vector3 idleAnchor;
        private Vector3 retreatTarget;
        private Vector3 committedDropPosition;

        private Collider2D[] _selfColliders;

        public bool HasStone => hasStone;
        public StoneReserve CurrentReserve => currentReserve;

        protected override void Awake()
        {
            base.Awake();

            if (visualRoot == null)
                visualRoot = transform;

            if (holdPoint == null)
                holdPoint = transform;

            _selfColliders = GetComponentsInChildren<Collider2D>(true);

            idleAnchor = transform.position;
            SetHasStone(false);
        }

        protected override void Start()
        {
            Range = 0f;
            attackCooldown = 0f;
            attackDamage = 0;

            base.Start();

            if (visualRoot == null)
                visualRoot = transform;

            if (holdPoint == null)
                holdPoint = transform;

            if (rigidbody2 != null)
            {
                rigidbody2.gravityScale = 0f;
                rigidbody2.linearVelocity = Vector2.zero;
                rigidbody2.angularVelocity = 0f;
            }

            CurrentplatForm = null;

            FindWarriorIfMissing();

            if (autoDiscoverReserves)
                DiscoverReserves();

            IgnoreAllPlatformCollisions(true);

            idleAnchor = transform.position;
            SetHasStone(false);
            EnterState(State.Idle);
        }

        protected override void FixedUpdate()
        {
            // Morvex is a flyer: never use grounded platform detection.
            CurrentplatForm = null;

            if (rigidbody2 != null)
            {
                rigidbody2.gravityScale = 0f;
                rigidbody2.linearVelocity = Vector2.zero;
                rigidbody2.angularVelocity = 0f;
            }
        }

        protected override void Update()
        {
            CheckWorldYDeathFallback();

            if (StopMovingWhenWarriorDie)
                return;

            FindWarriorIfMissing();

            if (autoDiscoverReserves && reserves.Count == 0)
                DiscoverReserves();

            if (stunTimer > 0f)
            {
                stunTimer -= Time.deltaTime;
                HoverAround(transform.position, 0.15f, 5f);
                UpdateCarriedStoneVisualPosition();
                return;
            }

            switch (currentState)
            {
                case State.Idle:
                    UpdateIdle();
                    break;

                case State.FlyToReserve:
                    UpdateFlyToReserve();
                    break;

                case State.GrabStone:
                    UpdateGrabStone();
                    break;

                case State.FlyAboveWarrior:
                    UpdateFlyAboveWarrior();
                    break;

                case State.DropStone:
                    UpdateDropStone();
                    break;

                case State.Retreat:
                    UpdateRetreat();
                    break;

                case State.SearchNewReserve:
                    UpdateSearchNewReserve();
                    break;

                case State.NoReserveMode:
                    UpdateNoReserveMode();
                    break;
            }

            UpdateCarriedStoneVisualPosition();
        }

        public override void TakeDamage(float damage)
        {
            base.TakeDamage(damage);

            if (hasStone)
                DropStone();

            if (IsDeadOrDying)
                return;

            stunTimer = hitStunDuration;
            LosingBalanceAnimationDisplay();
            RefreshCarryAnimator();
        }

        private void FindWarriorIfMissing()
        {
            if (warrior == null)
                warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : Warrior.Instance;

            if (warrior == null && target != null)
                warrior = target.GetComponent<Warrior>();
        }

        private void DiscoverReserves()
        {
            reserves.Clear();
            reserves.AddRange(FindObjectsByType<StoneReserve>(FindObjectsSortMode.None));
        }

        private void EnterState(State newState)
        {
            currentState = newState;

            switch (currentState)
            {
                case State.Idle:
                    idleAnchor = transform.position;
                    PlayFlyAnimation();
                    break;

                case State.GrabStone:
                    stateTimer = grabDuration;
                    PlayFlyAnimation();
                    break;

                case State.DropStone:
                    stateTimer = dropWindup;
                    committedDropPosition = transform.position;
                    AttackAnimationDisplay();
                    RefreshCarryAnimator();
                    break;

                case State.Retreat:
                    stateTimer = retreatDuration;
                    retreatTarget = BuildRetreatTarget();
                    PlayFlyAnimation();
                    break;

                case State.NoReserveMode:
                    noReserveRecheckTimer = 0f;
                    PlayFlyAnimation();
                    break;

                default:
                    PlayFlyAnimation();
                    break;
            }
        }

        private void UpdateIdle()
        {
            Vector3 patrolOffset = new Vector3(
                Mathf.Sin(Time.time * idlePatrolSpeed) * idlePatrolDistance,
                Mathf.Sin(Time.time * idleHoverFrequency) * idleHoverAmplitude,
                0f);

            FlyTowards(idleAnchor + patrolOffset, flySpeed * 0.5f);

            if (!WarriorInDetectRange())
                return;

            currentReserve = FindNearestAvailableReserve();
            EnterState(currentReserve != null ? State.FlyToReserve : State.NoReserveMode);
        }

        private void UpdateFlyToReserve()
        {
            if (currentReserve == null || !currentReserve.HasStones)
            {
                EnterState(State.SearchNewReserve);
                return;
            }

            Vector3 approachPos = currentReserve.GetApproachWorldPosition();
            float approachArriveDistance = Mathf.Max(arriveDistance, 0.2f);

            if (Vector2.Distance(transform.position, approachPos) > approachArriveDistance)
            {
                FlyTowards(approachPos, flySpeed);
                return;
            }

            Vector3 grabPos = currentReserve.GetGrabWorldPosition();
            FlyTowards(grabPos, flySpeed);

            if (Vector2.Distance(transform.position, grabPos) <= arriveDistance)
                EnterState(State.GrabStone);
        }

        private void UpdateGrabStone()
        {
            if (currentReserve == null)
            {
                EnterState(State.SearchNewReserve);
                return;
            }

            HoverAround(currentReserve.GetGrabWorldPosition(), 0.05f, 4f);

            stateTimer -= Time.deltaTime;
            if (stateTimer > 0f)
                return;

            if (!currentReserve.TryTakeStone())
            {
                EnterState(State.SearchNewReserve);
                return;
            }

            SetHasStone(true);
            EnterState(State.FlyAboveWarrior);
        }
        private void UpdateFlyAboveWarrior()
        {
            if (!hasStone) { EnterState(State.SearchNewReserve); return; }
            if (!HasValidWarrior()) { DropStone(); EnterState(State.Retreat); return; }

            float safeMinY = warrior.transform.position.y + minimumDropHeight;

            // Phase 1: if not yet at safe altitude, climb vertically first
            if (transform.position.y < safeMinY)
            {
                Vector3 climbTarget = new Vector3(transform.position.x, safeMinY, 0f);
                FlyTowards(climbTarget, carryFlySpeed);
                return; // don't move horizontally yet
            }

            // Phase 2: safe altitude reached — move toward drop position
            Vector3 targetPosition = warrior.transform.position + Vector3.up * preferredDropHeight;
            targetPosition.y = Mathf.Max(targetPosition.y, safeMinY); // keep the clamp too
            FlyTowards(targetPosition, carryFlySpeed);

            float horizontalDistance = Mathf.Abs(transform.position.x - warrior.transform.position.x);
            float verticalGap = transform.position.y - warrior.transform.position.y;

            if (horizontalDistance <= horizontalDropTolerance && verticalGap >= minimumDropHeight)
                EnterState(State.DropStone);
        }

        private void UpdateDropStone()
        {
            if (!hasStone)
            {
                EnterState(State.Retreat);
                return;
            }

            if (!HasValidWarrior())
            {
                DropStone();
                EnterState(State.Retreat);
                return;
            }

            FlyTowards(committedDropPosition, carryFlySpeed * 0.4f);

            stateTimer -= Time.deltaTime;
            if (stateTimer > 0f)
                return;

            DropStone();

            // Immediately after the release, Morvex is now in no-stone flight.
            EnterState(State.Retreat);
        }

        private void UpdateRetreat()
        {
            FlyTowards(retreatTarget, retreatFlySpeed);

            stateTimer -= Time.deltaTime;
            if (stateTimer > 0f && Vector2.Distance(transform.position, retreatTarget) > arriveDistance)
                return;

            EnterState(currentReserve != null && currentReserve.HasStones
                ? State.FlyToReserve
                : State.SearchNewReserve);
        }

        private void UpdateSearchNewReserve()
        {
            currentReserve = FindNearestAvailableReserve();
            EnterState(currentReserve != null ? State.FlyToReserve : State.NoReserveMode);
        }

        private void UpdateNoReserveMode()
        {
            noReserveRecheckTimer -= Time.deltaTime;

            if (noReserveRecheckTimer <= 0f)
            {
                currentReserve = FindNearestAvailableReserve();
                if (currentReserve != null)
                {
                    EnterState(State.FlyToReserve);
                    return;
                }

                noReserveRecheckTimer = noReserveRecheckInterval;
            }

            Vector3 center = HasValidWarrior()
                ? warrior.transform.position + Vector3.up * (preferredDropHeight + 1f)
                : idleAnchor;

            Vector3 offset = new Vector3(
                Mathf.Sin(Time.time * 1.5f) * noReserveHoverRadius,
                Mathf.Cos(Time.time * 2f) * 0.4f,
                0f);

            FlyTowards(center + offset, flySpeed * 0.7f);
        }

        private void DropStone()
        {
            if (!hasStone)
                return;

            Vector3 spawnPosition = holdPoint != null ? holdPoint.position : transform.position;

            SetHasStone(false);

            // Force immediate switch to the no-stone flying animation
            // the exact moment the stone is released.
            PlayFlyAnimation();

            if (fallingStonePrefab == null)
                return;

            FallingStone stone = Instantiate(fallingStonePrefab, spawnPosition, Quaternion.identity);
            stone.Launch();
        }

        private StoneReserve FindNearestAvailableReserve()
        {
            StoneReserve best = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = reserves.Count - 1; i >= 0; i--)
            {
                StoneReserve reserve = reserves[i];
                if (reserve == null)
                {
                    reserves.RemoveAt(i);
                    continue;
                }

                if (!reserve.HasStones)
                    continue;

                float distanceSqr = (reserve.GetApproachWorldPosition() - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    best = reserve;
                }
            }

            return best;
        }

        private bool WarriorInDetectRange()
        {
            return warrior != null &&
                   Vector2.Distance(transform.position, warrior.transform.position) <= detectRange &&
                   !warrior.CanDie;
        }

        private bool HasValidWarrior()
        {
            if (!HasLivingWarrior())
                return false;

            // IMPORTANT:
            // Once Morvex already has a stone, do NOT use forgetRange anymore.
            // The reserve can be far from Warrior, so Vector2.Distance(...) can be
            // greater than forgetRange immediately after pickup. If we return false
            // there, UpdateFlyAboveWarrior() calls DropStone() too early and the
            // stone explodes near the reserve/platform.
            if (hasStone)
                return true;

            return Vector2.Distance(transform.position, warrior.transform.position) <= forgetRange;
        }

        private bool HasLivingWarrior()
        {
            return warrior != null && !warrior.CanDie;
        }

        private void FlyTowards(Vector3 worldTarget, float speed)
        {
            Vector3 nextPosition = Vector3.MoveTowards(transform.position, worldTarget, speed * Time.deltaTime);
            Vector3 delta = nextPosition - transform.position;

            transform.position = nextPosition;
            CurrentplatForm = null;

            if (rigidbody2 != null)
            {
                rigidbody2.gravityScale = 0f;
                rigidbody2.linearVelocity = Vector2.zero;
                rigidbody2.angularVelocity = 0f;
            }

            UpdateFacing(delta.x);
        }

        private void HoverAround(Vector3 center, float amplitudeMultiplier = 1f, float frequencyMultiplier = 1f)
        {
            Vector3 hoverPosition = center + new Vector3(
                0f,
                Mathf.Sin(Time.time * idleHoverFrequency * frequencyMultiplier) * idleHoverAmplitude * amplitudeMultiplier,
                0f);

            FlyTowards(hoverPosition, flySpeed * 0.5f);
        }

        private Vector3 BuildRetreatTarget()
        {
            float direction = 1f;

            if (warrior != null)
            {
                float difference = transform.position.x - warrior.transform.position.x;
                direction = Mathf.Approximately(difference, 0f) ? 1f : Mathf.Sign(difference);
            }

            return transform.position + new Vector3(retreatOffset.x * direction, retreatOffset.y, 0f);
        }

        private void UpdateFacing(float xDelta)
        {
            if (visualRoot == null || Mathf.Abs(xDelta) < 0.001f)
                return;

            Vector3 scale = visualRoot.localScale;
            scale.x = Mathf.Abs(scale.x) * Mathf.Sign(xDelta);
            visualRoot.localScale = scale;
        }

        private void SetHasStone(bool value)
        {
            hasStone = value;

            if (carriedStoneVisual != null)
                carriedStoneVisual.SetActive(hasStone);

            RefreshCarryAnimator();
        }

        private void RefreshCarryAnimator()
        {
            if (animator == null)
                return;

            animator.SetBool("hasStone", hasStone);
        }

        private void PlayFlyAnimation()
        {
            if (animator == null)
                return;

            animator.SetBool("isWaiting", false);
            animator.SetBool("isJumping", false);
            animator.SetBool("isRunning", true);
            animator.SetBool("isWalking", false);
            animator.SetBool("isAttacking", false);
            animator.SetBool("isAttacking2", false);
            animator.SetBool("isAttacking3", false);
            animator.SetBool("isDying", false);
            animator.SetBool("IsLosingCtrl", false);

            RefreshCarryAnimator();
        }

        private void UpdateCarriedStoneVisualPosition()
        {
            if (!hasStone || carriedStoneVisual == null || holdPoint == null)
                return;

            carriedStoneVisual.transform.position = holdPoint.position;
        }

        public void ForceRefreshReserves()
        {
            DiscoverReserves();
            currentReserve = FindNearestAvailableReserve();
        }

        public void ForceSearchNewReserve()
        {
            EnterState(State.SearchNewReserve);
        }

        private void IgnoreAllPlatformCollisions(bool ignore)
        {
            if (_selfColliders == null || _selfColliders.Length == 0)
                return;

            var platforms = FindObjectsByType<PlatFormColliderTrigger>(FindObjectsSortMode.None);

            foreach (var platform in platforms)
            {
                if (platform == null)
                    continue;

                if (platform.platformCollider != null)
                    SetIgnoreAgainst(platform.platformCollider, ignore);

                if (platform.platformTrigger != null)
                    SetIgnoreAgainst(platform.platformTrigger, ignore);
            }
        }

        private void SetIgnoreAgainst(Collider2D other, bool ignore)
        {
            if (other == null || _selfColliders == null)
                return;

            for (int i = 0; i < _selfColliders.Length; i++)
            {
                var self = _selfColliders[i];
                if (self == null)
                    continue;

                Physics2D.IgnoreCollision(self, other, ignore);
            }
        }

        protected override void OnTriggerEnter2D(Collider2D collision)
        {
        }

        protected override void OnTriggerExit2D(Collider2D collision)
        {
        }

        protected override void OnTriggerStay2D(Collider2D collision)
        {
        }

        public override void OnDrawGizmos()
        {
            base.OnDrawGizmos();

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectRange);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * preferredDropHeight, 0.25f);

            if (holdPoint != null)
            {
                Gizmos.color = Color.gray;
                Gizmos.DrawWireSphere(holdPoint.position, 0.15f);
            }
        }
    }
}
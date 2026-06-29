using Assets.Scripts.Characteres.WarriorController;
using System;
using UnityEngine;

namespace Assets.Scripts.Scoring
{
    [DisallowMultipleComponent]
    public class SpectacularActionScorer : MonoBehaviour
    {
        [Header("Points")]
        [SerializeField] private int cleanTransferPoints = 5;
        [SerializeField] private int crowdHitPoints = 20;
        [SerializeField] private int crowdHitExtraPerEnemy = 8;

        [Header("Clean Transfer Timing")]
        [SerializeField] private float maxTransferTime = 1.6f;
        [SerializeField] private float minAirTime = 0.08f;

        [Header("Clean Transfer Constraints (Stairs-friendly)")]
        [Tooltip("Landing platform top Y must be higher than departure platform top Y by at least this.")]
        [SerializeField] private float minVerticalGain = 0.45f;

        [Tooltip("Minimum takeoff->landing travel distance (good for overlapping-X stairs layouts).")]
        [SerializeField] private float minJumpTravelDistance = 1.5f;

        [Header("Definite Landing Confirmation")]
        [Tooltip("How many FixedUpdate frames in a row we must satisfy landing conditions.")]
        [Min(1)][SerializeField] private int confirmLandingFixedFrames = 2;

        [Tooltip("Contact normal.y must be above this to count as standing on top.")]
        [Range(0.1f, 0.95f)][SerializeField] private float landingNormalMin = 0.55f;

        [Tooltip("Contact point must be near the platform top surface (platformTop - buffer).")]
        [Min(0f)][SerializeField] private float topPointBuffer = 0.08f;

        [Tooltip("Grace time to wait for Warrior.CurrentplatForm to update (yours updates in platform OnCollisionStay).")]
        [Min(0f)][SerializeField] private float currentPlatformSettleGrace = 0.12f;

        [Tooltip("Require velocity.y <= this to confirm landing (prevents counting while still moving up).")]
        [SerializeField] private float maxLandingVelY = 0.15f;

        public int SpectaclePoints { get; private set; }
        public event Action<int, string, Vector2> OnSpectacleAwarded;

        private Warrior _w;

        // --- Transfer tracking ---
        private bool _wasGroundedFixed;
        private bool _inTransfer;
        private float _airStartTime;
        private float _transferStartTime;
        private bool _losingBalanceShownDuringTransfer;

        private PlatFormColliderTrigger _departedPlatform;
        private Vector2 _takeoffPos;

        // --- Pending landing confirmation ---
        private bool _pendingLanding;
        private float _pendingLandingStartTime;
        private PlatFormColliderTrigger _landingCandidate;
        private int _landingConfirmCount;

        // crowd hit dedupe (avoid multiple awards same frame)
        private int _lastCrowdAwardFrame = -999;

        // contacts buffer
        private readonly ContactPoint2D[] _contacts = new ContactPoint2D[16];

        private void Awake()
        {
            _w = GetComponent<Warrior>();
        }

        private void FixedUpdate()
        {
            if (_w == null) return;

            bool grounded = _w.CountGroundPoints() > 0;

            // --- Start transfer when leaving ground ---
            if (!_inTransfer)
            {
                if (_wasGroundedFixed && !grounded)
                {
                    _inTransfer = true;
                    _pendingLanding = false;

                    _airStartTime = Time.time;
                    _transferStartTime = Time.time;
                    _losingBalanceShownDuringTransfer = false;

                    _departedPlatform = _w.CurrentplatForm;
                    _takeoffPos = (_w.collider2 != null)
                        ? (Vector2)_w.collider2.bounds.center
                        : (Vector2)_w.transform.position;
                }

                _wasGroundedFixed = grounded;
                return;
            }

            // --- Timeout ---
            if (Time.time - _transferStartTime > maxTransferTime)
            {
                ResetTransfer();
                _wasGroundedFixed = grounded;
                return;
            }

            // --- Begin pending landing when ground returns (DON'T evaluate yet) ---
            if (!_pendingLanding && !_wasGroundedFixed && grounded)
            {
                _pendingLanding = true;
                _pendingLandingStartTime = Time.time;
                _landingCandidate = null;
                _landingConfirmCount = 0;
            }

            // --- Confirm pending landing over several FixedUpdate frames ---
            if (_pendingLanding)
            {
                ConfirmPendingLanding(grounded);
            }

            _wasGroundedFixed = grounded;
        }

        private void ConfirmPendingLanding(bool grounded)
        {
            // If we lost ground again, cancel pending and wait for the next ground regain
            if (!grounded)
            {
                _pendingLanding = false;
                return;
            }

            float airTime = Time.time - _airStartTime;
            if (airTime < minAirTime)
            {
                _landingConfirmCount = 0;
                return;
            }

            var currentPlf = _w.CurrentplatForm;
            if (currentPlf == null)
            {
                // Wait a little for CurrentplatForm to be set (your platform does it in OnCollisionStay2D)
                if (Time.time - _pendingLandingStartTime > currentPlatformSettleGrace)
                    ResetTransfer();
                return;
            }

            // Candidate must be stable (same platform for N frames)
            if (_landingCandidate != currentPlf)
            {
                _landingCandidate = currentPlf;
                _landingConfirmCount = 0;
            }

            // Must be a different platform than departure
            if (_departedPlatform == null || _landingCandidate == null || _landingCandidate == _departedPlatform)
            {
                _landingConfirmCount = 0;
                return;
            }

            // Disqualify if losing balance happened during transfer
            if (_losingBalanceShownDuringTransfer)
            {
                ResetTransfer();
                return;
            }

            // Optional: prevent confirming while still going upward fast
            if (_w.rigidbody2 != null && _w.rigidbody2.linearVelocity.y > maxLandingVelY)
            {
                _landingConfirmCount = 0;
                return;
            }

            // Must be standing ON TOP of this platform (not touching side / edge weirdness)
            if (!IsStandingOnTopPlatform(_landingCandidate, out Vector2 landingPoint))
            {
                _landingConfirmCount = 0;
                return;
            }

            // Lower -> higher constraint
            if (!PassHeightConstraint(_departedPlatform, _landingCandidate))
            {
                _landingConfirmCount = 0;
                return;
            }

            // Travel constraint (stairs-friendly)
            float travelDist = Vector2.Distance(_takeoffPos, landingPoint);
            if (travelDist < minJumpTravelDistance)
            {
                _landingConfirmCount = 0;
                return;
            }

            // This frame qualifies
            _landingConfirmCount++;

            if (_landingConfirmCount >= confirmLandingFixedFrames)
            {
                Award(cleanTransferPoints, "Clean UP transfer", landingPoint);
                ResetTransfer();
            }
        }

        private bool PassHeightConstraint(PlatFormColliderTrigger from, PlatFormColliderTrigger to)
        {
            if (from == null || to == null) return false;
            if (from.platformCollider == null || to.platformCollider == null) return false;

            float fromTop = from.platformCollider.bounds.max.y;
            float toTop = to.platformCollider.bounds.max.y;

            return (toTop - fromTop) >= minVerticalGain;
        }

        private bool IsStandingOnTopPlatform(PlatFormColliderTrigger plf, out Vector2 avgPoint)
        {
            avgPoint = Vector2.zero;

            if (plf == null || plf.platformCollider == null) return false;
            if (_w == null) return false;

            float platformTopY = plf.platformCollider.bounds.max.y;

            int ok = 0;
            Vector2 sum = Vector2.zero;

            // Important: si le Warrior a plusieurs colliders (root + enfants),
            // on check tous les colliders NON trigger.
            var wCols = _w.GetComponentsInChildren<Collider2D>(true);
            for (int ci = 0; ci < wCols.Length; ci++)
            {
                var wcol = wCols[ci];
                if (wcol == null) continue;
                if (wcol.isTrigger) continue; // triggers => pas de contacts physiques

                // petit filtre rapide
                if (!plf.platformCollider.IsTouching(wcol))
                    continue;

                int n = wcol.GetContacts(_contacts); // Unity 6 OK

                for (int i = 0; i < n; i++)
                {
                    var c = _contacts[i];

                    // garder uniquement les contacts avec cette plateforme
                    if (c.collider != plf.platformCollider && c.otherCollider != plf.platformCollider)
                        continue;

                    // Normal orientée "depuis la plateforme" (robuste quel que soit collider/otherCollider)
                    Vector2 platformNormal = (c.collider == plf.platformCollider) ? c.normal : -c.normal;

                    // Standing on top = normal vers le haut + point proche du haut de la plateforme
                    if (platformNormal.y > landingNormalMin && c.point.y >= platformTopY - topPointBuffer)
                    {
                        ok++;
                        sum += c.point;
                    }
                }
            }

            if (ok <= 0) return false;

            avgPoint = sum / ok;
            return true;
        }

        private void ResetTransfer()
        {
            _inTransfer = false;
            _pendingLanding = false;

            _departedPlatform = null;
            _landingCandidate = null;
            _landingConfirmCount = 0;
        }

        // Called whenever LosingBalanceAnimationDisplay happens
        public void NotifyLosingBalanceDisplayed()
        {
            if (_inTransfer)
                _losingBalanceShownDuringTransfer = true;
        }

        // Called once per “attack resolution” when you know how many enemies were hit
        public void NotifyCrowdHit(int hitCount, Vector2 worldPos)
        {
            if (hitCount < 2) return;

            if (_lastCrowdAwardFrame == Time.frameCount) return;
            _lastCrowdAwardFrame = Time.frameCount;

            int extra = Mathf.Max(0, hitCount - 2) * crowdHitExtraPerEnemy;
            Award(crowdHitPoints + extra, $"Crowd hit x{hitCount}", worldPos);
        }
        // SpectacularActionScorer.cs

        // SpectacularActionScorer.cs

        [Header("Squeeze Escape (Jump)")]
        [SerializeField] private int squeezeJumpEscapePoints = 45;
        [SerializeField] private int squeezeJumpExtraPerEnemy = 12;
        [SerializeField] private float minSqueezeTimeToCount = 0.12f;

        private int _lastSqueezeAwardFrame = -999;

        public void NotifySqueezeJumpEscape(int enemyCount, float squeezedSeconds, Vector2 worldPos)
        {
            if (enemyCount < 2) return;
            if (squeezedSeconds < minSqueezeTimeToCount) return;

            if (_lastSqueezeAwardFrame == Time.frameCount) return;
            _lastSqueezeAwardFrame = Time.frameCount;

            int extra = Mathf.Max(0, enemyCount - 2) * squeezeJumpExtraPerEnemy;
            Award(squeezeJumpEscapePoints + extra, $"Jump escape x{enemyCount}", worldPos);
        }
        private void Award(int points, string label, Vector2 worldPos)
        {
            int p = Mathf.Max(0, points);

            SpectaclePoints += p; // local spectacle total (optional)
            OnSpectacleAwarded?.Invoke(p, label, worldPos); // popup

            // GLOBAL total score
            if (ScoreManager.Instance != null)
                ScoreManager.Instance.Add(p, label, worldPos);
        }
    }
}
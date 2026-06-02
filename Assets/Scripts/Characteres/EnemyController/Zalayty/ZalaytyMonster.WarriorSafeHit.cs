using Assets.Scripts.Platforms;
using System.Collections;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public enum WarriorZalaytyHitKind
    {
        Attack1,
        Attack2,
        ShieldBlock,
        ShieldStomp,
        CollisionBounce,
        LandingOnEnemy,
        SideCollision,
        GenericWarriorConstraint
    }

    public struct WarriorZalaytyHitContext
    {
        public WarriorZalaytyHitKind Kind;
        public int RequestedDamage;
        public float RequestedKnockback;
        public float RequestedStunSeconds;
        public bool ApplyStun;
        public Vector2 ForceDirection;
        public bool AllowVerticalForce;
        public bool AllowCoroutineInterrupt;
        public bool AllowPhysicalStepBackWhenSupported;
    }

    /// <summary>
    /// Zalayty-only guard for Warrior-origin combat/collision effects.
    ///
    /// Important:
    /// This is intentionally implemented as extension methods so you do not have to rewrite
    /// ZalaytyMonster's existing AI, A*, same-platform combat, Warrior-top rebound,
    /// different-platform absorption, or moving-platform carry logic.
    /// </summary>
    public static class ZalaytyMonsterWarriorSafeHitExtensions
    {
        private const float SupportedTopPenetrationTolerance = 0.08f;
        private const float SupportedTopGapTolerance = 0.18f;
        private const float SupportedHorizontalSkin = 0.035f;
        private const float ClampAboveTopSkin = 0.018f;
        private const float GroundProbeExtraRadius = 0.035f;
        private const int PostHitFixedRestoreSteps = 2;

        private struct PlatformSupportSnapshot
        {
            public bool HasPlatform;
            public bool WasSupported;
            public bool WasCollisionIgnored;
            public PlatFormColliderTrigger Platform;
            public Collider2D PlatformCollider;
            public Collider2D BodyCollider;
            public float PlatformTopY;
            public float BodyBottomY;
            public Vector2 BodyPosition;
            public Vector2 Velocity;
        }

        public static bool ApplyWarriorSafeHit(
            this global::ZalaytyMonster zalayty,
            Warrior sourceWarrior,
            WarriorZalaytyHitContext context)
        {
            if (zalayty == null)
                return false;

            PlatformSupportSnapshot before = CapturePlatformState(zalayty);

            // Keep the gameplay result: Warrior can still disable attack, stun, and damage Zalayty.
            // The difference is that the generic SmoothStepBack / raw knockback path is skipped.
            zalayty.DisableAttackTemporarily();

            if (context.ApplyStun && context.RequestedStunSeconds > 0f)
                zalayty.ApplyStun(context.RequestedStunSeconds);

            bool killed = context.RequestedDamage > 0 &&
                          zalayty.TakeDamageAndReturnKilled(context.RequestedDamage);

            if (killed || zalayty.IsDeadOrDying)
                return killed;

            // If Zalayty was really standing/riding on a platform, Warrior-origin force is not
            // allowed to push him below/inside that platform or leave collision ignored.
            if (before.WasSupported)
            {
                RestorePlatformSupportAfterWarriorConstraint(zalayty, before, context);
                zalayty.StartCoroutine(PostFixedRestorePlatformSupport(zalayty, before, context));
                return killed;
            }

            // If CurrentplatForm was briefly lost because of physics order, recover it only if
            // Zalayty is still physically standing on a platform top. Do not snap real falls.
            if (TryRecoverCurrentPlatformAfterWarriorConstraint(zalayty))
            {
                PlatformSupportSnapshot recovered = CapturePlatformState(zalayty);
                if (recovered.WasSupported)
                {
                    RestorePlatformSupportAfterWarriorConstraint(zalayty, recovered, context);
                    zalayty.StartCoroutine(PostFixedRestorePlatformSupport(zalayty, recovered, context));
                }
            }

            return killed;
        }

        public static bool CanAcceptWarriorKnockbackSafely(
            this global::ZalaytyMonster zalayty,
            Vector2 requestedForce)
        {
            if (zalayty == null)
                return false;

            PlatformSupportSnapshot snapshot = CapturePlatformState(zalayty);

            // Airborne / real platform-change movement is owned by Zalayty's existing systems.
            if (!snapshot.WasSupported)
                return true;

            // Supported Zalayty must never receive downward Warrior-origin force.
            if (requestedForce.y < -0.001f)
                return false;

            // Horizontal generic knockback is still unsafe because SmoothStepBack can move him
            // below/inside a platform on solver gaps or moving-platform edges.
            return Mathf.Abs(requestedForce.x) <= 0.0001f;
        }

        public static void ProtectPlatformStateDuringWarriorHit(
            this global::ZalaytyMonster zalayty)
        {
            if (zalayty == null)
                return;

            PlatformSupportSnapshot snapshot = CapturePlatformState(zalayty);
            if (snapshot.WasSupported)
                RestorePlatformSupportAfterWarriorConstraint(zalayty, snapshot, default);
        }

        public static void RestorePlatformSupportAfterWarriorConstraint(
            this global::ZalaytyMonster zalayty)
        {
            if (zalayty == null)
                return;

            PlatformSupportSnapshot snapshot = CapturePlatformState(zalayty);
            if (snapshot.WasSupported)
                RestorePlatformSupportAfterWarriorConstraint(zalayty, snapshot, default);
            else
                TryRecoverCurrentPlatformAfterWarriorConstraint(zalayty);
        }

        public static bool IsBodyStillSupportedByPlatformTop(
            this global::ZalaytyMonster zalayty)
        {
            if (zalayty == null)
                return false;

            return CapturePlatformState(zalayty).WasSupported;
        }

        public static bool TryRecoverCurrentPlatformAfterWarriorConstraint(
            this global::ZalaytyMonster zalayty)
        {
            if (zalayty == null)
                return false;

            PlatformSupportSnapshot snapshot = CapturePlatformState(zalayty);

            if (snapshot.WasSupported && snapshot.Platform != null)
            {
                zalayty.CurrentplatForm = snapshot.Platform;
                ConfirmMovingPlatformSupportIfNeeded(zalayty, snapshot.Platform);
                return true;
            }

            if (TryFindSupportedPlatformFromGroundPoints(zalayty, out PlatFormColliderTrigger found) && found != null)
            {
                zalayty.CurrentplatForm = found;
                ConfirmMovingPlatformSupportIfNeeded(zalayty, found);
                return true;
            }

            return false;
        }

        public static void ClearWarriorOriginBadPlatformIgnoreState(
            this global::ZalaytyMonster zalayty)
        {
            if (zalayty == null)
                return;

            PlatformSupportSnapshot snapshot = CapturePlatformState(zalayty);
            if (snapshot.WasSupported)
                EmergencyRestorePlatformCollisionIfSafe(zalayty, snapshot);
        }

        public static void ClampBodyAboveSupportedPlatformTopIfNeeded(
            this global::ZalaytyMonster zalayty)
        {
            if (zalayty == null)
                return;

            PlatformSupportSnapshot snapshot = CapturePlatformState(zalayty);
            if (snapshot.WasSupported)
                ClampBodyAbovePlatformTop(zalayty, snapshot);
        }

        private static PlatformSupportSnapshot CapturePlatformState(global::ZalaytyMonster zalayty)
        {
            PlatformSupportSnapshot snapshot = new PlatformSupportSnapshot();

            if (zalayty == null)
                return snapshot;

            snapshot.BodyCollider = GetBestBodyCollider(zalayty);
            snapshot.BodyPosition = zalayty.rigidbody2 != null
                ? zalayty.rigidbody2.position
                : (Vector2)zalayty.transform.position;
            snapshot.Velocity = zalayty.rigidbody2 != null
                ? zalayty.rigidbody2.linearVelocity
                : Vector2.zero;

            PlatFormColliderTrigger platform = zalayty.CurrentplatForm;

            if (!IsSupportedByPlatformTop(zalayty, platform, snapshot.BodyCollider, out float topY, out float bottomY))
            {
                if (TryFindSupportedPlatformFromGroundPoints(zalayty, out PlatFormColliderTrigger found))
                    platform = found;
            }

            snapshot.Platform = platform;
            snapshot.PlatformCollider = platform != null ? platform.platformCollider : null;
            snapshot.HasPlatform = snapshot.PlatformCollider != null;

            if (snapshot.HasPlatform && snapshot.BodyCollider != null)
            {
                snapshot.WasSupported = IsSupportedByPlatformTop(
                    zalayty,
                    platform,
                    snapshot.BodyCollider,
                    out snapshot.PlatformTopY,
                    out snapshot.BodyBottomY);

                snapshot.WasCollisionIgnored = Physics2D.GetIgnoreCollision(
                    snapshot.PlatformCollider,
                    snapshot.BodyCollider);
            }

            return snapshot;
        }

        private static void RestorePlatformSupportAfterWarriorConstraint(
            global::ZalaytyMonster zalayty,
            PlatformSupportSnapshot snapshot,
            WarriorZalaytyHitContext context)
        {
            if (zalayty == null || !snapshot.WasSupported || snapshot.Platform == null)
                return;

            zalayty.CurrentplatForm = snapshot.Platform;

            CancelUnsafeWarriorOriginVelocity(zalayty, context);
            ClampBodyAbovePlatformTop(zalayty, snapshot);
            EmergencyRestorePlatformCollisionIfSafe(zalayty, snapshot);
            ConfirmMovingPlatformSupportIfNeeded(zalayty, snapshot.Platform);

            Physics2D.SyncTransforms();
        }

        private static IEnumerator PostFixedRestorePlatformSupport(
            global::ZalaytyMonster zalayty,
            PlatformSupportSnapshot snapshot,
            WarriorZalaytyHitContext context)
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();

            for (int i = 0; i < PostHitFixedRestoreSteps; i++)
            {
                yield return wait;

                if (zalayty == null || zalayty.IsDeadOrDying)
                    yield break;

                if (snapshot.Platform == null || snapshot.Platform.platformCollider == null)
                    yield break;

                if (!IsSupportedByPlatformTop(
                        zalayty,
                        snapshot.Platform,
                        GetBestBodyCollider(zalayty),
                        out _,
                        out _))
                {
                    // Zalayty really left the platform. Do not snap him back.
                    yield break;
                }

                RestorePlatformSupportAfterWarriorConstraint(zalayty, snapshot, context);
            }
        }

        private static void CancelUnsafeWarriorOriginVelocity(
            global::ZalaytyMonster zalayty,
            WarriorZalaytyHitContext context)
        {
            if (zalayty == null || zalayty.rigidbody2 == null)
                return;

            Vector2 v = zalayty.rigidbody2.linearVelocity;

            if (!context.AllowVerticalForce && v.y < 0f)
                v.y = 0f;

            if (!context.AllowPhysicalStepBackWhenSupported)
                v.x = 0f;

            zalayty.rigidbody2.linearVelocity = v;
            zalayty.rigidbody2.angularVelocity = 0f;
        }

        private static void ClampBodyAbovePlatformTop(
            global::ZalaytyMonster zalayty,
            PlatformSupportSnapshot snapshot)
        {
            if (zalayty == null || snapshot.PlatformCollider == null)
                return;

            Collider2D body = GetBestBodyCollider(zalayty);
            if (body == null)
                return;

            Bounds bodyBounds = body.bounds;
            float targetBottom = snapshot.PlatformCollider.bounds.max.y + ClampAboveTopSkin;
            float deltaY = targetBottom - bodyBounds.min.y;

            // Only lift out of penetration / tiny sink. Never pull a real airborne Zalayty down/up.
            if (deltaY <= 0f)
                return;

            Vector2 newPosition = zalayty.rigidbody2 != null
                ? zalayty.rigidbody2.position + Vector2.up * deltaY
                : (Vector2)zalayty.transform.position + Vector2.up * deltaY;

            if (zalayty.rigidbody2 != null)
                zalayty.rigidbody2.position = newPosition;
            else
                zalayty.transform.position = new Vector3(newPosition.x, newPosition.y, zalayty.transform.position.z);
        }

        private static void EmergencyRestorePlatformCollisionIfSafe(
            global::ZalaytyMonster zalayty,
            PlatformSupportSnapshot snapshot)
        {
            if (zalayty == null || snapshot.Platform == null || snapshot.PlatformCollider == null)
                return;

            if (!snapshot.WasSupported)
                return;

            // Do not restore an intentional jump-down pass-through while Zalayty is really falling.
            // Restore only when his body is still top-supported / seated.
            if (zalayty.rigidbody2 != null && zalayty.rigidbody2.linearVelocity.y < -0.05f)
                return;

            snapshot.Platform.ForceRestoreZalaytyJumpDownSourcePlatform(zalayty);

            Collider2D[] colliders = zalayty.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D col = colliders[i];
                if (col == null || !col.enabled)
                    continue;

                Physics2D.IgnoreCollision(snapshot.PlatformCollider, col, false);
            }
        }

        private static void ConfirmMovingPlatformSupportIfNeeded(
            global::ZalaytyMonster zalayty,
            PlatFormColliderTrigger platform)
        {
            if (zalayty == null || platform == null)
                return;

            if (platform is MovingHorizontalPlatform || platform is MovingVerticalPlatform)
                zalayty.NotifyMovingPlatformTopSupport(platform);
        }

        private static bool TryFindSupportedPlatformFromGroundPoints(
            global::ZalaytyMonster zalayty,
            out PlatFormColliderTrigger platform)
        {
            platform = null;

            if (zalayty == null || zalayty.GroundPoints == null || zalayty.GroundPoints.Length == 0)
                return false;

            float radius = Mathf.Max(zalayty.Groundradius + GroundProbeExtraRadius, 0.04f);

            for (int i = 0; i < zalayty.GroundPoints.Length; i++)
            {
                Transform point = zalayty.GroundPoints[i];
                if (point == null)
                    continue;

                Collider2D[] hits = Physics2D.OverlapCircleAll(point.position, radius, zalayty.PlatformLayer);
                for (int h = 0; h < hits.Length; h++)
                {
                    Collider2D hit = hits[h];
                    if (hit == null)
                        continue;

                    PlatFormColliderTrigger candidate = hit.GetComponentInParent<PlatFormColliderTrigger>();
                    if (candidate == null || candidate.platformCollider == null)
                        continue;

                    if (IsSupportedByPlatformTop(zalayty, candidate, GetBestBodyCollider(zalayty), out _, out _))
                    {
                        platform = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsSupportedByPlatformTop(
            global::ZalaytyMonster zalayty,
            PlatFormColliderTrigger platform,
            Collider2D body,
            out float platformTopY,
            out float bodyBottomY)
        {
            platformTopY = 0f;
            bodyBottomY = 0f;

            if (zalayty == null || platform == null || platform.platformCollider == null || body == null)
                return false;

            Bounds pb = platform.platformCollider.bounds;
            Bounds bb = body.bounds;

            platformTopY = pb.max.y;
            bodyBottomY = bb.min.y;

            float left = pb.min.x + SupportedHorizontalSkin;
            float right = pb.max.x - SupportedHorizontalSkin;

            if (left > right)
            {
                left = pb.min.x;
                right = pb.max.x;
            }

            bool horizontallyAbove =
                bb.max.x > left &&
                bb.min.x < right;

            if (!horizontallyAbove)
                return false;

            float bottomToTop = bb.min.y - pb.max.y;

            return bottomToTop >= -SupportedTopPenetrationTolerance &&
                   bottomToTop <= SupportedTopGapTolerance;
        }

        private static Collider2D GetBestBodyCollider(global::ZalaytyMonster zalayty)
        {
            if (zalayty == null)
                return null;

            if (zalayty.NormalCollider != null && zalayty.NormalCollider.enabled)
                return zalayty.NormalCollider;

            if (zalayty.collider2 != null && zalayty.collider2.enabled)
                return zalayty.collider2;

            return zalayty.GetComponent<Collider2D>();
        }
    }
}

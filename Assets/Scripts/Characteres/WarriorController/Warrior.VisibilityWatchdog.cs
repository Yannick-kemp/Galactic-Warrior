using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    /// <summary>
    /// Last-resort guarantee that the Warrior never stays invisible while he is alive and playable.
    ///
    /// Several systems legitimately hide him for a while — Hashagar's Attack2 grab, the Hivernox
    /// freeze overlay, the Attack3 ice-ball cast — and each one owns the hide and is supposed to
    /// hand it back. Each of those already has its own targeted watchdog, and each of those
    /// watchdogs has turned out to have a hole: an owner destroyed mid-hold, a coroutine cut short
    /// by a scene change, a release path that skips the visual restore when the Warrior happens to
    /// be dying. The result is always the same and always the worst kind of bug to diagnose from a
    /// video: the Warrior is gone from the screen but still moves and still takes input.
    ///
    /// So this does not try to know WHY he is hidden. It only asserts the invariant: if he is alive,
    /// nobody currently holds a legitimate hide, and not a single body sprite is actually being
    /// drawn, then bring him back. It is deliberately the lowest-priority recovery — every owner
    /// gets its grace period first, and the ice overlay is never revealed by it.
    /// </summary>
    public partial class Warrior
    {
        [Header("Invisible-Warrior failsafe")]
        [Tooltip("How long the Warrior may be fully invisible, while nothing claims to be hiding " +
                 "him, before the failsafe brings him back. Real seconds.")]
        [SerializeField, Min(0.1f)] private float invisibleRecoveryGraceSeconds = 0.6f;

        [Tooltip("Logs when the failsafe has to fire. It should never fire in normal play: if it " +
                 "does, an owner failed to release and that is worth knowing.")]
        [SerializeField] private bool logInvisibleRecovery = true;

        private float _invisibleSince = -1f;

        /// <summary>Called every frame from Update, after the owner-specific watchdogs.</summary>
        private void EnsureWarriorNeverStaysInvisible()
        {
            if (!ShouldBeVisibleNow())
            {
                _invisibleSince = -1f;
                return;
            }

            if (HasAnyVisibleBodySprite())
            {
                _invisibleSince = -1f;
                return;
            }

            // Grace period so a legitimate one-frame transition is never mistaken for a stranding.
            if (_invisibleSince < 0f)
            {
                _invisibleSince = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - _invisibleSince < invisibleRecoveryGraceSeconds)
                return;

            RecoverWarriorVisibility();
            _invisibleSince = -1f;
        }

        /// <summary>
        /// False whenever somebody is legitimately entitled to hide him, so the failsafe never
        /// fights an owner that is doing its job.
        /// </summary>
        private bool ShouldBeVisibleNow()
        {
            if (IsDeadOrDying || _deathStarted)
                return false;

            if (_frozenByHivernox)
                return false;   // the ice overlay is the intended look here

            if (IsExternallyHiddenByHashagar)
                return false;   // its own liveness check releases a dead grab

            if (_attack3Casting || _iceBallShotPending)
                return false;   // the cast watchdog owns orphan recovery, with its own grace

            return true;
        }

        /// <summary>
        /// The ice overlay does not count: it is drawn only while frozen, and treating it as "the
        /// Warrior is visible" would let a stranded freeze hide the real body indefinitely.
        /// </summary>
        private bool HasAnyVisibleBodySprite()
        {
            if (_renderers == null)
                return true;   // nothing cached yet: assume fine rather than act on no information

            for (int i = 0; i < _renderers.Length; i++)
            {
                SpriteRenderer r = _renderers[i];
                if (r == null)
                    continue;

                if (warriorIceOverlay != null && r == warriorIceOverlay.OverlayRenderer)
                    continue;

                if (r.enabled && r.gameObject.activeInHierarchy && r.color.a > 0.02f)
                    return true;
            }

            return false;
        }

        private void RecoverWarriorVisibility()
        {
            // The project's own restore first: it knows which renderer is the body and, unlike a
            // blind loop, it will not reveal overlays that are meant to stay hidden.
            ForceRestoreNormalVisualsAfterExternalHide();

            if (HasAnyVisibleBodySprite())
            {
                Log("owner restore was enough");
                return;
            }

            // Still nothing on screen. A renderer can be enabled and yet invisible because an
            // ANCESTOR GameObject was deactivated — BodyFixedRoot is switched off after a freeze,
            // for instance — so walk the chain back up to this Warrior and reactivate it.
            for (int i = 0; i < _renderers.Length; i++)
            {
                SpriteRenderer r = _renderers[i];
                if (r == null || warriorIceOverlay != null && r == warriorIceOverlay.OverlayRenderer)
                    continue;

                Transform t = r.transform;
                while (t != null && t != transform)
                {
                    if (!t.gameObject.activeSelf)
                        t.gameObject.SetActive(true);

                    t = t.parent;
                }

                r.enabled = true;

                Color c = r.color;
                if (c.a <= 0.02f)
                {
                    c.a = 1f;
                    r.color = c;
                }
            }

            Log("had to re-enable the sprite chain by hand");
        }

        private void Log(string what)
        {
            if (!logInvisibleRecovery)
                return;

            Debug.LogWarning(
                $"[Warrior] Invisible-Warrior failsafe fired ({what}). An owner failed to hand the " +
                "visuals back; the Warrior was alive and playable but not drawn.", this);
        }
    }
}

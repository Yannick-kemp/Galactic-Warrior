using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Definitions;
using Assets.Scripts.Relics.Projectiles;
using System.Collections;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {
        [Header("Attack3 IK / Ice Ball Cast")]
        [SerializeField] private GameObject bodyFixedRoot;
        [SerializeField] private GameObject attack3VisualsRoot;
        [SerializeField] private Transform attack3IkTarget;      // Attack3AimTarget
        [SerializeField] private Transform attack3Shoulder;      // Attack3AimOrbit
        [SerializeField] private Transform iceBallSpawnSocket;   // HandBoneEnd/IceBallSpawnSocket
        [SerializeField] private Transform orbSocket;            // HandBoneEnd/OrbSocket

        [SerializeField] private SpriteRenderer defaultWarriorSpriteRenderer;
        [SerializeField] private GameObject iceChargeVfxPrefab;

        [Header("Attack3 IK Aim")]
        [SerializeField] private bool useAttack3IK = true;
        [SerializeField] private bool faceIceTargetWhenCasting = true;
        [SerializeField] private float attack3DefaultAimDistance = 1.25f;
        [SerializeField] private bool resetAttack3AimOnEnd = true;

        [Header("Ice Ball Cast")]
        [SerializeField] private float iceTouchGuardDuration = 0.12f;
        [SerializeField] private float minIceAimDistance = 0.12f;

        [Header("Attack3 Safety")]
        [SerializeField, Min(0.2f)] private float attack3FailsafeSeconds = 1.25f;

        private Coroutine _attack3FailsafeRoutine;

        private bool _iceBallArmed;
        private string _iceBallRelicId;
        private bool _iceBallConsumeOnCast;
        private IceBallRelic _armedIceBallDef;

        private bool _iceBallShotPending;
        private Vector2 _pendingIceBallAimWorld;
        private bool _attack3Casting;

        private GameObject _iceChargeVfxInstance;

        public bool IsIceBallArmed => _iceBallArmed;
        public bool IsAttack3Casting => _attack3Casting;
        public bool IsIceBallShotPending => _iceBallShotPending;

        private void AwakeAttack3VisualDefaults()
        {
            ShowDefaultWarriorVisuals();
            HideAttack3Body();
            HideAttack3Visuals();
            HideIceChargeVfx();
            ResetAttack3OrbitAim();
            EndAttack3Lock();
        }

        private void ShowDefaultWarriorVisuals()
        {
            // While Hivernox freeze is active, WarriorRootIceOverlay is the only
            // sprite allowed. Do not re-enable the normal Warrior sprite here.
            if (_frozenByHivernox)
                return;

            if (defaultWarriorSpriteRenderer != null)
                defaultWarriorSpriteRenderer.enabled = true;
        }

        private void HideDefaultWarriorVisuals()
        {
            if (defaultWarriorSpriteRenderer != null)
                defaultWarriorSpriteRenderer.enabled = false;
        }

        /// <summary>
        /// Self-heal for the "invisible Warrior" bug: the Attack3 / ice-ball cast hides the default
        /// sprite and only re-shows it from the AE_EndAttack3 animation event. Jumping (or any other
        /// interrupt) can exit the Attack3 animation before that event fires, leaving the sprite
        /// hidden while the Warrior stays fully functional. Called every Update: if the sprite is
        /// hidden while we are demonstrably NOT casting (and not Hivernox-frozen), restore it.
        /// </summary>
        private void EnsureDefaultWarriorSpriteVisibleWhenNotCasting()
        {
            if (_frozenByHivernox) return;
            if (_attack3Casting || _iceBallShotPending) return;   // a real cast owns the sprite
            if (defaultWarriorSpriteRenderer == null) return;
            if (defaultWarriorSpriteRenderer.enabled) return;     // already visible

            // Not casting, not frozen, but the sprite is off -> a cast-exit restore was missed.
            ShowDefaultWarriorVisuals();
            HideAttack3Body();
        }

        private void ShowAttack3Body()
        {
            // BodyFixedRoot is an Attack3 replacement body. It must never be visible
            // while WarriorSprite_IceOverlay is the active frozen visual.
            if (_frozenByHivernox)
                return;

            if (bodyFixedRoot != null && !bodyFixedRoot.activeSelf)
                bodyFixedRoot.SetActive(true);
        }

        private void HideAttack3Body()
        {
            if (bodyFixedRoot != null && bodyFixedRoot.activeSelf)
                bodyFixedRoot.SetActive(false);
        }

        private void ShowAttack3Visuals()
        {
            if (_frozenByHivernox)
                return;

            if (attack3VisualsRoot != null && !attack3VisualsRoot.activeSelf)
                attack3VisualsRoot.SetActive(true);
        }

        private void HideAttack3Visuals()
        {
            if (attack3VisualsRoot != null && attack3VisualsRoot.activeSelf)
                attack3VisualsRoot.SetActive(false);
        }

        private void BeginAttack3Lock()
        {
            _attack3Casting = true;
            CanMove = false;

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();

            if (rigidbody2 != null)
            {
                Vector2 v = rigidbody2.linearVelocity;
                v.x = 0f;
                rigidbody2.linearVelocity = v;
            }
        }

        private void EndAttack3Lock()
        {
            _attack3Casting = false;

            // Do not restore movement while another external lock owns the Warrior,
            // especially Hivernox freeze / hit-lock.
            if (!CanDie && !_platformStoneRepulseActive && !_frozenByHivernox && _hivernoxHitLockRoutine == null)
                CanMove = true;
        }

        public event System.Action<bool> OnIceBallArmedStateChanged;

        public bool TryArmIceBallRelic(IceBallRelic def, bool consumeOnCast)
        {
            // Do not allow the UI button to arm Attack3 while Warrior is frozen or hit-locked.
            if (IsHardActionLocked || _frozenByHivernox || !CanAttackWarrior)
                return false;

            if (def == null) return false;
            if (def.projectilePrefab == null) return false;
            if (IsDead || CanDie) return false;
            if (_iceBallArmed) return false;
            if (_iceBallShotPending) return false;
            if (_attack3Casting) return false;

            _armedIceBallDef = def;
            _iceBallRelicId = !string.IsNullOrEmpty(def.relicId) ? def.relicId : def.name;
            _iceBallConsumeOnCast = consumeOnCast;
            _iceBallArmed = true;
            OnIceBallArmedStateChanged?.Invoke(true);
            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, iceTouchGuardDuration));
            return true;
        }

        public bool DisarmIceBallRelic()
        {
            // Cancellation is allowed only while Ice Ball is armed and still waiting
            // for the world-touch action. Once a shot is pending / Attack3 is casting,
            // the action has started and the UI must not refund it.
            if (!_iceBallArmed || _iceBallShotPending || _attack3Casting)
                return false;

            HideIceChargeVfx();
            ClearArmedIceBall();
            ResetAttack3OrbitAim();

            if (attackMode == AttackAnimMode.Attack3)
                SetAttackMode(AttackAnimMode.Attack1);

            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, iceTouchGuardDuration));
            return true;
        }

        private bool TryHandleArmedIceBallTouch()
        {
            // Do not allow a stored/armed IceBall touch to turn into Attack3 while frozen or hit-locked.
            if (IsHardActionLocked || _frozenByHivernox || !CanAttackWarrior)
            {
                if (HasAnyIceBallCastState())
                    CancelIceBallCastForExternalLock();

                return true;
            }

            if (_attack3Casting || _iceBallShotPending)
                return true;

            if (!_iceBallArmed)
                return false;

            if (InputMgr.Instance == null)
                return false;

            Vector2 touchWorld = InputMgr.Instance.TouchedVector;

            // IMPORTANT:
            // If the player touches inside the Warrior's BoxCollider2D,
            // do NOT launch the IceBulletProjectile.
            // Return true so this touch does not trigger normal movement/jump either.
            if (!IsIceTouchOutsideWarriorCollider(touchWorld))
                return true;

            BeginArmedIceBallCast(touchWorld);
            return true;
        }

        private bool IsIceTouchOutsideWarriorCollider(Vector2 touchWorld)
        {
            // collider2 is the Warrior main BoxCollider2D cached in CharacterController.Awake().
            // If it is missing, allow the shot instead of blocking the relic forever.
            if (collider2 == null || !collider2.enabled)
                return true;

            // True means the touched point is inside the Warrior collider.
            bool touchedInsideWarriorCollider = collider2.OverlapPoint(touchWorld);

            // We only allow the projectile when the touch is OUTSIDE.
            return !touchedInsideWarriorCollider;
        }

        private void StartAttack3Failsafe()
        {
            if (_attack3FailsafeRoutine != null)
                StopCoroutine(_attack3FailsafeRoutine);

            _attack3FailsafeRoutine = StartCoroutine(Attack3FailsafeRoutine());
        }

        private void StopAttack3Failsafe()
        {
            if (_attack3FailsafeRoutine == null)
                return;

            Coroutine routine = _attack3FailsafeRoutine;
            _attack3FailsafeRoutine = null;
            StopCoroutine(routine);
        }

        private IEnumerator Attack3FailsafeRoutine()
        {
            float endTime = Time.unscaledTime + Mathf.Max(0.2f, attack3FailsafeSeconds);

            while (Time.unscaledTime < endTime)
            {
                if (!_attack3Casting && !_iceBallShotPending)
                {
                    _attack3FailsafeRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _attack3FailsafeRoutine = null;

            if (!HasAnyIceBallCastState())
                yield break;

            bool restoreMovement =
                !IsHardActionLocked &&
                !_frozenByHivernox &&
                CanAttackWarrior &&
                !CanDie &&
                !IsDeadOrDying;

            ForceFinishAttack3Cast("Attack3 failsafe timeout", restoreMovement);
        }

        private void ForceFinishAttack3Cast(string reason, bool restoreMovement)
        {
            StopAttack3Failsafe();

            if (animator != null && HasBoolParam("isAttacking3"))
                animator.SetBool("isAttacking3", false);

            _iceBallShotPending = false;

            HideIceChargeVfx();
            HideAttack3Visuals();
            HideAttack3Body();
            ShowDefaultWarriorVisuals();

            ClearArmedIceBall();
            ResetAttack3OrbitAim();

            SetAttackMode(AttackAnimMode.Attack1);

            if (restoreMovement)
            {
                EndAttack3Lock();
                ExitAttackToBestState();
            }
            else
            {
                _attack3Casting = false;
            }

            Debug.Log($"[Warrior] ForceFinishAttack3Cast: {reason}", this);
        }

        private void BeginArmedIceBallCast(Vector2 touchWorld)
        {
            if (IsHardActionLocked || _frozenByHivernox || !CanAttackWarrior)
            {
                CancelIceBallCastForExternalLock();
                return;
            }

            if (_armedIceBallDef == null)
            {
                ClearArmedIceBall();
                return;
            }

            if (IsDead || CanDie)
            {
                CancelPendingIceBallCast();
                return;
            }

            if (_iceBallConsumeOnCast)
            {
                var rm = GetComponent<RelicManager>();
                if (rm == null || !rm.TryConsumeById(_iceBallRelicId, 1))
                {
                    CancelPendingIceBallCast();
                    return;
                }
            }

            _pendingIceBallAimWorld = touchWorld;
            _iceBallShotPending = true;

            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, iceTouchGuardDuration));

            StopMoveTowardCoroutine();
            StopJumpTowardCoroutine();

            BeginAttack3Lock();
            StartAttack3Failsafe();

            PlayAttack3CastSfx();

            if (animator != null && animator.GetBool("IsLosingCtrl"))
                animator.SetBool("IsLosingCtrl", false);

            if (faceIceTargetWhenCasting)
            {
                SetDirectionVariables(_pendingIceBallAimWorld.x);
                FlipCharacter(_pendingIceBallAimWorld.x);
            }

            ApplyAttack3OrbitAim(_pendingIceBallAimWorld);

            HideDefaultWarriorVisuals();
            ShowAttack3Body();
            ShowAttack3Visuals();
            ShowIceChargeVfx();

            SetAttackMode(AttackAnimMode.Attack3);

            _attack1HitEventConsumed = false;
            GuardIdleAfterAttackRequest();
            PlayCurrentAttackAnimation();
        }

        private void ApplyAttack3OrbitAim(Vector2 targetWorld)
        {
            if (!useAttack3IK)
                return;

            if (attack3IkTarget == null)
                return;

            // Exact alignment: target follows the touched world point directly.
            attack3IkTarget.position = targetWorld;
        }

        private void ResetAttack3OrbitAim()
        {
            if (!resetAttack3AimOnEnd)
                return;

            if (attack3IkTarget == null || attack3Shoulder == null)
                return;

            Vector2 shoulderWorld = attack3Shoulder.position;
            Vector2 defaultDir = rightFacing ? Vector2.right : Vector2.left;

            attack3IkTarget.position =
                shoulderWorld + defaultDir * Mathf.Max(minIceAimDistance, attack3DefaultAimDistance);
        }

        public void AE_FireIceBall()
        {
            FirePendingIceBall();
        }

        private void FirePendingIceBall()
        {
            if (IsHardActionLocked || _frozenByHivernox || !CanAttackWarrior)
            {
                CancelIceBallCastForExternalLock();
                return;
            }

            if (!_iceBallShotPending)
                return;

            if (_armedIceBallDef == null || _armedIceBallDef.projectilePrefab == null)
            {
                CancelPendingIceBallCast();
                return;
            }

            ApplyAttack3OrbitAim(_pendingIceBallAimWorld);

            Vector3 spawnPos = GetIceBallSpawnPosition(_armedIceBallDef);
            Vector2 shootDir = GetIceBallDirection(spawnPos);

            GameObject go = Instantiate(_armedIceBallDef.projectilePrefab, spawnPos, Quaternion.identity);

            IceBulletProjectile projectile = go.GetComponent<IceBulletProjectile>();
            if (projectile != null)
            {
                projectile.Init(
                    this,
                    shootDir,
                    _armedIceBallDef.projectileSpeed,
                    _armedIceBallDef.damage,
                    _armedIceBallDef.stunSeconds,
                    _armedIceBallDef.lifeTime
                );
            }
            else
            {
                // Fallback only if prefab was not set up correctly.
                Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    go.transform.right = shootDir;
                    rb.gravityScale = 0f;
                    rb.linearVelocity = shootDir * _armedIceBallDef.projectileSpeed;
                }

                Destroy(go, Mathf.Max(0.1f, _armedIceBallDef.lifeTime));
            }

            _iceBallShotPending = false;
            HideIceChargeVfx();
            ClearArmedIceBall();
        }

        public void AE_EndAttack3()
        {
            ForceFinishAttack3Cast("AE_EndAttack3", restoreMovement: true);
        }

        private Vector3 GetIceBallSpawnPosition(IceBallRelic def)
        {
            if (iceBallSpawnSocket != null)
                return iceBallSpawnSocket.position;

            Vector3 offset = def != null ? def.spawnLocalOffset : Vector3.zero;
            offset.x = rightFacing ? Mathf.Abs(offset.x) : -Mathf.Abs(offset.x);
            return transform.position + offset;
        }

        private Vector2 GetIceBallDirection(Vector3 spawnPos)
        {
            Vector2 dir = _pendingIceBallAimWorld - (Vector2)spawnPos;

            if (dir.sqrMagnitude <= minIceAimDistance * minIceAimDistance)
                dir = rightFacing ? Vector2.right : Vector2.left;

            return dir.normalized;
        }

        private void ShowIceChargeVfx()
        {
            if (_frozenByHivernox)
                return;

            if (orbSocket == null || iceChargeVfxPrefab == null)
                return;

            HideIceChargeVfx();

            _iceChargeVfxInstance = Instantiate(
                iceChargeVfxPrefab,
                orbSocket.position,
                Quaternion.identity,
                orbSocket);

            _iceChargeVfxInstance.transform.localPosition = Vector3.zero;
            _iceChargeVfxInstance.transform.localRotation = Quaternion.identity;
            _iceChargeVfxInstance.transform.localScale = Vector3.one;
        }

        private void HideIceChargeVfx()
        {
            if (_iceChargeVfxInstance != null)
            {
                Destroy(_iceChargeVfxInstance);
                _iceChargeVfxInstance = null;
            }
        }

        private bool HasAnyIceBallCastState()
        {
            return _attack3Casting
                   || _iceBallShotPending
                   || _iceBallArmed
                   || _iceChargeVfxInstance != null
                   || (bodyFixedRoot != null && bodyFixedRoot.activeSelf)
                   || (attack3VisualsRoot != null && attack3VisualsRoot.activeSelf);
        }

        private void CancelPendingIceBallCast()
        {
            CancelIceBallCastVisualState(restoreMovementAfterCancel: true);
        }

        private void CancelIceBallCastForExternalLock()
        {
            // Used by Hivernox freeze / hit lock.
            // It cleans Attack3 visuals/state, but does NOT unlock movement.
            // HivernoxFreezeRoutine / HivernoxHitLockRoutine owns CanMove.
            CancelIceBallCastVisualState(restoreMovementAfterCancel: false);
        }

        private void CancelIceBallCastVisualState(bool restoreMovementAfterCancel)
        {
            StopAttack3Failsafe();

            _iceBallShotPending = false;

            HideIceChargeVfx();
            HideAttack3Visuals();
            HideAttack3Body();
            ShowDefaultWarriorVisuals();

            ClearArmedIceBall();
            ResetAttack3OrbitAim();

            if (animator != null && HasBoolParam("isAttacking3"))
                animator.SetBool("isAttacking3", false);

            SetAttackMode(AttackAnimMode.Attack1);

            if (restoreMovementAfterCancel)
                EndAttack3Lock();
            else
                _attack3Casting = false;
        }

        private void ClearArmedIceBall()
        {
            bool wasArmed = _iceBallArmed;

            _iceBallArmed = false;
            _iceBallRelicId = null;
            _iceBallConsumeOnCast = false;
            _armedIceBallDef = null;

            if (wasArmed)
                OnIceBallArmedStateChanged?.Invoke(false);
        }
    }
}
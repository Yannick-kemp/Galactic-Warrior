using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Relics.Core;
using System.Collections;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {
        private float _sprintEndTime = -999f;

        #region Relic Effects

        /// <summary>
        /// Backward-compatible entry point.
        /// First click arms sprint.
        /// Extra clicks while armed add duration to the upcoming sprint.
        /// Extra clicks while active add duration to the current sprint timer.
        /// </summary>
        public bool TryArmSprintRelic(string relicId, float speedMultiplier, float duration, float cooldown, bool consumeOnUse)
        {
            return TryExtendOrQueueSprintRelic(
                relicId: relicId,
                speedMultiplier: speedMultiplier,
                duration: duration,
                cooldown: cooldown,
                consumeOnUse: consumeOnUse);
        }

        public bool TryExtendOrQueueSprintRelic(string relicId, float speedMultiplier, float duration, float cooldown, bool consumeOnUse)
        {
            if (CanDie) return false;
            if (string.IsNullOrEmpty(relicId)) return false;
            if (IsShieldBlockingSprintUse()) return false;

            float safeDuration = Mathf.Max(sprintMinDuration, duration);
            float safeMultiplier = Mathf.Max(sprintMinMultiplier, speedMultiplier);
            float safeCooldown = Mathf.Max(0f, cooldown);

            // Sprint is already running: extend the SAME timer.
            // Do not restart the coroutine and do not multiply Speed again.
            if (_sprintActive)
            {
                if (consumeOnUse && !TryConsumeSprintStackInsideWarriorNow(relicId))
                    return false;

                _sprintEndTime = Mathf.Max(_sprintEndTime, Time.time) + safeDuration;
                NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, 0.12f));
                return true;
            }

            // Sprint is armed but has not started yet: queue extra duration
            // for the upcoming sprint. Keep the first relic's multiplier.
            if (_sprintArmed)
            {
                if (consumeOnUse && !TryConsumeSprintStackInsideWarriorNow(relicId))
                    return false;

                _sprintDuration = Mathf.Max(sprintMinDuration, _sprintDuration) + safeDuration;

                // Keep existing relic id/multiplier once armed. Only fill safe defaults if needed.
                if (string.IsNullOrEmpty(_sprintRelicId))
                    _sprintRelicId = relicId;

                if (_sprintSpeedMultiplier <= 0f)
                    _sprintSpeedMultiplier = safeMultiplier;

                _sprintCooldown = Mathf.Max(_sprintCooldown, safeCooldown);
                _sprintConsumeOnUse = _sprintConsumeOnUse || consumeOnUse;
                _sprintArmFrame = Time.frameCount;

                NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, 0.12f));
                return true;
            }

            // First relic: arm sprint. Actual activation still happens on movement.
            _sprintArmed = true;
            _sprintRelicId = relicId;
            _sprintSpeedMultiplier = safeMultiplier;
            _sprintDuration = safeDuration;
            _sprintCooldown = safeCooldown;
            _sprintConsumeOnUse = consumeOnUse;
            _sprintArmFrame = Time.frameCount;
            _sprintEndTime = -999f;

            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, 0.12f));
            return true;
        }

        private bool TryConsumeSprintStackInsideWarriorNow(string relicId)
        {
            if (!consumeSprintStackInsideWarrior)
                return true;

            var rm = GetComponent<RelicManager>();
            return rm != null && rm.TryConsumeById(relicId, 1);
        }

        private bool TryStartArmedSprintFromMove()
        {
            if (!_sprintArmed || _sprintActive) return false;
            if (Time.time < _nextSprintReadyTime) return false;

            // Don't start sprint while shield is up.
            if (IsShieldBlockingSprintUse()) return false;

            // Only consume here if Warrior is responsible for stack consumption.
            if (_sprintConsumeOnUse && consumeSprintStackInsideWarrior)
            {
                var rm = GetComponent<RelicManager>();
                if (rm == null)
                {
                    CancelArmedSprintRelic();
                    return false;
                }

                if (!rm.TryConsumeById(_sprintRelicId, 1))
                {
                    CancelArmedSprintRelic();
                    return false;
                }
            }

            if (_sprintRoutine != null)
                StopCoroutine(_sprintRoutine);

            _sprintRoutine = StartCoroutine(SprintRoutine(_sprintSpeedMultiplier, _sprintDuration, _sprintCooldown));
            _sprintArmed = false;
            _sprintArmFrame = -1;
            return true;
        }

        private IEnumerator SprintRoutine(float speedMultiplier, float duration, float cooldown)
        {
            _sprintActive = true;
            _nextSprintReadyTime = Time.time + cooldown;

            // Prevent previous enemy-contact lock from freezing movement.
            _blockedByEnemyContact = false;
            _blockingEnemy = null;

            _speedBeforeSprint = Speed;
            Speed = _speedBeforeSprint * speedMultiplier;

            RefreshIgnoredEnemyColliders();
            _nextSprintIgnoreRefreshTime = Time.time + sprintIgnoreRefreshInterval;

            _sprintEndTime = Time.time + Mathf.Max(sprintMinDuration, duration);

            while (Time.time < _sprintEndTime)
            {
                if (CanDie) break;

                if (Time.time >= _nextSprintIgnoreRefreshTime)
                {
                    RefreshIgnoredEnemyColliders();
                    _nextSprintIgnoreRefreshTime = Time.time + sprintIgnoreRefreshInterval;
                }

                yield return null;
            }

            Speed = _speedBeforeSprint;
            ClearIgnoredEnemyColliders();

            _sprintActive = false;
            _sprintRoutine = null;
            _sprintEndTime = -999f;
        }

        private void RefreshIgnoredEnemyColliders()
        {
            if (_warriorCollidersDuringSprint.Count == 0)
                CacheWarriorCollidersForSprint();

            if (Enemy.ActiveEnemies == null) return;

            for (int i = 0; i < Enemy.ActiveEnemies.Count; i++)
            {
                Enemy e = Enemy.ActiveEnemies[i];
                if (e == null) continue;

                var cols = e.GetComponentsInChildren<Collider2D>(true);
                for (int c = 0; c < cols.Length; c++)
                {
                    Collider2D col = cols[c];
                    if (col == null) continue;

                    if (_ignoredEnemyCollidersDuringSprint.Add(col))
                        SetIgnoreWithAllWarriorColliders(col, true);
                }
            }
        }

        private void ClearIgnoredEnemyColliders()
        {
            foreach (var col in _ignoredEnemyCollidersDuringSprint)
            {
                if (col != null)
                    SetIgnoreWithAllWarriorColliders(col, false);
            }

            _ignoredEnemyCollidersDuringSprint.Clear();
        }

        private void ForceStopSprint()
        {
            if (_sprintRoutine != null)
            {
                StopCoroutine(_sprintRoutine);
                _sprintRoutine = null;
            }

            if (_sprintActive)
            {
                Speed = _speedBeforeSprint;
                ClearIgnoredEnemyColliders();
            }

            _sprintActive = false;
            _sprintArmed = false;
            _sprintArmFrame = -1;
            _sprintEndTime = -999f;
        }

        public void CancelArmedSprintRelic()
        {
            if (_sprintActive)
                return;

            _sprintArmed = false;
            _sprintRelicId = null;
            _sprintConsumeOnUse = false;
            _sprintDuration = 0f;
            _sprintSpeedMultiplier = 0f;
            _sprintCooldown = 0f;
            _sprintArmFrame = -1;
            _sprintEndTime = -999f;
        }

        #endregion
    }
}
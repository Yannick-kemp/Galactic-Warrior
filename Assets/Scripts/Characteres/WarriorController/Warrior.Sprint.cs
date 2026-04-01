using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Relics.Core;
using System.Collections;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {

        #region Relic Effects
        public bool TryArmSprintRelic(string relicId, float speedMultiplier, float duration, float cooldown, bool consumeOnUse)
        {
            if (CanDie) return false;
            if (string.IsNullOrEmpty(relicId)) return false;
            if (_sprintActive) return false;
            // NEW: shield/sprint mutual exclusion

            if (IsShieldBlockingSprintUse()) return false;

            //if (Time.time < _nextSprintReadyTime)
            //    return false;

            _sprintArmed = true;
            _sprintRelicId = relicId;
            _sprintSpeedMultiplier = Mathf.Max(sprintMinMultiplier, speedMultiplier);
            _sprintDuration = Mathf.Max(sprintMinDuration, duration);
            _sprintCooldown = Mathf.Max(0f, cooldown);
            _sprintConsumeOnUse = consumeOnUse;
            _sprintArmFrame = Time.frameCount; // NEW

            NotifyUIConsumedInput(Mathf.Max(uiInputGuardDuration, 0.12f));

            if (!_sprintActive && (activesMoveCoroutine != null || Mathf.Abs(rigidbody2.linearVelocity.x) > 0.05f))
            {
                TryStartArmedSprintFromMove();
            }

            return true;
        }

        private bool TryStartArmedSprintFromMove()
        {
            if (!_sprintArmed || _sprintActive) return false;
            if (Time.time < _nextSprintReadyTime) return false;


            // NEW: don't start sprint while shield is up
            if (IsShieldBlockingSprintUse()) return false;

            // Only consume here if Warrior is responsible for stack consumption.
            if (_sprintConsumeOnUse && consumeSprintStackInsideWarrior)
            {
                var rm = GetComponent<RelicManager>();
                if (rm == null)
                {
                    _sprintArmed = false;
                    return false;
                }

                if (!rm.TryConsumeById(_sprintRelicId, 1))
                {
                    _sprintArmed = false;
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

            // Prevent previous enemy-contact lock from freezing movement
            _blockedByEnemyContact = false;
            _blockingEnemy = null;

            _speedBeforeSprint = Speed;
            Speed = _speedBeforeSprint * speedMultiplier;

            RefreshIgnoredEnemyColliders();
            _nextSprintIgnoreRefreshTime = Time.time + sprintIgnoreRefreshInterval;

            float endTime = Time.time + duration;
            while (Time.time < endTime)
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
        }
        #endregion
    }
}

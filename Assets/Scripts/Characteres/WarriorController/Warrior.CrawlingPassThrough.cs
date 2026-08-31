using Assets.Scripts.Characteres.EnemyContoller;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    /// <summary>
    /// Crawling-monster jump pass-through.
    ///
    /// Rule: while the Warrior collider is IN CONTACT with a CrawlingMonster and the Warrior
    /// jumps, every collision between the two is ignored for as long as they stay in contact,
    /// and ALL collisions are restored as soon as they are separated again.
    ///
    /// Design notes (why it is built this way):
    ///  - Contact is measured GEOMETRICALLY (Collider2D.Distance), never through collision
    ///    callbacks: once Physics2D.IgnoreCollision is on, the pair produces no contact at all,
    ///    so OnCollisionStay2D / IsTouching would report "not in contact" on the very next frame
    ///    and the ignore would be dropped immediately.
    ///  - Only NON-trigger colliders are paired by default. Triggers never block movement, and
    ///    the monster's trigger-based detection must keep working while the Warrior passes over.
    ///  - Restoring never fights another system: while a window that legitimately owns the
    ///    Warrior/enemy ignore state is active (sprint dodge, post-bounce, enemy-top trap ignore,
    ///    absolute failsafe, death) the restore is deferred instead of forced.
    /// </summary>
    public partial class Warrior : CharacterController
    {
        #region Crawling Monster Jump Pass-Through

        [Header("Crawling Monster - Jump Pass-Through")]
        [Tooltip("ON = when the Warrior jumps while touching a CrawlingMonster, all collisions with THAT monster are ignored, and restored as soon as the two are no longer in contact.")]
        [SerializeField] private bool enableCrawlingJumpPassThrough = true;

        [Tooltip("Gap (meters) under which the Warrior and the CrawlingMonster count as touching when the jump starts. Physics leaves a small skin between resting bodies, so keep a little slack here.")]
        [SerializeField, Min(0f)] private float crawlingPassThroughContactTolerance = 0.06f;

        [Tooltip("Extra separation (meters) required ON TOP of the contact tolerance before collisions are restored. Pure hysteresis: prevents an ignore/restore flicker while the two bodies graze each other.")]
        [SerializeField, Min(0f)] private float crawlingPassThroughRestoreClearance = 0.05f;

        [Tooltip("Padding added around the Warrior collider when looking for CrawlingMonsters at jump start.")]
        [SerializeField, Min(0f)] private float crawlingPassThroughScanPad = 0.12f;

        [Tooltip("Safety cap (seconds). If the two never separate (e.g. the Warrior ends up standing inside the monster), collisions are restored anyway after this delay so the pair can never stay ignored forever. 0 = no cap.")]
        [SerializeField, Min(0f)] private float crawlingPassThroughMaxSeconds = 6f;

        [Tooltip("OFF (recommended) = only solid colliders are ignored. ON = trigger colliders too, which also suspends the monster's trigger-based detection while the Warrior passes through.")]
        [SerializeField] private bool crawlingPassThroughIncludeTriggerColliders = false;

        private sealed class CrawlingPassThroughRecord
        {
            public CrawlingMonster Monster;
            public readonly List<Collider2D> WarriorColliders = new List<Collider2D>();
            public readonly List<Collider2D> MonsterColliders = new List<Collider2D>();
            public float StartedAt;
        }

        private readonly List<CrawlingPassThroughRecord> _crawlingPassThroughRecords =
            new List<CrawlingPassThroughRecord>(4);

        private readonly Collider2D[] _crawlingPassThroughScanBuffer = new Collider2D[16];
        private readonly List<Collider2D> _crawlingPassThroughTempWarriorCols = new List<Collider2D>(8);
        private ContactFilter2D _crawlingPassThroughFilter;
        private bool _crawlingPassThroughFilterReady;

        /// <summary>
        /// True while the given enemy is currently phased through by the jump pass-through.
        /// Other systems use it to stand down: that overlap is intentional.
        /// </summary>
        public bool IsCrawlingJumpPassThroughActiveWith(Enemy enemy)
        {
            return IsCrawlingJumpPassThroughActiveFor(enemy);
        }

        private bool IsCrawlingJumpPassThroughActiveFor(Enemy enemy)
        {
            if (enemy == null || _crawlingPassThroughRecords.Count == 0)
                return false;

            for (int i = 0; i < _crawlingPassThroughRecords.Count; i++)
            {
                if (_crawlingPassThroughRecords[i].Monster == enemy)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Called from MarkJumpStarted() — i.e. from every jump entry point (tap jump, direct /
        /// joystick jump, scripted bounce jump). Opens a pass-through for each CrawlingMonster
        /// the Warrior is touching at that instant.
        /// </summary>
        private void BeginCrawlingJumpPassThroughOnJumpStart()
        {
            if (!enableCrawlingJumpPassThrough) return;
            if (collider2 == null || enemyLayer.value == 0) return;

            if (!_crawlingPassThroughFilterReady)
            {
                _crawlingPassThroughFilter = new ContactFilter2D
                {
                    // Triggers are scanned so the monster is still found through its detection
                    // trigger; which colliders actually get ignored is decided further down.
                    useTriggers = true,
                    useLayerMask = true
                };
                _crawlingPassThroughFilter.SetLayerMask(enemyLayer);
                _crawlingPassThroughFilterReady = true;
            }

            Bounds b = collider2.bounds;
            Vector2 size = (Vector2)b.size +
                           new Vector2(crawlingPassThroughScanPad * 2f, crawlingPassThroughScanPad * 2f);

            int count = Physics2D.OverlapBox(b.center, size, 0f,
                                             _crawlingPassThroughFilter, _crawlingPassThroughScanBuffer);
            if (count <= 0) return;

            CollectPassThroughColliders(gameObject, _crawlingPassThroughTempWarriorCols);
            if (_crawlingPassThroughTempWarriorCols.Count == 0) return;

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _crawlingPassThroughScanBuffer[i];
                if (hit == null) continue;

                CrawlingMonster monster = hit.GetComponentInParent<CrawlingMonster>();
                if (monster == null) continue;
                if (IsCrawlingJumpPassThroughActiveFor(monster)) continue;

                var record = new CrawlingPassThroughRecord
                {
                    Monster = monster,
                    StartedAt = Time.time
                };

                record.WarriorColliders.AddRange(_crawlingPassThroughTempWarriorCols);
                CollectPassThroughColliders(monster.gameObject, record.MonsterColliders);

                if (record.MonsterColliders.Count == 0) continue;

                // Only a monster the Warrior is ACTUALLY touching right now opens a pass-through.
                if (!IsRecordInContact(record, crawlingPassThroughContactTolerance)) continue;

                SetRecordIgnored(record, true);
                _crawlingPassThroughRecords.Add(record);
            }

            _crawlingPassThroughTempWarriorCols.Clear();
        }

        /// <summary>
        /// Per-physics-frame maintenance: restores every collision as soon as the Warrior and the
        /// monster are geometrically separated (plus the hysteresis clearance).
        /// </summary>
        private void UpdateCrawlingJumpPassThrough()
        {
            if (_crawlingPassThroughRecords.Count == 0) return;

            float breakGap = crawlingPassThroughContactTolerance + crawlingPassThroughRestoreClearance;

            for (int i = _crawlingPassThroughRecords.Count - 1; i >= 0; i--)
            {
                CrawlingPassThroughRecord record = _crawlingPassThroughRecords[i];

                // Monster destroyed / pooled away: nothing left to restore.
                if (record.Monster == null)
                {
                    _crawlingPassThroughRecords.RemoveAt(i);
                    continue;
                }

                bool stillInContact = IsRecordInContact(record, breakGap);

                bool timedOut = crawlingPassThroughMaxSeconds > 0f &&
                                Time.time - record.StartedAt >= crawlingPassThroughMaxSeconds;

                if (stillInContact && !timedOut)
                {
                    // Re-assert every frame: enemy-side trigger callbacks (Enemy.OnTriggerExit2D)
                    // and other systems may flip this pair back on mid-flight. While the two are
                    // still in contact after a jump, the pass-through stays authoritative.
                    SetRecordIgnored(record, true);
                    continue;
                }

                // Separated (or safety cap reached): give every collision back.
                if (!TryRestoreCrawlingPassThrough(record))
                    continue; // another system owns the ignore state right now — retry next frame.

                _crawlingPassThroughRecords.RemoveAt(i);
            }
        }

        /// <summary>Restores every pass-through (Warrior disabled / destroyed / scene teardown).</summary>
        private void ClearAllCrawlingJumpPassThrough()
        {
            for (int i = 0; i < _crawlingPassThroughRecords.Count; i++)
            {
                CrawlingPassThroughRecord record = _crawlingPassThroughRecords[i];
                if (record.Monster == null) continue;

                SetRecordIgnored(record, false);
            }

            _crawlingPassThroughRecords.Clear();
        }

        private bool TryRestoreCrawlingPassThrough(CrawlingPassThroughRecord record)
        {
            // Never clobber a window that legitimately wants the Warrior to phase through
            // enemies: it would re-enable collisions in the middle of a sprint dodge, of the
            // enemy-top trap escape, of the absolute failsafe, or of the death sequence.
            if (IsForeignEnemyIgnoreWindowActive())
                return false;

            SetRecordIgnored(record, false);
            return true;
        }

        private bool IsForeignEnemyIgnoreWindowActive()
        {
            return _sprintActive
                || _postBounceActive
                || _absoluteFailsafeActive
                || _enemyTopTrapIgnoreRoutine != null
                || IsDeadOrDying;
        }

        private void SetRecordIgnored(CrawlingPassThroughRecord record, bool ignore)
        {
            for (int w = 0; w < record.WarriorColliders.Count; w++)
            {
                Collider2D wcol = record.WarriorColliders[w];
                if (wcol == null) continue;

                for (int m = 0; m < record.MonsterColliders.Count; m++)
                {
                    Collider2D mcol = record.MonsterColliders[m];
                    if (mcol == null) continue;

                    Physics2D.IgnoreCollision(wcol, mcol, ignore);
                }
            }
        }

        /// <summary>
        /// Geometric contact test. Independent of the ignore state (Collider2D.Distance is a pure
        /// query), which is exactly what makes the "restore on separation" rule possible.
        /// </summary>
        private bool IsRecordInContact(CrawlingPassThroughRecord record, float maxGap)
        {
            for (int w = 0; w < record.WarriorColliders.Count; w++)
            {
                Collider2D wcol = record.WarriorColliders[w];
                if (wcol == null || !wcol.enabled || !wcol.gameObject.activeInHierarchy) continue;

                for (int m = 0; m < record.MonsterColliders.Count; m++)
                {
                    Collider2D mcol = record.MonsterColliders[m];
                    if (mcol == null || !mcol.enabled || !mcol.gameObject.activeInHierarchy) continue;

                    ColliderDistance2D cd = wcol.Distance(mcol);
                    if (!cd.isValid) continue;

                    // Negative while overlapped, positive gap otherwise.
                    if (cd.distance <= maxGap)
                        return true;
                }
            }

            return false;
        }

        private void CollectPassThroughColliders(GameObject root, List<Collider2D> into)
        {
            into.Clear();
            if (root == null) return;

            Collider2D[] cols = root.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D c = cols[i];
                if (c == null) continue;
                if (c.isTrigger && !crawlingPassThroughIncludeTriggerColliders) continue;

                into.Add(c);
            }
        }

        #endregion
    }
}

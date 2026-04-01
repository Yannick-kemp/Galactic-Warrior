using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using System.Collections.Generic;
using UnityEngine;

public class WarriorBehaveEnemyAttack : MonoBehaviour, ICollisionHandler
{
    [Header("Squeeze Detection")]
    [Tooltip("How much wider the check box is than the warrior's collider.")]
    [SerializeField] private float boxWidthMultiplier = 1.2f;
    [Tooltip("How tall the check box is relative to the warrior collider.")]
    [SerializeField] private float boxHeightMultiplier = 0.8f;
    [Tooltip("Layers that count as enemies.")]
    [SerializeField] private LayerMask EnemyLayer;


    [Tooltip("Debug visualization of the check box.")]
    [SerializeField] private bool drawGizmos = true;

    [Header("Shield vs Squeeze")]
    [SerializeField] private bool shieldBlocksSqueezeKill = true;
    [SerializeField] private float squeezeShieldCost = 18f;     // how much durability to spend each tick
    [SerializeField] private float squeezeShieldInterval = 0.15f;
    [SerializeField] private float squeezeKnockback = 0.35f;
    [SerializeField] private float squeezeStunSeconds = 0.12f;

    [SerializeField] private float squeezeCheckInterval = 0.05f;

    private float _nextSqueezeCheck;

    private void FixedUpdate()
    {
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + squeezeCheckInterval;

        TryKillIfSqueezed();
    }

    private bool warriorKilled;

    // ICollisionHandler requirement
    public bool ShouldHandle(Collision2D collision)
    {
        if (collision.gameObject.layer != LayerMask.NameToLayer("Enemy"))
            return false;

        return collision.gameObject.GetComponent<Enemy>() != null;
    }

    // Called when the warrior collides with an enemy
    public void HandleCollision(Collision2D collision)
    {
        TryKillIfSqueezed();
    }

    /// <summary>
    /// Main logic to determine if the warrior is squeezed by at least two enemies.
    /// </summary>
    private void TryKillIfSqueezed()
    {
        if (warriorKilled) return;

        var w = GameMgr.Instance?.WarriorInstance;
        if (w == null) return;

        Physics2D.SyncTransforms();

        // --- your existing box + enemies list build ---
        Bounds wb = w.collider2.bounds;
        Vector2 boxCenter = wb.center;
        Vector2 boxSize = new Vector2(wb.size.x * boxWidthMultiplier, wb.size.y * boxHeightMultiplier);

        Collider2D[] hits = Physics2D.OverlapBoxAll(boxCenter, boxSize, 0f, EnemyLayer);

        var enemies = new List<Enemy>();
        var seen = new HashSet<Enemy>();
        foreach (var h in hits)
        {
            if (h == null) continue;
            var e = h.GetComponent<Enemy>();
            if (e == null) continue;
            if (seen.Add(e)) enemies.Add(e);
        }

        bool squeezed = IsSqueezedByTwoEnemies(w, enemies);
        bool groundedNow = w.CountGroundPoints() > 0;

        // ============ SQUEEZE START / UPDATE ============
        if (squeezed)
        {
            if (!_squeezeActive)
            {
                _squeezeActive = true;
                _squeezeStartTime = Time.time;
                _squeezeStartPos = (Vector2)w.collider2.bounds.center;
                _squeezeMaxEnemies = enemies.Count;

                _jumpStartedDuringSqueeze = false;
                _airborneDuringSqueeze = false;
            }
            else
            {
                _squeezeMaxEnemies = Mathf.Max(_squeezeMaxEnemies, enemies.Count);
            }

            // detect jump started while squeezed (best signal)
            if (Time.time - w.LastJumpStartTime <= 0.20f)
                _jumpStartedDuringSqueeze = true;

            // confirm airborne happened (prevents “edge fall” counting as jump escape)
            if (w.CountGroundPoints() == 0)
                _airborneDuringSqueeze = true;

            // give player time to escape
            if (Time.time - _squeezeStartTime < squeezeKillDelay)
                return;

            // after delay, still squeezed => kill (your existing kill logic)

            // Track "escape method" evidence while squeezed:
            // A) Sprint/dodge was used while squeezed
            if (w.IsDodging) _escapeBySprint = true;

            // B) Jump happened while squeezed:
            // - we were grounded during squeeze, then became airborne
            // - OR jump coroutine started
            if (_wasGroundedWhileSqueezed && !groundedNow) _escapeByJump = true;
            if (w.activesJumpCoroutine != null) _escapeByJump = true;

            if (groundedNow) _wasGroundedWhileSqueezed = true;

            // Shield blocks squeeze kill (your existing behavior)
            if (shieldBlocksSqueezeKill && w.ShieldIsUp)
            {
                bool absorbed = w.TryAbsorbSqueeze(squeezeShieldCost, squeezeShieldInterval);

                if (absorbed)
                {
                    foreach (var e in enemies)
                    {
                        if (e == null) continue;

                        e.ApplyStun(squeezeStunSeconds);
                        float dir = (e.transform.position.x - w.transform.position.x) >= 0f ? 1f : -1f;
                        e.stepBackDistance = squeezeKnockback;
                        StartCoroutine(dir > 0f ? e.SmoothStepBack(true) : e.SmoothStepBack(false));
                    }
                }

                return; // IMPORTANT: never kill while shield up
            }

            // Give time to escape (jump or sprint through)
            if (Time.time - _squeezeStartTime < squeezeKillDelay)
                return;

            // If still squeezed after delay => kill
            warriorKilled = true;

            // Stop movement of enemies (optional)
            foreach (var e in enemies) e?.StopMoveTowardCoroutine();

            Debug.Log($"[SQUEEZED] Warrior killed between {enemies.Count} enemies.");

            // Use the warrior's unified death pipeline
            w.ForceDeath();
            return;
        }

        // ============ SQUEEZE ENDED => SCORE ESCAPE ============
        if (_squeezeActive && !squeezed)
        {
            _squeezeActive = false;

            float squeezedSeconds = Time.time - _squeezeStartTime;
            Vector2 nowPos = (Vector2)w.collider2.bounds.center;
            float escapeDist = Vector2.Distance(_squeezeStartPos, nowPos);

            bool jumpEscape = _jumpStartedDuringSqueeze && _airborneDuringSqueeze;

            if (!warriorKilled && !w.CanDie && jumpEscape && escapeDist >= minEscapeDistance)
            {
                w.GetComponent<Assets.Scripts.Scoring.SpectacularActionScorer>()
                 ?.NotifySqueezeJumpEscape(_squeezeMaxEnemies, squeezedSeconds, nowPos);
            }
        }
    }

    /// <summary>
    /// Check if there are enemies on both sides (left & right) of the warrior.
    /// </summary>
    private bool IsSqueezedByTwoEnemies(Warrior warrior, List<Enemy> enemies)
    {
        //if (hits == null || hits.Length < 2)
        //    return false;
        if (enemies == null || enemies.Count < 2) return false;

        int leftSideCount = 0;
        int rightSideCount = 0;

        bool hasFacingLeft = false;
        bool hasFacingRight = false;

        foreach (var enemy in enemies)
        {
            if (enemy == null) continue;

            float dx = enemy.transform.position.x - warrior.transform.position.x;
            if (dx < 0f) leftSideCount++;
            else if (dx > 0f) rightSideCount++;

            bool? facingRight = null;
            if (enemy.Front != null && enemy.Back != null)
            {
                float fx = enemy.Front.position.x;
                float bx = enemy.Back.position.x;
                if (fx > bx) facingRight = true;
                else if (fx < bx) facingRight = false;
            }

            if (facingRight == null)
                facingRight = enemy.transform.localScale.x >= 0f;

            if (facingRight.Value) hasFacingRight = true;
            else hasFacingLeft = true;
        }

        bool squeezedFromBothSides = leftSideCount > 0 && rightSideCount > 0;
        bool oppositeFacingPresent = hasFacingLeft && hasFacingRight;

        return squeezedFromBothSides && oppositeFacingPresent;

    }
    // WarriorBehaveEnemyAttack.cs


    [Header("Squeeze Escape Scoring")]
    [SerializeField] private float squeezeKillDelay = 0.35f;   // gives a chance to escape
    [SerializeField] private float minEscapeDistance = 1.0f;   // must actually move out

    private bool _squeezeActive;
    private float _squeezeStartTime;
    private Vector2 _squeezeStartPos;
    private int _squeezeMaxEnemies;

    private bool _escapeBySprint;
    private bool _escapeByJump;

    private bool _wasGroundedWhileSqueezed;
    private float _nextCheck;

    private bool _jumpStartedDuringSqueeze;
    private bool _airborneDuringSqueeze;
    /// <summary>
    /// Optional debug gizmo to visualize the OverlapBox in the Scene view.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        var w = GameMgr.Instance?.WarriorInstance;
        if (w == null) return;

        Bounds wb = w.collider2.bounds;
        Vector2 boxCenter = wb.center;
        Vector2 boxSize = new Vector2(
            wb.size.x * boxWidthMultiplier,
            wb.size.y * boxHeightMultiplier
        );

        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(boxCenter, boxSize);
    }
}

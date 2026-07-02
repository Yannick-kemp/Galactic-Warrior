using System.Collections;
using System.Collections.Generic;
using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Onboarding tutorial that runs once, on the very first WarriorScene playthrough.
///
/// It guides the player through 4 ordered steps — move-left, move-right, jump, attack.
///
/// The hand is OWNED by this controller: it instantiates <see cref="handPrefab"/> (a simple UI
/// Image with a hand sprite) directly under the HUD canvas at runtime, so it is free of the
/// existing TutorialHand hierarchy (parent CanvasGroups, anchors, draw order) that kept hiding it.
/// For the movement/jump steps the hand is parked next to the Warrior's SCREEN position in the
/// direction of the step (left / right / up) so it is always visible and points the right way,
/// independent of the camera. Validation is by the Warrior's actual displacement (moved far enough
/// left/right, or left the ground for the jump), so it can never soft-lock. For the attack step a
/// TutorialTapHint over the attack button is enabled instead.
///
/// While any step is pending, real gameplay is frozen: enemy spawn points are disabled and combat
/// damage is short-circuited via <see cref="TutorialActive"/> (read by Warrior.TakeDamage and
/// Warrior.KnockbackEnemiesInRange). Because WarriorScene only ends on a full enemy clear, freezing
/// combat is enough to gate scene progression.
///
/// Completion is persisted through GameMgr (PlayerPrefs), so replaying WarriorScene later never
/// shows the tutorial again.
/// </summary>
public class WarriorTutorialController : MonoBehaviour
{
    /// <summary>
    /// True while the onboarding tutorial is running. Combat code reads this so the Warrior
    /// deals/receives no damage until the 4 steps are validated.
    /// </summary>
    public static bool TutorialActive { get; private set; }

    private enum Step { Left, Right, Jump, Attack, Done }

    [Header("Scene gate (lets this component live safely in the shared _GameTools prefab)")]
    [Tooltip("The tutorial only runs in this scene. In every other scene the component is a no-op.")]
    [SerializeField] private string tutorialSceneName = "WarriorScene";

    [Header("Hand prefab (a simple UI Image with a hand sprite)")]
    [Tooltip("Prefab instantiated under the HUD canvas at runtime and driven by this controller.")]
    [SerializeField] private GameObject handPrefab;
    [Tooltip("Canvas to host the spawned hand. If empty, found automatically from the HUD.")]
    [SerializeField] private Canvas hostCanvas;
    [Tooltip("Scale applied to the spawned hand RectTransform.")]
    [SerializeField, Min(0.1f)] private float handScale = 1f;

    [Header("Validation / target distance (world units)")]
    [Tooltip("How far left/right the destination sits from the Warrior's start; the hand marks " +
             "that fixed spot and the step validates when the Warrior gets near it.")]
    [SerializeField, Min(0.5f)] private float requiredMoveDistance = 2.5f;
    [Tooltip("How close to the destination counts as 'reached' (a tap is never pixel-precise, so " +
             "the Warrior usually stops a bit short — this slack lets the step validate anyway).")]
    [SerializeField, Min(0f)] private float reachTolerance = 1f;

    [Header("Hand placement (screen pixels)")]
    [Tooltip("How high above the Warrior the hand sits for the jump step (must stay above the " +
             "Warrior so the tap reads as a jump; left/right hands stay at ground level so they walk).")]
    [SerializeField, Min(0f)] private float handJumpRiseY = 170f;
    [Tooltip("Keep the hand inside the screen with this pixel margin.")]
    [SerializeField, Min(0f)] private float handScreenMargin = 90f;

    [Header("Other HUD refs")]
    [Tooltip("Gameplay camera used for the world→screen conversion (defaults to Camera.main).")]
    [SerializeField] private Camera worldCamera;
    [Tooltip("Optional: the attack button RectTransform. If empty, found automatically via ButtonClickHandler.")]
    [SerializeField] private RectTransform attackButton;
    [Tooltip("Vertical offset (screen px) of the hand relative to the attack button (positive = above).")]
    [SerializeField] private float handAttackOffsetY = 60f;
    [Tooltip("Optional legacy TutorialTapHint object over the attack button (also enabled for the attack step).")]
    [SerializeField] private GameObject attackHint;

    [Header("Training halo (optional, purely cosmetic)")]
    [Tooltip("Dark halo / vignette UI image shown during the tutorial and faded out when it ends. " +
             "Authoring tip: a dark image with a soft transparent center; Raycast Target OFF.")]
    [SerializeField] private GameObject haloPrefab;
    [Tooltip("If true, the halo is re-centered on the Warrior every frame (a spotlight on him). " +
             "If false, it stays where the prefab is anchored (e.g. a fixed full-screen vignette).")]
    [SerializeField] private bool haloFollowsWarrior = true;
    [Tooltip("Seconds to fade the halo out when the tutorial completes.")]
    [SerializeField, Min(0f)] private float haloFadeOutDuration = 0.6f;

    [Header("Platform confinement (invisible walls during the tutorial)")]
    [Tooltip("Spawn two invisible BoxCollider2D walls at the platform edges to keep the Warrior on " +
             "his starting platform until the tutorial is finished.")]
    [SerializeField] private bool confineToPlatform = true;
    [Tooltip("How far inside the platform edges the walls sit (world units).")]
    [SerializeField, Min(0f)] private float confineInset = 0.3f;
    [Tooltip("How far above the platform top the walls reach, to also block jumps (world units).")]
    [SerializeField, Min(0f)] private float wallHeight = 8f;

    // Outward depth of each wall block. Deliberately large (and a code constant, not a serialized
    // field) so a fast MovePosition can never tunnel through, regardless of inspector values.
    private const float WallDepth = 60f;

    [Header("Debug")]
    [Tooltip("Print step-by-step diagnostics to the Console (turn off once it works).")]
    [SerializeField] private bool debugLogs = true;

    private Step _step;
    private Warrior _warrior;
    private Collider2D _warriorCollider;
    private bool _running;

    private Canvas _canvas;                 // resolved host canvas (shared by hand + halo)
    private RectTransform _hand;            // working hand (spawned from handPrefab)
    private RectTransform _handParent;      // the host canvas RectTransform
    private RectTransform _halo;            // training halo (spawned from haloPrefab)
    private CanvasGroup _haloGroup;         // for fading the halo out
    private bool _handActive;
    private bool _handUseWorldTarget;       // true = pin to a fixed world spot (L/R)
    private bool _handOverButton;           // true = pin over the attack button (attack step)
    private Vector3 _handWorldTarget;       // fixed destination for L/R steps
    private Vector2 _handScreenOffset;      // pixel offset from the Warrior (jump step)
    private RectTransform _attackButtonRect;

    private float _stepStartX;              // Warrior X when the current move step began
    private bool _jumpSawGrounded;          // jump step: confirm grounded before checking airborne
    private float _nextLiveLog;

    private GameObject _wallLeft, _wallRight;

    private readonly List<GameObject> _disabledEnemies = new List<GameObject>();

    private IEnumerator Start()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        Log($"Start on '{name}' — active scene='{sceneName}', gate='{tutorialSceneName}'.");

        // Scene gate: when hosted in the shared _GameTools prefab this instance also exists in
        // AgeOfIce/LandOfFire/etc. — only ever run the tutorial in WarriorScene.
        if (!string.IsNullOrEmpty(tutorialSceneName) && sceneName != tutorialSceneName)
        {
            Log($"Scene gate blocked tutorial (scene '{sceneName}' != '{tutorialSceneName}').");
            TutorialActive = false;
            SetActiveSafe(attackHint, false);
            enabled = false;
            yield break;
        }

        // Control-scheme gate: the whole tutorial is tap-based (a hand on a world target the player
        // taps, tap-to-jump). In joystick (Direct) mode it makes no sense and would soft-lock the
        // player (combat is frozen, the Warrior is boxed in by confinement walls) → skip it cleanly.
        // We deliberately do NOT mark it completed, so switching back to Tap later still shows it.
        if (ControlScheme.IsDirect)
        {
            Log("Control scheme = Direct (joystick) → skipping tap tutorial (normal gameplay).");
            TutorialActive = false;
            SetActiveSafe(attackHint, false);
            enabled = false;
            yield break;
        }

        bool done = GameMgr.Instance != null && GameMgr.Instance.IsTutorialCompleted;
        Log($"GameMgr.Instance={(GameMgr.Instance != null)}, IsTutorialCompleted={done}.");

        // Already done on a previous run → leave gameplay completely normal.
        if (done)
        {
            Log("Tutorial already completed → skipping (normal gameplay).");
            TutorialActive = false;
            SetActiveSafe(attackHint, false);
            enabled = false;
            yield break;
        }

        TutorialActive = true;
        SetActiveSafe(attackHint, false);

        if (worldCamera == null)
            worldCamera = Camera.main;

        _canvas = ResolveCanvas();
        _handParent = _canvas != null ? _canvas.GetComponent<RectTransform>() : null;
        SpawnHand();
        SpawnHalo();

        // Freeze combat visually: WarriorScene enemies are spawned at runtime by EnemySpawnPoint
        // when the spawn point becomes camera-visible, and each spawned enemy is parented under
        // its spawn point. So we disable the spawn points (stops spawning AND deactivates any
        // already-spawned child enemy). EnemyMgr counts spawn points with FindObjectsInactive.Include
        // and HasAliveCountableEnemy stays true while disabled, so the full-clear total is preserved
        // (no premature victory).
        FreezeEnemies();

        // Wait for the Warrior to exist before validating movement.
        float waited = 0f;
        while (_warrior == null)
        {
            _warrior = Warrior.Instance;
            if (_warrior == null)
            {
                waited += Time.deltaTime;
                if (waited > 3f)
                {
                    Log("Still waiting for Warrior.Instance after 3s — is the Warrior in this scene?");
                    waited = 0f;
                }
                yield return null;
            }
        }

        _warriorCollider = _warrior.GetComponent<Collider2D>();

        // Wait until the Warrior is actually standing on a platform surface before measuring the
        // hand height, so the (first) left hand isn't placed off while he's still settling/falling.
        float groundedWait = 0f;
        while (_warrior.CountGroundPoints() == 0)
        {
            groundedWait += Time.deltaTime;
            if (groundedWait > 5f)
            {
                Log("Warrior not grounded after 5s — starting the sequence anyway.");
                break;
            }
            yield return null;
        }

        BuildConfinementWalls();

        Log("Warrior grounded — starting step sequence.");
        ButtonClickHandler.OnAttackButtonPressed += HandleAttackButtonPressed;
        _running = true;

        EnterStep(Step.Left);
    }

    private void OnDisable()
    {
        // Safety net: if the tutorial is torn down before completing (scene change, object disabled,
        // domain reload), never leave the world frozen — clear the combat lock and restore state.
        // Without this, a stuck TutorialActive makes the Warrior invulnerable AND unable to damage
        // enemies, which breaks death / retry / revive.
        if (_running)
        {
            _running = false;
            RestoreDisabledEnemies();
            DestroyConfinementWalls();
        }
        TutorialActive = false;
    }

    private void OnDestroy()
    {
        ButtonClickHandler.OnAttackButtonPressed -= HandleAttackButtonPressed;
        TutorialActive = false;
        if (_hand != null)
            Destroy(_hand.gameObject);
        if (_halo != null)
            Destroy(_halo.gameObject);
        DestroyConfinementWalls();
    }

    private void Update()
    {
        if (_warrior == null)
            return;

        // The halo follows the Warrior even before the step sequence starts (during the grounded wait).
        if (_halo != null && haloFollowsWarrior)
            PositionHaloOnWarrior();

        if (!_running)
            return;

        // Keep the hand pinned to its target (fixed world spot for L/R, above the Warrior for jump).
        if (_handActive && _hand != null)
            PositionHand();

        if (debugLogs && _handActive && Time.unscaledTime >= _nextLiveLog)
        {
            _nextLiveLog = Time.unscaledTime + 0.5f;
            Log($"[live] step={_step} warriorX={_warrior.transform.position.x:F2} startX={_stepStartX:F2} " +
                $"handPos={(_hand != null ? _hand.anchoredPosition.ToString() : "n/a")}");
        }

        switch (_step)
        {
            case Step.Left:
                if (_warrior.transform.position.x <= _stepStartX - requiredMoveDistance + reachTolerance)
                    EnterStep(Step.Right);
                break;

            case Step.Right:
                if (_warrior.transform.position.x >= _stepStartX + requiredMoveDistance - reachTolerance)
                    EnterStep(Step.Jump);
                break;

            case Step.Jump:
                if (HasJumped())
                    EnterStep(Step.Attack);
                break;

            // Step.Attack is validated by the attack-button event, not by Update.
        }
    }

    private void BuildConfinementWalls()
    {
        if (!confineToPlatform)
            return;

        // Detect the actual ground collider under the Warrior by raycast (deterministic), instead
        // of relying on the Warrior's CurrentplatForm bookkeeping which can lag a frame at launch
        // (the cause of the "blocks only every other launch" race).
        Collider2D ground = FindGroundCollider();
        if (ground == null)
        {
            var plat = _warrior.CurrentplatForm ?? _warrior.LastSafePlatform;
            if (plat != null) ground = plat.platformCollider;
        }
        if (ground == null)
        {
            Log("Confinement skipped: no ground collider found under the Warrior.");
            return;
        }

        Bounds b = ground.bounds;
        float startX = _warrior.transform.position.x;

        // Inner faces of the walls. Never closer than the move targets (+ margin) so the required
        // left/right travel is always reachable — no soft-lock.
        float reach = requiredMoveDistance + 0.5f;
        float leftFace = Mathf.Min(b.min.x + confineInset, startX - reach);
        float rightFace = Mathf.Max(b.max.x - confineInset, startX + reach);

        // Solid (non-effector) walls on the platform's layer, which the Warrior collides with.
        // Big outward blocks (WallDepth) so fast moves cannot tunnel through; NOT parented to the
        // platform so a platform scale != 1 can't distort the collider size.
        int layer = ground.gameObject.layer;
        float top = b.max.y + wallHeight;
        float bottom = b.min.y - 1f;
        float h = top - bottom;
        float cy = (top + bottom) * 0.5f;

        _wallLeft = CreateWall("TutorialWall_L", new Vector2(leftFace - WallDepth * 0.5f, cy), new Vector2(WallDepth, h), layer, null, StopWarriorAtWall);
        _wallRight = CreateWall("TutorialWall_R", new Vector2(rightFace + WallDepth * 0.5f, cy), new Vector2(WallDepth, h), layer, null, StopWarriorAtWall);

        Log($"Confinement walls built: innerL={leftFace:F2}, innerR={rightFace:F2}, depth={WallDepth}, layer='{LayerMask.LayerToName(layer)}'.");
    }

    // Deterministically find the platform collider the Warrior is standing on, by raycasting down
    // on the "PlatformLayer" mask (the Warrior is not on that layer, so it is ignored by the cast).
    private Collider2D FindGroundCollider()
    {
        if (_warriorCollider == null)
            return null;

        int mask = LayerMask.GetMask("PlatformLayer");
        if (mask == 0)
            return null;

        Bounds wb = _warriorCollider.bounds;
        Vector2 origin = wb.center;
        float distance = wb.extents.y + 1.5f;

        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, distance, mask);
        if (hit.collider != null)
            return hit.collider;

        // Backup: a thin overlap box right under the feet.
        return Physics2D.OverlapBox(new Vector2(wb.center.x, wb.min.y - 0.1f), new Vector2(wb.size.x * 0.8f, 0.3f), 0f, mask);
    }

    private static GameObject CreateWall(string wallName, Vector2 center, Vector2 size, int layer, Transform parent, System.Action onWarriorHit)
    {
        var go = new GameObject(wallName);
        go.layer = layer;
        if (parent != null)
            go.transform.SetParent(parent, worldPositionStays: true);
        go.transform.position = center;
        var box = go.AddComponent<BoxCollider2D>();
        box.size = size;
        go.AddComponent<TutorialWallStopper>().onWarriorHit = onWarriorHit;
        return go;
    }

    // Called by the walls when the Warrior runs/jumps into them: stop his run AND his jump so he
    // doesn't keep pushing against the wall (run-in-place / hanging on the wall mid-jump).
    private void StopWarriorAtWall()
    {
        if (!_running || _warrior == null)
            return;

        _warrior.StopMoveTowardCoroutine();
        _warrior.StopJumpTowardCoroutine();   // jump into wall → cut the jump, let gravity drop him

        if (_warrior.CountGroundPoints() > 0)
            _warrior.WaitAnimationDisplay();
    }

    private void DestroyConfinementWalls()
    {
        if (_wallLeft != null) Destroy(_wallLeft);
        if (_wallRight != null) Destroy(_wallRight);
        _wallLeft = null;
        _wallRight = null;
    }

    private void EnterStep(Step step)
    {
        _step = step;

        switch (step)
        {
            case Step.Left:
                _stepStartX = _warrior.transform.position.x;
                ShowHandAtWorldTarget(new Vector3(_stepStartX - requiredMoveDistance, WarriorCenterY(), 0f));
                break;

            case Step.Right:
                _stepStartX = _warrior.transform.position.x;
                ShowHandAtWorldTarget(new Vector3(_stepStartX + requiredMoveDistance, WarriorCenterY(), 0f));
                break;

            case Step.Jump:
                _jumpSawGrounded = false;
                ShowHandAboveWarrior(new Vector2(0f, handJumpRiseY));
                break;

            case Step.Attack:
                EnsureAttackButtonRect();
                SetActiveSafe(attackHint, true);   // optional legacy hint, if assigned
                if (_attackButtonRect != null)
                    ShowHandOverButton();
                else
                {
                    _handActive = false;
                    ShowHand(false);
                    Log("Attack step: no attack button found — hand hidden (validation still works on press).");
                }
                break;

            case Step.Done:
                CompleteTutorial();
                break;
        }

        Log($"Entered step {step}.");
    }

    private void HandleAttackButtonPressed()
    {
        if (_running && _step == Step.Attack)
            EnterStep(Step.Done);
    }

    private void CompleteTutorial()
    {
        _running = false;
        TutorialActive = false;
        _handActive = false;
        DestroyConfinementWalls();

        ShowHand(false);
        SetActiveSafe(attackHint, false);

        RestoreDisabledEnemies();

        GameMgr.Instance?.MarkTutorialCompleted();

        ButtonClickHandler.OnAttackButtonPressed -= HandleAttackButtonPressed;

        // Fade the training halo out as a "lights come back on" beat (coroutine survives enabled=false).
        if (_halo != null)
            StartCoroutine(FadeOutHalo());

        enabled = false;

        Debug.Log("[Tutorial] All steps validated — gameplay unlocked, tutorial marked complete.");
    }

    private IEnumerator FadeOutHalo()
    {
        float startAlpha = _haloGroup != null ? _haloGroup.alpha : 1f;
        float t = 0f;
        while (t < haloFadeOutDuration && _halo != null)
        {
            t += Time.deltaTime;
            if (_haloGroup != null)
                _haloGroup.alpha = Mathf.Lerp(startAlpha, 0f, t / haloFadeOutDuration);
            yield return null;
        }

        if (_halo != null)
            Destroy(_halo.gameObject);
        _halo = null;
    }

    // ─── Hand lifecycle / positioning ────────────────────────────────────────────

    private Canvas ResolveCanvas()
    {
        Canvas canvas = hostCanvas;
        if (canvas == null && attackHint != null)
            canvas = attackHint.GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        return canvas;
    }

    private void SpawnHand()
    {
        if (handPrefab == null || _canvas == null)
        {
            Log($"SpawnHand FAILED: handPrefab={(handPrefab != null)}, canvas={(_canvas != null)} — assign a hand prefab and/or a host canvas.");
            return;
        }

        GameObject go = Instantiate(handPrefab);
        go.transform.SetParent(_canvas.transform, worldPositionStays: false);

        _hand = go.GetComponent<RectTransform>();
        if (_hand == null)
            _hand = go.AddComponent<RectTransform>();

        // Center-anchor so anchoredPosition is a plain canvas-local position, and put it on top.
        _hand.anchorMin = _hand.anchorMax = new Vector2(0.5f, 0.5f);
        _hand.pivot = new Vector2(0.5f, 0.5f);
        _hand.localScale = Vector3.one * handScale;
        _hand.SetAsLastSibling();

        _handParent = _canvas.GetComponent<RectTransform>();
        ShowHand(false);

        Log($"Spawned hand under canvas '{_canvas.name}' (order={_canvas.sortingOrder}).");
    }

    private void SpawnHalo()
    {
        if (haloPrefab == null || _canvas == null)
            return;   // halo is optional

        GameObject go = Instantiate(haloPrefab);
        go.transform.SetParent(_canvas.transform, worldPositionStays: false);

        _halo = go.GetComponent<RectTransform>();
        if (_halo == null)
            _halo = go.AddComponent<RectTransform>();

        if (haloFollowsWarrior)
        {
            _halo.anchorMin = _halo.anchorMax = new Vector2(0.5f, 0.5f);
            _halo.pivot = new Vector2(0.5f, 0.5f);
        }

        // Behind the hand/HUD, but the Overlay canvas still keeps it above the gameplay world.
        _halo.SetAsFirstSibling();

        _haloGroup = go.GetComponent<CanvasGroup>();
        if (_haloGroup == null)
            _haloGroup = go.AddComponent<CanvasGroup>();
        _haloGroup.blocksRaycasts = false;   // never intercept taps
        _haloGroup.interactable = false;

        Log($"Spawned training halo (follows={haloFollowsWarrior}).");
    }

    // Pin the hand to a FIXED world destination (left/right steps): the hand stays put, the player
    // taps it once, the Warrior walks there and the step validates — no chasing / double tap.
    private void ShowHandAtWorldTarget(Vector3 worldTarget)
    {
        _handUseWorldTarget = true;
        _handOverButton = false;
        _handWorldTarget = worldTarget;
        BeginHand();
    }

    private void ShowHandOverButton()
    {
        _handUseWorldTarget = false;
        _handOverButton = true;
        BeginHand();
    }

    private void EnsureAttackButtonRect()
    {
        if (_attackButtonRect != null)
            return;

        if (attackButton != null)
        {
            _attackButtonRect = attackButton;
            return;
        }

        var bch = FindFirstObjectByType<ButtonClickHandler>();
        if (bch != null)
            _attackButtonRect = bch.GetComponent<RectTransform>();
    }

    // Park the hand above the Warrior (jump step); a single tap-jump validates so a moving anchor
    // is fine here.
    private void ShowHandAboveWarrior(Vector2 screenOffset)
    {
        _handUseWorldTarget = false;
        _handOverButton = false;
        _handScreenOffset = screenOffset;
        BeginHand();
    }

    private void BeginHand()
    {
        SetActiveSafe(attackHint, false);
        _handActive = true;

        if (_hand == null)
            return;

        ShowHand(true);
        _hand.SetAsLastSibling();
        PositionHand();
    }

    private void PositionHand()
    {
        if (_hand == null || _handParent == null)
            return;

        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
            return;

        Vector2 screen;
        if (_handOverButton && _attackButtonRect != null)
        {
            // Over the attack button (a UI element): for an Overlay canvas its world position maps
            // 1:1 to screen, so use a null UI camera for the conversion.
            screen = RectTransformUtility.WorldToScreenPoint(null, _attackButtonRect.position);
            screen.y += handAttackOffsetY;
        }
        else if (_handUseWorldTarget)
        {
            // Fixed ground-level destination (captured at step start). No upward lift, so the tap
            // reads as a walk, not a jump.
            screen = RectTransformUtility.WorldToScreenPoint(cam, _handWorldTarget);
        }
        else
        {
            screen = RectTransformUtility.WorldToScreenPoint(cam, _warrior.transform.position);
            screen += _handScreenOffset;
        }

        // Keep the hand on screen.
        float m = handScreenMargin;
        screen.x = Mathf.Clamp(screen.x, m, Screen.width - m);
        screen.y = Mathf.Clamp(screen.y, m, Screen.height - m);

        // Overlay canvas → pass null as the UI camera.
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_handParent, screen, null, out Vector2 local))
            _hand.anchoredPosition = local;
    }

    private void ShowHand(bool visible)
    {
        if (_hand != null)
            SetActiveSafe(_hand.gameObject, visible);
    }

    private void PositionHaloOnWarrior()
    {
        if (_halo == null || _handParent == null)
            return;

        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
            return;

        Vector3 center = _warriorCollider != null ? _warriorCollider.bounds.center : _warrior.transform.position;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, center);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_handParent, screen, null, out Vector2 local))
            _halo.anchoredPosition = local;
    }

    // ─── Validation helpers ──────────────────────────────────────────────────────

    // Warrior's visual center Y (collider center). Always below the collider top, so a tap there
    // still reads as a walk, not a jump. Falls back to the transform position if no collider.
    private float WarriorCenterY()
    {
        return _warriorCollider != null ? _warriorCollider.bounds.center.y : _warrior.transform.position.y;
    }

    // Validate the jump by detecting the Warrior leaving the ground (after confirming it was
    // grounded first). Robust against geometry — any actual jump completes the step.
    private bool HasJumped()
    {
        bool grounded = _warrior.CountGroundPoints() > 0;

        if (!_jumpSawGrounded)
        {
            _jumpSawGrounded = grounded;
            return false;
        }

        return !grounded;
    }

    // ─── Enemy freeze ────────────────────────────────────────────────────────────

    private void FreezeEnemies()
    {
        _disabledEnemies.Clear();

        // Disable spawn points first: that stops future spawns and deactivates any enemy already
        // parented under them.
        foreach (var sp in FindObjectsByType<EnemySpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (sp == null) continue;
            GameObject go = sp.gameObject;
            if (!go.activeSelf) continue;

            _disabledEnemies.Add(go);
            go.SetActive(false);
        }

        // Catch any directly-placed enemy that isn't owned by a spawn point.
        foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (enemy == null) continue;
            GameObject go = enemy.gameObject;
            if (!go.activeSelf) continue;
            if (_disabledEnemies.Contains(go)) continue;

            _disabledEnemies.Add(go);
            go.SetActive(false);
        }

        Log($"Froze {_disabledEnemies.Count} enemy spawners/enemies for the duration of the tutorial.");
    }

    private void RestoreDisabledEnemies()
    {
        foreach (var go in _disabledEnemies)
        {
            if (go != null && !go.activeSelf)
                go.SetActive(true);
        }
        _disabledEnemies.Clear();
    }

    private static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }

    private void Log(string msg)
    {
        if (debugLogs)
            Debug.Log($"[Tutorial] {msg}", this);
    }
}

/// <summary>
/// Added at runtime to each confinement wall. Notifies the tutorial controller when the Warrior
/// collides with the wall so it can stop his run/jump (otherwise he keeps pushing into it and
/// replaying the run animation).
/// </summary>
public class TutorialWallStopper : MonoBehaviour
{
    public System.Action onWarriorHit;

    private void OnCollisionEnter2D(Collision2D collision) => Notify(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => Notify(collision.collider);

    private void Notify(Collider2D other)
    {
        if (other == null) return;
        if (other.GetComponentInParent<Assets.Scripts.Characteres.WarriorController.Warrior>() != null)
            onWarriorHit?.Invoke();
    }
}

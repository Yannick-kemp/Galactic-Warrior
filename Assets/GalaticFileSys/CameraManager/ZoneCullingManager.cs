using System.Collections.Generic;
using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

/// <summary>
/// Central zone culler. At a fixed interval (not every frame) it gets the camera's visible world
/// rectangle from CameraMgr and activates/deactivates each ZoneCullable using hysteresis margins:
/// a zone is activated when it comes within <see cref="enterMarginScreens"/> of the viewport, and
/// only deactivated once it is past the larger <see cref="exitMarginScreens"/> — so a zone hovering
/// on the border never flickers.
///
/// The zone containing the Warrior is never deactivated (extra guard on top of the fact that the
/// camera is centered on him, so his zone always overlaps the viewport). Managers and the Warrior
/// live outside any zone, so they are never touched.
/// </summary>
public class ZoneCullingManager : MonoBehaviour
{
    [Header("Check cadence")]
    [Tooltip("Seconds between culling checks. Not every frame: ~0.1-0.2s is visually invisible and far cheaper.")]
    [SerializeField, Min(0f)] private float cullingCheckInterval = 0.15f;

    [Header("Hysteresis margins (fraction of a screen, added on each side)")]
    [Tooltip("Activate a zone when it comes within this fraction of a screen of the viewport. 0.5 = half a screen early.")]
    [SerializeField, Min(0f)] private float enterMarginScreens = 0.5f;

    [Tooltip("Deactivate a zone only once it is this fraction of a screen beyond the viewport. MUST be > enter to avoid flicker. 1 = one screen late.")]
    [SerializeField, Min(0f)] private float exitMarginScreens = 1.0f;

    [Header("Refs (optional)")]
    [Tooltip("Warrior transform for the 'never cull the Warrior's zone' guard. Falls back to GameMgr.WarriorInstance.")]
    [SerializeField] private Transform warriorOverride;

    [Header("Debug")]
    [Tooltip("Draw the viewport rect (green), the activate margin (yellow) and the deactivate margin (red) in the Scene view.")]
    [SerializeField] private bool drawDebugGizmos = true;

    private readonly List<ZoneCullable> _zones = new List<ZoneCullable>();
    private float _timer;

    public static ZoneCullingManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Immediately activates any zone whose bounds contain <paramref name="worldPoint"/>. Call this
    /// just before teleporting / respawning the Warrior into a position that may sit in a currently
    /// culled (deactivated) zone: it brings that zone's platform colliders back to life so the
    /// Warrior lands on a live surface instead of dropping through a disabled one. The regular cull
    /// pass then keeps the zone active while he stands in it (the "never cull the Warrior's zone"
    /// guard), so this is a safe one-shot.
    /// </summary>
    public void EnsureZoneActiveAt(Vector2 worldPoint)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            ZoneCullable z = _zones[i];
            if (z != null && z.ContainsPoint(worldPoint))
                z.SetZoneActive(true);
        }
    }

    private void Start()
    {
        // Zones start active, so this finds them all (Include also catches any that start inactive,
        // though those will have no cached bounds and are kept always-on by the manager).
        _zones.Clear();
        _zones.AddRange(FindObjectsByType<ZoneCullable>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        // Immediate first pass so off-screen zones turn off right away instead of after one interval.
        CullCheck();
    }

    private void Update()
    {
        // Unscaled so culling keeps working during slow-motion / pauses that scale Time.
        _timer += Time.unscaledDeltaTime;
        if (_timer < cullingCheckInterval)
            return;

        _timer = 0f;
        CullCheck();
    }

    private void CullCheck()
    {
        if (CameraMgr.Instance == null)
            return;

        if (!CameraMgr.Instance.TryGetVisibleWorldRect(out Rect camRect))
            return;

        // exit margin must never be smaller than enter margin, or the hysteresis inverts and flickers.
        float exitMargin = Mathf.Max(exitMarginScreens, enterMarginScreens);

        Rect enterRect = Expand(camRect, enterMarginScreens);
        Rect exitRect = Expand(camRect, exitMargin);

        Transform warrior = GetWarrior();

        // Until the Warrior is registered/positioned (e.g. the very first cull pass at scene load,
        // before GameMgr.RegisterHero has placed him and snapped the camera), never cull anything.
        // Otherwise the zone holding his spawn / landing platform could be deactivated for one
        // interval and he would spawn onto a disabled platform collider and fall/tunnel. Keeping
        // every zone active for this pass is strictly safe; normal culling resumes once he exists.
        if (warrior == null)
        {
            for (int i = 0; i < _zones.Count; i++)
                if (_zones[i] != null)
                    _zones[i].SetZoneActive(true);
            return;
        }

        Vector2 warriorPos = (Vector2)warrior.position;

        for (int i = 0; i < _zones.Count; i++)
        {
            ZoneCullable z = _zones[i];
            if (z == null)
                continue;

            if (z.NeverCull)
            {
                z.SetZoneActive(true);
                continue;
            }

            if (!z.TryGetWorldRect(out Rect zoneRect))
            {
                // No usable bounds -> keep it on rather than risk hiding live content.
                z.SetZoneActive(true);
                continue;
            }

            // Never deactivate the zone the Warrior is standing in.
            if (z.ContainsPoint(warriorPos))
            {
                z.SetZoneActive(true);
                continue;
            }

            if (z.IsZoneActive)
            {
                // Active -> deactivate only once fully outside the (larger) exit rect.
                if (!exitRect.Overlaps(zoneRect))
                    z.SetZoneActive(false);
            }
            else
            {
                // Inactive -> activate as soon as it touches the (smaller) enter rect.
                if (enterRect.Overlaps(zoneRect))
                    z.SetZoneActive(true);
            }
        }
    }

    private Transform GetWarrior()
    {
        if (warriorOverride != null)
            return warriorOverride;

        Warrior w = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
        return w != null ? w.transform : null;
    }

    private static Rect Expand(Rect r, float marginScreens)
    {
        float mx = r.width * marginScreens;
        float my = r.height * marginScreens;
        return new Rect(r.xMin - mx, r.yMin - my, r.width + 2f * mx, r.height + 2f * my);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos)
            return;

        if (!TryGetCameraRectForGizmo(out Rect camRect))
            return;

        float exitMargin = Mathf.Max(exitMarginScreens, enterMarginScreens);
        Rect enterRect = Expand(camRect, enterMarginScreens);
        Rect exitRect = Expand(camRect, exitMargin);

        // Red = a zone fully outside this is deactivated (exit margin, hysteresis outer band).
        DrawRectGizmo(exitRect, new Color(1f, 0.3f, 0.3f, 1f));
        // Yellow = a zone touching this is activated (enter margin, hysteresis inner band).
        DrawRectGizmo(enterRect, new Color(1f, 0.9f, 0.2f, 1f));
        // Green = the actual camera viewport.
        DrawRectGizmo(camRect, new Color(0.3f, 1f, 0.4f, 1f));

        DrawZoneStateGizmos();
    }

    // Draw every zone's bounds tinted by its active/inactive state. Driven from the manager (always
    // active) so culled zones are still shown -- Unity never calls OnDrawGizmos on inactive objects.
    private void DrawZoneStateGizmos()
    {
        ZoneCullable[] zones =
            (Application.isPlaying && _zones.Count > 0)
                ? _zones.ToArray()
                : FindObjectsByType<ZoneCullable>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (zones == null)
            return;

        Color activeFill = new Color(0.2f, 1f, 0.3f, 0.12f);
        Color activeLine = new Color(0.2f, 1f, 0.3f, 0.9f);
        Color inactiveFill = new Color(1f, 0.25f, 0.25f, 0.12f);
        Color inactiveLine = new Color(1f, 0.25f, 0.25f, 0.9f);

        for (int i = 0; i < zones.Length; i++)
        {
            ZoneCullable z = zones[i];
            if (z == null || !z.TryGetGizmoRect(out Rect zr))
                continue;

            bool active = z.IsZoneActive;
            Vector3 center = new Vector3(zr.center.x, zr.center.y, 0f);
            Vector3 size = new Vector3(zr.width, zr.height, 0.01f);

            Gizmos.color = active ? activeFill : inactiveFill;
            Gizmos.DrawCube(center, size);

            DrawRectGizmo(zr, active ? activeLine : inactiveLine);
        }
    }

    // Works in edit mode too: uses CameraMgr while playing, else Camera.main directly.
    private bool TryGetCameraRectForGizmo(out Rect rect)
    {
        rect = default;

        if (Application.isPlaying && CameraMgr.Instance != null)
            return CameraMgr.Instance.TryGetVisibleWorldRect(out rect);

        Camera cam = Camera.main;
        if (cam == null)
            return false;

        Vector3 c = cam.transform.position;
        if (cam.orthographic)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            rect = new Rect(c.x - halfW, c.y - halfH, halfW * 2f, halfH * 2f);
            return true;
        }

        return false;
    }

    private static void DrawRectGizmo(Rect r, Color color)
    {
        Gizmos.color = color;
        Vector3 bl = new Vector3(r.xMin, r.yMin, 0f);
        Vector3 br = new Vector3(r.xMax, r.yMin, 0f);
        Vector3 tr = new Vector3(r.xMax, r.yMax, 0f);
        Vector3 tl = new Vector3(r.xMin, r.yMax, 0f);

        Gizmos.DrawLine(bl, br);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tr, tl);
        Gizmos.DrawLine(tl, bl);
    }
#endif
}

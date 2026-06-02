using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class IgnoreSafeAreaLeftInset : MonoBehaviour
{
    [Tooltip("Authored anchoredPosition.x BEFORE any safe-area compensation. This is the " +
             "fixed design value. Apply() never overwrites it, so the position cannot drift.")]
    [SerializeField] private float baseAnchoredX = 0f;

    [Tooltip("Tick this in the editor to (re)capture baseAnchoredX from the CURRENT " +
             "anchoredPosition.x. It auto-clears after one capture. Use it once to seed the " +
             "design value, then leave it off.")]
    [SerializeField] private bool captureBaseFromCurrent = false;

    [Tooltip("Extra padding from the true left edge (UI units).")]
    public float extraLeftPadding = 8f;

    RectTransform _rt;
    Canvas _canvas;

    Rect _lastSafeArea;
    Vector2Int _lastScreenSize;
    ScreenOrientation _lastOrientation;

    void OnEnable()
    {
        _rt = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        Apply();
    }

    void Start() => Apply();

    void OnRectTransformDimensionsChange() => Apply();

    void Update()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Apply();
            return;
        }
#endif
        // Re-snap on orientation / size / safe-area changes (e.g. 180° flip),
        // in case the parent resize callback doesn't propagate down to us.
        if (Screen.safeArea != _lastSafeArea ||
            _lastScreenSize.x != Screen.width ||
            _lastScreenSize.y != Screen.height ||
            _lastOrientation != Screen.orientation)
        {
            Apply();
        }
    }

    void Apply()
    {
        if (_rt == null) _rt = GetComponent<RectTransform>();
        if (_rt == null) return;
        if (_canvas == null) _canvas = GetComponentInParent<Canvas>();

#if UNITY_EDITOR
        // One-shot manual capture only. Apply() never auto-captures the base from the
        // live transform, which is what previously let the X value compound every run.
        if (captureBaseFromCurrent)
        {
            baseAnchoredX = _rt.anchoredPosition.x;
            captureBaseFromCurrent = false;
        }
#endif

        _lastSafeArea = Screen.safeArea;
        _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        _lastOrientation = Screen.orientation;

        float scale = (_canvas != null) ? _canvas.scaleFactor : 1f;
        float leftInsetUiUnits = Screen.safeArea.xMin / Mathf.Max(0.0001f, scale);

        // Always computed from the FIXED authored base, never from the mutated transform.
        float targetX = baseAnchoredX - leftInsetUiUnits + extraLeftPadding;

        Vector2 p = _rt.anchoredPosition;
        if (!Mathf.Approximately(p.x, targetX))
        {
            p.x = targetX;
            _rt.anchoredPosition = p;
        }
    }
}

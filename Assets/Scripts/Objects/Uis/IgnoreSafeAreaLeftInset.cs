using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class IgnoreSafeAreaLeftInset : MonoBehaviour
{
    [Tooltip("Extra padding from the true left edge (UI units).")]
    public float extraLeftPadding = 8f;

    RectTransform _rt;
    Canvas _canvas;
    Vector2 _baseAnchoredPos;
    bool _captured;

    Rect _lastSafeArea;
    Vector2Int _lastScreenSize;
    ScreenOrientation _lastOrientation;

    void OnEnable()
    {
        _rt = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        CaptureBase();
        Apply();
    }

    void Start()
    {
        CaptureBase();
        Apply();
    }

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

    void CaptureBase()
    {
        if (_rt == null) return;
        if (_captured) return;
        _baseAnchoredPos = _rt.anchoredPosition;
        _captured = true;
    }

    void Apply()
    {
        if (_rt == null) return;
        if (_canvas == null) _canvas = GetComponentInParent<Canvas>();

        _lastSafeArea = Screen.safeArea;
        _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        _lastOrientation = Screen.orientation;

        float scale = (_canvas != null) ? _canvas.scaleFactor : 1f;
        float leftInsetUiUnits = Screen.safeArea.xMin / Mathf.Max(0.0001f, scale);

        // shift left by the safe area inset, so it's closer to the real screen edge
        var p = _baseAnchoredPos;
        p.x = _baseAnchoredPos.x - leftInsetUiUnits + extraLeftPadding;

        _rt.anchoredPosition = p;
    }
}
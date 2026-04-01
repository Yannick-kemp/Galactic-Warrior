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

#if UNITY_EDITOR
    void Update()
    {
        if (!Application.isPlaying) Apply();
    }
#endif

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

        float scale = (_canvas != null) ? _canvas.scaleFactor : 1f;
        float leftInsetUiUnits = Screen.safeArea.xMin / Mathf.Max(0.0001f, scale);

        // shift left by the safe area inset, so it's closer to the real screen edge
        var p = _baseAnchoredPos;
        p.x = _baseAnchoredPos.x - leftInsetUiUnits + extraLeftPadding;

        _rt.anchoredPosition = p;
    }
}
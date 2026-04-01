using UnityEngine;

public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform _rt;
    private Rect _lastSafeArea;
    private Vector2Int _lastScreenSize;
    private ScreenOrientation _lastOrientation;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        if (Screen.safeArea != _lastSafeArea ||
            _lastScreenSize.x != Screen.width ||
            _lastScreenSize.y != Screen.height ||
            _lastOrientation != Screen.orientation)
        {
            Apply();
        }
    }

    private void Apply()
    {
        _lastSafeArea = Screen.safeArea;
        _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        _lastOrientation = Screen.orientation;

        Vector2 min = _lastSafeArea.position;
        Vector2 max = _lastSafeArea.position + _lastSafeArea.size;

        min.x /= Screen.width;
        min.y /= Screen.height;
        max.x /= Screen.width;
        max.y /= Screen.height;

        _rt.anchorMin = min;
        _rt.anchorMax = max;
        _rt.offsetMin = Vector2.zero;
        _rt.offsetMax = Vector2.zero;
    }
}
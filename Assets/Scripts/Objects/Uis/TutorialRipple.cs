using UnityEngine;
using UnityEngine.UI;

public class TutorialRipple : MonoBehaviour
{
    public float speed = 2f;
    public float minScale = 0.8f;
    public float maxScale = 1.3f;

    private RectTransform _rt;
    private Image _img;
    private float _baseAlpha;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _img = GetComponent<Image>();
        _baseAlpha = _img != null ? _img.color.a : 1f;
    }

    void Update()
    {
        float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f; // 0..1
        float s = Mathf.Lerp(minScale, maxScale, t);
        _rt.localScale = Vector3.one * s;

        // Optional: fade a bit with the pulse
        if (_img != null)
        {
            var c = _img.color;
            c.a = Mathf.Lerp(0.15f, 0.5f, t);
            _img.color = c;
        }
    }
}
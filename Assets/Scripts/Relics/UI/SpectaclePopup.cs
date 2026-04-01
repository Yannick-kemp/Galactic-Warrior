using TMPro;
using UnityEngine;

namespace Assets.Scripts.UI
{
    public class SpectaclePopup : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Motion")]
        [SerializeField] private float lifetime = 1.1f;
        [SerializeField] private float riseSpeed = 60f;      // UI units per second
        [SerializeField] private float driftX = 18f;         // small horizontal drift
        [SerializeField] private float startScale = 1.05f;
        [SerializeField] private float endScale = 0.95f;

        private RectTransform _rt;
        private float _t;
        private float _xDir;

        private void Awake()
        {
            _rt = transform as RectTransform;

            if (text == null) text = GetComponentInChildren<TMP_Text>(true);
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

            _xDir = Random.value < 0.5f ? -1f : 1f;
        }

        public void Init(int points, string label)
        {
            if (text != null)
                text.text = $"+{points}  {label}";

            _t = 0f;

            if (canvasGroup != null)
                canvasGroup.alpha = 1f;

            if (_rt != null)
                _rt.localScale = Vector3.one * startScale;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float u = (lifetime <= 0.0001f) ? 1f : Mathf.Clamp01(_t / lifetime);

            // rise + slight drift
            if (_rt != null)
            {
                Vector2 p = _rt.anchoredPosition;
                p.y += riseSpeed * Time.deltaTime;
                p.x += _xDir * driftX * Time.deltaTime;
                _rt.anchoredPosition = p;

                _rt.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, u);
            }

            // fade out
            if (canvasGroup != null)
                canvasGroup.alpha = 1f - u;

            if (_t >= lifetime)
                Destroy(gameObject);
        }
    }
}
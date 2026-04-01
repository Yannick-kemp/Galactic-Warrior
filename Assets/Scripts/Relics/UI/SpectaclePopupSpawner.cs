using Assets.Scripts.Scoring;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Scripts.UI
{
    public class SpectaclePopupSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private Camera worldCamera; // gameplay camera used for WorldToScreenPoint
        [SerializeField] private SpectaclePopup popupPrefab;

        [Header("Placement")]
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, 40f);
        [SerializeField] private bool clampToScreen = true;
        [SerializeField] private float screenPadding = 24f;

        private RectTransform _canvasRt;

        private void Awake()
        {
            RebindReferences();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Rebind again in case object persisted across scenes
            RebindReferences();

            if (ScoreManager.Instance != null)
                ScoreManager.Instance.OnPointsAdded += HandlePointsAdded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (ScoreManager.Instance != null)
                ScoreManager.Instance.OnPointsAdded -= HandlePointsAdded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Camera.main / canvas may be different after reload
            RebindReferences();
        }

        private void RebindReferences()
        {
            if (canvas == null)
                canvas = GetComponentInParent<Canvas>();

            if (canvas != null)
                _canvasRt = canvas.transform as RectTransform;
            else
                _canvasRt = null;

            // Reacquire gameplay camera if needed (or if previous got destroyed)
            if (worldCamera == null)
                worldCamera = Camera.main;

            // Optional fallback: if canvas is ScreenSpaceCamera and has a camera assigned
            if (worldCamera == null && canvas != null && canvas.worldCamera != null)
                worldCamera = canvas.worldCamera;
        }

        private void HandlePointsAdded(int added, int total, string label, Vector2 worldPos)
        {
            if (added <= 0) return;
            if (popupPrefab == null) return;

            // Rebind lazily too (important after respawn/reload order changes)
            if (canvas == null || _canvasRt == null || worldCamera == null)
                RebindReferences();

            if (canvas == null || _canvasRt == null)
            {
                Debug.LogWarning("[SpectaclePopupSpawner] Missing Canvas / Canvas RectTransform.");
                return;
            }

            // We need a REAL world camera to convert worldPos -> screenPos
            if (worldCamera == null)
            {
                Debug.LogWarning("[SpectaclePopupSpawner] worldCamera is null. Popup skipped.");
                return;
            }

            // 1) World -> Screen (using gameplay/world camera)
            Vector2 screen = worldCamera.WorldToScreenPoint(worldPos);
            screen += screenOffset;

            if (clampToScreen)
            {
                screen.x = Mathf.Clamp(screen.x, screenPadding, Screen.width - screenPadding);
                screen.y = Mathf.Clamp(screen.y, screenPadding, Screen.height - screenPadding);
            }

            // 2) Screen -> Canvas local
            // For ScreenSpaceOverlay => null
            // For ScreenSpaceCamera / WorldSpace => prefer canvas.worldCamera, fallback to worldCamera
            Camera uiEventCamera = null;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                uiEventCamera = (canvas.worldCamera != null) ? canvas.worldCamera : worldCamera;

            bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt,
                screen,
                uiEventCamera,
                out Vector2 localPoint
            );

            if (!ok)
            {
                Debug.LogWarning("[SpectaclePopupSpawner] Screen->Canvas conversion failed. Popup skipped.");
                return;
            }

            var popup = Instantiate(popupPrefab, _canvasRt);

            var rt = popup.transform as RectTransform;
            if (rt != null)
            {
                rt.anchoredPosition = localPoint;
                rt.localScale = Vector3.one; // defensive reset
            }

            popup.Init(added, label); // show delta, not total
        }
    }
}
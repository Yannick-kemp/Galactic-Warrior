using Assets.Scripts.Scoring;
using System.Collections;
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
        private bool _bound;

        private void Awake()
        {
            RebindReferences();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Rebind again in case object persisted across scenes
            RebindReferences();

            // ScoreManager.Instance may not exist yet when this scene is played in isolation
            // (init order between _APP and _GameTools is not guaranteed). The old one-shot
            // "if (Instance != null) subscribe" silently failed in that case → no popups at all
            // while the score still rose. Wait for the singleton like TotalScoreText does.
            StartCoroutine(BindWhenReady());
        }

        private IEnumerator BindWhenReady()
        {
            while (ScoreManager.Instance == null)
                yield return null;

            if (!_bound)
            {
                ScoreManager.Instance.OnPointsAdded += HandlePointsAdded;
                _bound = true;
            }
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (_bound && ScoreManager.Instance != null)
                ScoreManager.Instance.OnPointsAdded -= HandlePointsAdded;

            _bound = false;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Camera.main / canvas may be different after reload
            RebindReferences();

            // Defensive: if we never managed to bind (spawner enabled before ScoreManager
            // existed), retry now that another scene — and its managers — are up.
            if (!_bound)
                StartCoroutine(BindWhenReady());
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
            SpawnPopup(popup => popup.Init(added, label), worldPos);
        }

        /// <summary>Reuse the score-popup motion for arbitrary text at a world position
        /// (e.g. the obligatory "RETRY +1" feedback). Public so RetryRewardManager can call it
        /// without duplicating the world→canvas placement logic.</summary>
        public void SpawnTextAt(string text, Vector2 worldPos)
        {
            SpawnPopup(popup => popup.InitText(text), worldPos);
        }

        private void SpawnPopup(System.Action<SpectaclePopup> initialize, Vector2 worldPos)
        {
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

            initialize(popup); // score delta or custom text, set by the caller
        }
    }
}
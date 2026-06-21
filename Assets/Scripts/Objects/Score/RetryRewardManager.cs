using Assets.Scripts.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Scripts.Scoring
{
    /// <summary>
    /// Retry-reward feature — a PURE ADDITION on top of the existing systems.
    ///
    /// It observes the score (read-only, via the existing <see cref="ScoreManager.OnPointsAdded"/>
    /// event) and the retry system (via the additive <see cref="GameMgr.OnRetryConsumed"/> event +
    /// <see cref="GameMgr.GrantRetryReward"/> method). It NEVER modifies score calculation,
    /// accumulation, display, reset, existing events or score persistence.
    ///
    /// Rule (model A — "redeem credit"):
    ///  • Every <see cref="pointsPerRetry"/> points earned → +1 redeem token.
    ///  • Tokens are only ever spent to buy back a previously-lost retry; the retry count never
    ///    exceeds the maximum (GameMgr.GrantRetryReward clamps it).
    ///  • Tokens earned while already at max stay in reserve and buy the next loss back at once.
    ///  • Each retry actually restored plays the obligatory "retry gained" animation.
    ///
    /// Lifecycle: score and retries are per-run (both reset by GameMgr.StartNewRun, which raises
    /// OnPointsAdded with the "Reset" label). The reward state shares that exact lifecycle, so it
    /// is reset on the "Reset" event and needs no disk persistence.
    /// </summary>
    public class RetryRewardManager : MonoBehaviour
    {
        public static RetryRewardManager Instance { get; private set; }

        [Header("Retry Reward")]
        [Tooltip("Points nécessaires pour gagner un jeton de rachat de retry.")]
        [SerializeField] private int pointsPerRetry = 400;

        [Header("Animation (obligatoire)")]
        [Tooltip("Texte du popup flottant joué à chaque retry effectivement racheté.")]
        [SerializeField] private string retryGainedText = "RETRY +1";
        [Tooltip("Spawner de popup réutilisé pour l'animation. Laissé vide → résolu automatiquement à l'exécution.")]
        [SerializeField] private SpectaclePopupSpawner popupSpawner;
        [Tooltip("VFX joué une fois à chaque retry racheté, en plus du popup (ex. HearthAnimation.prefab). Optionnel.")]
        [SerializeField] private GameObject retryGainedVfxPrefab;
        [Tooltip("Durée de vie de secours du VFX si aucun ParticleSystem n'est trouvé pour la déduire.")]
        [SerializeField] private float retryGainedVfxFallbackLifetime = 2f;

        // Redeem-credit state (per-run, mirrors the score/retry lifecycle — no disk persistence).
        private int _redeemTokens;
        private long _lastRewardThreshold;

        // Subscription guards (sources are persistent singletons; subscribe once each).
        private bool _subscribedToScore;
        private bool _subscribedToRetry;

        private int PointsPerRetry => Mathf.Max(1, pointsPerRetry); // guard against div/loop on 0

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySubscribe();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Unsubscribe();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The persistent singletons may come online after us on first load; keep trying.
            // The per-scene popup spawner is re-resolved lazily when needed.
            TrySubscribe();
            popupSpawner = null;
        }

        private void TrySubscribe()
        {
            if (!_subscribedToScore && ScoreManager.Instance != null)
            {
                ScoreManager.Instance.OnPointsAdded += HandlePointsAdded;
                _subscribedToScore = true;
            }

            if (!_subscribedToRetry && GameMgr.Instance != null)
            {
                GameMgr.Instance.OnRetryConsumed += HandleRetryConsumed;
                _subscribedToRetry = true;
            }
        }

        private void Unsubscribe()
        {
            if (_subscribedToScore && ScoreManager.Instance != null)
                ScoreManager.Instance.OnPointsAdded -= HandlePointsAdded;
            _subscribedToScore = false;

            if (_subscribedToRetry && GameMgr.Instance != null)
                GameMgr.Instance.OnRetryConsumed -= HandleRetryConsumed;
            _subscribedToRetry = false;
        }

        // ─── Score observation (read-only) ───────────────────────────────────────

        private void HandlePointsAdded(int added, int total, string label, Vector2 worldPos)
        {
            // StartNewRun() fires (0, 0, "Reset", zero). The score restarts → so does our credit,
            // keeping _lastRewardThreshold in lockstep with the per-run/per-level score reset.
            if (label == "Reset")
            {
                _redeemTokens = 0;
                _lastRewardThreshold = 0;
                return;
            }

            if (added <= 0) return;

            // Credit one token per threshold crossed. Loop (not if) to handle a single gain that
            // jumps several paliers at once. _lastRewardThreshold advances by exactly one palier
            // per token → each palier is credited once (anti double-attribution).
            while (total >= _lastRewardThreshold + PointsPerRetry)
            {
                _redeemTokens++;
                _lastRewardThreshold += PointsPerRetry;
            }

            TryRedeemRetries(worldPos);
        }

        // ─── Retry-loss hook ─────────────────────────────────────────────────────

        private void HandleRetryConsumed()
        {
            // A retry was just spent → buy it back immediately if a token is in reserve (model A).
            TryRedeemRetries(GetWarriorWorldPos());
        }

        // ─── Core redemption ─────────────────────────────────────────────────────

        private void TryRedeemRetries(Vector2 feedbackWorldPos)
        {
            GameMgr gm = GameMgr.Instance;
            if (gm == null) return;

            // While we have a token AND a retry is actually missing (GrantRetryReward returns false
            // once at max), spend a token, restore one retry, and play the obligatory animation.
            while (_redeemTokens > 0 && gm.GrantRetryReward())
            {
                _redeemTokens--;
                PlayRetryGainedAnimation(feedbackWorldPos);
            }
        }

        // ─── Obligatory "retry gained" animation (reused SpectaclePopup) ──────────

        private void PlayRetryGainedAnimation(Vector2 worldPos)
        {
            if (popupSpawner == null)
                popupSpawner = FindFirstObjectByType<SpectaclePopupSpawner>(FindObjectsInactive.Include);

            if (popupSpawner != null)
            {
                popupSpawner.SpawnTextAt(retryGainedText, worldPos);
            }
            else
            {
                // Animation is obligatory — never silently skip. If the popup feedback is missing
                // in the scene, make the failure loud so it gets wired up.
                Debug.LogWarning("[RetryRewardManager] No SpectaclePopupSpawner found → 'retry gained' " +
                                 "animation could not play. Add one to the gameplay UI canvas.");
            }

            PlayRetryGainedVfx(worldPos);
        }

        /// <summary>Plays the assigned VFX prefab (e.g. HearthAnimation) once at the feedback
        /// position, in addition to the popup, then auto-destroys it after the particle lifetime.</summary>
        private void PlayRetryGainedVfx(Vector2 worldPos)
        {
            if (retryGainedVfxPrefab == null) return;

            GameObject go = Instantiate(retryGainedVfxPrefab);
            // Keep the prefab's own rotation/scale (the heart VFX is authored facing a given way);
            // only move it to the feedback world position, preserving its original z.
            go.transform.position = new Vector3(worldPos.x, worldPos.y, go.transform.position.z);

            float life = retryGainedVfxFallbackLifetime;
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                life = main.duration + main.startLifetime.constantMax;
            }

            Destroy(go, Mathf.Max(0.1f, life));
        }

        private Vector2 GetWarriorWorldPos()
        {
            Transform warrior = GameMgr.Instance != null && GameMgr.Instance.WarriorInstance != null
                ? GameMgr.Instance.WarriorInstance.transform
                : null;

            return warrior != null ? (Vector2)warrior.position : Vector2.zero;
        }
    }
}

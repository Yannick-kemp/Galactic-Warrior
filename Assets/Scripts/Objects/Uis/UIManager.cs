using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [SerializeField] private GameOverUI gameOver;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Debug.Log("[UIManager] Awake() - Instance ready");
    }

    public void ShowGameOver()
    {
        if (gameOver == null)
            gameOver = FindFirstObjectByType<GameOverUI>();

        if (gameOver == null)
        {
            Debug.LogError("[UIManager] GameOverUI not found in scene (is WarriorUI prefab in Hierarchy?)");
            return;
        }

        int score = 0;
        if (Assets.Scripts.Scoring.ScoreManager.Instance != null)
            score = Assets.Scripts.Scoring.ScoreManager.Instance.TotalPoints;

        gameOver.Show(score);
    }

    public void HideGameOver()
    {
        if (gameOver == null)
            gameOver = FindFirstObjectByType<GameOverUI>();

        if (gameOver == null) return;

        gameOver.Hide();
    }

    public void TryReviveFromGameOver()
    {
        var w = Assets.Scripts.Characteres.WarriorController.Warrior.Instance
             ?? GameMgr.Instance?.WarriorInstance;

        if (w == null)
        {
            Debug.LogError("[UIManager] No warrior to revive.");
            return;
        }

        bool ok = w.TryRevive();
        if (!ok)
        {
            Debug.LogWarning("[UIManager] TryRevive() returned false.");
            return;
        }

        HideGameOver();

        var autoHeal = w.GetComponent<AutoHealthRelicConsumer>();
        if (autoHeal != null)
            autoHeal.ResetThresholdTriggers();
    }
}
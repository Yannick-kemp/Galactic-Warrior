using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [SerializeField] private GameOverUI gameOver;
    [SerializeField] private LevelTransitionUI levelTransitionUI;
    [SerializeField] private PurchaseUI purchaseUI;
    [SerializeField] private RewardUI rewardUI;
    [SerializeField] private UpgradeUI upgradeUI;
    [SerializeField] private VictoryUI victoryUI;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Debug.Log("[UIManager] Awake() - Instance ready");
    }

    private GameOverUI ResolveGameOverUI()
    {
        if (gameOver == null)
            gameOver = FindFirstObjectByType<GameOverUI>(FindObjectsInactive.Include);

        return gameOver;
    }

    public void ShowGameOver()
    {
        var ui = ResolveGameOverUI();
        if (ui == null)
        {
            Debug.LogError("[UIManager] GameOverUI not found in scene.");
            return;
        }

        if (!ui.gameObject.activeSelf)
            ui.gameObject.SetActive(true);

        int score = 0;
        if (Assets.Scripts.Scoring.ScoreManager.Instance != null)
            score = Assets.Scripts.Scoring.ScoreManager.Instance.TotalPoints;

        ui.Show(score);
    }

    public void HideGameOver()
    {
        var ui = ResolveGameOverUI();
        if (ui == null)
            return;

        if (!ui.gameObject.activeSelf)
            ui.gameObject.SetActive(true);

        ui.Hide();
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

    public void PlayLevelTransition(string levelName, string subtitle = "")
    {
        // Prefer the persistent singleton (survives the campaign -> menu scene load),
        // then the serialized reference, then a last-resort scene search.
        if (levelTransitionUI == null)
            levelTransitionUI = LevelTransitionUI.Instance;

        if (levelTransitionUI == null)
            levelTransitionUI = FindFirstObjectByType<LevelTransitionUI>(FindObjectsInactive.Include);

        if (levelTransitionUI == null)
        {
            // Not fatal: the overlay is purely cosmetic. Warn and let the scene flow continue
            // so a missing overlay never blocks the end-of-level transition.
            Debug.LogWarning("[UIManager] LevelTransitionUI not found - skipping transition overlay. " +
                             "Place a LevelTransitionUI on the persistent _APP prefab to enable it.");
            return;
        }

        levelTransitionUI.Play(levelName, subtitle);
    }

    public void ShowPurchaseScreen()
    {
        if (purchaseUI == null)
            purchaseUI = FindFirstObjectByType<PurchaseUI>(FindObjectsInactive.Include);

        if (purchaseUI == null)
        {
            Debug.LogError("[UIManager] PurchaseUI not found.");
            return;
        }

        purchaseUI.Show();
    }

    public void ShowPurchaseScreen(string title, string description, string price = null)
    {
        if (purchaseUI == null)
            purchaseUI = FindFirstObjectByType<PurchaseUI>(FindObjectsInactive.Include);

        if (purchaseUI == null)
        {
            Debug.LogError("[UIManager] PurchaseUI not found.");
            return;
        }

        purchaseUI.ConfigureOffer(title, description, price);
        purchaseUI.Show();
    }

    public void HidePurchaseScreen()
    {
        if (purchaseUI == null)
            purchaseUI = FindFirstObjectByType<PurchaseUI>(FindObjectsInactive.Include);

        if (purchaseUI == null)
            return;

        purchaseUI.Hide();
    }

    public void ShowRewardScreen(int coins, int tokens)
    {
        if (rewardUI == null)
            rewardUI = FindFirstObjectByType<RewardUI>(FindObjectsInactive.Include);

        if (rewardUI == null)
        {
            Debug.LogError("[UIManager] RewardUI not found.");
            return;
        }

        rewardUI.Show(coins, tokens);
    }

    public void HideRewardScreen()
    {
        if (rewardUI == null)
            rewardUI = FindFirstObjectByType<RewardUI>(FindObjectsInactive.Include);

        if (rewardUI == null)
            return;

        rewardUI.Hide();
    }

    public void ShowUpgradeScreen()
    {
        if (upgradeUI == null)
            upgradeUI = FindFirstObjectByType<UpgradeUI>(FindObjectsInactive.Include);

        if (upgradeUI == null)
        {
            Debug.LogError("[UIManager] UpgradeUI not found.");
            return;
        }

        upgradeUI.Show();
    }

    public void HideUpgradeScreen()
    {
        if (upgradeUI == null)
            upgradeUI = FindFirstObjectByType<UpgradeUI>(FindObjectsInactive.Include);

        if (upgradeUI == null)
            return;

        upgradeUI.Hide();
    }

    public void ShowVictoryScreen(int score, float runSeconds, int retries, int coins, int tokens)
    {
        if (victoryUI == null)
            victoryUI = FindFirstObjectByType<VictoryUI>(FindObjectsInactive.Include);

        if (victoryUI == null)
        {
            Debug.LogError("[UIManager] VictoryUI not found in scene.");
            return;
        }

        victoryUI.Show(score, runSeconds, retries, coins, tokens);
    }

    public void HideVictoryScreen()
    {
        if (victoryUI == null)
            victoryUI = FindFirstObjectByType<VictoryUI>(FindObjectsInactive.Include);

        victoryUI?.Hide();
    }
}
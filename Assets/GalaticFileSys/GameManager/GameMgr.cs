using System.Collections;
using System.Collections.Generic;
using System.Text;
using Assets.GalaticfFileSys;
using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Platforms;
using Assets.Scripts.Scoring;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameMgr : MonoBehaviour, IGame
{
    [Header("Retry System")]
    [SerializeField] private int maxRetries = 3;

    private int retryCount = 0;
    // True between a death and its resolution (revive or game over). Guards the retry-consume so a
    // single death only costs one life, even though HandleWarriorDead fires twice (StartDeath + EndDeath).
    private bool _deathConsumed;
    private Vector3 lastDeathPosition;
    public bool IsRestarting { get; private set; }
    public static GameMgr Instance { get; private set; }

    [Header("Spawn Bubble Fallback")]
    [SerializeField] private bool useSpawnBubbleOnRetry = true;
    [SerializeField] private float spawnBubbleRadius = 1.5f;
    [SerializeField] private float spawnBubblePushDistance = 1.0f;
    [SerializeField] private bool pushEnemiesOnSpawnBubble = true;
    [SerializeField] private bool ignoreEnemyCollisionOnSpawnBubble = true;
    [SerializeField] private float spawnBubbleIgnoreSeconds = 0.45f;

    [Header("Checkpoint System")]
    [SerializeField] private Transform currentCheckpoint;
    [SerializeField] private bool useCheckpointRespawn = true;

    // Persistent checkpoint save point.
    // Unlike the currentCheckpoint Transform (which is destroyed whenever the scene reloads),
    // these fields survive scene reloads AND app restarts (mirrored to PlayerPrefs). They let
    // Revive and Continue resume the player at the most recently reached checkpoint instead of
    // the level start. _checkpointSceneName records which level the checkpoint belongs to.
    private Vector3 _checkpointPosition;
    private string _checkpointSceneName;
    private bool _hasCheckpointPosition;

    // Set right before a scene load when the warrior should be re-seated at the saved
    // checkpoint once it registers (Revive reload, or Continue from the main menu).
    private bool _pendingCheckpointRespawn;

    // One-shot guard that forces a level to start at its DEFAULT spawn even if a saved
    // checkpoint exists for it (used by New Game and Level Select "play from start").
    private bool _suppressCheckpointRespawnOnce;

    [Header("Forced Retry Respawn Override")]
    [SerializeField] private bool useForcedRetryZoneRespawn = true;

    private bool _hasForcedRetryRespawn;
    private Vector3 _forcedRetryRespawnPosition;
    private int _checkpointVersion;
    private int _forcedRetryCheckpointVersionAtEnter = -1;

    public Warrior WarriorInstance { get; private set; }
    public Transform CurrentCheckpoint => currentCheckpoint;

    [Header("Background Music")]
    [SerializeField] private string warriorSceneName = "WarriorScene";
    [SerializeField] private AudioClip level1Music;
    [SerializeField] private AudioClip level2Music; // assign IceOfAge.mp3 for AgeOfIce
    [SerializeField] private string level2MusicResourcesPath = "Music/IceOfAge"; // optional fallback: Assets/Resources/Music/IceOfAge.mp3
    [SerializeField] private AudioClip level3Music; // assign LandOfFire.mp3 for LandOfFire
    [SerializeField] private string level3MusicResourcesPath = "Music/LandOfFire"; // optional fallback: Assets/Resources/Music/LandOfFire.mp3
    [SerializeField] private AudioClip level4Music; // assign Redemption.mp3 for Redemption
    [SerializeField] private string level4MusicResourcesPath = "Music/Redemption"; // optional fallback: Assets/Resources/Music/Redemption.mp3
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.35f;
    [SerializeField] private bool restartMusicOnLevelRestart = false;

    [Header("Enemy Reset On Retry")]
    [SerializeField] private float enemyResetRadius = 12f;
    [SerializeField] private float enemyResetHealthPercent = 1f;

    [Header("Moving Platform Respawn")]
    [SerializeField] private float movingPlatformRespawnSeatOffset = 0.05f;

    [Header("Campaign / Scenes")]
    [SerializeField] private string mainMenuSceneName = "menu";
    [SerializeField] private string level2SceneName = "AgeOfIce";
    [SerializeField] private string level3SceneName = "LandOfFire";
    [SerializeField] private string level4SceneName = "Redemption";
    [SerializeField] private List<string> campaignSceneOrder = new List<string> { "WarriorScene", "AgeOfIce", "LandOfFire", "Redemption" };

    [Header("Scene Transition")]
    [SerializeField] private float levelCompleteSlowMoScale = 0.30f;
    [SerializeField] private float levelCompleteSlowMoDuration = 0.45f;
    [SerializeField] private float transitionBeforeLoadDelay = 0.65f;
    [SerializeField] private float transitionAfterLoadDelay = 0.20f;

    [Header("Boss Death Finish")]
    [SerializeField] private float bossDeathSlowMoScale = 0.15f;
    [SerializeField] private float bossDeathSlowMoDuration = 0.60f;
    [SerializeField] private float bossDeathCompletionDelay = 0.05f;

    [Header("Progression / Purchase")]
    [SerializeField] private bool level2Unlocked = false; // legacy + "paid for the rest" flag
    [SerializeField] private bool autoUnlockForTesting = false;
    [SerializeField] private string purchasePriceText = "€4.99";

    [Header("Level 1 Entry Rewards")]
    [SerializeField] private int level2EntryCoinsReward = 50;
    [SerializeField] private int level2EntryUpgradeTokens = 1;

    [Header("Final Victory Reward")]
    [SerializeField] private int finalRewardCoins = 250;
    [SerializeField] private int finalRewardTokens = 2;

    [Header("Boss Memory Relic")]
    [Tooltip("Small world prefab (SpriteRenderer + BossRelicRiseAnimation) spawned at the boss " +
             "death position: the relic rises then disappears. Optional — leave empty for no animation.")]
    [SerializeField] private GameObject bossRelicRisePrefab;

    [Tooltip("Extra pause (seconds) after the rise animation, before the end-of-level / victory UI " +
             "appears, so the player can see the relic. Only applied the first time a boss is defeated.")]
    [SerializeField, Min(0f)] private float bossRelicHoldBeforeUiSeconds = 1.5f;

    private const string GameCompletedKey = "GW_GameCompleted";

    // Boss memory relics — distinct from gameplay relics, persisted to disk like checkpoints.
    private const string BossRelicsDefeatedKey = "GW_BossRelicsDefeated";
    private readonly HashSet<EnemyType> _bossRelicsDefeated = new HashSet<EnemyType>();
    public int BossRelicCount => _bossRelicsDefeated.Count;
    public event System.Action<int> OnBossRelicCountChanged;

    private EnemyType _pendingBossRelicType;
    private Vector3 _pendingBossDeathPosition;

    private const string CheckpointHasKey = "GW_HasCheckpoint";
    private const string CheckpointSceneKey = "GW_CheckpointScene";
    private const string CheckpointXKey = "GW_CheckpointX";
    private const string CheckpointYKey = "GW_CheckpointY";
    private const string CheckpointZKey = "GW_CheckpointZ";

    private const string CampaignPurchasedKey = "GW_CampaignPurchased";
    private const string HighestReachedSceneIndexKey = "GW_HighestReachedSceneIndex";
    private const string ContinueSceneIndexKey = "GW_ContinueSceneIndex";
    private const string LegacyLevel2UnlockedKey = "GW_Level2Unlocked";

    [Header("Post-Level Complete")]
    [SerializeField] private bool returnToMenuAfterPurchasedLevelComplete = true;

    // Monotonic: the furthest scene ever reached. Drives level-select unlocks. Only ever increases.
    private int _highestReachedSceneIndex;

    // The scene "Continue" should resume at = the level right after the one most recently completed.
    // Unlike _highestReachedSceneIndex this is NOT monotonic, so replaying an earlier level correctly
    // sends Continue to that level's successor instead of jumping to the furthest level ever reached.
    private int _continueSceneIndex;

    private bool _isSceneTransitionRunning;
    private bool _level2EntryFlowShownThisLoad;
    private bool _shouldShowLevel2EntryFlowOnNextLoad;

    private bool _bossSlowMoPlaying;
    private bool _bossFinalDeathFlowRunning;
    private bool _skipNextLevelTransitionSlowMo;
    private bool _levelCompletionHandledThisScene;

    private bool _deathWasOnMovingVerticalPlatform;
    private MovingVerticalPlatform _deathMovingVerticalPlatform;
    private string _deathMovingVerticalPlatformId;

    private bool _deathWasOnMovingHorizontalPlatform;
    private MovingHorizontalPlatform _deathMovingHorizontalPlatform;
    private string _deathMovingHorizontalPlatformId;

    private bool _deathWasOnRotatingPlatform;
    private RotatingPlatform _deathRotatingPlatform;
    private string _deathRotatingPlatformId;

    // Static (non-moving) platform captured at death time
    private bool _deathWasOnStaticPlatform;
    private PlatFormColliderTrigger _deathStaticPlatform;

    private bool _hasPendingReviveMovingPlatformRespawn;
    private string _pendingReviveMovingPlatformId;

    private AudioSource _musicSource;
    private Vector3 _initialSpawnPosition;
    private Transform _initialSpawnParent;

    public int RetriesRemaining => Mathf.Max(0, maxRetries - retryCount);

    // ─── Retry-reward feature (additive, scoring untouched) ──────────────────────
    // Exposed so a separate RetryRewardManager can observe retry losses and restore retries
    // it has "bought" with score. The scoring system is never modified by any of this.

    /// <summary>Max retries available per run (mirrors the configured ceiling).</summary>
    public int MaxRetries => maxRetries;

    /// <summary>
    /// Raised right after a retry has been successfully consumed (a death's "Retry" was accepted
    /// and retryCount incremented). RetryRewardManager listens to attempt an immediate redemption
    /// (model A: a token earned pre-loss buys the retry back at once).
    /// </summary>
    public event System.Action OnRetryConsumed;

    // Treat this as "campaign purchased / rest unlocked"
    public bool Level2Unlocked => autoUnlockForTesting || level2Unlocked;

    // Menu-facing properties
    public bool HasCampaignPurchase => Level2Unlocked;
    public int HighestReachedSceneIndex => _highestReachedSceneIndex;
    public int CampaignSceneCount => campaignSceneOrder != null ? campaignSceneOrder.Count : 0;
    public string PurchasePriceText => purchasePriceText;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Initialize();
        EnsureMusicSource();
        NormalizeCampaignSceneOrder();
        LoadProgression();
        LoadCheckpointFromDisk();
        LoadBossRelicsFromDisk();

        SceneManager.sceneLoaded += HandleSceneLoaded;
        HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    public void Initialize()
    {
        AudioMute.Apply();
    }

    public void RegisterHero(Warrior warrior)
    {
        if (warrior == null) return;
        if (warrior != Warrior.Instance) return;

        WarriorInstance = warrior;

        if (_initialSpawnPosition == Vector3.zero)
        {
            _initialSpawnPosition = warrior.transform.position;
            _initialSpawnParent = warrior.transform.parent;
        }

        TryApplyPendingReviveMovingPlatformRespawn(warrior);
        TryApplyPendingCheckpointRespawn(warrior);

        var cam = Camera.main;
        if (cam != null)
        {
            var follow = cam.GetComponent<CameraFollow>();
            if (follow != null)
            {
                follow.SetTarget(warrior.transform);
                follow.SnapImmediately();
            }
        }
    }

    private void EnsureMusicSource()
    {
        if (_musicSource != null) return;

        _musicSource = gameObject.AddComponent<AudioSource>();
        _musicSource.playOnAwake = false;
        _musicSource.loop = true;
        _musicSource.spatialBlend = 0f;
        _musicSource.volume = musicVolume;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        NormalizeCampaignSceneOrder();

        bool isLevel1 = scene.name == warriorSceneName;
        bool isLevel2 = scene.name == level2SceneName;
        bool isLevel3 = scene.name == level3SceneName;
        bool isLevel4 = scene.name == level4SceneName;

        _levelCompletionHandledThisScene = false;
        _bossSlowMoPlaying = false;
        _bossFinalDeathFlowRunning = false;
        _skipNextLevelTransitionSlowMo = false;
        _isSceneTransitionRunning = false;
        Time.timeScale = 1f;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.HidePurchaseScreen();
            UIManager.Instance.HideGameOver();
        }

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = false;

        int currentCampaignIndex = GetCampaignSceneIndex(scene.name);
        if (currentCampaignIndex == 0 || Level2Unlocked)
            MarkSceneAsReached(currentCampaignIndex);

        if (isLevel1)
        {
            ScoreManager.Instance?.StartNewRun();
            StartLevel1Music();
        }
        else if (isLevel2)
        {
            StartLevel2Music();
        }
        else if (isLevel3)
        {
            StartLevel3Music();
        }
        else if (isLevel4)
        {
            StartLevel4Music();
        }
        else
        {
            StopMusic();
        }

        if (isLevel2)
        {
            _level2EntryFlowShownThisLoad = false;

            if (_shouldShowLevel2EntryFlowOnNextLoad)
            {
                _shouldShowLevel2EntryFlowOnNextLoad = false;
                StartCoroutine(ShowLevel2PostEntryFlow());
            }
        }
        else
        {
            _shouldShowLevel2EntryFlowOnNextLoad = false;
        }
    }

    private IEnumerator ShowLevel2PostEntryFlow()
    {
        if (_level2EntryFlowShownThisLoad)
            yield break;

        _level2EntryFlowShownThisLoad = true;

        yield return new WaitForSeconds(0.5f);

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = true;

        UIManager.Instance?.ShowRewardScreen(level2EntryCoinsReward, level2EntryUpgradeTokens);
    }

    private void StartLevel1Music()
    {
        StartMusic(level1Music);
    }

    private void StartLevel2Music()
    {
        AudioClip clip = level2Music;

        // Useful if GameMgr is created at runtime by GameInitializer and the Inspector field is empty.
        // Put the file here: Assets/Resources/Music/IceOfAge.mp3
        // Then Resources path must be: Music/IceOfAge  (no .mp3 extension)
        if (clip == null && !string.IsNullOrEmpty(level2MusicResourcesPath))
            clip = Resources.Load<AudioClip>(level2MusicResourcesPath);

        StartMusic(clip);
    }

    private void StartLevel3Music()
    {
        AudioClip clip = level3Music;

        // Useful if GameMgr is created at runtime by GameInitializer and the Inspector field is empty.
        // Put the file here: Assets/Resources/Music/LandOfFire.mp3
        // Then Resources path must be: Music/LandOfFire  (no .mp3 extension)
        if (clip == null && !string.IsNullOrEmpty(level3MusicResourcesPath))
            clip = Resources.Load<AudioClip>(level3MusicResourcesPath);

        StartMusic(clip);
    }

    private void StartLevel4Music()
    {
        AudioClip clip = level4Music;

        // If no clip is assigned in the inspector, fall back to Resources.
        // Put the file here: Assets/Resources/Music/Redemption.mp3
        // Then Resources path must be: Music/Redemption  (no .mp3 extension)
        if (clip == null && !string.IsNullOrEmpty(level4MusicResourcesPath))
            clip = Resources.Load<AudioClip>(level4MusicResourcesPath);

        StartMusic(clip);
    }

    private void StartMusic(AudioClip clip)
    {
        if (_musicSource == null)
            EnsureMusicSource();

        if (clip == null)
        {
            StopMusic();
            return;
        }

        _musicSource.volume = musicVolume;

        if (_musicSource.clip != clip)
        {
            _musicSource.Stop();
            _musicSource.clip = clip;
            _musicSource.time = 0f;
        }

        if (!_musicSource.isPlaying)
            _musicSource.Play();
    }

    private void StopMusic()
    {
        if (_musicSource != null && _musicSource.isPlaying)
            _musicSource.Stop();
    }

    private void MaybeRestartMusicForLevelRestart()
    {
        if (!restartMusicOnLevelRestart) return;

        string activeSceneName = SceneManager.GetActiveScene().name;
        if (activeSceneName != warriorSceneName &&
            activeSceneName != level2SceneName &&
            activeSceneName != level3SceneName &&
            activeSceneName != level4SceneName)
            return;

        if (_musicSource != null && _musicSource.clip != null)
        {
            _musicSource.Stop();
            _musicSource.time = 0f;
            _musicSource.Play();
        }
    }

    public void RestartCurrentLevel()
    {
        Debug.Log("[GameMgr] RestartCurrentLevel()");

        IsRestarting = true;
        _levelCompletionHandledThisScene = false;
        _bossSlowMoPlaying = false;
        _bossFinalDeathFlowRunning = false;
        _skipNextLevelTransitionSlowMo = false;
        _shouldShowLevel2EntryFlowOnNextLoad = false;
        Time.timeScale = 1f;

        if (Warrior.Instance != null)
            Destroy(Warrior.Instance.gameObject);

        MaybeRestartMusicForLevelRestart();

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void HandleWarriorDead()
    {
        if (WarriorInstance != null)
        {
            lastDeathPosition = WarriorInstance.transform.position;
            CaptureDeathMovingPlatformRespawn();
        }

        foreach (var e in Enemy.ActiveEnemies)
        {
            if (e == null) continue;
            e.StopMoveTowardCoroutine();
        }

        // A death costs one life immediately so the HUD and the Game-Over overlay both read the
        // post-death count: RetriesRemaining hits 0 exactly when it's a real game over (no extra death).
        // Guarded: HandleWarriorDead is called twice per death (StartDeath + EndDeath) → consume once.
        if (!_deathConsumed)
        {
            _deathConsumed = true;
            retryCount = Mathf.Min(retryCount + 1, maxRetries);
        }

        UIManager.Instance?.ShowGameOver();
    }

    public void LoadMenu(string menuSceneName)
    {
        Debug.Log($"[GameMgr] LoadMenu({menuSceneName})");

        Time.timeScale = 1f;
        ScoreManager.Instance?.StartNewRun();

        if (Warrior.Instance != null)
            Destroy(Warrior.Instance.gameObject);

        WarriorInstance = null;
        SceneManager.LoadScene(menuSceneName);
    }

    private void LoadMainMenu()
    {
        LoadMenu(mainMenuSceneName);
    }

    public void EnterForcedRetryZone(Vector3 respawnPosition)
    {
        if (!useForcedRetryZoneRespawn)
            return;

        _hasForcedRetryRespawn = true;
        _forcedRetryRespawnPosition = respawnPosition;
        _forcedRetryCheckpointVersionAtEnter = _checkpointVersion;

        Debug.Log($"[GameMgr] Forced retry zone armed at {respawnPosition}");
    }

    public void ExitForcedRetryZone()
    {
        _hasForcedRetryRespawn = false;
        _forcedRetryRespawnPosition = Vector3.zero;
        _forcedRetryCheckpointVersionAtEnter = -1;

        Debug.Log("[GameMgr] Forced retry zone cleared");
    }

    private bool ShouldUseForcedRetryZoneRespawn()
    {
        if (!useForcedRetryZoneRespawn) return false;
        if (!_hasForcedRetryRespawn) return false;

        return _checkpointVersion == _forcedRetryCheckpointVersionAtEnter;
    }

    public bool TryRetryFromDeath()
    {
        // The life was already consumed at death (HandleWarriorDead). If none remain → real game over.
        if (retryCount >= maxRetries)
            return false;

        Warrior warrior = WarriorInstance;
        if (warrior == null)
            return false;

        ResetMeteorHazards(true);

        Vector3 respawnPosition;
        PlatFormColliderTrigger respawnPlatform = null;
        MovingVerticalPlatform respawnMovingVerticalPlatform = null;
        MovingHorizontalPlatform respawnMovingHorizontalPlatform = null;
        RotatingPlatform respawnRotatingPlatform = null;

        bool useForcedMeteorRespawn = ShouldUseForcedRetryZoneRespawn();

        if (useForcedMeteorRespawn)
        {
            // Forced zone respawn (meteor hazard trigger): use stored position as-is.
            respawnPosition = _forcedRetryRespawnPosition;
            ExitForcedRetryZone();
        }
        else if (TryGetDeathMovingPlatformRespawn(warrior, out respawnPosition, out respawnMovingVerticalPlatform))
        {
            // Died on a vertical moving platform — respawn back on it.
            respawnPlatform = respawnMovingVerticalPlatform;

        }
        else if (TryGetDeathMovingHorizontalPlatformRespawn(warrior, out respawnPosition, out respawnMovingHorizontalPlatform))
        {
            // Died on a horizontal moving platform — respawn back on it.
            respawnPlatform = respawnMovingHorizontalPlatform;

        }
        else if (TryGetDeathRotatingPlatformRespawn(warrior, out respawnPosition, out respawnRotatingPlatform))
        {
            // Died on a rotating platform — respawn back on it.
            respawnPlatform = respawnRotatingPlatform;

        }
        else if (_deathWasOnStaticPlatform &&
                 _deathStaticPlatform != null &&
                 _deathStaticPlatform.platformCollider != null)
        {
            // Died on a static platform — compute surface position and restore collision.
            Physics2D.SyncTransforms();
            respawnPosition = BuildSurfaceRespawnOnStaticPlatform(_deathStaticPlatform, warrior);
            respawnPlatform = _deathStaticPlatform;
            Debug.Log($"[GameMgr] Retry: static platform respawn on {_deathStaticPlatform.name}");
        }
        else if (currentCheckpoint != null && useCheckpointRespawn)
        {
            respawnPosition = currentCheckpoint.position;
        }
        else if (warrior.LastSafePlatform is MovingVerticalPlatform lastSafeMovingPlatform &&
                 lastSafeMovingPlatform.platformCollider != null)
        {
            respawnPlatform = lastSafeMovingPlatform;
            respawnPosition = BuildSurfaceRespawnOnMovingPlatform(lastSafeMovingPlatform, warrior);

        }
        else if (warrior.LastSafePlatform is MovingHorizontalPlatform lastSafeHorizontalPlatform &&
                 lastSafeHorizontalPlatform.platformCollider != null)
        {
            respawnPlatform = lastSafeHorizontalPlatform;
            respawnPosition = BuildSurfaceRespawnOnMovingHorizontalPlatform(lastSafeHorizontalPlatform, warrior);

        }
        else if (warrior.LastSafePlatform is RotatingPlatform lastSafeRotatingPlatform &&
                 lastSafeRotatingPlatform.platformCollider != null)
        {
            respawnPlatform = lastSafeRotatingPlatform;
            respawnPosition = BuildSurfaceRespawnOnRotatingPlatform(lastSafeRotatingPlatform, warrior);

        }
        else if (warrior.LastSafePosition != Vector3.zero)
        {
            // LastSafePosition is set by every platform type on landing.
            respawnPosition = warrior.LastSafePosition;

            // Pass the platform along so collision can be properly restored.
            if (warrior.LastSafePlatform != null &&
                warrior.LastSafePlatform.platformCollider != null)
                respawnPlatform = warrior.LastSafePlatform;
        }
        else if (warrior.LastSafePlatform != null && warrior.LastSafePlatform.platformCollider != null)
        {
            // Fallback: compute a fresh surface position from the last known platform.
            Physics2D.SyncTransforms();
            respawnPosition = BuildSurfaceRespawnOnStaticPlatform(warrior.LastSafePlatform, warrior);
            respawnPlatform = warrior.LastSafePlatform;
        }
        else if (_initialSpawnPosition != Vector3.zero)
        {
            // No safe platform or position was ever recorded (e.g. the Warrior fell off
            // the starting ground before any platform contact registered, as on the
            // AgeOfIce opening platform). Respawn at the scene's spawn point — which is
            // safe ground by construction — instead of lastDeathPosition, which is where
            // the Warrior died (below / under the platform).
            respawnPosition = _initialSpawnPosition;
        }
        else
        {
            // Absolute last resort.
            respawnPosition = lastDeathPosition;
        }

        // IMPORTANT:
        // TryRevive() calls PrepareForSafeRespawn(), which re-enables the Warrior Rigidbody2D
        // and colliders. Respawning onto any platform must happen AFTER this so the platform
        // sees an active collider when seating the rider.
        warrior.ResetMeteorHitState(0.2f);

        bool revived = warrior.TryRevive(0.6f);
        if (!revived)
        {
            Debug.LogWarning("[GameMgr] Retry failed: warrior was not in death state.");
            return false;
        }

        ApplyRespawnToWarrior(warrior, respawnPosition, respawnPlatform);

        if (useSpawnBubbleOnRetry)
            ApplySpawnBubble(warrior, warrior.transform.position);

        ResetAllEnemies();

        Debug.Log($"[GameMgr] Retry {retryCount}/{maxRetries}");

        // This death is resolved → the next death will consume a fresh life.
        _deathConsumed = false;

        // A retry was effectively consumed → let the reward system buy it back immediately
        // if a token is in reserve (model A). Fired only on the success path.
        OnRetryConsumed?.Invoke();

        return true;
    }

    /// <summary>
    /// Additive hook for the retry-reward feature. Restores one previously-consumed retry
    /// (retryCount--). retryCount is clamped at 0, so RetriesRemaining can never exceed maxRetries
    /// — the ceiling is enforced here. Returns true only if a retry was actually restored.
    /// Does NOT touch the scoring system.
    /// </summary>
    public bool GrantRetryReward()
    {
        if (retryCount <= 0)
            return false; // already at max retries — nothing to restore

        retryCount--;
        Debug.Log($"[GameMgr] Retry reward granted → consumed {retryCount}/{maxRetries} ({RetriesRemaining} remaining)");
        return true;
    }

    public void HandleLevel1Completed()
    {
        if (_levelCompletionHandledThisScene)
            return;

        _levelCompletionHandledThisScene = true;
        CompleteCurrentCampaignSceneInternal();
    }

    private void CompleteCurrentCampaignSceneInternal()
    {
        int currentIndex = GetCurrentCampaignSceneIndex();

        if (currentIndex < 0)
        {
            LoadMainMenu();
            return;
        }

        // The level is finished: any checkpoint inside it is no longer a valid resume point.
        ClearSavedCheckpoint();

        if (!HasNextCampaignScene(currentIndex))
        {
            // Dernier niveau du jeu terminé (boss final vaincu) → écran de victoire
            // au lieu du retour-menu silencieux.
            Debug.Log("[GameMgr] Final campaign scene cleared. Showing victory screen.");
            ShowFinalVictoryScreen();
            return;
        }

        if (!Level2Unlocked)
        {
            if (currentIndex == 0)
            {
                ShowPurchaseGateForNextScene(currentIndex);
                return;
            }

            LoadMainMenu();
            return;
        }

        int nextIndex = currentIndex + 1;
        string nextSceneName = campaignSceneOrder[nextIndex];

        MarkSceneAsReached(nextIndex);
        // Continue must resume at the level that follows the one just completed (not the furthest
        // level ever reached), so replaying an earlier level advances to its successor correctly.
        SetContinueScene(nextIndex);

        if (returnToMenuAfterPurchasedLevelComplete)
        {
            StartCoroutine(ReturnToMenuAfterLevelCompleteRoutine(nextSceneName));
            return;
        }

        GoToCampaignSceneByIndex(nextIndex);
    }

    // ─── Final victory (last campaign scene boss defeated) ───────────────────────

    public bool IsGameCompleted => PlayerPrefs.GetInt(GameCompletedKey, 0) == 1;

    private void ShowFinalVictoryScreen()
    {
        Time.timeScale = 1f;

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = true;

        PlayerPrefs.SetInt(GameCompletedKey, 1);
        PlayerPrefs.Save();
        SaveProgression();

        int score = ScoreManager.Instance != null ? ScoreManager.Instance.TotalPoints : 0;
        float dur = ScoreManager.Instance != null ? ScoreManager.Instance.RunDuration : 0f;
        int retries = ScoreManager.Instance != null ? ScoreManager.Instance.RetryCount : 0;

        UIManager.Instance?.ShowVictoryScreen(score, dur, retries, finalRewardCoins, finalRewardTokens);
    }

    // Called by the victory screen buttons.
    public void ReturnToMenuFromVictory()
    {
        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = false;

        // LoadMenu already restores timeScale, resets the run, AND destroys the persistent
        // Warrior (which owns WarriorUI / the VictoryScreen) so nothing leaks into the menu.
        LoadMenu(mainMenuSceneName);
    }

    public void StartNewGamePlus()
    {
        Time.timeScale = 1f;

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = false;

        // The persistent Warrior owns WarriorUI/VictoryScreen; destroy it before loading the
        // first scene, otherwise it carries over as a duplicate.
        if (Warrior.Instance != null)
            Destroy(Warrior.Instance.gameObject);

        // Reuses the existing New Game flow (restarts from the first scene).
        // The GW_GameCompleted flag stays in PlayerPrefs for menu unlocks.
        StartNewGame();
    }

    private IEnumerator ReturnToMenuAfterLevelCompleteRoutine(string nextSceneName)
    {
        if (_isSceneTransitionRunning)
            yield break;

        _isSceneTransitionRunning = true;

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = true;

        bool doSlowMo = !_skipNextLevelTransitionSlowMo;
        _skipNextLevelTransitionSlowMo = false;

        Time.timeScale = 1f;

        if (doSlowMo)
        {
            Time.timeScale = levelCompleteSlowMoScale;
            yield return new WaitForSecondsRealtime(levelCompleteSlowMoDuration);
            Time.timeScale = 1f;
        }

        foreach (var e in Enemy.ActiveEnemies)
        {
            if (e == null) continue;
            e.StopMoveTowardCoroutine();
        }

        UIManager.Instance?.PlayLevelTransition(
            NicifySceneName(nextSceneName),
            GetSceneSubtitle(nextSceneName)
        );

        yield return new WaitForSecondsRealtime(transitionBeforeLoadDelay);

        _shouldShowLevel2EntryFlowOnNextLoad = false;

        // Destroy the persistent (DontDestroyOnLoad) warrior so it does NOT leak its accumulated
        // state into the next level. With it gone, the next scene's own Warrior prefab instance
        // registers as a brand-new Instance with prefab defaults = a full "new game" reset.
        if (Warrior.Instance != null)
            Destroy(Warrior.Instance.gameObject);

        WarriorInstance = null;
        SceneManager.LoadScene(mainMenuSceneName);

        yield return new WaitForSecondsRealtime(transitionAfterLoadDelay);

        _isSceneTransitionRunning = false;
    }

    private void ShowPurchaseGateForNextScene(int currentIndex)
    {
        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = true;

        string nextSceneName = GetNextCampaignSceneName(currentIndex);
        string nextDisplayName = NicifySceneName(nextSceneName);

        string title = string.IsNullOrWhiteSpace(nextDisplayName)
            ? "UNLOCK THE FULL GAME"
            : $"UNLOCK {nextDisplayName.ToUpperInvariant()}";

        string description = string.IsNullOrWhiteSpace(nextDisplayName)
            ? "You defeated the boss. Buy the rest of the game to continue your adventure."
            : $"You defeated the boss. Buy the rest of the game to continue into {nextDisplayName}.";

        UIManager.Instance?.ShowPurchaseScreen(title, description, purchasePriceText);
    }

    public void PlayBossDeathSlowMotion()
    {
        if (_bossSlowMoPlaying) return;
        StartCoroutine(PlayBossDeathSlowMotionRoutine());
    }

    private IEnumerator PlayBossDeathSlowMotionRoutine()
    {
        _bossSlowMoPlaying = true;

        Time.timeScale = 1f;
        Time.timeScale = bossDeathSlowMoScale;

        yield return new WaitForSecondsRealtime(bossDeathSlowMoDuration);

        Time.timeScale = 1f;
        _bossSlowMoPlaying = false;
    }

    public void HandleBossFinalDeathLevelComplete(EnemyType bossType, Vector3 bossDeathPosition)
    {
        if (_bossFinalDeathFlowRunning) return;
        if (_levelCompletionHandledThisScene) return;

        _pendingBossRelicType = bossType;
        _pendingBossDeathPosition = bossDeathPosition;

        StartCoroutine(HandleBossFinalDeathLevelCompleteRoutine());
    }

    private IEnumerator HandleBossFinalDeathLevelCompleteRoutine()
    {
        _bossFinalDeathFlowRunning = true;
        _levelCompletionHandledThisScene = true;
        _skipNextLevelTransitionSlowMo = true;

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = true;

        while (_bossSlowMoPlaying)
            yield return null;

        yield return new WaitForSecondsRealtime(bossDeathCompletionDelay);

        // After the slow-mo: play the boss MemoryRelic sequence (reused VFX + SFX + new "rise"
        // animation), then increment the persistent boss-relic counter, before the end-of-level UI.
        yield return GrantBossRelicSequence(_pendingBossRelicType, _pendingBossDeathPosition);

        CompleteCurrentCampaignSceneInternal();

        _bossFinalDeathFlowRunning = false;
    }

    // ─── Boss MemoryRelic (distinct, persistent counter) ─────────────────────────

    private IEnumerator GrantBossRelicSequence(EnemyType bossType, Vector3 worldPos)
    {
        // Anti-double: a boss already in the set (scene replay / re-kill) plays no sequence.
        if (_bossRelicsDefeated.Contains(bossType))
            yield break;

        // Simple "rise" animation at the boss death spot: the relic rises then disappears.
        if (bossRelicRisePrefab != null)
        {
            GameObject go = Instantiate(bossRelicRisePrefab, worldPos, Quaternion.identity);
            BossRelicRiseAnimation rise = go.GetComponent<BossRelicRiseAnimation>();
            float wait = rise != null ? rise.Duration : 0.8f;
            Debug.Log($"[GameMgr] Boss relic rise spawned '{go.name}' at {worldPos}, wait={wait}s.");
            yield return new WaitForSeconds(wait);
        }
        else
        {
            Debug.LogWarning("[GameMgr] bossRelicRisePrefab is NOT assigned → no rise animation will appear.");
        }

        // Increment persistent counter → RelicMemory CountText shows it (this scene + next scenes).
        TryGrantBossRelic(bossType);

        // Hold so the relic is visible before the end-of-level / victory UI appears.
        if (bossRelicHoldBeforeUiSeconds > 0f)
            yield return new WaitForSecondsRealtime(bossRelicHoldBeforeUiSeconds);
    }

    /// <summary>Adds the boss to the persistent defeated set. Returns false if already granted.</summary>
    public bool TryGrantBossRelic(EnemyType bossType)
    {
        if (!_bossRelicsDefeated.Add(bossType))
            return false;

        SaveBossRelicsToDisk();
        OnBossRelicCountChanged?.Invoke(_bossRelicsDefeated.Count);
        Debug.Log($"[GameMgr] Boss relic granted: {bossType}. Total={_bossRelicsDefeated.Count}");
        return true;
    }

    private void LoadBossRelicsFromDisk()
    {
        _bossRelicsDefeated.Clear();

        string csv = PlayerPrefs.GetString(BossRelicsDefeatedKey, string.Empty);
        if (string.IsNullOrEmpty(csv))
            return;

        string[] parts = csv.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            string token = parts[i].Trim();
            if (string.IsNullOrEmpty(token))
                continue;

            if (System.Enum.TryParse(token, out EnemyType type))
                _bossRelicsDefeated.Add(type);
        }
    }

    private void SaveBossRelicsToDisk()
    {
        PlayerPrefs.SetString(BossRelicsDefeatedKey, string.Join(",", _bossRelicsDefeated));
        PlayerPrefs.Save();
    }

    public void ClearBossRelics()
    {
        _bossRelicsDefeated.Clear();
        PlayerPrefs.DeleteKey(BossRelicsDefeatedKey);
        PlayerPrefs.Save();
        OnBossRelicCountChanged?.Invoke(0);
    }

    public void UnlockLevel2()
    {
        level2Unlocked = true;

        int level2Index = GetCampaignSceneIndex(level2SceneName);
        if (level2Index >= 0)
            MarkSceneAsReached(level2Index);

        SaveProgression();
        Debug.Log("[GameMgr] Campaign purchased / Level 2 unlocked.");
    }

    public void OnPurchaseConfirmed()
    {
        UnlockLevel2();
        HideAnyPurchaseScreen();

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = false;

        int currentIndex = GetCurrentCampaignSceneIndex();

        if (currentIndex < 0 || IsMainMenuScene())
        {
            var menu = FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (menu != null)
                menu.Refresh();
            else
                LoadMainMenu();

            return;
        }

        if (HasNextCampaignScene(currentIndex))
        {
            int nextIndex = currentIndex + 1;
            MarkSceneAsReached(nextIndex);
            // Just unlocked the campaign right after finishing this level: point "Continue"
            // at the next scene (e.g. AgeOfIce after WarriorScene), not the level we just cleared.
            SetContinueScene(nextIndex);
        }

        LoadMainMenu();
    }

    public void OnPurchaseDeclined()
    {
        HideAnyPurchaseScreen();

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = false;

        int currentIndex = GetCurrentCampaignSceneIndex();

        if (currentIndex < 0 || IsMainMenuScene())
        {
            var menu = FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (menu != null)
                menu.Refresh();

            return;
        }

        Debug.Log("[GameMgr] Purchase declined. Returning to main menu.");
        LoadMainMenu();
    }

    public void GoToAgeOfGlace()
    {
        int currentIndex = GetCurrentCampaignSceneIndex();
        if (!HasNextCampaignScene(currentIndex))
        {
            LoadMainMenu();
            return;
        }

        GoToCampaignSceneByIndex(currentIndex + 1);
    }

    private void GoToCampaignSceneByIndex(int sceneIndex)
    {
        if (_isSceneTransitionRunning)
            return;

        if (sceneIndex < 0 || sceneIndex >= campaignSceneOrder.Count)
        {
            LoadMainMenu();
            return;
        }

        StartCoroutine(GoToCampaignSceneRoutine(campaignSceneOrder[sceneIndex]));
    }

    private IEnumerator GoToCampaignSceneRoutine(string targetSceneName)
    {
        _isSceneTransitionRunning = true;

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = true;

        bool doSlowMo = !_skipNextLevelTransitionSlowMo;
        _skipNextLevelTransitionSlowMo = false;

        Time.timeScale = 1f;

        if (doSlowMo)
        {
            Time.timeScale = levelCompleteSlowMoScale;
            yield return new WaitForSecondsRealtime(levelCompleteSlowMoDuration);
            Time.timeScale = 1f;
        }

        foreach (var e in Enemy.ActiveEnemies)
        {
            if (e == null) continue;
            e.StopMoveTowardCoroutine();
        }

        UIManager.Instance?.PlayLevelTransition(
            NicifySceneName(targetSceneName),
            GetSceneSubtitle(targetSceneName)
        );

        yield return new WaitForSecondsRealtime(transitionBeforeLoadDelay);

        _shouldShowLevel2EntryFlowOnNextLoad = (targetSceneName == level2SceneName);

        // Moving on to a different level: drop the previous level's transient checkpoint refs.
        // (The saved checkpoint was already cleared in CompleteCurrentCampaignSceneInternal.)
        ResetTransientCheckpoint();

        // Destroy the persistent warrior so the next level spawns a fresh one (new-game defaults)
        // instead of inheriting this level's accumulated state.
        if (Warrior.Instance != null)
            Destroy(Warrior.Instance.gameObject);

        WarriorInstance = null;
        SceneManager.LoadScene(targetSceneName);

        yield return new WaitForSecondsRealtime(transitionAfterLoadDelay);

        _isSceneTransitionRunning = false;
    }

    private void ApplySpawnBubble(Warrior warrior, Vector3 spawnPos)
    {
        if (warrior == null) return;

        foreach (var enemy in Enemy.ActiveEnemies)
        {
            if (enemy == null) continue;
            if (!enemy.gameObject.activeInHierarchy) continue;

            Collider2D enemyCol = enemy.NormalCollider != null ? enemy.NormalCollider : enemy.collider2;
            if (enemyCol == null || !enemyCol.enabled) continue;

            float dist = Vector2.Distance(enemyCol.bounds.center, spawnPos);
            if (dist > spawnBubbleRadius) continue;

            if (pushEnemiesOnSpawnBubble)
                PushEnemyAwayFromSpawn(enemy, spawnPos);

            if (ignoreEnemyCollisionOnSpawnBubble)
                warrior.IgnoreEnemyCollisionTemporarily(enemy, spawnBubbleIgnoreSeconds);
        }
    }

    private void PushEnemyAwayFromSpawn(Enemy enemy, Vector3 spawnPos)
    {
        if (enemy == null) return;

        float dir = Mathf.Sign(enemy.transform.position.x - spawnPos.x);
        if (Mathf.Abs(dir) < 0.001f)
            dir = Random.value < 0.5f ? -1f : 1f;

        Vector3 pos = enemy.transform.position;
        pos.x += dir * spawnBubblePushDistance;

        if (enemy.CurrentplatForm != null)
            pos.x = enemy.ClampToCurrentPlatform(pos.x);

        enemy.transform.position = pos;

        if (enemy.rigidbody2 != null)
        {
            Vector2 v = enemy.rigidbody2.linearVelocity;
            v.x = 0f;
            enemy.rigidbody2.linearVelocity = v;
        }
    }

    public void SetCheckpoint(Transform checkpoint)
    {
        if (checkpoint == null) return;

        currentCheckpoint = checkpoint;
        _checkpointPosition = checkpoint.position;
        _checkpointSceneName = SceneManager.GetActiveScene().name;
        _hasCheckpointPosition = true;
        _checkpointVersion++;

        SaveCheckpointToDisk();

        Debug.Log($"[GameMgr] Checkpoint activated: {checkpoint.name} in '{_checkpointSceneName}' at {_checkpointPosition}");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        IsRestarting = false;
        _deathConsumed = false; // fresh warrior spawns alive → no unresolved death

        ExitForcedRetryZone();

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = false;

        Time.timeScale = 1f;

        Debug.Log("[GameMgr] Scene loaded → restart finished");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(lastDeathPosition, enemyResetRadius);
    }

    public void ResetAllEnemies()
    {
        foreach (var enemy in Enemy.ActiveEnemies)
        {
            if (enemy == null) continue;
            enemy.ResetCombatState(enemyResetHealthPercent);
        }
    }

    public void ReviveLevel()
    {
        if (WarriorInstance == null)
            return;

        // If a checkpoint was reached in THIS level, Revive must restart from that checkpoint
        // instead of the level start. The currentCheckpoint Transform does not survive the scene
        // reload, so we rely on the persisted _checkpointPosition and re-seat the warrior once it
        // re-registers (see TryApplyPendingCheckpointRespawn / RegisterHero).
        _pendingCheckpointRespawn = HasCheckpointForActiveScene();

        // Force RegisterHero to re-capture the fresh spawn position/parent of the reloaded scene
        // (otherwise it would keep a stale parent reference from the previous scene instance).
        _initialSpawnPosition = Vector3.zero;
        _initialSpawnParent = null;

        Debug.Log(_pendingCheckpointRespawn
            ? "[GameMgr] ReviveLevel() - Restarting from last checkpoint"
            : "[GameMgr] ReviveLevel() - Resetting to Default Spawn");

        Time.timeScale = 1f;
        IsRestarting = true;

        _levelCompletionHandledThisScene = false;
        _bossSlowMoPlaying = false;
        _bossFinalDeathFlowRunning = false;
        _skipNextLevelTransitionSlowMo = false;
        _shouldShowLevel2EntryFlowOnNextLoad = false;

        ResetMeteorHazards(true);

        _deathWasOnMovingVerticalPlatform = false;
        _deathMovingVerticalPlatform = null;
        _deathMovingVerticalPlatformId = null;
        _deathWasOnMovingHorizontalPlatform = false;
        _deathMovingHorizontalPlatform = null;
        _deathMovingHorizontalPlatformId = null;
        _deathWasOnRotatingPlatform = false;
        _deathRotatingPlatform = null;
        _deathRotatingPlatformId = null;
        _hasPendingReviveMovingPlatformRespawn = false;
        _deathWasOnStaticPlatform = false;
        _deathStaticPlatform = null;

        ScoreManager.Instance?.StartNewRun();

        Destroy(WarriorInstance.gameObject);
        WarriorInstance = null;

        SceneManager.sceneLoaded += OnSceneLoadedAfterRevive;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void OnSceneLoadedAfterRevive(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoadedAfterRevive;

        retryCount = 0;
        _deathConsumed = false;
        IsRestarting = false;

        ExitForcedRetryZone();

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = false;

        Time.timeScale = 1f;

        Debug.Log("[GameMgr] Revive restart finished");
    }

    public void StartNewGame()
    {
        ResetMenuLaunchState();
        ClearSavedCheckpoint(); // New Game always restarts the campaign from the very beginning.
        ClearBossRelics();      // New Game also wipes boss-relic progress.
        _suppressCheckpointRespawnOnce = true;
        ScoreManager.Instance?.StartNewRun();
        SceneManager.LoadScene(warriorSceneName);
    }

    public void ContinueGame()
    {
        ResetMenuLaunchState();
        ScoreManager.Instance?.StartNewRun();

        string target = GetContinueSceneName();

        // Resume at the saved checkpoint when it belongs to the scene we are loading.
        _pendingCheckpointRespawn =
            _hasCheckpointPosition &&
            !string.IsNullOrWhiteSpace(_checkpointSceneName) &&
            target == _checkpointSceneName;

        SceneManager.LoadScene(target);
    }

    public void LoadCampaignSceneFromMenu(int sceneIndex)
    {
        if (!IsSceneUnlockedForMenu(sceneIndex))
        {
            Debug.LogWarning($"[GameMgr] Scene index {sceneIndex} is not unlocked for menu selection.");
            return;
        }

        ResetMenuLaunchState();
        _suppressCheckpointRespawnOnce = true; // Level Select always plays the chosen level from its start.
        ScoreManager.Instance?.StartNewRun();
        SceneManager.LoadScene(campaignSceneOrder[sceneIndex]);
    }

    private void ResetMenuLaunchState()
    {
        retryCount = 0;
        _deathConsumed = false;
        ResetTransientCheckpoint();
        _initialSpawnPosition = Vector3.zero;
        _initialSpawnParent = null;

        ExitForcedRetryZone();

        _deathWasOnMovingVerticalPlatform = false;
        _deathMovingVerticalPlatform = null;
        _deathMovingVerticalPlatformId = null;
        _deathWasOnMovingHorizontalPlatform = false;
        _deathMovingHorizontalPlatform = null;
        _deathMovingHorizontalPlatformId = null;
        _deathWasOnRotatingPlatform = false;
        _deathRotatingPlatform = null;
        _deathRotatingPlatformId = null;
        _hasPendingReviveMovingPlatformRespawn = false;
        _pendingReviveMovingPlatformId = null;
        _deathWasOnStaticPlatform = false;
        _deathStaticPlatform = null;
        _level2EntryFlowShownThisLoad = false;
        _shouldShowLevel2EntryFlowOnNextLoad = false;

        _levelCompletionHandledThisScene = false;
        _bossSlowMoPlaying = false;
        _bossFinalDeathFlowRunning = false;
        _skipNextLevelTransitionSlowMo = false;
        _isSceneTransitionRunning = false;

        WarriorInstance = null;
        IsRestarting = false;
        Time.timeScale = 1f;

        if (InputMgr.Instance != null)
            InputMgr.Instance.InputLocked = false;
    }

    private void ResetMeteorHazards(bool resetTriggers = true)
    {
        var rains = FindObjectsByType<MeteorRainFollowWarrior>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (var rain in rains)
        {
            if (rain == null) continue;
            rain.ResetMeteorState(true);
        }

        if (!resetTriggers) return;

        var startTriggers = FindObjectsByType<MeteorFollowTrigger>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (var trigger in startTriggers)
        {
            if (trigger == null) continue;
            trigger.ResetTriggerState();
        }

        var stopTriggers = FindObjectsByType<MeteorRainStopTrigger>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (var stop in stopTriggers)
        {
            if (stop == null) continue;
            stop.ResetTriggerState();
        }
    }

    private void CaptureDeathMovingPlatformRespawn()
    {
        _deathWasOnMovingVerticalPlatform = false;
        _deathMovingVerticalPlatform = null;
        _deathMovingVerticalPlatformId = null;

        _deathWasOnMovingHorizontalPlatform = false;
        _deathMovingHorizontalPlatform = null;
        _deathMovingHorizontalPlatformId = null;

        _deathWasOnRotatingPlatform = false;
        _deathRotatingPlatform = null;
        _deathRotatingPlatformId = null;

        _deathWasOnStaticPlatform = false;
        _deathStaticPlatform = null;

        Warrior warrior = WarriorInstance;
        if (warrior == null) return;

        PlatFormColliderTrigger candidate = warrior.CurrentplatForm != null
            ? warrior.CurrentplatForm
            : warrior.LastSafePlatform;

        if (candidate is MovingVerticalPlatform verticalPlatform &&
            verticalPlatform.platformCollider != null)
        {
            _deathWasOnMovingVerticalPlatform = true;
            _deathMovingVerticalPlatform = verticalPlatform;
            _deathMovingVerticalPlatformId = verticalPlatform.RespawnId;

            Debug.Log($"[GameMgr] Death vertical moving platform locked: {verticalPlatform.RespawnId}");
            return;
        }

        if (candidate is MovingHorizontalPlatform horizontalPlatform &&
            horizontalPlatform.platformCollider != null)
        {
            _deathWasOnMovingHorizontalPlatform = true;
            _deathMovingHorizontalPlatform = horizontalPlatform;
            _deathMovingHorizontalPlatformId = horizontalPlatform.RespawnId;

            Debug.Log($"[GameMgr] Death horizontal moving platform locked: {horizontalPlatform.RespawnId}");
            return;
        }

        if (candidate is RotatingPlatform rotatingPlatform &&
            rotatingPlatform.platformCollider != null)
        {
            _deathWasOnRotatingPlatform = true;
            _deathRotatingPlatform = rotatingPlatform;
            _deathRotatingPlatformId = rotatingPlatform.RespawnId;

            Debug.Log($"[GameMgr] Death rotating platform locked: {rotatingPlatform.RespawnId}");
            return;
        }

        // Static platform (plain PlatFormColliderTrigger / PlatFormPlfColliderTrigger)
        if (candidate != null && candidate.platformCollider != null)
        {
            _deathWasOnStaticPlatform = true;
            _deathStaticPlatform = candidate;
            Debug.Log($"[GameMgr] Death static platform locked: {candidate.name}");
        }
    }

    private MovingVerticalPlatform FindMovingVerticalPlatformByRespawnId(string respawnId)
    {
        if (string.IsNullOrWhiteSpace(respawnId))
            return null;

        var platforms = FindObjectsByType<MovingVerticalPlatform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (var p in platforms)
        {
            if (p == null) continue;
            if (p.RespawnId == respawnId)
                return p;
        }

        return null;
    }

    private MovingHorizontalPlatform FindMovingHorizontalPlatformByRespawnId(string respawnId)
    {
        if (string.IsNullOrWhiteSpace(respawnId))
            return null;

        var platforms = FindObjectsByType<MovingHorizontalPlatform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (var p in platforms)
        {
            if (p == null) continue;
            if (p.RespawnId == respawnId)
                return p;
        }

        return null;
    }

    private RotatingPlatform FindRotatingPlatformByRespawnId(string respawnId)
    {
        if (string.IsNullOrWhiteSpace(respawnId))
            return null;

        var platforms = FindObjectsByType<RotatingPlatform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (var p in platforms)
        {
            if (p == null) continue;
            if (p.RespawnId == respawnId)
                return p;
        }

        return null;
    }

    private Vector3 BuildSurfaceRespawnOnMovingPlatform(MovingVerticalPlatform platform, Warrior warrior)
    {
        if (platform == null || warrior == null)
            return lastDeathPosition;

        if (platform.platformCollider == null)
            return platform.transform.position;

        float preferredX = warrior.LastSafePosition != Vector3.zero
            ? warrior.LastSafePosition.x
            : warrior.transform.position.x;

        return platform.GetSafeRespawnPositionFor(warrior, preferredX);
    }

    private Vector3 BuildSurfaceRespawnOnMovingHorizontalPlatform(MovingHorizontalPlatform platform, Warrior warrior)
    {
        if (platform == null || warrior == null)
            return lastDeathPosition;

        if (platform.platformCollider == null)
            return platform.transform.position;

        float preferredX = warrior.LastSafePosition != Vector3.zero
            ? warrior.LastSafePosition.x
            : warrior.transform.position.x;

        return platform.GetSafeRespawnPositionFor(warrior, preferredX);
    }

    private Vector3 BuildSurfaceRespawnOnRotatingPlatform(RotatingPlatform platform, Warrior warrior)
    {
        if (platform == null || warrior == null)
            return lastDeathPosition;

        if (platform.platformCollider == null)
            return platform.transform.position;

        float preferredX = warrior.LastSafePosition != Vector3.zero
            ? warrior.LastSafePosition.x
            : warrior.transform.position.x;

        return platform.GetSafeRespawnPositionFor(warrior, preferredX);
    }

    /// <summary>
    /// Computes a safe spawn position on any static (non-moving) PlatFormColliderTrigger.
    /// Uses the warrior's actual collider half-height so the position is accurate for every
    /// platform type that does not expose its own GetSafeRespawnPositionFor() method.
    /// </summary>
    private Vector3 BuildSurfaceRespawnOnStaticPlatform(PlatFormColliderTrigger platform, Warrior warrior)
    {
        if (platform == null || warrior == null)
            return lastDeathPosition;

        if (platform.platformCollider == null)
            return platform.transform.position;

        Bounds pb = platform.platformCollider.bounds;

        // Use the real collider half-height; fall back only if the collider is not yet enabled.
        float halfHeight = (warrior.collider2 != null && warrior.collider2.enabled)
            ? warrior.collider2.bounds.extents.y
            : 0.8f;

        float halfWidth = (warrior.collider2 != null && warrior.collider2.enabled)
            ? warrior.collider2.bounds.extents.x
            : 0.4f;

        // Clamp X inside the platform surface with a small horizontal skin.
        const float horizontalSkin = 0.08f;
        float minX = pb.min.x + halfWidth + horizontalSkin;
        float maxX = pb.max.x - halfWidth - horizontalSkin;

        float preferredX = warrior.LastSafePosition != Vector3.zero
            ? warrior.LastSafePosition.x
            : warrior.transform.position.x;

        float safeX = (minX <= maxX)
            ? Mathf.Clamp(preferredX, minX, maxX)
            : pb.center.x;

        float safeY = pb.max.y + halfHeight + movingPlatformRespawnSeatOffset;

        return new Vector3(safeX, safeY, warrior.transform.position.z);
    }

    private bool TryGetDeathMovingPlatformRespawn(
        Warrior warrior,
        out Vector3 respawnPosition,
        out MovingVerticalPlatform respawnPlatform)
    {
        respawnPosition = default;
        respawnPlatform = null;

        if (!_deathWasOnMovingVerticalPlatform)
            return false;

        if (warrior == null || warrior.collider2 == null)
            return false;

        MovingVerticalPlatform platform = _deathMovingVerticalPlatform;

        if (platform == null)
            platform = FindMovingVerticalPlatformByRespawnId(_deathMovingVerticalPlatformId);

        if (platform == null || platform.platformCollider == null)
            return false;

        Physics2D.SyncTransforms();

        respawnPosition = BuildSurfaceRespawnOnMovingPlatform(platform, warrior);
        respawnPlatform = platform;
        return true;
    }

    private bool TryGetDeathMovingHorizontalPlatformRespawn(
        Warrior warrior,
        out Vector3 respawnPosition,
        out MovingHorizontalPlatform respawnPlatform)
    {
        respawnPosition = default;
        respawnPlatform = null;

        if (!_deathWasOnMovingHorizontalPlatform)
            return false;

        if (warrior == null)
            return false;

        MovingHorizontalPlatform platform = _deathMovingHorizontalPlatform;

        if (platform == null)
            platform = FindMovingHorizontalPlatformByRespawnId(_deathMovingHorizontalPlatformId);

        if (platform == null || platform.platformCollider == null)
            return false;

        Physics2D.SyncTransforms();

        respawnPosition = BuildSurfaceRespawnOnMovingHorizontalPlatform(platform, warrior);
        respawnPlatform = platform;
        return true;
    }

    private bool TryGetDeathRotatingPlatformRespawn(
        Warrior warrior,
        out Vector3 respawnPosition,
        out RotatingPlatform respawnPlatform)
    {
        respawnPosition = default;
        respawnPlatform = null;

        if (!_deathWasOnRotatingPlatform)
            return false;

        if (warrior == null || warrior.collider2 == null)
            return false;

        RotatingPlatform platform = _deathRotatingPlatform;

        if (platform == null)
            platform = FindRotatingPlatformByRespawnId(_deathRotatingPlatformId);

        if (platform == null || platform.platformCollider == null)
            return false;

        Physics2D.SyncTransforms();

        respawnPosition = BuildSurfaceRespawnOnRotatingPlatform(platform, warrior);
        respawnPlatform = platform;
        return true;
    }

    private void ApplyRespawnToWarrior(
        Warrior warrior,
        Vector3 respawnPosition,
        PlatFormColliderTrigger platform = null)
    {
        if (warrior == null)
            return;

        // A respawn / checkpoint / forced-retry teleport can target a position inside a zone that is
        // currently culled (off-camera), whose platform colliders are disabled. Bring that zone back
        // to life BEFORE seating the Warrior, otherwise he lands on a disabled collider and drops
        // through it. The regular cull pass keeps the zone active afterwards while he stands in it.
        ZoneCullingManager.Instance?.EnsureZoneActiveAt(respawnPosition);

        // Never parent the Warrior to a moving platform here.
        // MovingVerticalPlatform carries riders with its own delta/rider system.
        if (warrior.transform.parent != _initialSpawnParent)
            warrior.transform.SetParent(_initialSpawnParent, worldPositionStays: true);

        if (platform is MovingVerticalPlatform movingVerticalPlatform)
        {
            float preferredX = respawnPosition.x;
            movingVerticalPlatform.RespawnRiderOnLift(warrior, preferredX);
            return;
        }

        if (platform is MovingHorizontalPlatform movingHorizontalPlatform)
        {
            float preferredX = respawnPosition.x;
            movingHorizontalPlatform.RespawnRiderOnLift(warrior, preferredX);
            return;
        }

        if (platform is RotatingPlatform rotatingPlatform)
        {
            float preferredX = respawnPosition.x;
            rotatingPlatform.RespawnRiderOnLift(warrior, preferredX);
            return;
        }

        RestoreCollisionBetweenWarriorAndPlatform(warrior, platform);

        if (warrior.rigidbody2 != null)
        {
            warrior.rigidbody2.simulated = true;
            warrior.rigidbody2.linearVelocity = Vector2.zero;
            warrior.rigidbody2.angularVelocity = 0f;
            warrior.rigidbody2.constraints = RigidbodyConstraints2D.FreezeRotation;
            warrior.rigidbody2.position = new Vector2(respawnPosition.x, respawnPosition.y);
            warrior.rigidbody2.WakeUp();
        }

        warrior.transform.position = respawnPosition;

        warrior.CurrentplatForm = platform;

        if (platform != null)
            warrior.LastSafePlatform = platform;

        warrior.LastSafePosition = respawnPosition;
        warrior.IsFallingPlfExit = false;
        warrior.IsFallingGrazesEdge = false;
        warrior.IsFallingEdge = false;
        warrior.IsFallingHitEnemy = false;
        warrior.CanMove = true;
        warrior.CanAttackWarrior = true;
        warrior._blockAction = false;

        warrior.StopJumpTowardCoroutine();
        warrior.StopMoveTowardCoroutine();
        warrior.WaitAnimationDisplay();

        Physics2D.SyncTransforms();
    }

    private void RestoreCollisionBetweenWarriorAndPlatform(
        Warrior warrior,
        PlatFormColliderTrigger platform)
    {
        if (warrior == null || platform == null || platform.platformCollider == null)
            return;

        Collider2D[] warriorColliders = warrior.GetComponentsInChildren<Collider2D>(true);

        for (int i = 0; i < warriorColliders.Length; i++)
        {
            Collider2D col = warriorColliders[i];

            if (col == null || col.isTrigger)
                continue;

            Physics2D.IgnoreCollision(platform.platformCollider, col, false);
        }
    }

    private void TryApplyPendingReviveMovingPlatformRespawn(Warrior warrior)
    {
        if (!_hasPendingReviveMovingPlatformRespawn)
            return;

        if (warrior == null || warrior.collider2 == null)
            return;

        MovingVerticalPlatform platform =
            FindMovingVerticalPlatformByRespawnId(_pendingReviveMovingPlatformId);

        if (platform != null && platform.platformCollider != null)
        {
            // RegisterHero can run when the Warrior is already alive after a scene load.
            // We do not call TryRevive() here; we only seat/register him on the moving lift.
            Vector3 respawnPosition = BuildSurfaceRespawnOnMovingPlatform(platform, warrior);
            ApplyRespawnToWarrior(warrior, respawnPosition, platform);
        }

        _hasPendingReviveMovingPlatformRespawn = false;
        _pendingReviveMovingPlatformId = null;
    }

    // Clears only the per-scene-instance checkpoint references (the Transform + version + the
    // armed pending flag). Does NOT touch the persisted save point, so a later Continue can still
    // resume there. Used whenever a level scene is (re)launched from the menu.
    private void ResetTransientCheckpoint()
    {
        currentCheckpoint = null;
        _checkpointVersion = 0;
        _pendingCheckpointRespawn = false;
        _suppressCheckpointRespawnOnce = false;
    }

    // Fully wipes the checkpoint save point, in memory and on disk. Used when starting a brand
    // new game and when a level is completed (its checkpoint must no longer be the resume point).
    private void ClearSavedCheckpoint()
    {
        ResetTransientCheckpoint();

        _checkpointPosition = Vector3.zero;
        _checkpointSceneName = null;
        _hasCheckpointPosition = false;

        PlayerPrefs.DeleteKey(CheckpointHasKey);
        PlayerPrefs.DeleteKey(CheckpointSceneKey);
        PlayerPrefs.DeleteKey(CheckpointXKey);
        PlayerPrefs.DeleteKey(CheckpointYKey);
        PlayerPrefs.DeleteKey(CheckpointZKey);
        PlayerPrefs.Save();
    }

    // ----------------------------------------------------------------------------------
    // Development helpers: wipe the persisted checkpoint save so a level starts fresh.
    // ----------------------------------------------------------------------------------

    // Right-click the GameManager component in the Inspector (works during a Play session).
    [ContextMenu("Dev/Reset Saved Checkpoint")]
    private void DevResetSavedCheckpointContext()
    {
        ClearSavedCheckpoint();
        Debug.Log("[GameMgr] DEV: Saved checkpoint reset (in-memory + PlayerPrefs).");
    }

    // Static entry point usable from an Editor menu item even when no GameMgr exists yet
    // (e.g. outside Play mode). Deletes the on-disk keys and, if a live instance is running,
    // also clears its in-memory checkpoint state.
    public static void DevResetSavedCheckpoint()
    {
        if (Instance != null)
        {
            Instance.ClearSavedCheckpoint();
        }
        else
        {
            PlayerPrefs.DeleteKey(CheckpointHasKey);
            PlayerPrefs.DeleteKey(CheckpointSceneKey);
            PlayerPrefs.DeleteKey(CheckpointXKey);
            PlayerPrefs.DeleteKey(CheckpointYKey);
            PlayerPrefs.DeleteKey(CheckpointZKey);
            PlayerPrefs.Save();
        }

        Debug.Log("[GameMgr] DEV: Saved checkpoint reset.");
    }

    private void SaveCheckpointToDisk()
    {
        PlayerPrefs.SetInt(CheckpointHasKey, _hasCheckpointPosition ? 1 : 0);

        if (_hasCheckpointPosition)
        {
            PlayerPrefs.SetString(CheckpointSceneKey, _checkpointSceneName ?? string.Empty);
            PlayerPrefs.SetFloat(CheckpointXKey, _checkpointPosition.x);
            PlayerPrefs.SetFloat(CheckpointYKey, _checkpointPosition.y);
            PlayerPrefs.SetFloat(CheckpointZKey, _checkpointPosition.z);
        }

        PlayerPrefs.Save();
    }

    private void LoadCheckpointFromDisk()
    {
        _hasCheckpointPosition = PlayerPrefs.GetInt(CheckpointHasKey, 0) == 1;

        if (!_hasCheckpointPosition)
        {
            _checkpointSceneName = null;
            _checkpointPosition = Vector3.zero;
            return;
        }

        _checkpointSceneName = PlayerPrefs.GetString(CheckpointSceneKey, string.Empty);

        float x = PlayerPrefs.GetFloat(CheckpointXKey, 0f);
        float y = PlayerPrefs.GetFloat(CheckpointYKey, 0f);
        float z = PlayerPrefs.GetFloat(CheckpointZKey, 0f);
        _checkpointPosition = new Vector3(x, y, z);

        // A saved checkpoint is only usable if it names a known campaign scene.
        if (string.IsNullOrWhiteSpace(_checkpointSceneName) ||
            GetCampaignSceneIndex(_checkpointSceneName) < 0)
        {
            _hasCheckpointPosition = false;
            _checkpointSceneName = null;
            _checkpointPosition = Vector3.zero;
            return;
        }

        Debug.Log($"[GameMgr] Loaded checkpoint save: '{_checkpointSceneName}' at {_checkpointPosition}");
    }

    // True when we hold a saved checkpoint that belongs to the currently active scene.
    private bool HasCheckpointForActiveScene()
    {
        return _hasCheckpointPosition &&
               !string.IsNullOrWhiteSpace(_checkpointSceneName) &&
               _checkpointSceneName == SceneManager.GetActiveScene().name;
    }

    private void TryApplyPendingCheckpointRespawn(Warrior warrior)
    {
        // A fresh start (New Game / Level Select) was requested: ignore the saved checkpoint
        // exactly once so the level begins at its default spawn.
        if (_suppressCheckpointRespawnOnce)
        {
            _suppressCheckpointRespawnOnce = false;
            _pendingCheckpointRespawn = false;
            Debug.Log("[GameMgr] Checkpoint respawn suppressed for this launch (fresh start).");
            return;
        }

        // Apply whenever we hold a saved checkpoint for the scene we just entered. This covers
        // every entry path: Revive reload, Continue from the menu, AND launching directly into
        // the level. The explicit _pendingCheckpointRespawn flag is kept as a fast-path signal.
        bool shouldApply = _pendingCheckpointRespawn || HasCheckpointForActiveScene();

        Debug.Log($"[GameMgr] Checkpoint respawn check: pending={_pendingCheckpointRespawn}, " +
                  $"hasCheckpoint={_hasCheckpointPosition}, savedScene='{_checkpointSceneName}', " +
                  $"activeScene='{SceneManager.GetActiveScene().name}', shouldApply={shouldApply}");

        _pendingCheckpointRespawn = false;

        if (!shouldApply)
            return;

        if (warrior == null || !_hasCheckpointPosition)
        {
            Debug.Log($"[GameMgr] Checkpoint respawn skipped (warrior null={warrior == null}, hasCheckpoint={_hasCheckpointPosition}).");
            return;
        }

        // Only seat at the checkpoint if it belongs to the scene we actually loaded.
        if (SceneManager.GetActiveScene().name != _checkpointSceneName)
        {
            Debug.Log($"[GameMgr] Checkpoint respawn skipped: saved scene '{_checkpointSceneName}' != active scene '{SceneManager.GetActiveScene().name}'.");
            return;
        }

        // Re-seat the freshly spawned warrior at the last reached checkpoint.
        // ApplyRespawnToWarrior resets falling/movement flags and syncs physics so the
        // warrior is in a clean, playable state at the checkpoint position.
        ApplyRespawnToWarrior(warrior, _checkpointPosition, null);

        // Keep the in-scene checkpoint reference consistent with where we spawned.
        currentCheckpoint = null;

        Debug.Log($"[GameMgr] Respawned at saved checkpoint {_checkpointPosition} in '{_checkpointSceneName}'");
    }

    private void NormalizeCampaignSceneOrder()
    {
        if (campaignSceneOrder == null)
            campaignSceneOrder = new List<string>();

        var seen = new HashSet<string>();

        for (int i = campaignSceneOrder.Count - 1; i >= 0; i--)
        {
            string sceneName = campaignSceneOrder[i];

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                campaignSceneOrder.RemoveAt(i);
                continue;
            }

            sceneName = sceneName.Trim();
            campaignSceneOrder[i] = sceneName;

            if (!seen.Add(sceneName))
                campaignSceneOrder.RemoveAt(i);
        }

        EnsureCampaignSceneFirst(warriorSceneName);
        EnsureCampaignSceneAfter(level2SceneName, warriorSceneName);
        EnsureCampaignSceneAfter(level3SceneName, level2SceneName);
        EnsureCampaignSceneAfter(level4SceneName, level3SceneName);
    }

    private void EnsureCampaignSceneFirst(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        sceneName = sceneName.Trim();
        campaignSceneOrder.RemoveAll(s => s == sceneName);
        campaignSceneOrder.Insert(0, sceneName);
    }

    private void EnsureCampaignSceneAfter(string sceneName, string previousSceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        sceneName = sceneName.Trim();
        campaignSceneOrder.RemoveAll(s => s == sceneName);

        int insertIndex = campaignSceneOrder.Count;

        if (!string.IsNullOrWhiteSpace(previousSceneName))
        {
            previousSceneName = previousSceneName.Trim();
            int previousIndex = campaignSceneOrder.IndexOf(previousSceneName);
            if (previousIndex >= 0)
                insertIndex = previousIndex + 1;
        }

        insertIndex = Mathf.Clamp(insertIndex, 0, campaignSceneOrder.Count);
        campaignSceneOrder.Insert(insertIndex, sceneName);
    }

    private void LoadProgression()
    {
        int legacyUnlocked = PlayerPrefs.GetInt(LegacyLevel2UnlockedKey, level2Unlocked ? 1 : 0);
        int purchased = PlayerPrefs.GetInt(CampaignPurchasedKey, legacyUnlocked > 0 ? 1 : 0);

        level2Unlocked = purchased == 1 || legacyUnlocked == 1;

        int defaultReachedIndex = level2Unlocked ? 1 : 0;
        int maxIndex = Mathf.Max(0, campaignSceneOrder.Count - 1);

        _highestReachedSceneIndex = PlayerPrefs.GetInt(HighestReachedSceneIndexKey, defaultReachedIndex);
        _highestReachedSceneIndex = Mathf.Clamp(_highestReachedSceneIndex, 0, maxIndex);

        if (level2Unlocked)
            _highestReachedSceneIndex = Mathf.Max(_highestReachedSceneIndex, Mathf.Min(1, maxIndex));

        // Continue target defaults to the first playable level; it is set explicitly on level completion.
        _continueSceneIndex = PlayerPrefs.GetInt(ContinueSceneIndexKey, defaultReachedIndex);
        _continueSceneIndex = Mathf.Clamp(_continueSceneIndex, 0, maxIndex);
    }

    private void SaveProgression()
    {
        PlayerPrefs.SetInt(CampaignPurchasedKey, level2Unlocked ? 1 : 0);
        PlayerPrefs.SetInt(LegacyLevel2UnlockedKey, level2Unlocked ? 1 : 0);
        PlayerPrefs.SetInt(HighestReachedSceneIndexKey, _highestReachedSceneIndex);
        PlayerPrefs.SetInt(ContinueSceneIndexKey, _continueSceneIndex);
        PlayerPrefs.Save();
    }

    private void MarkSceneAsReached(int sceneIndex)
    {
        if (sceneIndex < 0)
            return;

        int clamped = Mathf.Clamp(sceneIndex, 0, Mathf.Max(0, campaignSceneOrder.Count - 1));
        if (clamped <= _highestReachedSceneIndex)
            return;

        _highestReachedSceneIndex = clamped;
        SaveProgression();

        Debug.Log($"[GameMgr] Highest reached scene index saved: {_highestReachedSceneIndex} ({campaignSceneOrder[_highestReachedSceneIndex]})");
    }

    // Records where "Continue" should resume next. Set to the successor of a level on completion.
    private void SetContinueScene(int sceneIndex)
    {
        int clamped = Mathf.Clamp(sceneIndex, 0, Mathf.Max(0, campaignSceneOrder.Count - 1));
        if (clamped == _continueSceneIndex)
            return;

        _continueSceneIndex = clamped;
        SaveProgression();

        Debug.Log($"[GameMgr] Continue target set: {_continueSceneIndex} ({campaignSceneOrder[_continueSceneIndex]})");
    }

    private int GetCampaignSceneIndex(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return -1;

        for (int i = 0; i < campaignSceneOrder.Count; i++)
        {
            if (campaignSceneOrder[i] == sceneName)
                return i;
        }

        return -1;
    }

    private int GetCurrentCampaignSceneIndex()
    {
        return GetCampaignSceneIndex(SceneManager.GetActiveScene().name);
    }

    private bool HasNextCampaignScene(int currentIndex)
    {
        return currentIndex >= 0 && currentIndex < campaignSceneOrder.Count - 1;
    }

    private string GetNextCampaignSceneName(int currentIndex)
    {
        if (!HasNextCampaignScene(currentIndex))
            return string.Empty;

        return campaignSceneOrder[currentIndex + 1];
    }

    public string GetContinueSceneName()
    {
        NormalizeCampaignSceneOrder();

        if (campaignSceneOrder == null || campaignSceneOrder.Count == 0)
            return warriorSceneName;

        // A saved checkpoint is the most precise resume point: continue in its level.
        if (_hasCheckpointPosition &&
            !string.IsNullOrWhiteSpace(_checkpointSceneName) &&
            GetCampaignSceneIndex(_checkpointSceneName) >= 0)
            return _checkpointSceneName;

        // Resume at the level following the most recently completed one (NOT the furthest level ever
        // reached) so that replaying an earlier level still advances to its correct successor.
        int sceneIndex = Mathf.Clamp(_continueSceneIndex, 0, campaignSceneOrder.Count - 1);
        return campaignSceneOrder[sceneIndex];
    }

    public string GetContinueSceneDisplayName()
    {
        return NicifySceneName(GetContinueSceneName());
    }

    public bool IsSceneUnlockedForMenu(int sceneIndex)
    {
        NormalizeCampaignSceneOrder();

        if (sceneIndex < 0 || sceneIndex >= campaignSceneOrder.Count)
            return false;

        if (sceneIndex == 0)
            return true;

        if (!Level2Unlocked)
            return false;

        return sceneIndex <= _highestReachedSceneIndex;
    }

    private string GetSceneSubtitle(string sceneName)
    {
        int index = GetCampaignSceneIndex(sceneName);
        if (index < 0)
            return string.Empty;

        return $"Level {index + 1}";
    }

    private string NicifySceneName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        string cleaned = raw.Replace("_", " ").Trim();

        var sb = new StringBuilder(cleaned.Length + 8);
        for (int i = 0; i < cleaned.Length; i++)
        {
            char c = cleaned[i];

            if (i > 0 &&
                char.IsUpper(c) &&
                !char.IsWhiteSpace(cleaned[i - 1]) &&
                !char.IsUpper(cleaned[i - 1]))
            {
                sb.Append(' ');
            }

            sb.Append(c);
        }

        string result = sb.ToString().Trim();

        if (result.EndsWith(" Scene"))
            result = result.Substring(0, result.Length - " Scene".Length).Trim();

        return result;
    }

    private bool IsMainMenuScene()
    {
        return SceneManager.GetActiveScene().name == mainMenuSceneName;
    }

    private void HideAnyPurchaseScreen()
    {
        UIManager.Instance?.HidePurchaseScreen();

        var purchase = FindFirstObjectByType<PurchaseUI>(FindObjectsInactive.Include);
        if (purchase != null)
            purchase.Hide();
    }
}
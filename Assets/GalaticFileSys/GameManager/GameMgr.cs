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
    [SerializeField] private List<string> campaignSceneOrder = new List<string> { "WarriorScene", "AgeOfIce" };

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
    [SerializeField] private string purchasePriceText = "€3.99";

    [Header("Level 1 Entry Rewards")]
    [SerializeField] private int level2EntryCoinsReward = 50;
    [SerializeField] private int level2EntryUpgradeTokens = 1;

    private const string CampaignPurchasedKey = "GW_CampaignPurchased";
    private const string HighestReachedSceneIndexKey = "GW_HighestReachedSceneIndex";
    private const string LegacyLevel2UnlockedKey = "GW_Level2Unlocked";

    [Header("Post-Level Complete")]
    [SerializeField] private bool returnToMenuAfterPurchasedLevelComplete = true;

    private int _highestReachedSceneIndex;

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

    private bool _hasPendingReviveMovingPlatformRespawn;
    private string _pendingReviveMovingPlatformId;

    private AudioSource _musicSource;
    private Vector3 _initialSpawnPosition;
    private Transform _initialSpawnParent;

    public int RetriesRemaining => Mathf.Max(0, maxRetries - retryCount);

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
        if (activeSceneName != warriorSceneName && activeSceneName != level2SceneName) return;

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
        if (retryCount >= maxRetries)
            return false;

        retryCount++;

        Warrior warrior = WarriorInstance;
        if (warrior == null)
        {
            retryCount--;
            return false;
        }

        ResetMeteorHazards(true);

        Vector3 respawnPosition;
        PlatFormColliderTrigger respawnMovingPlatform = null;
        MovingVerticalPlatform respawnMovingVerticalPlatform = null;
        MovingHorizontalPlatform respawnMovingHorizontalPlatform = null;
        RotatingPlatform respawnRotatingPlatform = null;
        bool usedMovingPlatformRespawn = false;

        bool useForcedMeteorRespawn = ShouldUseForcedRetryZoneRespawn();

        if (useForcedMeteorRespawn)
        {
            respawnPosition = _forcedRetryRespawnPosition;
            ExitForcedRetryZone();
        }
        else if (TryGetDeathMovingPlatformRespawn(warrior, out respawnPosition, out respawnMovingVerticalPlatform))
        {
            respawnMovingPlatform = respawnMovingVerticalPlatform;
            usedMovingPlatformRespawn = true;
        }
        else if (TryGetDeathMovingHorizontalPlatformRespawn(warrior, out respawnPosition, out respawnMovingHorizontalPlatform))
        {
            respawnMovingPlatform = respawnMovingHorizontalPlatform;
            usedMovingPlatformRespawn = true;
        }
        else if (TryGetDeathRotatingPlatformRespawn(warrior, out respawnPosition, out respawnRotatingPlatform))
        {
            respawnMovingPlatform = respawnRotatingPlatform;
            usedMovingPlatformRespawn = true;
        }
        else if (currentCheckpoint != null && useCheckpointRespawn)
        {
            respawnPosition = currentCheckpoint.position;
        }
        else if (warrior.LastSafePlatform is MovingVerticalPlatform lastSafeMovingPlatform &&
                 lastSafeMovingPlatform.platformCollider != null)
        {
            respawnMovingPlatform = lastSafeMovingPlatform;
            respawnPosition = BuildSurfaceRespawnOnMovingPlatform(lastSafeMovingPlatform, warrior);
            usedMovingPlatformRespawn = true;
        }
        else if (warrior.LastSafePlatform is MovingHorizontalPlatform lastSafeHorizontalPlatform &&
                 lastSafeHorizontalPlatform.platformCollider != null)
        {
            respawnMovingPlatform = lastSafeHorizontalPlatform;
            respawnPosition = BuildSurfaceRespawnOnMovingHorizontalPlatform(lastSafeHorizontalPlatform, warrior);
            usedMovingPlatformRespawn = true;
        }
        else if (warrior.LastSafePlatform is RotatingPlatform lastSafeRotatingPlatform &&
                 lastSafeRotatingPlatform.platformCollider != null)
        {
            respawnMovingPlatform = lastSafeRotatingPlatform;
            respawnPosition = BuildSurfaceRespawnOnRotatingPlatform(lastSafeRotatingPlatform, warrior);
            usedMovingPlatformRespawn = true;
        }
        else if (warrior.LastSafePosition != Vector3.zero)
        {
            respawnPosition = warrior.LastSafePosition;
        }
        else if (warrior.LastSafePlatform != null && warrior.LastSafePlatform.platformCollider != null)
        {
            Bounds pb = warrior.LastSafePlatform.platformCollider.bounds;
            float halfHeight = warrior.collider2 != null ? warrior.collider2.bounds.extents.y : 0.8f;

            respawnPosition = new Vector3(
                pb.center.x,
                pb.max.y + halfHeight + 0.05f,
                warrior.transform.position.z
            );
        }
        else
        {
            respawnPosition = lastDeathPosition;
        }

        // IMPORTANT:
        // TryRevive() calls PrepareForSafeRespawn(), which re-enables the Warrior Rigidbody2D
        // and colliders. Respawning onto a moving platform must happen AFTER this,
        // otherwise the lift may try to seat/register a disabled collider.
        warrior.ResetMeteorHitState(0.2f);

        bool revived = warrior.TryRevive(0.6f);
        if (!revived)
        {
            Debug.LogWarning("[GameMgr] Retry failed: warrior was not in death state.");
            retryCount--;
            return false;
        }

        ApplyRespawnToWarrior(
            warrior,
            respawnPosition,
            usedMovingPlatformRespawn ? respawnMovingPlatform : null
        );

        if (useSpawnBubbleOnRetry)
            ApplySpawnBubble(warrior, warrior.transform.position);

        ResetAllEnemies();

        Debug.Log($"[GameMgr] Retry {retryCount}/{maxRetries}");
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

        if (!HasNextCampaignScene(currentIndex))
        {
            Debug.Log("[GameMgr] No next campaign scene. Returning to menu.");
            LoadMainMenu();
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

        if (returnToMenuAfterPurchasedLevelComplete)
        {
            StartCoroutine(ReturnToMenuAfterLevelCompleteRoutine(nextSceneName));
            return;
        }

        GoToCampaignSceneByIndex(nextIndex);
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

    public void HandleBossFinalDeathLevelComplete()
    {
        if (_bossFinalDeathFlowRunning) return;
        if (_levelCompletionHandledThisScene) return;

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

        CompleteCurrentCampaignSceneInternal();

        _bossFinalDeathFlowRunning = false;
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
        _checkpointVersion++;

        Debug.Log("[GameMgr] Checkpoint activated: " + checkpoint.name);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        IsRestarting = false;

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

        Debug.Log("[GameMgr] ReviveLevel() - Resetting to Default Spawn");

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
        ScoreManager.Instance?.StartNewRun();
        SceneManager.LoadScene(warriorSceneName);
    }

    public void ContinueGame()
    {
        ResetMenuLaunchState();
        ScoreManager.Instance?.StartNewRun();
        SceneManager.LoadScene(GetContinueSceneName());
    }

    public void LoadCampaignSceneFromMenu(int sceneIndex)
    {
        if (!IsSceneUnlockedForMenu(sceneIndex))
        {
            Debug.LogWarning($"[GameMgr] Scene index {sceneIndex} is not unlocked for menu selection.");
            return;
        }

        ResetMenuLaunchState();
        ScoreManager.Instance?.StartNewRun();
        SceneManager.LoadScene(campaignSceneOrder[sceneIndex]);
    }

    private void ResetMenuLaunchState()
    {
        retryCount = 0;
        currentCheckpoint = null;
        _checkpointVersion = 0;
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

    private void NormalizeCampaignSceneOrder()
    {
        if (campaignSceneOrder == null)
            campaignSceneOrder = new List<string>();

        for (int i = campaignSceneOrder.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(campaignSceneOrder[i]))
                campaignSceneOrder.RemoveAt(i);
        }

        if (!campaignSceneOrder.Contains(warriorSceneName))
            campaignSceneOrder.Insert(0, warriorSceneName);

        if (!string.IsNullOrWhiteSpace(level2SceneName) && !campaignSceneOrder.Contains(level2SceneName))
            campaignSceneOrder.Add(level2SceneName);

        int warriorIndex = campaignSceneOrder.IndexOf(warriorSceneName);
        if (warriorIndex > 0)
        {
            campaignSceneOrder.RemoveAt(warriorIndex);
            campaignSceneOrder.Insert(0, warriorSceneName);
        }
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
    }

    private void SaveProgression()
    {
        PlayerPrefs.SetInt(CampaignPurchasedKey, level2Unlocked ? 1 : 0);
        PlayerPrefs.SetInt(LegacyLevel2UnlockedKey, level2Unlocked ? 1 : 0);
        PlayerPrefs.SetInt(HighestReachedSceneIndexKey, _highestReachedSceneIndex);
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

        int sceneIndex = Mathf.Clamp(_highestReachedSceneIndex, 0, campaignSceneOrder.Count - 1);
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

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
    private bool retryPending;
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
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.35f;
    [SerializeField] private bool restartMusicOnLevelRestart = false;

    [Header("Enemy Reset On Retry")]
    [SerializeField] private float enemyResetRadius = 12f;
    [SerializeField] private float enemyResetHealthPercent = 1f;

    [Header("Moving Platform Respawn")]
    [SerializeField] private float movingPlatformRespawnSeatOffset = 0.05f;

    private bool _deathWasOnMovingVerticalPlatform;
    private MovingVerticalPlatform _deathMovingVerticalPlatform;
    private string _deathMovingVerticalPlatformId;

    private bool _hasPendingReviveMovingPlatformRespawn;
    private string _pendingReviveMovingPlatformId;

    private AudioSource _musicSource;

    public int RetriesRemaining => Mathf.Max(0, maxRetries - retryCount);

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
        bool isLevel1 = scene.name == warriorSceneName;

        if (isLevel1)
        {
            ScoreManager.Instance?.StartNewRun();
            StartLevel1Music();
        }
        else
        {
            StopLevel1Music();
        }
    }

    private void StartLevel1Music()
    {
        if (level1Music == null) return;

        _musicSource.volume = musicVolume;

        if (_musicSource.clip != level1Music)
            _musicSource.clip = level1Music;

        if (!_musicSource.isPlaying)
            _musicSource.Play();
    }

    private void StopLevel1Music()
    {
        if (_musicSource != null && _musicSource.isPlaying)
            _musicSource.Stop();
    }

    private void MaybeRestartMusicForLevelRestart()
    {
        if (!restartMusicOnLevelRestart) return;
        if (SceneManager.GetActiveScene().name != warriorSceneName) return;

        if (_musicSource != null)
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

        SceneManager.LoadScene(menuSceneName);
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

        var warrior = WarriorInstance;
        if (warrior == null)
        {
            retryCount--;
            return false;
        }

        ResetMeteorHazards(true);

        Vector3 respawnPosition;
        MovingVerticalPlatform respawnMovingPlatform = null;
        bool usedMovingPlatformRespawn = false;

        bool useForcedMeteorRespawn = ShouldUseForcedRetryZoneRespawn();

        if (useForcedMeteorRespawn)
        {
            respawnPosition = _forcedRetryRespawnPosition;
            ExitForcedRetryZone();
        }
        else if (TryGetDeathMovingPlatformRespawn(warrior, out respawnPosition, out respawnMovingPlatform))
        {
            usedMovingPlatformRespawn = true;
        }
        else if (currentCheckpoint != null && useCheckpointRespawn)
        {
            respawnPosition = currentCheckpoint.position;
        }
        else if (warrior.LastSafePosition != Vector3.zero)
        {
            respawnPosition = warrior.LastSafePosition;
        }
        else if (warrior.LastSafePlatform != null)
        {
            var pb = warrior.LastSafePlatform.platformCollider.bounds;

            respawnPosition = new Vector3(
                pb.center.x,
                pb.max.y + warrior.collider2.bounds.extents.y + 0.05f,
                warrior.transform.position.z
            );
        }
        else
        {
            respawnPosition = lastDeathPosition;
        }

        ApplyRespawnToWarrior(
            warrior,
            respawnPosition,
            usedMovingPlatformRespawn ? respawnMovingPlatform : null
        );

        warrior.ResetMeteorHitState(0.2f);

        bool revived = warrior.TryRevive(0.6f);
        if (!revived)
        {
            Debug.LogWarning("[GameMgr] Retry failed: warrior was not in death state.");
            retryCount--;
            return false;
        }

        if (useSpawnBubbleOnRetry)
            ApplySpawnBubble(warrior, respawnPosition);

        ResetAllEnemies();

        Debug.Log($"[GameMgr] Retry {retryCount}/{maxRetries}");

        return true;
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

        Debug.Log("[GameMgr] ReviveLevel()");

        Time.timeScale = 1f;
        IsRestarting = true;

        ResetMeteorHazards(true);

        CaptureDeathMovingPlatformRespawn();

        _hasPendingReviveMovingPlatformRespawn = _deathWasOnMovingVerticalPlatform;
        _pendingReviveMovingPlatformId = _deathMovingVerticalPlatformId;

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

        Debug.Log("[GameMgr] Revive restart finished");
    }

    public void StartNewGame()
    {
        retryCount = 0;
        currentCheckpoint = null;
        _checkpointVersion = 0;
        ExitForcedRetryZone();

        _deathWasOnMovingVerticalPlatform = false;
        _deathMovingVerticalPlatform = null;
        _deathMovingVerticalPlatformId = null;
        _hasPendingReviveMovingPlatformRespawn = false;
        _pendingReviveMovingPlatformId = null;

        ScoreManager.Instance?.StartNewRun();
        SceneManager.LoadScene(warriorSceneName);
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

        Warrior warrior = WarriorInstance;
        if (warrior == null)
            return;

        MovingVerticalPlatform movingPlatform = null;

        if (warrior.CurrentplatForm is MovingVerticalPlatform currentMoving)
            movingPlatform = currentMoving;
        //else if (warrior.LastSafePlatform is MovingVerticalPlatform lastSafeMoving)
        //    movingPlatform = lastSafeMoving;

        if (movingPlatform == null)
            return;

        if (movingPlatform.platformCollider == null)
            return;

        _deathWasOnMovingVerticalPlatform = true;
        _deathMovingVerticalPlatform = movingPlatform;
        _deathMovingVerticalPlatformId = movingPlatform.RespawnId;

        Debug.Log($"[GameMgr] Captured moving-platform respawn: {movingPlatform.RespawnId}");
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

    private Vector3 BuildSurfaceRespawnOnMovingPlatform(
     MovingVerticalPlatform platform,
     Warrior warrior)
    {
        // Force an update to get the platform's exact current position in world space
        Physics2D.SyncTransforms();

        // Bounds.max.y is the top edge of the platform collider
        float platformTop = platform.platformCollider.bounds.max.y;

        // Bounds.extents.y is half the height of the Warrior
        float warriorHalfHeight = warrior.collider2.bounds.extents.y;

        // Use a slightly larger offset (e.g., 0.15f) so he is clearly ABOVE the surface
        float spawnY = platformTop + warriorHalfHeight + movingPlatformRespawnSeatOffset + 0.1f;

        // X is the center of the platform
        float spawnX = platform.platformCollider.bounds.center.x;

        return new Vector3(spawnX, spawnY, warrior.transform.position.z);
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
    private void ApplyRespawnToWarrior(
    Warrior warrior,
    Vector3 respawnPosition,
    PlatFormColliderTrigger platform = null)
    {
        if (warrior == null) return;

        // 1. Prepare Warrior (Must re-enable physics disabled in StartDeath)
        if (warrior.rigidbody2 != null)
        {
            warrior.rigidbody2.simulated = true;
            warrior.rigidbody2.linearVelocity = Vector2.zero;
            warrior.rigidbody2.angularVelocity = 0f;

            // Snap the physics body to the calculated surface position
            warrior.rigidbody2.position = respawnPosition;
        }

        // 2. Snap the Transform
        warrior.transform.position = respawnPosition;

        // 3. Assign Platform
        warrior.CurrentplatForm = platform;
        if (platform != null)
        {
            warrior.LastSafePlatform = platform;
            // Parent him so he moves with the lift immediately
            warrior.transform.SetParent(platform.transform);
        }

        // 4. Reset Fall States
        warrior.LastSafePosition = respawnPosition;
        warrior.IsFallingPlfExit = false;
        warrior.IsFallingGrazesEdge = false;
        warrior.IsFallingEdge = false;
        warrior.IsFallingHitEnemy = false;

        // 5. Tell the physics engine to update his location right now
        Physics2D.SyncTransforms();
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
            Vector3 respawnPosition = BuildSurfaceRespawnOnMovingPlatform(platform, warrior);
            ApplyRespawnToWarrior(warrior, respawnPosition, platform);
        }

        _hasPendingReviveMovingPlatformRespawn = false;
        _pendingReviveMovingPlatformId = null;
    }
}

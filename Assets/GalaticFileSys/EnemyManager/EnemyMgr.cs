using System;
using System.Collections.Generic;
using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Platforms;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EnemyMgr : MonoBehaviour
{
    private class SceneEnemyState
    {
        public Enemy enemy;
        public bool permanentlyDefeated;
        public bool countsForLevelClear;
    }

    public static EnemyMgr Instance { get; private set; }

    [SerializeField] private List<EnemyPrefabEntry> enemyPrefabs;

    private Dictionary<EnemyType, GameObject> prefabLookup;
    private readonly List<Enemy> activeEnemies = new List<Enemy>();
    private readonly List<EnemySpawnPoint> spawnPoints = new List<EnemySpawnPoint>();
    private readonly List<SceneEnemyState> sceneEnemyStates = new List<SceneEnemyState>();

    private Enemy currentBoss;
    private bool _levelClearTriggeredThisScene;

    public event Action<int, int> OnEnemyCounterChanged;

    private int _totalCountableEnemyCount;
    private int _lastPublishedRemaining = -1;
    private int _lastPublishedTotal = -1;

    public Enemy CurrentBoss => currentBoss;
    public bool HasAliveBoss => currentBoss != null && !currentBoss.IsDeadOrDying;

    public int TotalCountableEnemyCount => _totalCountableEnemyCount;
    public int RemainingCountableEnemyCount => AliveCountableEnemyCount;

    public int AliveEnemyCount
    {
        get
        {
            CleanupActiveEnemies();
            return activeEnemies.Count;
        }
    }

    public int AliveCountableEnemyCount
    {
        get
        {
            CleanupActiveEnemies();
            CleanupSpawnPoints();

            int count = 0;

            foreach (var sp in spawnPoints)
            {
                if (sp == null) continue;
                if (sp.HasAliveCountableEnemy)
                    count++;
            }

            foreach (var state in sceneEnemyStates)
            {
                if (state == null) continue;
                if (state.permanentlyDefeated) continue;
                if (!state.countsForLevelClear) continue;
                if (state.enemy != null && !state.enemy.IsDeadOrDying)
                    count++;
            }

            foreach (var enemy in activeEnemies)
            {
                if (enemy == null) continue;
                if (enemy.OwnerSpawnPoint != null) continue;
                if (FindSceneEnemyState(enemy) != null) continue;
                if (enemy.IsDeadOrDying) continue;
                if (!enemy.CountsForLevelClear) continue;

                count++;
            }

            return count;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildPrefabLookup();
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        StartCoroutine(RegisterSceneStateNextFrame());
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(RegisterSceneStateNextFrame());
    }

    private System.Collections.IEnumerator RegisterSceneStateNextFrame()
    {
        yield return null;
        RegisterSceneState();
    }

    private void BuildPrefabLookup()
    {
        prefabLookup = new Dictionary<EnemyType, GameObject>();

        foreach (var entry in enemyPrefabs)
        {
            if (entry == null || entry.prefab == null)
                continue;

            if (!prefabLookup.ContainsKey(entry.type))
                prefabLookup.Add(entry.type, entry.prefab);
        }
    }

    private void EnsurePrefabLookup()
    {
        if (prefabLookup == null)
            BuildPrefabLookup();
    }

    private void CleanupActiveEnemies()
    {
        activeEnemies.RemoveAll(e => e == null);
    }

    private void CleanupSpawnPoints()
    {
        spawnPoints.RemoveAll(s => s == null);
    }

    private void RegisterSceneState()
    {
        _levelClearTriggeredThisScene = false;

        spawnPoints.Clear();
        sceneEnemyStates.Clear();
        activeEnemies.Clear();
        currentBoss = null;

        EnemySpawnPoint[] sceneSpawnPoints = FindObjectsByType<EnemySpawnPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (var sp in sceneSpawnPoints)
        {
            if (sp == null) continue;
            if (!spawnPoints.Contains(sp))
                spawnPoints.Add(sp);
        }

        Enemy[] sceneEnemies = FindObjectsByType<Enemy>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (var enemy in sceneEnemies)
        {
            RegisterExistingEnemy(enemy);
        }

        _totalCountableEnemyCount = ComputeTotalCountableEnemiesForScene();
        PublishEnemyCounter(true);

        Debug.Log($"[EnemyMgr] Scene enemies registered: total={AliveEnemyCount}, required={AliveCountableEnemyCount}, boss={(currentBoss != null ? currentBoss.BossDisplayName : "none")}");
    }

    private int ComputeTotalCountableEnemiesForScene()
    {
        CleanupSpawnPoints();

        int total = 0;

        foreach (var sp in spawnPoints)
        {
            if (sp == null) continue;
            if (sp.CountsForLevelClear)
                total++;
        }

        foreach (var state in sceneEnemyStates)
        {
            if (state == null) continue;
            if (state.countsForLevelClear)
                total++;
        }

        return total;
    }

    private void PublishEnemyCounter(bool force = false)
    {
        int remaining = AliveCountableEnemyCount;

        if (remaining > _totalCountableEnemyCount)
            _totalCountableEnemyCount = remaining;

        int total = _totalCountableEnemyCount;

        if (!force &&
            remaining == _lastPublishedRemaining &&
            total == _lastPublishedTotal)
        {
            return;
        }

        _lastPublishedRemaining = remaining;
        _lastPublishedTotal = total;

        Debug.Log($"[EnemyMgr] PublishEnemyCounter -> {remaining}/{total}");
        OnEnemyCounterChanged?.Invoke(remaining, total);
    }
    private void RegisterExistingEnemy(Enemy enemy)
    {
        if (enemy == null)
            return;

        if (!activeEnemies.Contains(enemy))
            activeEnemies.Add(enemy);

        var warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior != null && enemy.target == null)
            enemy.target = warrior.transform;

        if (enemy.IsBoss)
        {
            currentBoss = enemy;
            Debug.Log($"[EnemyMgr] Boss registered from scene: {enemy.BossDisplayName}");
        }

        if (enemy.OwnerSpawnPoint == null)
            RegisterScenePlacedEnemy(enemy);
    }

    private void RegisterScenePlacedEnemy(Enemy enemy)
    {
        if (enemy == null) return;
        if (FindSceneEnemyState(enemy) != null) return;

        var state = new SceneEnemyState
        {
            enemy = enemy,
            permanentlyDefeated = false,
            countsForLevelClear = enemy.CountsForLevelClear
        };

        sceneEnemyStates.Add(state);
    }

    private SceneEnemyState FindSceneEnemyState(Enemy enemy)
    {
        if (enemy == null) return null;

        for (int i = 0; i < sceneEnemyStates.Count; i++)
        {
            var state = sceneEnemyStates[i];
            if (state == null) continue;
            if (state.enemy == enemy)
                return state;
        }

        return null;
    }

    public Enemy SpawnEnemy(EnemyType type, Vector3 position)
    {
        return SpawnEnemy(type, position, null, null);
    }

    public Enemy SpawnEnemy(EnemyType type, Vector3 position, EnemySpawnOverrides overrides = null, EnemySpawnPoint ownerSpawnPoint = null)
    {
        EnsurePrefabLookup();

        if (!prefabLookup.ContainsKey(type))
        {
            Debug.LogError("Enemy type not registered: " + type);
            return null;
        }

        GameObject prefab = prefabLookup[type];
        return SpawnEnemyByPrefab(prefab, position, type, overrides, false, null, ownerSpawnPoint);
    }

    public Enemy SpawnBoss(EnemyType type, Vector3 position, string bossName = null, EnemySpawnOverrides overrides = null)
    {
        EnsurePrefabLookup();

        if (!prefabLookup.ContainsKey(type))
        {
            Debug.LogError("Enemy type not registered: " + type);
            return null;
        }

        GameObject prefab = prefabLookup[type];
        return SpawnEnemyByPrefab(prefab, position, type, overrides, true, bossName, null);
    }

    /// <summary>
    /// Parents a freshly spawned enemy under its owner spawn point so it lives inside the SAME
    /// ZoneCullable as the platform it stands on, and therefore culls atomically with that platform.
    /// Without this the enemy is Instantiated at the scene root and is never culled, while its
    /// platform (inside a zone) IS deactivated when off-screen -> the enemy loses its now-disabled
    /// support collider and falls/tunnels into the void (Enemy.ClampGroundBoundToSurface releases
    /// the ground-bound Y-lock as soon as the platform GameObject becomes inactive).
    /// Bosses intentionally stay at the root (never culled): their fight must never pause.
    /// </summary>
    private void ParentSpawnedEnemyToOwnerZone(Enemy enemy, EnemySpawnPoint ownerSpawnPoint)
    {
        if (enemy == null || ownerSpawnPoint == null)
            return;

        if (enemy.IsBoss)
            return;

        // worldPositionStays:true keeps the world position/rotation set by the Instantiate above.
        enemy.transform.SetParent(ownerSpawnPoint.transform, worldPositionStays: true);
    }

    private Enemy SpawnEnemyByPrefab(
        GameObject prefab,
        Vector3 position,
        EnemyType type,
        EnemySpawnOverrides overrides = null,
        bool isBoss = false,
        string bossName = null,
        EnemySpawnPoint ownerSpawnPoint = null)
    {
        if (prefab == null)
            return null;

        GameObject enemyObject = Instantiate(prefab, position, Quaternion.identity);
        Enemy enemy = enemyObject.GetComponent<Enemy>();

        if (enemy == null)
        {
            Debug.LogError($"Spawned prefab '{prefab.name}' has no Enemy component.");
            Destroy(enemyObject);
            return null;
        }

        enemy.SetEnemyType(type);

        bool finalIsBoss = isBoss || enemy.IsBoss;
        string finalBossName = !string.IsNullOrWhiteSpace(bossName)
            ? bossName
            : (finalIsBoss ? enemy.BossDisplayName : null);

        enemy.SetBoss(finalIsBoss, finalBossName);
        enemy.SetOwnerSpawnPoint(ownerSpawnPoint);

        ParentSpawnedEnemyToOwnerZone(enemy, ownerSpawnPoint);

        InitializeSpawnedEnemy(enemy, overrides);
        return enemy;
    }

    public Enemy SpawnEnemyOnMovingVerticalPlatform(
        EnemyType type,
        MovingVerticalPlatform platform,
        Transform spawnPoint = null,
        EnemySpawnOverrides overrides = null,
        EnemySpawnPoint ownerSpawnPoint = null)
    {
        EnsurePrefabLookup();

        if (!prefabLookup.ContainsKey(type))
        {
            Debug.LogError("Enemy type not registered: " + type);
            return null;
        }

        GameObject prefab = prefabLookup[type];

        return SpawnEnemyOnMovingVerticalPlatformInternal(
            prefab,
            type,
            platform,
            spawnPoint,
            overrides,
            false,
            null,
            ownerSpawnPoint
        );
    }

    public Enemy SpawnBossOnMovingVerticalPlatform(
        EnemyType type,
        MovingVerticalPlatform platform,
        Transform spawnPoint = null,
        string bossName = null,
        EnemySpawnOverrides overrides = null)
    {
        EnsurePrefabLookup();

        if (!prefabLookup.ContainsKey(type))
        {
            Debug.LogError("Enemy type not registered: " + type);
            return null;
        }

        GameObject prefab = prefabLookup[type];

        return SpawnEnemyOnMovingVerticalPlatformInternal(
            prefab,
            type,
            platform,
            spawnPoint,
            overrides,
            true,
            bossName,
            null
        );
    }

    public Enemy SpawnEnemyOnMovingVerticalPlatformByPrefab(
        GameObject prefab,
        EnemyType type,
        MovingVerticalPlatform platform,
        Transform spawnPoint = null,
        EnemySpawnOverrides overrides = null,
        bool isBoss = false,
        string bossName = null,
        EnemySpawnPoint ownerSpawnPoint = null)
    {
        return SpawnEnemyOnMovingVerticalPlatformInternal(
            prefab,
            type,
            platform,
            spawnPoint,
            overrides,
            isBoss,
            bossName,
            ownerSpawnPoint
        );
    }

    private Enemy SpawnEnemyOnMovingVerticalPlatformInternal(
        GameObject prefab,
        EnemyType type,
        MovingVerticalPlatform platform,
        Transform spawnPoint,
        EnemySpawnOverrides overrides,
        bool isBoss = false,
        string bossName = null,
        EnemySpawnPoint ownerSpawnPoint = null)
    {
        if (prefab == null || platform == null)
            return null;

        Vector3 spawnPos = spawnPoint != null
            ? spawnPoint.position
            : platform.transform.position;

        GameObject enemyObject = Instantiate(prefab, spawnPos, Quaternion.identity);
        Enemy enemy = enemyObject.GetComponent<Enemy>();

        if (enemy == null)
        {
            Debug.LogError($"Spawned prefab '{prefab.name}' has no Enemy component.");
            Destroy(enemyObject);
            return null;
        }

        enemy.SetEnemyType(type);

        bool finalIsBoss = isBoss || enemy.IsBoss;
        string finalBossName = !string.IsNullOrWhiteSpace(bossName)
            ? bossName
            : (finalIsBoss ? enemy.BossDisplayName : null);

        enemy.SetBoss(finalIsBoss, finalBossName);
        enemy.SetOwnerSpawnPoint(ownerSpawnPoint);

        ParentSpawnedEnemyToOwnerZone(enemy, ownerSpawnPoint);

        InitializeSpawnedEnemy(enemy, overrides);

        enemy.CurrentplatForm = platform;

        return enemy;
    }

    private void InitializeSpawnedEnemy(Enemy enemy, EnemySpawnOverrides overrides)
    {
        if (enemy == null)
            return;

        var warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior != null)
            enemy.target = warrior.transform;

        enemy.SetSpawnOverrides(overrides);

        if (!activeEnemies.Contains(enemy))
            activeEnemies.Add(enemy);

        if (enemy.IsBoss)
        {
            currentBoss = enemy;
            Debug.Log($"[EnemyMgr] Boss registered: {enemy.BossDisplayName}");
        }

        PublishEnemyCounter();

        Debug.Log($"[EnemyMgr] Spawned {enemy.EnemyType} | Boss={enemy.IsBoss} | Counts={enemy.CountsForLevelClear}");
    }

    public void OnEnemyDeathStarted(Enemy enemy)
    {
        if (enemy == null) return;

        PublishEnemyCounter();

        if (enemy.IsBoss)
        {
            currentBoss = enemy;
            GameMgr.Instance?.PlayBossDeathSlowMotion();

            if (_levelClearTriggeredThisScene)
                return;

            _levelClearTriggeredThisScene = true;

            Debug.Log($"[EnemyMgr] Boss death triggered final flow: {enemy.BossDisplayName}");
            GameMgr.Instance?.HandleBossFinalDeathLevelComplete(enemy.EnemyType, enemy.transform.position);
            return;
        }

        if (_levelClearTriggeredThisScene)
            return;

        if (AreLevelClearConditionsMet())
        {
            _levelClearTriggeredThisScene = true;
            Debug.Log("[EnemyMgr] Non-boss level clear conditions met.");
            GameMgr.Instance?.HandleLevel1Completed();
        }
    }

    public void OnEnemyDestroyed(Enemy enemy)
    {
        if (enemy != null && activeEnemies.Contains(enemy))
            activeEnemies.Remove(enemy);

        var state = FindSceneEnemyState(enemy);
        if (state != null)
        {
            state.permanentlyDefeated = true;
        }

        if (enemy != null && enemy == currentBoss)
        {
            Debug.Log($"[EnemyMgr] Boss defeated: {enemy.BossDisplayName}");
            currentBoss = null;
        }

        PublishEnemyCounter();

        Debug.Log($"[EnemyMgr] Remaining enemies: {AliveEnemyCount}, required: {AliveCountableEnemyCount}, boss alive: {HasAliveBoss}");

        if (!_levelClearTriggeredThisScene && AreLevelClearConditionsMet())
        {
            _levelClearTriggeredThisScene = true;
            Debug.Log("[EnemyMgr] Level clear conditions met.");
            GameMgr.Instance?.HandleLevel1Completed();
        }
    }

    public List<Enemy> GetAllEnemies()
    {
        CleanupActiveEnemies();
        return activeEnemies;
    }

    public int GetAliveEnemyCountByType(EnemyType type)
    {
        CleanupActiveEnemies();

        int count = 0;

        foreach (var enemy in activeEnemies)
        {
            if (enemy == null) continue;
            if (enemy.EnemyType == type) count++;
        }

        return count;
    }

    public bool AreLevelClearConditionsMet()
    {
        bool bossDead = currentBoss == null || currentBoss.IsDeadOrDying;
        bool allRequiredEnemiesDead = AliveCountableEnemyCount == 0;

        return bossDead && allRequiredEnemiesDead;
    }

    public Enemy SpawnEnemy(GameObject prefab, Vector3 position, EnemySpawnOverrides overrides = null, EnemySpawnPoint ownerSpawnPoint = null)
    {
        if (prefab == null)
            return null;

        GameObject enemyObject = Instantiate(prefab, position, Quaternion.identity);
        Enemy enemy = enemyObject.GetComponent<Enemy>();

        if (enemy != null)
        {
            var warrior = GameMgr.Instance?.WarriorInstance;
            if (warrior != null)
                enemy.target = warrior.transform;

            enemy.SetSpawnOverrides(overrides);
            enemy.SetOwnerSpawnPoint(ownerSpawnPoint);

            ParentSpawnedEnemyToOwnerZone(enemy, ownerSpawnPoint);

            if (enemy.IsBoss)
            {
                enemy.SetBoss(true, enemy.BossDisplayName);
            }

            if (!activeEnemies.Contains(enemy))
                activeEnemies.Add(enemy);

            if (enemy.IsBoss)
            {
                currentBoss = enemy;
                Debug.Log($"[EnemyMgr] Boss registered: {enemy.BossDisplayName}");
            }

            PublishEnemyCounter();
        }

        return enemy;
    }
}
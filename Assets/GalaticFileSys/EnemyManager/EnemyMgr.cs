using Assets.Scripts.Characteres.EnemyContoller;
<<<<<<< HEAD
using Assets.Scripts.Platforms;
=======
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d
using System.Collections.Generic;
using UnityEngine;

public class EnemyMgr : MonoBehaviour
{
    public static EnemyMgr Instance { get; private set; }

    [SerializeField] private List<EnemyPrefabEntry> enemyPrefabs;

    private Dictionary<EnemyType, GameObject> prefabLookup;
<<<<<<< HEAD
    private readonly List<Enemy> activeEnemies = new List<Enemy>();
=======
    private List<Enemy> activeEnemies = new List<Enemy>();
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
<<<<<<< HEAD

        BuildPrefabLookup();
    }

    private void BuildPrefabLookup()
=======
    }

    private void Start()
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d
    {
        prefabLookup = new Dictionary<EnemyType, GameObject>();

        foreach (var entry in enemyPrefabs)
        {
<<<<<<< HEAD
            if (entry == null || entry.prefab == null)
                continue;

=======
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d
            if (!prefabLookup.ContainsKey(entry.type))
                prefabLookup.Add(entry.type, entry.prefab);
        }
    }

<<<<<<< HEAD
    private void EnsurePrefabLookup()
    {
        if (prefabLookup == null)
            BuildPrefabLookup();
    }

    public Enemy SpawnEnemy(EnemyType type, Vector3 position)
    {
        return SpawnEnemy(type, position, null);
    }

    public Enemy SpawnEnemy(EnemyType type, Vector3 position, EnemySpawnOverrides overrides = null)
    {
        EnsurePrefabLookup();

=======
    // Spawn by enemy type
    public Enemy SpawnEnemy(EnemyType type, Vector3 position)
    {
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d
        if (!prefabLookup.ContainsKey(type))
        {
            Debug.LogError("Enemy type not registered: " + type);
            return null;
        }

<<<<<<< HEAD
        GameObject prefab = prefabLookup[type];
        return SpawnEnemyByPrefab(prefab, position, overrides);
    }

    public Enemy SpawnEnemy(GameObject prefab, Vector3 position)
    {
        return SpawnEnemyByPrefab(prefab, position, null);
    }

    public Enemy SpawnEnemy(GameObject prefab, Vector3 position, EnemySpawnOverrides overrides = null)
    {
        return SpawnEnemyByPrefab(prefab, position, overrides);
    }

    private Enemy SpawnEnemyByPrefab(GameObject prefab, Vector3 position, EnemySpawnOverrides overrides = null)
    {
=======
        return SpawnEnemy(prefabLookup[type], position);
    }

    // Spawn by prefab
    public Enemy SpawnEnemy(GameObject prefab, Vector3 position)
    {
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d
        if (prefab == null)
            return null;

        GameObject enemyObject = Instantiate(prefab, position, Quaternion.identity);
<<<<<<< HEAD
        Enemy enemy = enemyObject.GetComponent<Enemy>();

        if (enemy == null)
        {
            Debug.LogError($"Spawned prefab '{prefab.name}' has no Enemy component.");
            Destroy(enemyObject);
            return null;
        }

        InitializeSpawnedEnemy(enemy, overrides);
        return enemy;
    }

    public Enemy SpawnEnemyOnMovingVerticalPlatform(
        EnemyType type,
        MovingVerticalPlatform platform,
        Transform spawnPoint = null,
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
            platform,
            spawnPoint,
            overrides
        );
    }

    public Enemy SpawnEnemyOnMovingVerticalPlatformByPrefab(
        GameObject prefab,
        MovingVerticalPlatform platform,
        Transform spawnPoint = null,
        EnemySpawnOverrides overrides = null)
    {
        return SpawnEnemyOnMovingVerticalPlatformInternal(
            prefab,
            platform,
            spawnPoint,
            overrides
        );
    }

    private Enemy SpawnEnemyOnMovingVerticalPlatformInternal(
        GameObject prefab,
        MovingVerticalPlatform platform,
        Transform spawnPoint,
        EnemySpawnOverrides overrides)
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

        InitializeSpawnedEnemy(enemy, overrides);

        // Let the enemy know which platform it belongs to,
        // but do NOT parent it to the platform.
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
=======

        Enemy enemy = enemyObject.GetComponent<Enemy>();

        if (enemy != null)
        {
            enemy.target = GameMgr.Instance.WarriorInstance.transform;
            activeEnemies.Add(enemy);
        }

        return enemy;
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d
    }

    public void OnEnemyDestroyed(Enemy enemy)
    {
<<<<<<< HEAD
        if (enemy != null && activeEnemies.Contains(enemy))
=======
        if (activeEnemies.Contains(enemy))
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d
            activeEnemies.Remove(enemy);
    }

    public List<Enemy> GetAllEnemies()
    {
        return activeEnemies;
    }
<<<<<<< HEAD
=======

    public Enemy SpawnEnemy(EnemyType type, Vector3 position, EnemySpawnOverrides overrides = null)
    {
        if (prefabLookup == null || !prefabLookup.ContainsKey(type))
        {
            Debug.LogError("Enemy type not registered: " + type);
            return null;
        }

        return SpawnEnemy(prefabLookup[type], position, overrides);
    }

    public Enemy SpawnEnemy(GameObject prefab, Vector3 position, EnemySpawnOverrides overrides = null)
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
            activeEnemies.Add(enemy);
        }

        return enemy;
    }
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d
}
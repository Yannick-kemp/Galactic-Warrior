<<<<<<< HEAD
﻿using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Platforms;
using UnityEngine;
=======
﻿using UnityEngine;
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d

public class EnemySpawnPoint : MonoBehaviour
{
    [SerializeField] private EnemyType enemyType;
    [SerializeField] private EnemySpawnOverrides overrides = new EnemySpawnOverrides();

<<<<<<< HEAD
    [Header("Optional moving platform owner")]
    [SerializeField] private MovingVerticalPlatform movingVerticalPlatform;

    [Header("Optional explicit spawn point")]
    [SerializeField] private Transform spawnPoint;

    [Header("Camera spawn")]
    [SerializeField] private float visibilityMargin = 0.2f;

    private bool spawned;

    private void Reset()
    {
        if (movingVerticalPlatform == null)
            movingVerticalPlatform = GetComponentInParent<MovingVerticalPlatform>();

        if (spawnPoint == null)
            spawnPoint = transform;
    }

    private void Awake()
    {
        if (movingVerticalPlatform == null)
            movingVerticalPlatform = GetComponentInParent<MovingVerticalPlatform>();

        if (spawnPoint == null)
            spawnPoint = transform;
    }

    private void Update()
=======
    private bool spawned;

    void Update()
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d
    {
        if (spawned) return;

        if (IsVisibleToCamera())
        {
<<<<<<< HEAD
            Enemy enemy = Spawn();

            if (enemy != null)
                spawned = true;
        }
    }

    private bool IsVisibleToCamera()
    {
        if (Camera.main == null)
            return false;

        Vector3 checkPos = movingVerticalPlatform != null
            ? movingVerticalPlatform.transform.position
            : transform.position;

        Vector3 viewportPos = Camera.main.WorldToViewportPoint(checkPos);

        return viewportPos.x >= -visibilityMargin && viewportPos.x <= 1f + visibilityMargin &&
               viewportPos.y >= -visibilityMargin && viewportPos.y <= 1f + visibilityMargin &&
               viewportPos.z > 0f;
    }

    private Enemy Spawn()
    {
        if (EnemyMgr.Instance == null)
            return null;

        Transform point = spawnPoint != null ? spawnPoint : transform;

        if (movingVerticalPlatform != null)
        {
            return EnemyMgr.Instance.SpawnEnemyOnMovingVerticalPlatform(
                enemyType,
                movingVerticalPlatform,
                point,
                overrides
            );
        }

        return EnemyMgr.Instance.SpawnEnemy(
            enemyType,
            point.position,
            overrides
        );
=======
            Spawn();
            spawned = true;
        }
    }

    bool IsVisibleToCamera()
    {
        if (Camera.main == null) return false;

        Vector3 viewportPos = Camera.main.WorldToViewportPoint(transform.position);
        float margin = 0.2f;

        return viewportPos.x >= -margin && viewportPos.x <= 1f + margin &&
               viewportPos.y >= -margin && viewportPos.y <= 1f + margin &&
               viewportPos.z > 0f;
    }

    void Spawn()
    {
        EnemyMgr.Instance?.SpawnEnemy(enemyType, transform.position, overrides);
>>>>>>> ef28a05e7f3e835850479d0c06d0acbd616b537d
    }
}
using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Platforms;
using UnityEngine;

public class EnemySpawnPoint : MonoBehaviour
{
    [SerializeField] private EnemyType enemyType;
    [SerializeField] private EnemySpawnOverrides overrides = new EnemySpawnOverrides();

    [Header("Optional moving platform owner")]
    [SerializeField] private MovingVerticalPlatform movingVerticalPlatform;

    [Header("Optional explicit spawn point")]
    [SerializeField] private Transform spawnPoint;

    [Header("Camera spawn")]
    [SerializeField] private float visibilityMargin = 0.2f;

    private Enemy currentEnemy;
    private bool permanentlyDefeated;

    public bool HasActiveEnemy => currentEnemy != null;

    public bool CountsForLevelClear =>
        enemyType != EnemyType.Bee &&
        enemyType != EnemyType.BeeEretic;

    public bool HasAliveCountableEnemy =>
        !permanentlyDefeated &&
        CountsForLevelClear;

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
    {
        if (permanentlyDefeated) return;
        if (currentEnemy != null) return;

        if (IsVisibleToCamera())
        {
            Enemy enemy = Spawn();

            if (enemy != null)
                currentEnemy = enemy;
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

        Enemy enemy;

        if (movingVerticalPlatform != null)
        {
            enemy = EnemyMgr.Instance.SpawnEnemyOnMovingVerticalPlatform(
                enemyType,
                movingVerticalPlatform,
                point,
                overrides,
                this
            );
        }
        else
        {
            enemy = EnemyMgr.Instance.SpawnEnemy(
                enemyType,
                point.position,
                overrides,
                this
            );
        }

        if (enemy != null)
            currentEnemy = enemy;

        return enemy;
    }

    public void NotifyEnemyDefeated(Enemy enemy)
    {
        if (enemy == null) return;

        if (currentEnemy == enemy)
            currentEnemy = null;

        permanentlyDefeated = true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        DrawGizmos(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawGizmos(true);
    }

    private void DrawGizmos(bool selected)
    {
        Transform point = spawnPoint != null ? spawnPoint : transform;
        Vector3 pos = point.position;

        Color color = ColorForType(enemyType);
        if (!CountsForLevelClear)
            color.a = 0.6f; // dim types that don't count for level clear (bees)

        // Marker at the spawn position.
        Gizmos.color = color;
        Gizmos.DrawWireSphere(pos, 0.35f);

        Color fill = color;
        fill.a = selected ? 0.35f : 0.15f;
        Gizmos.color = fill;
        Gizmos.DrawSphere(pos, 0.35f);

        // Line to the moving platform that owns this spawn, if any.
        if (movingVerticalPlatform != null)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(pos, movingVerticalPlatform.transform.position);
            Gizmos.DrawWireCube(movingVerticalPlatform.transform.position, Vector3.one * 0.2f);
        }

        // If the marker lives on a different object than the explicit spawn point, link them.
        if (spawnPoint != null && spawnPoint != transform)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(transform.position, pos);
        }

        // Label with the enemy type.
        var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
        style.normal.textColor = color;
        UnityEditor.Handles.Label(pos + Vector3.up * 0.55f, enemyType.ToString(), style);
    }

    private static Color ColorForType(EnemyType type)
    {
        switch (type)
        {
            case EnemyType.Hashagar: return new Color(0.85f, 0.30f, 0.30f);
            case EnemyType.Zalayty:  return new Color(0.95f, 0.60f, 0.15f);
            case EnemyType.Bee:      return new Color(0.95f, 0.90f, 0.20f);
            case EnemyType.BeeEretic:return new Color(0.70f, 0.65f, 0.10f);
            case EnemyType.Crawling: return new Color(0.40f, 0.75f, 0.35f);
            case EnemyType.M97:      return new Color(0.30f, 0.70f, 0.85f);
            case EnemyType.P39:      return new Color(0.30f, 0.50f, 0.90f);
            case EnemyType.Raka:     return new Color(0.65f, 0.40f, 0.85f);
            case EnemyType.Morvex:   return new Color(0.90f, 0.40f, 0.70f);
            case EnemyType.Hivernox: return new Color(0.55f, 0.85f, 0.90f);
            case EnemyType.Arachnee: return new Color(0.50f, 0.30f, 0.20f);
            case EnemyType.Zort:     return new Color(1.00f, 0.10f, 0.10f);
            case EnemyType.Wraith:   return new Color(0.60f, 0.60f, 0.70f);
            default:                 return Color.white;
        }
    }
#endif
}
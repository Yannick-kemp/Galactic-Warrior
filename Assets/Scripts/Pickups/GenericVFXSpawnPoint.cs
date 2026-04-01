using UnityEngine;

public class GenericVFXSpawnPoint : MonoBehaviour
{
    [Header("Prefab(s) to Spawn")]
    [SerializeField] private GameObject[] prefabs;

    [Header("Spawn Points (children transforms)")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Behavior")]
    [SerializeField] private bool spawnOnCameraEnter = true;
    [SerializeField] private float margin = 0.2f;
    [SerializeField] private bool parentToThis = true;

    [Header("Variation")]
    [SerializeField] private bool randomPrefab = false;
    [SerializeField] private bool randomRotation = false;
    [SerializeField] private bool randomScale = false;
    [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.2f);

    private bool spawned;

    void Update()
    {
        if (spawned) return;

        if (spawnOnCameraEnter && IsVisibleToCamera())
        {
            SpawnAll();
            spawned = true;
        }
    }

    bool IsVisibleToCamera()
    {
        if (Camera.main == null) return false;

        Vector3 vp = Camera.main.WorldToViewportPoint(transform.position);

        return vp.x >= -margin && vp.x <= 1 + margin &&
               vp.y >= -margin && vp.y <= 1 + margin &&
               vp.z > 0f;
    }

    void SpawnAll()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return;
        if (prefabs == null || prefabs.Length == 0) return;

        foreach (var point in spawnPoints)
        {
            if (point == null) continue;

            GameObject prefab = randomPrefab
                ? prefabs[Random.Range(0, prefabs.Length)]
                : prefabs[0];

            Quaternion baseRot = point.rotation * prefab.transform.rotation;

            Quaternion rot = randomRotation
                ? baseRot * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f))
                : baseRot;

            GameObject obj;

            if (VFXPool.Instance != null)
            {
                obj = VFXPool.Instance.Spawn(
                    prefab,
                    point.position,
                    rot,
                    parentToThis ? transform : null
                );

                var auto = obj.GetComponent<VFXAutoReturn>();
                if (auto != null)
                    auto.Init(prefab);
            }
            else
            {
                obj = Instantiate(
                    prefab,
                    point.position,
                    rot,
                    parentToThis ? transform : null
                );
            }

            Vector3 baseScale = prefab.transform.localScale;

            if (randomScale)
            {
                float s = Random.Range(scaleRange.x, scaleRange.y);
                obj.transform.localScale = baseScale * s;
            }
            else
            {
                obj.transform.localScale = baseScale;
            }
        }
    }
    [ContextMenu("Auto Fill Spawn Points")]
    void AutoFillSpawnPoints()
    {
        var points = new System.Collections.Generic.List<Transform>();

        foreach (Transform child in transform)
        {
            if (!child.gameObject.activeInHierarchy) continue;
            points.Add(child);
        }

        spawnPoints = points.ToArray();
    }
    void OnDrawGizmos()
    {
        if (spawnPoints == null) return;

        Gizmos.color = Color.cyan;

        foreach (var p in spawnPoints)
        {
            if (p == null) continue;
            Gizmos.DrawSphere(p.position, 0.2f);
        }
    }
    void OnValidate()
    {
        AutoFillSpawnPoints();
    }

}
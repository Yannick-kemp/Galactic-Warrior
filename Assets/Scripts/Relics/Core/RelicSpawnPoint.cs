using UnityEngine;

public class RelicSpawnPoint : MonoBehaviour
{
    [Header("Pickup Prefab (NOT RelicDefinition!)")]
    [SerializeField] private GameObject pickupPrefab;

    [Header("Spawn Settings")]
    [SerializeField] private bool spawnOnCameraEnter = true;
    [SerializeField] private float margin = 0.2f;

    private bool spawned;

    void Update()
    {
        if (spawned) return;

        if (spawnOnCameraEnter && IsVisibleToCamera())
        {
            Spawn();
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

    void Spawn()
    {
        if (pickupPrefab == null)
        {
            Debug.LogWarning("RelicSpawnPoint: No prefab assigned", this);
            return;
        }

        Instantiate(pickupPrefab, transform.position, Quaternion.identity);
    }
}
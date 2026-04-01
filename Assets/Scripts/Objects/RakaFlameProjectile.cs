using UnityEngine;

public class RakaFlameProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 6f;

    private Vector3 direction;

    public void Initialize(Transform target)
    {
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 dir = target.position - transform.position;

        // keep projectile horizontal
        dir.y = 0f;

        direction = dir.normalized;
    }

    void Update()
    {
        transform.position += direction * speed * Time.deltaTime;
    }
}
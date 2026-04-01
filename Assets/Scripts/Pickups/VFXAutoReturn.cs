using UnityEngine;

public class VFXAutoReturn : MonoBehaviour
{
    private GameObject prefab;

    public void Init(GameObject p)
    {
        prefab = p;
    }

    void OnEnable()
    {
        CancelInvoke();

        var ps = GetComponentInChildren<ParticleSystem>();
        float t = 2f;

        if (ps != null)
        {
            var main = ps.main;
            t = main.duration + main.startLifetime.constantMax;
        }

        Invoke(nameof(Return), t);
    }

    void OnDisable()
    {
        CancelInvoke();
    }

    void Return()
    {
        if (VFXPool.Instance != null && prefab != null)
            VFXPool.Instance.Despawn(prefab, gameObject);
        else
            Destroy(gameObject);
    }
}
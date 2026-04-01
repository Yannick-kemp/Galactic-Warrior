using System.Collections.Generic;
using UnityEngine;

public class VFXPool : MonoBehaviour
{
    public static VFXPool Instance;

    private readonly Dictionary<GameObject, Queue<GameObject>> pool = new();

    void Awake()
    {
        Instance = this;
    }

    public GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent = null)
    {
        if (!pool.ContainsKey(prefab))
            pool[prefab] = new Queue<GameObject>();

        var queue = pool[prefab];

        GameObject obj = queue.Count > 0 ? queue.Dequeue() : Instantiate(prefab);

        Transform t = obj.transform;

        // Parent first if wanted
        if (parent != null)
            t.SetParent(parent, true);
        else
            t.SetParent(null, true);

        // Reset transform
        t.SetPositionAndRotation(pos, rot);
        t.localScale = prefab.transform.localScale;

        obj.SetActive(true);

        // Reset all particle systems
        var systems = obj.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            ps.Clear(true);
            ps.Play(true);
        }

        return obj;
    }

    public void Despawn(GameObject prefab, GameObject obj)
    {
        // Optional: clear particle systems before pooling
        var systems = obj.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);
        }

        obj.SetActive(false);
        obj.transform.SetParent(transform, false); // optional pool root
        pool[prefab].Enqueue(obj);
    }
}
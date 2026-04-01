using System.Collections.Generic;
using UnityEngine;

public class EchoPool : MonoBehaviour
{
    private static EchoPool instance;
    public static EchoPool Instance => instance;

    [SerializeField] private GameObject echoPrefab;
    [SerializeField] private int poolSize = 20;

    private Queue<GameObject> echoPool = new Queue<GameObject>();

    private void Awake()
    {
        instance = this;

        for (int i = 0; i < poolSize; i++)
        {
            GameObject echo = Instantiate(echoPrefab);
            echo.SetActive(false);
            echoPool.Enqueue(echo);
        }
    }

    public GameObject GetEcho()
    {
        if (echoPool.Count > 0)
        {
            GameObject echo = echoPool.Dequeue();
            echo.SetActive(true);
            return echo;
        }

        return Instantiate(echoPrefab);
    }

    public void ReturnEcho(GameObject echo)
    {
        echo.SetActive(false);
        echoPool.Enqueue(echo);
    }
}

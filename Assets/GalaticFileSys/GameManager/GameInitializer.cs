using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    private void Awake()
    {
        Debug.Log("[GameInitializer] Awake()");

        if (GameMgr.Instance == null)
        {
            var go = new GameObject("GameManager");
            go.AddComponent<GameMgr>();
            Debug.Log("[GameInitializer] GameMgr created.");
        }

        if (InputMgr.Instance == null)
        {
            var go = new GameObject("InputManager");
            go.AddComponent<InputMgr>();
            Debug.Log("[GameInitializer] InputMgr created.");
        }

        if (TimerMgr.Instance == null)
        {
            var go = new GameObject("TimerManager");
            go.AddComponent<TimerMgr>();
            Debug.Log("[GameInitializer] TimerMgr created.");
        }
    }
}
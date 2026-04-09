using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    private void Awake()
    {
        if (GameMgr.Instance == null)
        {
            var go = new GameObject("GameManager");
            go.AddComponent<GameMgr>();
        }

        if (InputMgr.Instance == null)
        {
            var go = new GameObject("InputManager");
            go.AddComponent<InputMgr>();
        }

        if (TimerMgr.Instance == null)
        {
            var go = new GameObject("TimerManager");
            go.AddComponent<TimerMgr>();
        }
    }
}
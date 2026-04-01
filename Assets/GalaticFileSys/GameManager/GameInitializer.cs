using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    private void Start()
    {
        if (GameMgr.Instance == null)
        {
            GameObject gameManager = new GameObject("GameManager");
            gameManager.AddComponent<GameMgr>();
            GameMgr.Instance.Initialize();
        }
        if (InputMgr.Instance == null)
        {
            GameObject gameManager = new GameObject("InputManager");
            gameManager.AddComponent<InputMgr>();
            InputMgr.Instance.Initialize();
        }
        if (TimerMgr.Instance == null)
        {
            GameObject timerManager = new GameObject("TimerManager");
            timerManager.AddComponent<TimerMgr>();
            TimerMgr.Instance.Initialize();
        }
        //if (CameraMgr.Instance == null)
        //{
        //    GameObject cameraManager = new GameObject("CameraManager");
        //    cameraManager.AddComponent<CameraMgr>();
        //    CameraMgr.Instance.Initialize();
        //}
    }
}

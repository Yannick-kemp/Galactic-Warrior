using UnityEngine;

public class CameraMgr : MonoBehaviour
{
    public static CameraMgr Instance { get; private set; }

    private Camera mainCamera;
    private Transform currentTarget;
    private Vector3 offset;
    private float followSpeed;
    private CameraMode currentMode;

    private void Awake()
    {
        // Scene-local on purpose. The MainCamera carries this scene's BackGroundLoop (parallax)
        // configuration, so it must NOT survive a scene change: a persistent (DontDestroyOnLoad)
        // camera would leak the first scene's background into every later scene, and the new
        // scene's own camera would be destroyed by the singleton dedup. Each scene owns its camera.
        Instance = this;
        mainCamera = Camera.main;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetCameraMode(CameraMode mode, Transform target = null, Vector3? customOffset = null, float speed = 5f)
    {
        currentMode = mode;

        switch (mode)
        {
            case CameraMode.FollowHero:
                if (target != null)
                {
                    currentTarget = target;
                    offset = customOffset ?? new Vector3(0, 5, -10);
                    followSpeed = speed;
                    StartCoroutine(FollowTargetCoroutine());
                }
                break;

            case CameraMode.Cinematic:
                // Handle cinematic logic (e.g., fixed position, pan across scene, etc.)
                break;

            case CameraMode.FreeRoam:
                // Handle free camera control (e.g., controlled by player input)
                break;
        }
    }

    private System.Collections.IEnumerator FollowTargetCoroutine()
    {
        while (currentMode == CameraMode.FollowHero && currentTarget != null)
        {
            //Vector3 desiredPosition = currentTarget.position + offset;
            // Vector3 smoothedPosition = Vector3.Lerp(mainCamera.transform.position, desiredPosition, followSpeed * Time.deltaTime);
            mainCamera.transform.position = new Vector3(currentTarget.position.x, currentTarget.position.y, transform.position.z);
            // mainCamera.transform.position = smoothedPosition;

            //  mainCamera.transform.LookAt(currentTarget);
            yield return null;
        }
    }

    public void Initialize()
    {
        Debug.Log("CameraManager Initialized");
    }
}

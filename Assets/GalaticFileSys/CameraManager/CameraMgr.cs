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

    /// <summary>This scene's main camera (lazily falls back to Camera.main). Single camera source
    /// of truth for systems like zone culling.</summary>
    public Camera MainCamera
    {
        get
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
            return mainCamera;
        }
    }

    /// <summary>World-space rectangle currently visible by the camera (XY plane). Returns false if
    /// there is no camera. Handles orthographic (this game) and a perspective fallback.</summary>
    public bool TryGetVisibleWorldRect(out Rect rect)
    {
        rect = default;

        Camera cam = MainCamera;
        if (cam == null)
            return false;

        Vector3 c = cam.transform.position;

        if (cam.orthographic)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            rect = new Rect(c.x - halfW, c.y - halfH, halfW * 2f, halfH * 2f);
            return true;
        }

        // Perspective fallback: frustum size at the camera's distance to the z = 0 play plane.
        float dist = Mathf.Abs(c.z);
        float h = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * cam.aspect;
        rect = new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h);
        return true;
    }
}

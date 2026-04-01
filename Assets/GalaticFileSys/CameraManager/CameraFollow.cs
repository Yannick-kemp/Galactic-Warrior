using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] float xMax;
    [SerializeField] float yMax;
    [SerializeField] public float xMin;
    [SerializeField] public float yMin;
    [SerializeField] float smoothSpeed = 5f;

    private Transform target;
    private bool _warnedMissingTarget;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        TryFindTarget();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // old target reference is gone after reload
        target = null;
        _warnedMissingTarget = false;

        // try immediately; if warrior spawns a bit later, LateUpdate will retry
        TryFindTarget();
    }

    private void LateUpdate()
    {
        // Reacquire if target was destroyed / not ready yet
        if (target == null)
        {
            TryFindTarget();
            if (target == null) return;
        }

        // KEEP YOUR ORIGINAL FOLLOW BEHAVIOR (no x clamp)
        Vector3 desiredPosition = new Vector3(
            Mathf.Clamp(target.position.x, float.MinValue, float.MaxValue),
            Mathf.Clamp(target.position.y, yMin, float.MaxValue),
            transform.position.z
        );

        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        transform.position = smoothedPosition;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        _warnedMissingTarget = false;
    }

    private void TryFindTarget()
    {
        // 1) Best: Warrior singleton
        if (Warrior.Instance != null)
        {
            target = Warrior.Instance.transform;
            return;
        }

        // 2) Fallback: by type
        Warrior w = FindFirstObjectByType<Warrior>();
        if (w != null)
        {
            target = w.transform;
            return;
        }

        // 3) Last fallback: name search (your original style)
        var go = GameObject.Find("Warrior");
        if (go != null)
        {
            target = go.transform;
            return;
        }

        if (!_warnedMissingTarget)
        {
            Debug.LogWarning("[CameraFollow] Target not found yet. Will retry.");
            _warnedMissingTarget = true;
        }
    }
    public void SnapImmediately()
    {
        if (target == null) return;

        transform.position = new Vector3(
            target.position.x,
            Mathf.Clamp(target.position.y, yMin, float.MaxValue),
            transform.position.z
        );
    }
}
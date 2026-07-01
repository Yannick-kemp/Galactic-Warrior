using UnityEngine;

public class DebugSaveReset : MonoBehaviour
{
    [SerializeField] private bool resetProgressOnStart = false;

    private void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!resetProgressOnStart) return;

        // Single source of truth: wipes everything incl. tutorial, checkpoints and progression.
        if (GameMgr.Instance != null)
        {
            GameMgr.Instance.ResetAllProgressForDev();
        }
        else
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        Debug.Log("[DebugSaveReset] Full dev reset (incl. tutorial, checkpoints, progression).");
#endif
    }
}
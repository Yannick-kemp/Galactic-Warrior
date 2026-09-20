using UnityEngine;

public class DebugSaveHotkeys : MonoBehaviour
{
    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F9))
        {
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

            Debug.Log("[DebugSaveHotkeys] Full dev reset (incl. tutorial, checkpoints, boss relics).");
        }
#endif
    }
}
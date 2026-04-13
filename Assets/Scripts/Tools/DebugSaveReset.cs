using UnityEngine;

public class DebugSaveReset : MonoBehaviour
{
    [SerializeField] private bool resetProgressOnStart = false;

    private void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!resetProgressOnStart) return;

        PlayerPrefs.DeleteKey("GW_CampaignPurchased");
        PlayerPrefs.DeleteKey("GW_HighestReachedSceneIndex");
        PlayerPrefs.DeleteKey("GW_Level2Unlocked");
        PlayerPrefs.Save();

        Debug.Log("[DebugSaveReset] Progress reset.");
#endif
    }
}
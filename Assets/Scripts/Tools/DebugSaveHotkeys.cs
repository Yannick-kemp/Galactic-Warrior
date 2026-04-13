using UnityEngine;

public class DebugSaveHotkeys : MonoBehaviour
{
    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F9))
        {
            PlayerPrefs.DeleteKey("GW_CampaignPurchased");
            PlayerPrefs.DeleteKey("GW_HighestReachedSceneIndex");
            PlayerPrefs.DeleteKey("GW_Level2Unlocked");
            PlayerPrefs.Save();

            Debug.Log("[DebugSaveHotkeys] Progress reset.");
        }
#endif
    }
}
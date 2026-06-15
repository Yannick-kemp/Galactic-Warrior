#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only helper to reset the persistent boss-relic counter for testing.
/// Menu: Tools ▸ Galactic Warrior ▸ Reset Boss Relics.
/// Works in Play mode (clears memory + disk via GameMgr) and in Edit mode (clears disk key).
/// Independent of the F9 debug hotkey, so it works regardless of the active scene or input backend.
/// </summary>
public static class ResetBossRelicsMenu
{
    private const string BossRelicsKey = "GW_BossRelicsDefeated";

    [MenuItem("Tools/Galactic Warrior/Reset Boss Relics")]
    public static void ResetBossRelics()
    {
        if (Application.isPlaying && GameMgr.Instance != null)
        {
            // Clears the in-memory set AND the disk key, and refreshes the RelicMemory CountText.
            GameMgr.Instance.ClearBossRelics();
        }
        else
        {
            PlayerPrefs.DeleteKey(BossRelicsKey);
            PlayerPrefs.Save();
        }

        Debug.Log("[ResetBossRelicsMenu] Boss relics reset to 0.");
    }
}
#endif

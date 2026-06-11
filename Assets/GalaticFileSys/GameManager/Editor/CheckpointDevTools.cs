#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only developer utilities for the checkpoint save system.
/// Lets you wipe the persisted checkpoint so a level starts from its default spawn again.
/// Works both in Edit mode and during a Play session.
/// </summary>
public static class CheckpointDevTools
{
    private const string MenuPath = "Galactic Warrior/Reset Saved Checkpoint";

    [MenuItem(MenuPath, false, 0)]
    private static void ResetSavedCheckpoint()
    {
        // Clears in-memory state when playing, and always deletes the on-disk PlayerPrefs keys.
        GameMgr.DevResetSavedCheckpoint();
        Debug.Log("[CheckpointDevTools] Saved checkpoint reset. Next level launch uses the default spawn.");
    }
}
#endif

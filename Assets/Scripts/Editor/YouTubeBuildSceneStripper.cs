using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Web demo builds only (WebGL + YOUTUBE_PLAYABLES or POLYMART_SITE): removes, from the scenes
/// being built, references that would drag other chapters' content into the demo bundle. Only the
/// copy of the scene that goes into the build is changed; scene and prefab files stay untouched.
///
///   - EnemyMgr keeps the enemy prefabs that the scene's spawn points actually use
///     (the shared _EnemyTools lists every enemy of the game).
///   - GameMgr loses the music of the chapters that are not shipped.
///   - Purchase screens are removed: nothing is sold in the web demo.
///
/// Found by reading the build report: Hashagar alone was ~60 MB of textures the demo never shows.
/// </summary>
public class YouTubeBuildSceneStripper : IProcessSceneWithReport
{
    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        // report is null when entering Play Mode: never strip outside a real web demo build.
        if (report == null || report.summary.platform != BuildTarget.WebGL)
            return;

        string[] defines = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.WebGL).Split(';');
        if (!defines.Contains(YouTubePlayablesBuilder.Define) && !defines.Contains(YouTubePlayablesBuilder.SiteDefine))
            return;

        StripEnemyPrefabs(scene);
        StripOtherChapterMusic(scene);
        StripPurchaseScreens(scene);
    }

    private static IEnumerable<T> InScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (T component in root.GetComponentsInChildren<T>(true))
            yield return component;
    }

    private static void StripEnemyPrefabs(Scene scene)
    {
        var usedTypes = new HashSet<int>();
        foreach (EnemySpawnPoint spawnPoint in InScene<EnemySpawnPoint>(scene))
            usedTypes.Add(new SerializedObject(spawnPoint).FindProperty("enemyType").enumValueIndex);

        foreach (EnemyMgr mgr in InScene<EnemyMgr>(scene))
        {
            var so = new SerializedObject(mgr);
            SerializedProperty list = so.FindProperty("enemyPrefabs");
            var removed = new List<string>();

            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty type = list.GetArrayElementAtIndex(i).FindPropertyRelative("type");
                if (usedTypes.Contains(type.enumValueIndex))
                    continue;

                removed.Add(type.enumNames[type.enumValueIndex]);
                list.DeleteArrayElementAtIndex(i);
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            if (removed.Count > 0)
                Debug.Log($"[YouTubeBuildSceneStripper] {scene.name}: enemy prefabs not spawned by the demo removed: {string.Join(", ", removed)}");
        }
    }

    private static void StripOtherChapterMusic(Scene scene)
    {
        foreach (GameMgr mgr in InScene<GameMgr>(scene))
        {
            var so = new SerializedObject(mgr);
            foreach (string field in new[] { "level2Music", "level3Music", "level4Music" })
            {
                SerializedProperty clip = so.FindProperty(field);
                if (clip != null)
                    clip.objectReferenceValue = null;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[YouTubeBuildSceneStripper] {scene.name}: music of the chapters not shipped removed from GameMgr.");
        }
    }

    private static void StripPurchaseScreens(Scene scene)
    {
        var roots = new HashSet<GameObject>();
        foreach (PurchaseUI purchase in InScene<PurchaseUI>(scene))
        {
            // The purchase screen normally has its own canvas (PurchaseUICanvas): drop that canvas.
            // If it ever sits under a shared HUD canvas, only its own object goes.
            Canvas canvas = purchase.GetComponentInParent<Canvas>(true);
            bool ownCanvas = canvas != null && canvas.rootCanvas.gameObject.name.Contains("Purchase");
            roots.Add(ownCanvas ? canvas.rootCanvas.gameObject : purchase.gameObject);
        }

        foreach (GameObject root in roots)
        {
            Debug.Log($"[YouTubeBuildSceneStripper] {scene.name}: purchase screen '{root.name}' removed.");
            Object.DestroyImmediate(root);
        }
    }
}

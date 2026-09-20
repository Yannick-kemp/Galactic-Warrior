using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlatformSceneThemeLoader : MonoBehaviour
{
    [System.Serializable]
    public class SceneThemeEntry
    {
        public string sceneName;
        public PlatformTheme theme;
    }

    [SerializeField] private PlatformTheme fallbackTheme;
    [SerializeField] private SceneThemeEntry[] sceneThemes;

    private IEnumerator Start()
    {
        // Wait one frame so platform Start() methods finish first.
        yield return null;

        string activeSceneName = SceneManager.GetActiveScene().name;
        PlatformTheme chosenTheme = GetThemeForScene(activeSceneName);

        if (chosenTheme == null)
            yield break;

        PlatFormColliderTrigger[] platforms =
            FindObjectsByType<PlatFormColliderTrigger>(FindObjectsSortMode.None);

        foreach (var platform in platforms)
        {
            if (platform != null)
                platform.ApplyVisualTheme(chosenTheme);
        }
    }

    private PlatformTheme GetThemeForScene(string sceneName)
    {
        if (sceneThemes != null)
        {
            for (int i = 0; i < sceneThemes.Length; i++)
            {
                if (sceneThemes[i] != null &&
                    !string.IsNullOrWhiteSpace(sceneThemes[i].sceneName) &&
                    sceneThemes[i].sceneName == sceneName)
                {
                    return sceneThemes[i].theme;
                }
            }
        }

        return fallbackTheme;
    }
}
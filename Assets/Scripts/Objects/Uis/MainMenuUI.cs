using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [Header("Scene names (must be in Build Settings)")]
    [SerializeField] private string gameScene = "WarriorScene";
    [SerializeField] private string relicsScene = "Relics";
    [SerializeField] private string upgradesScene = "Upgrades";

    public void Play() => SceneManager.LoadScene(gameScene);
    public void Relics() => SceneManager.LoadScene(relicsScene);
    public void Upgrades() => SceneManager.LoadScene(upgradesScene);

    [SerializeField] private SettingsPopupUI settingsPopup;

    public void Settings()
    {
        settingsPopup.Show();
    }

    public void Credits()
    {
        // open a panel or load scene
        Debug.Log("Open credits");
    }

    public void Exit()
    {
        Application.Quit();

#if UNITY_EDITOR
        // So it also "works" when testing in the Editor
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Declencheur sans clavier du mode d'essai, pour l'appareil.
///
/// F9 (DebugSaveHotkeys / GameMgr) est inutilisable sur un telephone. Ce composant ecoute donc un
/// APPUI LONG D'UN SEUL DOIGT DANS LE COIN HAUT GAUCHE, maintenu trois secondes sans bouger. Dans
/// l'Editeur, la touche F10 et le meme appui long a la souris font la meme chose.
///
/// Pourquoi pas plusieurs doigts: Android reserve les gestes a trois doigts au systeme (capture
/// d'ecran sur la plupart des telephones), donc le geste partait en photo au lieu d'atteindre le
/// jeu. Un appui long immobile, lui, n'est revendique par aucun geste systeme.
///
/// Le coin haut gauche est choisi parce qu'il est vide dans le menu, d'ou l'on arme le mode. Un
/// appui long ailleurs dans le jeu declencherait aussi un deplacement, sans consequence: la bascule
/// efface la progression et ramene au menu de toute facon.
///
/// Le geste BASCULE: il efface toute la progression sauvegardee, puis arme ou desarme la simulation
/// "jamais achete", puis recharge la scene du menu pour repartir d'un etat propre.
///
/// L'ordre compte: l'effacement passe par PlayerPrefs.DeleteAll, qui emporterait le drapeau de
/// simulation s'il etait pose avant. On efface d'abord, on arme ensuite.
///
/// S'installe tout seul au lancement et ne vit que dans l'Editeur ou une build de developpement:
/// rien de tout ceci n'existe dans la build de production.
/// </summary>
public sealed class DevTestModeGesture : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD

    private const float RequiredHoldSeconds = 3f;
    private const KeyCode EditorKey = KeyCode.F10;

    // Coin actif, en fraction de l'ecran. Haut gauche: vide dans le menu.
    private const float CornerWidthFraction = 0.18f;
    private const float CornerHeightFraction = 0.18f;

    // Tolerance de bougé du doigt pendant le maintien, en fraction de la plus petite dimension.
    // Un doigt pose n'est jamais parfaitement immobile.
    private const float DriftToleranceFraction = 0.06f;

    private const float ToastSeconds = 3.5f;

    private float _holdStartedAt = -1f;
    private Vector2 _holdOrigin;
    private string _toast;
    private float _toastUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("_DevTestModeGesture");
        go.AddComponent<DevTestModeGesture>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        if (Input.GetKeyDown(EditorKey))
        {
            Toggle();
            return;
        }

        if (!TryGetPressPosition(out Vector2 press))
        {
            _holdStartedAt = -1f;
            return;
        }

        // Le maintien doit COMMENCER dans le coin. Un doigt pose ailleurs qui derive jusqu'au coin
        // ne doit pas declencher la bascule.
        if (_holdStartedAt < 0f)
        {
            if (!IsInCorner(press))
                return;

            _holdStartedAt = Time.unscaledTime;
            _holdOrigin = press;
            return;
        }

        float tolerance = Mathf.Min(Screen.width, Screen.height) * DriftToleranceFraction;

        if ((press - _holdOrigin).sqrMagnitude > tolerance * tolerance)
        {
            // Trop de mouvement: c'est un glissement, pas un appui maintenu.
            _holdStartedAt = -1f;
            return;
        }

        if (Time.unscaledTime - _holdStartedAt < RequiredHoldSeconds)
            return;

        _holdStartedAt = -1f;
        Toggle();
    }

    /// <summary>
    /// Un seul point de contact: le premier doigt sur l'appareil, le bouton gauche dans l'Editeur.
    /// Plusieurs doigts poses annulent, pour ne pas confondre avec un geste systeme.
    /// </summary>
    private static bool TryGetPressPosition(out Vector2 position)
    {
        position = Vector2.zero;

        if (Input.touchCount == 1)
        {
            position = Input.GetTouch(0).position;
            return true;
        }

        if (Input.touchCount == 0 && Input.GetMouseButton(0))
        {
            position = Input.mousePosition;
            return true;
        }

        return false;
    }

    private static bool IsInCorner(Vector2 position)
    {
        // L'origine des coordonnees d'entree est EN BAS a gauche: le haut de l'ecran est un y grand.
        return position.x <= Screen.width * CornerWidthFraction &&
               position.y >= Screen.height * (1f - CornerHeightFraction);
    }

    private void Toggle()
    {
        bool wasActive = DevPurchaseSimulation.IsActive;

        // 1. Effacer d'abord: DeleteAll emporte aussi le drapeau de simulation.
        if (GameMgr.Instance != null)
        {
            GameMgr.Instance.ResetAllProgressForDev();
        }
        else
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        // 2. Poser l'etat voulu ensuite.
        DevPurchaseSimulation.SetActive(!wasActive);

        _toast = "Progression effacee\n" + DevPurchaseSimulation.StateLabel;
        _toastUntil = Time.unscaledTime + ToastSeconds;

        Debug.Log("[DEV-ACHAT] bascule du mode d'essai -> " + DevPurchaseSimulation.StateLabel +
                  " | progression effacee, retour au menu.");

        Time.timeScale = 1f;

        // 3. Repartir du menu, sinon on reste dans une scene dont l'etat vient d'etre efface.
        // LoadMainMenu fait deja le menage complet: timeScale, run en cours, et destruction du
        // Warrior persistant. Le repli direct ne sert que si aucun GameMgr n'est joignable.
        if (GameMgr.Instance != null)
            GameMgr.Instance.LoadMainMenu();
        else
            SceneManager.LoadScene(0);
    }

    /// <summary>
    /// Retour visuel indispensable: sur un telephone le testeur n'a pas la console sous les yeux et
    /// ne saurait pas si le geste a ete pris en compte, ni dans quel etat il vient de basculer.
    /// </summary>
    private void OnGUI()
    {
        DrawHoldProgress();

        if (Time.unscaledTime > _toastUntil || string.IsNullOrEmpty(_toast))
            return;

        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = Mathf.RoundToInt(Screen.height * 0.028f),
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        float w = Screen.width * 0.7f;
        float h = Screen.height * 0.16f;

        GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.08f, w, h), _toast, style);
    }

    /// <summary>
    /// Jauge de remplissage pendant le maintien. Sans elle, le testeur garde le doigt pose trois
    /// secondes a l'aveugle et ne sait pas si le coin est le bon ni si le geste est pris en compte.
    /// </summary>
    private void DrawHoldProgress()
    {
        if (_holdStartedAt < 0f)
            return;

        float progress = Mathf.Clamp01((Time.unscaledTime - _holdStartedAt) / RequiredHoldSeconds);

        float barWidth = Screen.width * CornerWidthFraction;
        float barHeight = Mathf.Max(6f, Screen.height * 0.012f);

        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(0f, 0f, barWidth, barHeight), Texture2D.whiteTexture);

        GUI.color = Color.Lerp(Color.yellow, Color.green, progress);
        GUI.DrawTexture(new Rect(0f, 0f, barWidth * progress, barHeight), Texture2D.whiteTexture);

        GUI.color = Color.white;
    }

#endif
}

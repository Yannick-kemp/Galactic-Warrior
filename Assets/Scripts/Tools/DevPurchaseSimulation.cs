using UnityEngine;

/// <summary>
/// Interrupteur de developpement: faire croire au jeu que ce joueur n'a JAMAIS achete la campagne,
/// sur un appareil ou le compte Google la possede pourtant.
///
/// Pourquoi il faut un interrupteur et pas seulement un effacement des donnees: au demarrage,
/// <see cref="PurchaseUI"/> appelle la restauration des achats. Google repond que le produit non
/// consommable est possede, et le jeu se redeverrouille en silence quelques secondes apres le
/// lancement. Effacer les preferences ne sert donc a rien tant que la restauration repasse derriere.
///
/// Quand l'interrupteur est arme:
///   - la restauration au demarrage est ignoree;
///   - l'appui sur Acheter deverrouille LOCALEMENT, sans passer par Google, pour pouvoir rejouer la
///     sequence autant de fois qu'on veut.
///
/// Ce que ce mode ne teste PAS: la vraie feuille d'achat Google, son prix et ses erreurs. Pour ca il
/// faut retirer la possession cote Play Console, ou utiliser un compte qui n'a jamais achete.
///
/// En build de production l'interrupteur n'existe pas: <see cref="IsActive"/> renvoie toujours faux
/// et le compilateur elimine les branches qui en dependent.
/// </summary>
public static class DevPurchaseSimulation
{
    private const string SimulateNotPurchasedKey = "GW_DevSimulateNotPurchased";

    /// <summary>Vrai quand le jeu doit se comporter comme si la campagne n'avait jamais ete achetee.</summary>
    public static bool IsActive
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return PlayerPrefs.GetInt(SimulateNotPurchasedKey, 0) == 1;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Arme ou desarme le mode. A appeler APRES un effacement complet des preferences: le drapeau
    /// est lui-meme une preference, et <c>PlayerPrefs.DeleteAll</c> l'emporterait.
    /// </summary>
    public static void SetActive(bool active)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (active)
            PlayerPrefs.SetInt(SimulateNotPurchasedKey, 1);
        else
            PlayerPrefs.DeleteKey(SimulateNotPurchasedKey);

        PlayerPrefs.Save();

        Debug.Log("[DEV-ACHAT] simulation 'jamais achete' " + (active ? "ARMEE" : "DESARMEE") + ".");
#endif
    }

    /// <summary>Etiquette courte pour l'affichage a l'ecran pendant les essais.</summary>
    public static string StateLabel
    {
        get { return IsActive ? "SIMULATION : jamais achete" : "NORMAL : achat reel"; }
    }
}

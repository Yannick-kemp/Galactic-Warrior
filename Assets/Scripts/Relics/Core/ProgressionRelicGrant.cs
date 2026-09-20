using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Relics.Core
{
    /// <summary>
    /// Accorde au demarrage d'un chapitre les reliques de PROGRESSION que le joueur devrait deja
    /// posseder, plutot que de lui interdire l'entree.
    ///
    /// Pourquoi c'est necessaire, mesure dans le projet:
    ///   1. `GameMgr` DETRUIT le Warrior persistant a chaque transition de chapitre ("Destroy the
    ///      persistent warrior so the next level spawns a fresh one instead of inheriting this
    ///      level's accumulated state"). L'inventaire de `RelicManager` vit sur ce Warrior et n'est
    ///      ecrit nulle part sur le disque. Une relique ramassee dans la demo est donc perdue au
    ///      passage au chapitre suivant, meme dans une seule et meme partie.
    ///   2. `relic-rare-memory` n'a qu'un producteur dans tout le projet, la table de butin montee
    ///      sur Hashagar. Un joueur qui entre directement dans un chapitre par le menu ne peut donc
    ///      pas l'avoir.
    ///
    /// La regle retenue: si le joueur a achete la campagne et entre dans un chapitre autre que la
    /// demo, toute relique de progression manquante lui est donnee en silence. Personne ne peut
    /// rester coince, et la promesse "l'achat debloque tout le jeu" reste vraie.
    ///
    /// Volontairement NON couvert ici: la demo elle-meme (index 0), ou les reliques doivent encore
    /// se meriter, et les reliques de butin ordinaires (soin, bouclier, sprint), qui sont du jeu et
    /// pas de la progression.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RelicManager))]
    public sealed class ProgressionRelicGrant : MonoBehaviour
    {
        [Header("Relique de boss de la demo")]
        [Tooltip("ON = accorde la relique du boss de la demo a l'entree d'un chapitre paye, si le joueur ne l'a pas encore. C'est elle que compte l'emplacement Memoire de l'interface.")]
        [SerializeField] private bool grantDemoBossRelic = true;

        [Tooltip("Boss de la demo dont la relique est accordee d'office. Raka par defaut.")]
        [SerializeField] private EnemyType demoBossType = EnemyType.Raka;

        [Header("Reliques d'inventaire (optionnel)")]
        [Tooltip("Objets d'inventaire a completer en plus. Laisser VIDE: la regle du jeu porte sur la relique de boss ci-dessus, pas sur un objet ramassable.")]
        [SerializeField] private List<RelicDefinition> progressionRelics = new List<RelicDefinition>();

        [Header("Regle")]
        [Tooltip("OFF = plus aucun octroi automatique, ni relique de boss ni objet d'inventaire.")]
        [SerializeField] private bool grantMissingProgressionRelics = true;

        [Tooltip("Premier index de chapitre concerne. 0 est la demo, ou la relique doit encore se meriter, donc 1 par defaut.")]
        [SerializeField, Min(0)] private int firstChapterIndexRequiringGrant = 1;

        [Tooltip("ON = n'accorde qu'aux joueurs qui ont achete la campagne. Un joueur en demo ne voit de toute facon que le chapitre 0.")]
        [SerializeField] private bool requirePurchase = true;

        [Tooltip("Nombre d'exemplaires vise. L'octroi complete jusqu'a ce nombre, il ne s'ajoute pas a un stock deja suffisant.")]
        [SerializeField, Min(1)] private int targetCount = 1;

        [Header("Diagnostic")]
        [Tooltip("ON = une ligne par relique reellement accordee. Rare par nature: au plus une par entree de chapitre.")]
        [SerializeField] private bool logGrants = true;

        [Tooltip("Nombre de trames d'attente maximum pour que GameMgr soit joignable. Son Awake peut finir apres celui du Warrior.")]
        [SerializeField, Min(1)] private int maxFramesWaitingForGameMgr = 120;

        private RelicManager _relics;

        private IEnumerator Start()
        {
            if (!grantMissingProgressionRelics)
            {
                NoteRefusal("octroi desactive sur le Warrior");
                yield break;
            }

            _relics = GetComponent<RelicManager>();

            // GameMgr.Instance peut etre null alors qu'un GameMgr existe: son Awake n'est pas
            // forcement termine quand celui du Warrior l'est. On attend, borne, plutot que d'abandonner.
            GameMgr mgr = null;

            for (int frame = 0; frame < maxFramesWaitingForGameMgr; frame++)
            {
                mgr = GameMgr.Instance != null ? GameMgr.Instance : FindFirstObjectByType<GameMgr>();

                if (mgr != null)
                    break;

                yield return null;
            }

            if (mgr == null)
            {
                NoteRefusal("GameMgr introuvable apres " + maxFramesWaitingForGameMgr + " trames");
                yield break;
            }

            int chapterIndex = mgr.CurrentCampaignSceneIndex;

            // -1 = on n'est pas dans une scene de campagne (le menu). Rien a accorder.
            if (chapterIndex < firstChapterIndexRequiringGrant)
            {
                NoteRefusal("chapitre " + chapterIndex + " < " + firstChapterIndexRequiringGrant +
                            " (scene active='" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "')");
                yield break;
            }

            if (requirePurchase && !mgr.HasCampaignPurchase)
            {
                NoteRefusal("campagne non achetee (HasCampaignPurchase=false)");
                yield break;
            }

            GrantDemoBossRelicIfMissing(mgr, chapterIndex);
            GrantMissing(chapterIndex);
        }

        /// <summary>
        /// Accorde la relique du boss de la demo a qui entre dans un chapitre paye sans l'avoir.
        ///
        /// La non-duplication est deja garantie par GameMgr: les reliques de boss sont un ensemble
        /// indexe par type d'ennemi, et <c>TryGrantBossRelic</c> renvoie faux si le type y figure
        /// deja. Un joueur servi d'office ici ne recevra donc plus rien s'il va tuer le boss de la
        /// demo ensuite, et celui qui a vraiment tue ce boss ne recoit rien en double en entrant
        /// dans un chapitre. Le compte reste juste dans les deux sens, sans code supplementaire.
        /// </summary>
        private void GrantDemoBossRelicIfMissing(GameMgr mgr, int chapterIndex)
        {
            if (!grantDemoBossRelic)
                return;

            bool granted = mgr.TryGrantBossRelic(demoBossType);

            if (!logGrants)
                return;

            if (granted)
            {
                Debug.Log("[PROGRESSION] relique de boss accordee d'office a l'entree du chapitre " + chapterIndex +
                          " : " + demoBossType + " (demo non jouee) | total=" + mgr.BossRelicCount, this);
            }
            else
            {
                Debug.Log("[PROGRESSION] relique de boss " + demoBossType +
                          " deja possedee, rien a accorder | total=" + mgr.BossRelicCount, this);
            }
        }

        /// <summary>
        /// Chaque sortie anticipee dit pourquoi. Sans ca, un octroi qui n'a pas lieu est muet et on
        /// ne peut pas distinguer "la regle a refuse" de "le composant n'a jamais demarre".
        /// </summary>
        private void NoteRefusal(string reason)
        {
            if (!logGrants)
                return;

            Debug.Log("[PROGRESSION] aucun octroi : " + reason, this);
        }

        private void GrantMissing(int chapterIndex)
        {
            if (_relics == null || progressionRelics == null)
                return;

            for (int i = 0; i < progressionRelics.Count; i++)
            {
                RelicDefinition def = progressionRelics[i];
                if (def == null)
                    continue;

                int owned = _relics.GetCount(def);
                int missing = targetCount - owned;

                if (missing <= 0)
                    continue;

                int granted = 0;

                // bypassFrameCap: l'octroi se fait en une seule trame et le plafond par trame est
                // concu contre le double ramassage d'un meme objet, pas contre une remise scriptee.
                for (int n = 0; n < missing; n++)
                {
                    if (_relics.Collect(def, bypassFrameCap: true))
                        granted++;
                }

                if (logGrants && granted > 0)
                {
                    Debug.Log("[PROGRESSION] relique accordee a l'entree du chapitre " + chapterIndex +
                              " : " + (!string.IsNullOrEmpty(def.relicId) ? def.relicId : def.name) +
                              " | stock " + owned + " -> " + _relics.GetCount(def), this);
                }
            }
        }
    }
}

using Assets.Scripts.Platforms;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    /// <summary>
    /// Diagnostic pur: "par moments le Warrior n'arrive pas a sauter depuis une plateforme
    /// mouvante" (Plf-bck_moving_Var, qui derive de PlatFormPlfColliderTrigger comme les plf
    /// fixes, donc toutes les regles de bord s'y appliquent aussi).
    ///
    /// Les deux chemins de saut refusent sur les memes conditions: `CanMove`, `CountGroundPoints()
    /// == 0`, `IsFallingEdge`, `IsFallingGrazesEdge`, `CanDie`, un saut deja en cours. Sur une
    /// plateforme qui bouge, deux de ces conditions sont suspectes: les points de sol peuvent
    /// clignoter pendant le portage, et un drapeau de chute de bord peut rester colle a true si
    /// plus personne ne le remet a zero.
    ///
    /// On enregistre donc trois choses: chaque saut REFUSE avec le verrou fautif et l'etat complet,
    /// chaque changement du nombre de points de sol pendant le portage, et chaque transition de
    /// CanMove avec la pile d'appel qui l'a decidee.
    /// </summary>
    public partial class Warrior
    {
        #region Reglages

        [Header("Diagnostic - Saut bloque / plateforme mouvante")]
        [Tooltip("ON = journalise chaque saut refuse (avec le verrou responsable), les changements de points de sol pendant le portage par une plateforme mouvante, et les transitions de CanMove.")]
        [SerializeField] private bool logJumpBlockDiagnostics = false;

        [Tooltip("Delai minimal entre deux journaux de saut refuse, pour ne pas noyer le journal quand le joueur insiste.")]
        [SerializeField, Min(0.05f)] private float jumpRefusalLogThrottle = 0.3f;

        [Tooltip("ON = joint une pile d'appel abregee aux transitions de CanMove, pour nommer le systeme qui a bloque le deplacement.")]
        [SerializeField] private bool logCanMoveCallers = false;

        #endregion

        #region Etat

        private float _jumpRefusalLogThrottleUntil;
        private int _lastSampledGroundPoints = -1;
        private float _lastGroundPointsChangeTime;
        private bool _canMoveTransitionLogging;

        #endregion

        #region 1. CanMove: qui le met a false, et quand

        /// <summary>
        /// Journalise chaque bascule de CanMove. C'est le verrou le plus probable d'un saut refuse
        /// alors que le Warrior est visiblement pose sur la plateforme.
        /// </summary>
        public override bool CanMove
        {
            get => base.CanMove;
            set
            {
                if (value != base.CanMove && logJumpBlockDiagnostics && !_canMoveTransitionLogging)
                {
                    _canMoveTransitionLogging = true;

                    Debug.Log("[CANMOVE] " + (value ? "REND le deplacement" : "BLOQUE le deplacement") +
                              " | pointsSol=" + CountGroundPoints() +
                              " | plateforme=" + DescribeCurrentPlatform() +
                              " | chuteBord=" + IsFallingEdge +
                              " frolementBord=" + IsFallingGrazesEdge +
                              " sortiePlf=" + IsFallingPlfExit +
                              (logCanMoveCallers ? " | appelant: " + DescribeShortStack() : ""), this);

                    _canMoveTransitionLogging = false;
                }

                base.CanMove = value;
            }
        }

        private string DescribeShortStack()
        {
            string stack = System.Environment.StackTrace;

            if (string.IsNullOrEmpty(stack))
                return "inconnu";

            string[] lines = stack.Split('\n');
            var sb = new System.Text.StringBuilder();
            int kept = 0;

            // On saute les cadres du diagnostic lui-meme et on garde les trois suivants.
            for (int i = 0; i < lines.Length && kept < 3; i++)
            {
                string line = lines[i].Trim();

                if (line.Length == 0 ||
                    line.Contains("DescribeShortStack") ||
                    line.Contains("get_StackTrace") ||
                    line.Contains("set_CanMove"))
                    continue;

                if (kept > 0)
                    sb.Append(" <- ");

                sb.Append(line.Replace("at ", string.Empty));
                kept++;
            }

            return sb.ToString();
        }

        #endregion

        #region 2. Sauts refuses

        /// <summary>
        /// Appele par les deux chemins de saut a chaque refus. `reason` nomme le verrou teste;
        /// le reste de l'etat est capture ici pour que la ligne se suffise a elle-meme.
        /// </summary>
        private void NoteJumpRefused(string path, string reason)
        {
            if (!logJumpBlockDiagnostics)
                return;

            if (Time.time < _jumpRefusalLogThrottleUntil)
                return;

            _jumpRefusalLogThrottleUntil = Time.time + jumpRefusalLogThrottle;

            Debug.LogWarning(
                "[SAUT] REFUSE (" + path + ") | verrou: " + reason +
                " | pointsSol=" + CountGroundPoints() +
                " CanMove=" + CanMove +
                " chuteBord=" + IsFallingEdge +
                " frolementBord=" + IsFallingGrazesEdge +
                " sortiePlf=" + IsFallingPlfExit +
                " CanDie=" + CanDie +
                " CanAttackWarrior=" + CanAttackWarrior +
                " verrouDur=" + IsHardActionLocked +
                " | sautEnCours=" + (activesJumpCoroutine != null) +
                " deplacementEnCours=" + (activesMoveCoroutine != null) +
                " | plateforme=" + DescribeCurrentPlatform() +
                " | corpsAligneSurLaPlateforme=" + IsWarriorBodyBottomAlignedWithCurrentPlatformTop() +
                " | pos=(" + transform.position.x.ToString("F2") + ", " + transform.position.y.ToString("F2") + ")" +
                " v=(" + (rigidbody2 != null ? rigidbody2.linearVelocity.x.ToString("F2") : "?") + ", " +
                (rigidbody2 != null ? rigidbody2.linearVelocity.y.ToString("F2") : "?") + ")", this);
        }

        private readonly System.Collections.Generic.Dictionary<string, float> _tapTraceThrottleByOutcome =
            new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>
        /// Trace de TOUT appui ecran traite (ou rejete) par le chemin tap, sans condition de
        /// hauteur. C'est l'angle mort de la version precedente: les refus n'etaient journalises
        /// que si l'appui etait au-dessus de la tete, donc un appui qui n'arrivait meme pas
        /// jusqu'a la decision de saut ne laissait aucune trace, et une session de test complete
        /// pouvait rester muette.
        /// </summary>
        private void NoteTapOutcome(string outcome)
        {
            if (!logJumpBlockDiagnostics)
                return;

            // Limitation de debit PAR ISSUE, pas globale. Avec un seul compteur, la premiere
            // ligne d'un appui ("pas assez haut pour un saut") consommait le quota et l'issue
            // reelle du meme appui (deplacement refuse, attaque...) n'etait jamais journalisee:
            // mesure, 23 appuis tous etiquetes "pas assez haut" et aucune ligne de deplacement.
            string key = outcome.Length > 24 ? outcome.Substring(0, 24) : outcome;

            if (_tapTraceThrottleByOutcome.TryGetValue(key, out float until) && Time.time < until)
                return;

            _tapTraceThrottleByOutcome[key] = Time.time + 0.25f;

            float touchY = InputMgr.Instance != null ? InputMgr.Instance.TouchedVector.y : float.NaN;
            float touchX = InputMgr.Instance != null ? InputMgr.Instance.TouchedVector.x : float.NaN;
            float headY = collider2 != null ? collider2.bounds.max.y : transform.position.y;

            Debug.Log("[TAP] " + outcome +
                      " | appui=(" + touchX.ToString("F2") + ", " + touchY.ToString("F2") + ")" +
                      " tete=" + headY.ToString("F2") +
                      " au-dessus=" + (touchY > headY) +
                      " | pointsSol=" + CountGroundPoints() +
                      " support=" + HasMovingPlatformSupportUnderFeet() +
                      " CanMove=" + CanMove +
                      " CanJump=" + CanJump +
                      " chuteBord=" + IsFallingEdge +
                      " frolementBord=" + IsFallingGrazesEdge +
                      " | sautEnCours=" + (activesJumpCoroutine != null) +
                      " | plateforme=" + DescribeCurrentPlatform(), this);
        }

        /// <summary>Le joueur vise-t-il un saut ? (chemin tap: appui au-dessus de la tete)</summary>
        private bool IsTapAimedAtJump()
        {
            if (InputMgr.Instance == null || collider2 == null)
                return false;

            return InputMgr.Instance.TouchedVector.y > collider2.bounds.max.y;
        }

        /// <summary>
        /// Quelle plateforme se trouve REELLEMENT sous les pieds, et est-ce la meme que
        /// `CurrentplatForm` ? C'est la mesure decisive quand le deplacement est refuse au bout
        /// d'une plateforme: le porteur reel peut etre la piece voisine, auquel cas le test
        /// d'alignement (tolerance 0,06) compare les pieds au sommet de la MAUVAISE plateforme.
        /// </summary>
        private string DescribePlatformUnderFeet()
        {
            if (collider2 == null || PlatformLayer.value == 0)
                return "inconnu";

            Bounds body = collider2.bounds;
            Vector2 origin = new Vector2(body.center.x, body.min.y + 0.05f);

            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 2f, PlatformLayer);

            if (hit.collider == null)
                return "rien sous les pieds (2 unites)";

            PlatFormColliderTrigger platform = hit.collider.GetComponentInParent<PlatFormColliderTrigger>();

            string name = platform != null ? platform.name : hit.collider.name;
            bool sameAsCurrent = platform != null && platform == CurrentplatForm;

            return name +
                   " sommet=" + hit.collider.bounds.max.y.ToString("F2") +
                   " distancePieds=" + (body.min.y - hit.collider.bounds.max.y).ToString("F2") +
                   " estLaPlateformeCourante=" + sameAsCurrent;
        }

        private string DescribeCurrentPlatform()
        {
            if (CurrentplatForm == null)
                return "aucune";

            string description = CurrentplatForm.name + " (" + CurrentplatForm.GetType().Name + ")";

            if (CurrentplatForm.platformCollider != null && collider2 != null)
            {
                Bounds platformBounds = CurrentplatForm.platformCollider.bounds;

                description +=
                    " sommet=" + platformBounds.max.y.ToString("F2") +
                    " pieds=" + collider2.bounds.min.y.ToString("F2") +
                    " traversable=" + Physics2D.GetIgnoreCollision(CurrentplatForm.platformCollider, collider2);
            }

            return description;
        }

        #endregion

        #region 3. Portage par une plateforme mouvante

        /// <summary>
        /// Pendant le portage, journalise CHAQUE changement du nombre de points de sol, avec la
        /// duree de l'etat precedent. Si le saut est refuse par intermittence sur une plateforme
        /// mouvante, c'est ici que le clignotement doit apparaitre.
        /// </summary>
        private void TrackMovingPlatformSupport()
        {
            if (!logJumpBlockDiagnostics)
                return;

            bool onMovingPlatform =
                CurrentplatForm is MovingHorizontalPlatform || CurrentplatForm is MovingVerticalPlatform;

            if (!onMovingPlatform)
            {
                _lastSampledGroundPoints = -1;
                return;
            }

            int groundPoints = CountGroundPoints();

            if (groundPoints == _lastSampledGroundPoints)
                return;

            float heldFor = _lastSampledGroundPoints < 0 ? 0f : Time.time - _lastGroundPointsChangeTime;

            Debug.Log("[PLF-MOUVANTE] pointsSol " +
                      (_lastSampledGroundPoints < 0 ? "?" : _lastSampledGroundPoints.ToString()) +
                      " -> " + groundPoints +
                      (heldFor > 0f ? " apres " + heldFor.ToString("F2") + "s" : string.Empty) +
                      " | " + DescribeCurrentPlatform() +
                      " | CanMove=" + CanMove +
                      " chuteBord=" + IsFallingEdge +
                      " frolementBord=" + IsFallingGrazesEdge +
                      " | sautPossibleMaintenant=" + IsJumpCurrentlyPossible() +
                      " (supportPlateforme=" + HasMovingPlatformSupportUnderFeet() + ")" +
                      " | v=(" + (rigidbody2 != null ? rigidbody2.linearVelocity.x.ToString("F2") : "?") + ", " +
                      (rigidbody2 != null ? rigidbody2.linearVelocity.y.ToString("F2") : "?") + ")", this);

            _lastSampledGroundPoints = groundPoints;
            _lastGroundPointsChangeTime = Time.time;
        }

        /// <summary>
        /// Etat des conditions de saut a cet instant, sans rien declencher. Reflete la regle REELLE
        /// depuis le correctif: un support confirme sous les pieds remplace a la fois le comptage
        /// des points de sol et `CanMove`.
        /// </summary>
        private bool IsJumpCurrentlyPossible()
        {
            bool supported = HasConfirmedSupportForJump;

            return (CanMove || supported) &&
                   CanAttackWarrior &&
                   !IsHardActionLocked &&
                   !CanDie &&
                   activesJumpCoroutine == null &&
                   (supported || (!IsFallingEdge && !IsFallingGrazesEdge && CountGroundPoints() > 0));
        }

        #endregion
    }
}

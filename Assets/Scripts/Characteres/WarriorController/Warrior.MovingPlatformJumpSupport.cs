using Assets.Scripts.Platforms;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    /// <summary>
    /// Support de saut sur une plateforme mouvante.
    ///
    /// Mesure en jeu sur Plf-bck_moving_Var (une MovingVerticalPlatform): pendant la descente de la
    /// plateforme, les pieds du Warrior se retrouvent 0,49 a 0,56 au-dessus du sommet et
    /// `CountGroundPoints()` tombe a 0 par tranches de 0,20 a 0,48 s, en boucle. Les deux chemins de
    /// saut exigent un point de sol: le saut est donc refuse par intermittence alors que le joueur
    /// est visiblement pose. Le code de la plateforme connait deja ce trou, il le mentionne
    /// ("through the platform's downward leg, where CountGroundPoints() reads 0").
    ///
    /// Deuxieme mesure, independante: `Enemy.OnTriggerStay2D` met `CanMove` a false 110 fois
    /// pendant une seule montee (blocage par le corps d'un ennemi), pendant que la plateforme le
    /// remet a true 98 fois. Un saut demande sur une de ces frames est refuse lui aussi. Or sauter
    /// est precisement la sortie d'un blocage par un corps.
    ///
    /// D'ou une seule regle: quand un VRAI support est confirme sous les pieds, le saut ne depend
    /// plus ni du comptage des points de sol ni de `CanMove`. Les verrous durs (mort, gel,
    /// verrouillage d'action, saut deja en cours) restent intacts.
    /// </summary>
    public partial class Warrior
    {
        [Header("Saut - support de plateforme mouvante")]
        [Tooltip("ON = le saut est accorde quand la geometrie confirme un support sous les pieds, meme si les points de sol clignotent a 0 pendant la descente d'une plateforme mouvante.")]
        [SerializeField] private bool enableMovingPlatformJumpSupport = true;

        [Tooltip("Hauteur maximale des pieds au-dessus du sommet de la plateforme mouvante pour considerer qu'elle le porte encore. Mesure du trou reel: 0,56.")]
        [SerializeField, Min(0.05f)] private float movingPlatformSupportGap = 0.75f;

        [Tooltip("Retrait horizontal exige: le corps doit vraiment surplomber la plateforme, pas l'effleurer par le bord.")]
        [SerializeField, Min(0f)] private float movingPlatformSupportSkin = 0.05f;

        [Tooltip("Journalise les sauts autorises par ce support, pour verifier qu'il ne s'applique que la ou il doit.")]
        [SerializeField] private bool logMovingPlatformJumpSupport = false;

        private float _movingSupportLogThrottleUntil;

        /// <summary>
        /// Support reellement confirme sous les pieds: soit un point de sol, soit la surface d'une
        /// plateforme mouvante juste en dessous. C'est la condition que les deux chemins de saut
        /// doivent consulter a la place du seul comptage de points de sol.
        /// </summary>
        public bool HasConfirmedSupportForJump =>
            CountGroundPoints() > 0 || HasMovingPlatformSupportUnderFeet();

        /// <summary>
        /// Geometrie pure: le corps surplombe une plateforme mouvante SOLIDE pour lui, et ses pieds
        /// sont dans la bande juste au-dessus de sa surface. Aucune memoire temporelle, donc aucun
        /// risque d'accorder un saut en l'air apres avoir quitte la plateforme.
        /// </summary>
        public bool HasMovingPlatformSupportUnderFeet()
        {
            if (!enableMovingPlatformJumpSupport || collider2 == null)
                return false;

            if (!(CurrentplatForm is MovingHorizontalPlatform || CurrentplatForm is MovingVerticalPlatform))
                return false;

            Collider2D platformCollider = CurrentplatForm.platformCollider;

            if (platformCollider == null || !platformCollider.enabled)
                return false;

            // Plateforme rendue traversable pour lui = elle ne le porte pas.
            if (Physics2D.GetIgnoreCollision(platformCollider, collider2))
                return false;

            Bounds platformBounds = platformCollider.bounds;
            Bounds body = collider2.bounds;

            bool overlapsHorizontally =
                body.max.x > platformBounds.min.x + movingPlatformSupportSkin &&
                body.min.x < platformBounds.max.x - movingPlatformSupportSkin;

            if (!overlapsHorizontally)
                return false;

            float gap = body.min.y - platformBounds.max.y;

            // Legerement negatif = enfonce dans la surface (depenetration en cours), toujours porte.
            return gap >= -0.15f && gap <= movingPlatformSupportGap;
        }

        /// <summary>Diagnostic: le saut n'est parti que grace a ce support.</summary>
        private void NoteJumpGrantedByMovingPlatformSupport(string path)
        {
            if (!logMovingPlatformJumpSupport)
                return;

            if (Time.time < _movingSupportLogThrottleUntil)
                return;

            _movingSupportLogThrottleUntil = Time.time + 0.3f;

            Collider2D platformCollider = CurrentplatForm != null ? CurrentplatForm.platformCollider : null;
            float gap = platformCollider != null && collider2 != null
                ? collider2.bounds.min.y - platformCollider.bounds.max.y
                : float.NaN;

            Debug.Log("[SAUT] autorise par le support de plateforme mouvante (" + path + ")" +
                      " | ecartPieds=" + gap.ToString("F2") +
                      " | pointsSol=" + CountGroundPoints() +
                      " CanMove=" + CanMove +
                      " chuteBord=" + IsFallingEdge +
                      " | plateforme=" + (CurrentplatForm != null ? CurrentplatForm.name : "aucune"), this);
        }
    }
}

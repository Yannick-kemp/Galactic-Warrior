using Assets.Scripts.Characteres.EnemyContoller;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    /// <summary>
    /// Diagnostic seulement: reperage des repulsions violentes du Warrior au contact d'un ennemi.
    /// Compare la vitesse d'un pas physique au suivant (donc APRES resolution du solveur) et
    /// journalise le contexte complet quand le saut de vitesse depasse le seuil alors qu'un corps
    /// d'ennemi est a portee. Aucun effet sur le gameplay.
    /// </summary>
    public partial class Warrior
    {
        [Header("Diagnostic - Repulsion violente au contact d'un ennemi")]
        [Tooltip("ON = journalise chaque saut de vitesse anormal survenant pres d'un ennemi.")]
        [SerializeField] private bool logViolentEnemyRepulse = false;

        [Tooltip("Gain de vitesse (unites/s) en un seul pas physique a partir duquel on journalise. On ne retient que les ACCELERATIONS: un atterrissage ramene la vitesse a zero et n'est pas une repulsion.")]
        [SerializeField, Min(0.5f)] private float violentRepulseSpeedJump = 6f;

        [Tooltip("Deplacement (unites) en un seul pas physique au-dela duquel on journalise, meme sans gain de vitesse: la depenetration du solveur repousse le corps par la POSITION, sans vitesse.")]
        [SerializeField, Min(0.1f)] private float violentRepulseStepDistance = 0.6f;

        [Tooltip("Distance max entre le corps du Warrior et celui d'un ennemi pour attribuer le saut a un contact.")]
        [SerializeField, Min(0f)] private float violentRepulseEnemySearchRadius = 1.5f;

        private Vector2 _repulseDiagPreviousVelocity;
        private Vector2 _repulseDiagPreviousPosition;
        private bool _hasRepulseDiagPreviousSample;

        // Penetration la plus profonde avec un corps d'ennemi au pas PRECEDENT: c'est elle que le
        // solveur depenetre, donc c'est elle qui explique l'ejection, pas l'etat d'apres.
        private readonly ContactPoint2D[] _repulseDiagContacts = new ContactPoint2D[24];
        private float _repulseDiagPreviousEnemySeparation = float.MaxValue;
        private string _repulseDiagPreviousEnemyName = "aucun";

        private void TrackViolentEnemyRepulse()
        {
            if (!logViolentEnemyRepulse || rigidbody2 == null || collider2 == null)
            {
                _hasRepulseDiagPreviousSample = false;
                return;
            }

            Vector2 velocity = rigidbody2.linearVelocity;
            Vector2 position = rigidbody2.position;

            if (_hasRepulseDiagPreviousSample)
            {
                Vector2 velocityJump = velocity - _repulseDiagPreviousVelocity;
                float speedGain = velocity.magnitude - _repulseDiagPreviousVelocity.magnitude;
                float stepDistance = Vector2.Distance(position, _repulseDiagPreviousPosition);

                // Un atterrissage produit un enorme saut de vitesse vers ZERO: mesure, +12.41 en Y
                // pour finir a (0,00, 0,00) au sol. Ce n'est pas une repulsion. Seul un GAIN de
                // vitesse est une ejection.
                // Deux signatures possibles d'une repulsion: le solveur donne de la VITESSE, ou il
                // deplace directement la POSITION (depenetration) sans que la vitesse bouge.
                bool ejectedByVelocity = speedGain >= violentRepulseSpeedJump;
                bool ejectedByPosition =
                    stepDistance >= violentRepulseStepDistance &&
                    _repulseDiagPreviousEnemySeparation < 0f;

                if (ejectedByVelocity || ejectedByPosition)
                    ReportViolentEnemyRepulse(velocityJump, velocity, speedGain, stepDistance);
            }

            SampleDeepestEnemyPenetration();

            _repulseDiagPreviousVelocity = velocity;
            _repulseDiagPreviousPosition = position;
            _hasRepulseDiagPreviousSample = true;
        }

        /// <summary>
        /// Records how deeply an enemy body currently overlaps the Warrior, so an ejection can be
        /// reported together with the penetration that produced it.
        /// </summary>
        private void SampleDeepestEnemyPenetration()
        {
            _repulseDiagPreviousEnemySeparation = float.MaxValue;
            _repulseDiagPreviousEnemyName = "aucun";

            int count = collider2.GetContacts(_repulseDiagContacts);

            for (int i = 0; i < count; i++)
            {
                Collider2D other = _repulseDiagContacts[i].collider;

                if (other == null || other.GetComponentInParent<Enemy>() == null)
                    continue;

                float separation = _repulseDiagContacts[i].separation;

                if (separation < _repulseDiagPreviousEnemySeparation)
                {
                    _repulseDiagPreviousEnemySeparation = separation;
                    _repulseDiagPreviousEnemyName = other.name;
                }
            }
        }

        private void ReportViolentEnemyRepulse(Vector2 velocityJump, Vector2 velocity, float speedGain, float stepDistance)
        {
            Enemy nearest = null;
            float nearestDistance = float.MaxValue;
            bool nearestOverlapped = false;

            Enemy[] enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);

            for (int i = 0; i < enemies.Length; i++)
            {
                Enemy enemy = enemies[i];

                if (enemy == null || !enemy.gameObject.activeInHierarchy || enemy.collider2 == null)
                    continue;

                ColliderDistance2D separation = Physics2D.Distance(collider2, enemy.collider2);

                if (separation.distance < nearestDistance)
                {
                    nearestDistance = separation.distance;
                    nearestOverlapped = separation.isOverlapped;
                    nearest = enemy;
                }
            }

            // Un saut de vitesse loin de tout ennemi n'est pas une repulsion de contact.
            if (nearest == null || nearestDistance > violentRepulseEnemySearchRadius)
                return;

            Rigidbody2D enemyBody = nearest.GetComponent<Rigidbody2D>();

            Debug.Log(
                $"[REPULSE] gain de vitesse {speedGain:F2} | saut {velocityJump.magnitude:F2} " +
                $"({velocityJump.x:F2}, {velocityJump.y:F2}) -> vitesse=({velocity.x:F2}, {velocity.y:F2}) " +
                $"| penetrationAvant={(_repulseDiagPreviousEnemySeparation == float.MaxValue ? "aucun contact" : _repulseDiagPreviousEnemySeparation.ToString("F3"))} " +
                $"surEnnemiAvant={_repulseDiagPreviousEnemyName} " +
                $"| deplacement du pas={stepDistance:F2} pos=({transform.position.x:F2}, {transform.position.y:F2}) " +
                $"| ennemi={nearest.name} distance={nearestDistance:F3} chevauche={nearestOverlapped} " +
                $"corps={(enemyBody != null ? enemyBody.bodyType.ToString() : "aucun")} " +
                $"vitesseEnnemi={(enemyBody != null ? enemyBody.linearVelocity.ToString("F2") : "n/a")} " +
                $"| pointsSol={CountGroundPoints()} coroutineSaut={(activesJumpCoroutine != null)} " +
                $"coroutineDeplacement={(activesMoveCoroutine != null)} sprint={_sprintActive} esquive={IsDodging} " +
                $"postRebond={_postBounceActive} descenteForcee={(_middleLandingIgnoreRoutine != null)} " +
                $"absorbeurZalayty={_zalaytyBodyImpactAbsorbActive}", this);
        }
    }
}

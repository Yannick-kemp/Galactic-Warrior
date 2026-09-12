using System.Collections;
using System.Collections.Generic;
using Assets.Scripts.Characteres.EnemyContoller;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    /// <summary>
    /// Sortie de coincement LATERAL entre deux corps d'ennemis.
    ///
    /// Cas non couvert jusqu'ici: le failsafe de ping-pong ne traite que le Warrior POSE SUR un
    /// ennemi (WarriorSitsOnEnemyTop), et la mort par ecrasement de WarriorBehaveEnemyAttack exige
    /// deux ennemis de FACES OPPOSEES. Deux P39 tournes du meme cote coincent donc le Warrior sans
    /// declencher ni l'un ni l'autre: il reste bloque, sans traversee de collider, indefiniment.
    /// </summary>
    public partial class Warrior
    {
        [Header("Sortie de coincement lateral entre ennemis")]
        [Tooltip("ON = libere le Warrior quand deux corps d'ennemis le coincent lateralement sans qu'aucun autre systeme ne s'en occupe.")]
        [SerializeField] private bool enableWedgeBetweenEnemiesEscape = true;

        [Tooltip("Duree de blocage lateral continu avant de liberer le Warrior.")]
        [SerializeField, Min(0.1f)] private float wedgeEscapeStuckSeconds = 0.6f;

        [Tooltip("Duree maximale de la traversee accordee pour sortir du coincement.")]
        [SerializeField, Min(0.2f)] private float wedgeEscapeMaxSeconds = 1.2f;

        [Tooltip("Vitesse horizontale de degagement (par defaut la vitesse de course).")]
        [SerializeField, Min(1f)] private float wedgeEscapeSpeedX = 6f;

        [Tooltip("Deplacement horizontal au-dela duquel on considere que le Warrior progresse encore, donc qu'il n'est pas coince.")]
        [SerializeField, Min(0f)] private float wedgeEscapeMinProgressX = 0.4f;

        [Tooltip("Jeu exige entre les corps pour considerer le Warrior degage.")]
        [SerializeField, Min(0f)] private float wedgeEscapeClearance = 0.06f;

        [Tooltip("Journalise les detections et les sorties de coincement.")]
        [SerializeField] private bool logWedgeEscape = true;

        private readonly ContactPoint2D[] _wedgeContacts = new ContactPoint2D[24];
        private readonly List<Enemy> _wedgeEnemies = new List<Enemy>();
        private float _wedgeStartedAt = -999f;
        private float _wedgeStartedX;
        private Coroutine _wedgeEscapeRoutine;

        private void TrackWedgeBetweenEnemies()
        {
            if (!enableWedgeBetweenEnemiesEscape || collider2 == null || rigidbody2 == null)
                return;

            // Ne jamais interferer avec les systemes qui ont deja la main sur le corps.
            if (_wedgeEscapeRoutine != null ||
                _middleLandingIgnoreRoutine != null ||
                _absoluteFailsafeActive ||
                IsDodging ||
                IsDeadOrDying)
            {
                _wedgeStartedAt = -999f;
                return;
            }

            if (!IsBlockedBetweenTwoEnemyBodies())
            {
                _wedgeStartedAt = -999f;
                return;
            }

            if (_wedgeStartedAt < 0f)
            {
                _wedgeStartedAt = Time.time;
                _wedgeStartedX = transform.position.x;
                return;
            }

            if (Time.time - _wedgeStartedAt < wedgeEscapeStuckSeconds)
                return;

            // Ce qui compte n'est pas la vitesse instantanee mais l'absence de PROGRES: coince
            // entre deux corps, le Warrior est pousse dans tous les sens sans jamais avancer.
            if (Mathf.Abs(transform.position.x - _wedgeStartedX) > wedgeEscapeMinProgressX)
            {
                _wedgeStartedAt = -999f;
                return;
            }

            _wedgeEscapeRoutine = StartCoroutine(EscapeWedgeBetweenEnemies(new List<Enemy>(_wedgeEnemies)));
            _wedgeStartedAt = -999f;
        }

        /// <summary>
        /// True quand des corps d'ennemis touchent le Warrior des DEUX cotes avec des normales
        /// quasi horizontales et qu'il n'avance plus: c'est un coincement, pas un simple contact.
        /// </summary>
        private bool IsBlockedBetweenTwoEnemyBodies()
        {
            _wedgeEnemies.Clear();

            int count = collider2.GetContacts(_wedgeContacts);

            bool pushedFromLeft = false;
            bool pushedFromRight = false;

            for (int i = 0; i < count; i++)
            {
                Collider2D other = _wedgeContacts[i].collider;

                if (other == null || other.isTrigger)
                    continue;

                Enemy enemy = other.GetComponentInParent<Enemy>();

                if (enemy == null)
                    continue;

                float normalX = _wedgeContacts[i].normal.x;

                if (Mathf.Abs(normalX) < 0.6f)
                    continue;

                if (normalX > 0f)
                    pushedFromRight = true;
                else
                    pushedFromLeft = true;

                if (!_wedgeEnemies.Contains(enemy))
                    _wedgeEnemies.Add(enemy);
            }

            // UN SEUL ennemi suffit. Mesure en direct sur le cas signale: 4 contacts, tous du
            // meme "P39 (1)(Clone)", normales (1.00, 0.00) ET (-1.00, 0.00), separation -0.005 —
            // le Warrior etait bien coince des deux cotes par un seul corps, et l'exigence de deux
            // ennemis distincts faisait repondre faux a la detection.
            if (pushedFromLeft && pushedFromRight && _wedgeEnemies.Count >= 1)
                return true;

            // Repli: quand les corps s'interpenetrent, le solveur peut ne remonter aucun point de
            // contact exploitable. On regarde alors la geometrie brute: un corps d'ennemi colle a
            // gauche ET un a droite, a la hauteur du Warrior, alors qu'il n'avance plus.
            return IsSandwichedByEnemyBodies();
        }

        /// <summary>
        /// Detection geometrique du coincement, independante des points de contact: un corps
        /// d'ennemi touche ou chevauche le Warrior de chaque cote, a sa hauteur.
        /// </summary>
        private bool IsSandwichedByEnemyBodies()
        {
            _wedgeEnemies.Clear();

            Bounds body = collider2.bounds;

            Collider2D[] around = Physics2D.OverlapBoxAll(
                body.center,
                new Vector2(body.size.x + 0.5f, body.size.y * 0.8f),
                0f);

            bool blockedLeft = false;
            bool blockedRight = false;

            for (int i = 0; i < around.Length; i++)
            {
                Collider2D other = around[i];

                if (other == null || other.isTrigger || other.transform.IsChildOf(transform))
                    continue;

                Enemy enemy = other.GetComponentInParent<Enemy>();

                if (enemy == null || enemy.IsDeadOrDying)
                    continue;

                ColliderDistance2D separation = Physics2D.Distance(collider2, other);

                if (!separation.isOverlapped && separation.distance > wedgeEscapeClearance)
                    continue;

                if (other.bounds.center.x < body.center.x)
                    blockedLeft = true;
                else
                    blockedRight = true;

                if (!_wedgeEnemies.Contains(enemy))
                    _wedgeEnemies.Add(enemy);
            }

            return blockedLeft && blockedRight && _wedgeEnemies.Count >= 1;
        }

        /// <summary>
        /// Traverse les corps qui le coincent et le degage vers le cote le plus libre, puis
        /// restaure les collisions des qu'il est reellement sorti.
        /// </summary>
        private IEnumerator EscapeWedgeBetweenEnemies(List<Enemy> blockingEnemies)
        {
            float escapeSign = ChooseWedgeEscapeDirection(blockingEnemies);

            if (logWedgeEscape)
                Debug.Log($"[COINCE] Warrior bloque entre {blockingEnemies.Count} ennemis " +
                          $"({DescribeWedgeEnemies(blockingEnemies)}) | sortie vers " +
                          $"{(escapeSign < 0f ? "la gauche" : "la droite")} " +
                          $"| pos=({transform.position.x:F2}, {transform.position.y:F2})", this);

            SetIgnoreNearbyEnemies(blockingEnemies, true);
            NotifyNearbyEnemiesToSideStep(blockingEnemies);

            float timeoutAt = Time.time + wedgeEscapeMaxSeconds;

            while (Time.time < timeoutAt)
            {
                if (rigidbody2 != null)
                {
                    Vector2 velocity = rigidbody2.linearVelocity;
                    velocity.x = escapeSign * wedgeEscapeSpeedX;
                    rigidbody2.linearVelocity = velocity;
                }

                yield return new WaitForFixedUpdate();

                // Meme exigence que pour l'atterrissage force: on ne rend pas les corps solides
                // tant qu'il n'est pas FRANCHEMENT pose. Degage mais encore en l'air, il retombe
                // droit sur un ennemi et le coincement recommence.
                bool degage = IsClearOfEnemies(blockingEnemies);
                bool poseAuSol = CountGroundPoints() > 0 &&
                                 (rigidbody2 == null || Mathf.Abs(rigidbody2.linearVelocity.y) <= 0.1f);

                if (degage && poseAuSol)
                    break;
            }

            SetIgnoreNearbyEnemies(blockingEnemies, false);

            if (logWedgeEscape)
                Debug.Log($"[COINCE]   degage | pos=({transform.position.x:F2}, {transform.position.y:F2}) " +
                          $"pointsSol={CountGroundPoints()} " +
                          $"vitesseY={(rigidbody2 != null ? rigidbody2.linearVelocity.y : 0f):F2}", this);

            _wedgeEscapeRoutine = null;
        }

        /// <summary>Sortie du cote oppose au corps le plus proche.</summary>
        private float ChooseWedgeEscapeDirection(List<Enemy> blockingEnemies)
        {
            float myX = collider2.bounds.center.x;
            float nearestDistance = float.MaxValue;
            float nearestX = myX;

            for (int i = 0; i < blockingEnemies.Count; i++)
            {
                Enemy enemy = blockingEnemies[i];

                if (enemy == null || enemy.collider2 == null)
                    continue;

                float distance = Mathf.Abs(enemy.collider2.bounds.center.x - myX);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestX = enemy.collider2.bounds.center.x;
                }
            }

            return nearestX >= myX ? -1f : 1f;
        }

        private bool IsClearOfEnemies(List<Enemy> enemies)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];

                if (enemy == null || enemy.collider2 == null || collider2 == null)
                    continue;

                ColliderDistance2D separation = Physics2D.Distance(collider2, enemy.collider2);

                if (separation.isOverlapped || separation.distance < wedgeEscapeClearance)
                    return false;
            }

            return true;
        }

        private string DescribeWedgeEnemies(List<Enemy> enemies)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] == null)
                    continue;

                if (sb.Length > 0)
                    sb.Append(", ");

                sb.Append(enemies[i].name);
            }

            return sb.ToString();
        }
    }
}

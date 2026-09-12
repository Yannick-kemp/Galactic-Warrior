using System.Collections.Generic;
using Assets.Scripts.Characteres.EnemyContoller;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    /// <summary>
    /// Diagnostic dedie a la regle "3 rebonds / 2 sur le meme ennemi".
    ///
    /// La question a trancher n'est pas "la regle se declenche-t-elle" (les journaux [MIDLAND] le
    /// montrent deja) mais "termine-t-elle l'episode". On enregistre donc un EPISODE complet: du
    /// premier contact sur un ennemi jusqu'au retour au calme, avec le detail de chaque contact,
    /// de chaque declenchement, de chaque atterrissage, et surtout le DELAI entre un atterrissage
    /// force et le contact suivant, qui dit si la regle echoue ou si l'ennemi le reprend aussitot.
    /// </summary>
    public partial class Warrior
    {
        [Header("Diagnostic - Episodes de ping-pong sur ennemi")]
        [Tooltip("ON = journalise chaque episode de ping-pong et son bilan.")]
        [SerializeField] private bool logPingPongEpisodes = false;

        [Tooltip("Duree sans contact au-dessus d'un ennemi apres laquelle l'episode est considere termine.")]
        [SerializeField, Min(0.2f)] private float pingPongEpisodeEndSeconds = 2f;

        private bool _ppEpisodeOpen;
        private float _ppEpisodeStart;
        private float _ppLastContactTime;
        private float _ppLastLandingTime = -1f;
        private int _ppContacts;
        private int _ppRuleTriggers;
        private int _ppForcedLandings;
        private int _ppLoopEscapes;
        private int _ppRefusedCandidates;
        private float _ppMinX;
        private float _ppMaxX;
        private readonly Dictionary<int, int> _ppPerEnemy = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _ppEnemyNames = new Dictionary<int, string>();
        private float _ppPreviousEpisodeEnd = -1f;

        private void TrackPingPongEpisode()
        {
            if (!logPingPongEpisodes || !_ppEpisodeOpen)
                return;

            if (Time.time - _ppLastContactTime >= pingPongEpisodeEndSeconds)
                ClosePingPongEpisode("plus aucun contact");
        }

        private void NotePingPongContact(Enemy enemy)
        {
            if (!logPingPongEpisodes)
                return;

            if (!_ppEpisodeOpen)
                OpenPingPongEpisode();

            _ppContacts++;
            _ppLastContactTime = Time.time;

            float x = transform.position.x;
            _ppMinX = Mathf.Min(_ppMinX, x);
            _ppMaxX = Mathf.Max(_ppMaxX, x);

            int id = enemy != null ? enemy.GetInstanceID() : 0;
            _ppPerEnemy.TryGetValue(id, out int perEnemy);
            _ppPerEnemy[id] = perEnemy + 1;
            _ppEnemyNames[id] = enemy != null ? enemy.name : "?";

            float sinceLanding = _ppLastLandingTime >= 0f ? Time.time - _ppLastLandingTime : -1f;

            Collider2D enemyCollider = enemy != null ? enemy.collider2 : null;
            float dx = enemyCollider != null ? enemyCollider.bounds.center.x - x : float.NaN;
            float enemyTop = enemyCollider != null ? enemyCollider.bounds.max.y : float.NaN;
            float feet = collider2 != null ? collider2.bounds.min.y : float.NaN;

            Debug.Log(
                $"[PP] contact #{_ppContacts} sur {(enemy != null ? enemy.name : "?")} " +
                $"| t={(Time.time - _ppEpisodeStart):F2}s de l'episode " +
                $"| depuis le dernier atterrissage force={(sinceLanding < 0f ? "aucun" : sinceLanding.ToString("F2") + "s")} " +
                $"| compteur global={_middleLandingBounceCount}/{enemyTopMiddleLandingBounceLimit} " +
                $"| ecartX ennemi={dx:F2} piedsAuDessusDuSommet={(feet - enemyTop):F2} " +
                $"| pos=({x:F2}, {transform.position.y:F2}) pointsSol={CountGroundPoints()} " +
                $"| gardeAntiRelance={(Time.time < _middleLandingSettleUntil)} " +
                $"descenteEnCours={(_middleLandingIgnoreRoutine != null)} " +
                $"failsafe={_absoluteFailsafeActive}", this);
        }

        private void NotePingPongRuleTriggered(Enemy touchedEnemy, int clusterCount)
        {
            if (!logPingPongEpisodes)
                return;

            if (!_ppEpisodeOpen)
                OpenPingPongEpisode();

            _ppRuleTriggers++;

            Debug.Log($"[PP] regle declenchee #{_ppRuleTriggers} " +
                      $"| ennemi touche={(touchedEnemy != null ? touchedEnemy.name : "?")} " +
                      $"| groupe={clusterCount} ennemi(s)", this);
        }

        private void NotePingPongCandidateRefused()
        {
            if (logPingPongEpisodes)
                _ppRefusedCandidates++;
        }

        private void NotePingPongForcedLanding(float landingX, Enemy touchedEnemy)
        {
            if (!logPingPongEpisodes)
                return;

            _ppForcedLandings++;

            Collider2D enemyCollider = touchedEnemy != null ? touchedEnemy.collider2 : null;
            float distanceToEnemy = enemyCollider != null
                ? Mathf.Abs(landingX - enemyCollider.bounds.center.x)
                : float.NaN;

            Debug.Log($"[PP] descente forcee #{_ppForcedLandings} vers x={landingX:F2} " +
                      $"| distance au centre de l'ennemi={distanceToEnemy:F2} " +
                      $"(demi-largeur ennemi={(enemyCollider != null ? enemyCollider.bounds.extents.x : 0f):F2}, " +
                      $"demi-largeur Warrior={(collider2 != null ? collider2.bounds.extents.x : 0f):F2})", this);
        }

        private void NotePingPongLanded()
        {
            if (!logPingPongEpisodes)
                return;

            _ppLastLandingTime = Time.time;

            Debug.Log($"[PP]   pose au sol | pos=({transform.position.x:F2}, {transform.position.y:F2}) " +
                      $"| ennemi le plus proche: {DescribeNearestEnemy()}", this);
        }

        private void NotePingPongLoopEscape()
        {
            if (!logPingPongEpisodes)
                return;

            _ppLoopEscapes++;

            Debug.Log($"[PP] sortie de boucle #{_ppLoopEscapes} declenchee", this);
        }

        private void OpenPingPongEpisode()
        {
            _ppEpisodeOpen = true;
            _ppEpisodeStart = Time.time;
            _ppLastContactTime = Time.time;
            _ppLastLandingTime = -1f;
            _ppContacts = 0;
            _ppRuleTriggers = 0;
            _ppForcedLandings = 0;
            _ppLoopEscapes = 0;
            _ppRefusedCandidates = 0;
            _ppMinX = transform.position.x;
            _ppMaxX = transform.position.x;
            _ppPerEnemy.Clear();
            _ppEnemyNames.Clear();

            Debug.Log($"[PP] ===== debut d'episode | pos=({transform.position.x:F2}, {transform.position.y:F2}) " +
                      $"| repit depuis l'episode precedent=" +
                      $"{(_ppPreviousEpisodeEnd < 0f ? "premier" : (Time.time - _ppPreviousEpisodeEnd).ToString("F2") + "s")}", this);
        }

        private void ClosePingPongEpisode(string reason)
        {
            _ppEpisodeOpen = false;
            _ppPreviousEpisodeEnd = Time.time;

            System.Text.StringBuilder parEnnemi = new System.Text.StringBuilder();

            foreach (KeyValuePair<int, int> pair in _ppPerEnemy)
            {
                if (parEnnemi.Length > 0)
                    parEnnemi.Append(", ");

                parEnnemi.Append(_ppEnemyNames.TryGetValue(pair.Key, out string nom) ? nom : "?")
                         .Append(" x").Append(pair.Value);
            }

            Debug.Log($"[PP] ===== fin d'episode ({reason}) " +
                      $"| duree={(Time.time - _ppEpisodeStart):F2}s " +
                      $"| contacts={_ppContacts} " +
                      $"| declenchements de la regle={_ppRuleTriggers} " +
                      $"| descentes forcees={_ppForcedLandings} " +
                      $"| candidats refuses={_ppRefusedCandidates} " +
                      $"| sorties de boucle={_ppLoopEscapes} " +
                      $"| plage X=[{_ppMinX:F2} ; {_ppMaxX:F2}] " +
                      $"| par ennemi: {parEnnemi}", this);
        }

        private string DescribeNearestEnemy()
        {
            Enemy[] enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);

            Enemy nearest = null;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < enemies.Length; i++)
            {
                Enemy enemy = enemies[i];

                if (enemy == null || !enemy.gameObject.activeInHierarchy || enemy.collider2 == null || collider2 == null)
                    continue;

                ColliderDistance2D separation = Physics2D.Distance(collider2, enemy.collider2);

                if (separation.distance < nearestDistance)
                {
                    nearestDistance = separation.distance;
                    nearest = enemy;
                }
            }

            if (nearest == null)
                return "aucun";

            return $"{nearest.name} a {nearestDistance:F2} " +
                   $"(ecartX={(nearest.collider2.bounds.center.x - transform.position.x):F2})";
        }
    }
}

using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Platforms;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    /// <summary>
    /// Diagnostic pur (aucun effet sur le gameplay) pour deux symptomes constates en jeu:
    ///
    /// 1. "La regle des 3 / 2 rebonds n'est pas toujours appliquee." Les journaux existants
    ///    ([MIDLAND], [PP]) ne montrent que les rebonds DEJA comptes: ils sont aveugles au cas
    ///    ou le rebond n'arrive jamais jusqu'au compteur. On detecte donc les rebonds
    ///    GEOMETRIQUEMENT — les pieds se posent sur le sommet d'un corps d'ennemi — sans passer
    ///    par les callbacks de collision, et on compare avec ce que la regle a compte. Chaque
    ///    refus de la regle est journalise avec sa raison exacte.
    ///
    /// 2. "Le Warrior a traverse une plateforme de haut en bas." On surveille, a chaque pas
    ///    physique, le passage des pieds sous le sommet d'une plateforme, avec l'etat complet au
    ///    moment du franchissement: collision ignoree ou non, descente forcee en cours, arc de
    ///    saut, vitesse, distance parcourue dans le pas.
    /// </summary>
    public partial class Warrior
    {
        #region Reglages

        [Header("Diagnostic - Rebonds REELS vs rebonds COMPTES")]
        [Tooltip("ON = detecte geometriquement chaque pose sur le haut d'un ennemi (independamment des callbacks de collision) et dit si la regle l'a compte.")]
        [SerializeField] private bool logRealVsCountedTopBounces = false;

        [Tooltip("Epaisseur de la bande sous les pieds dans laquelle un sommet d'ennemi compte comme 'le Warrior est sur sa tete'.")]
        [SerializeField, Min(0.05f)] private float topDetectionBand = 0.35f;

        [Tooltip("ON = le detecteur geometrique ALIMENTE la regle des rebonds, au lieu de seulement l'observer. Sans lui la regle ne voyait qu'une pose sur cinq: les rebonds scriptes ne produisent pas de contact exploitable. OFF = comportement d'origine, comptage par callback de collision seulement.")]
        [SerializeField] private bool countTopBouncesGeometrically = true;

        [Header("Diagnostic - Contacts refuses par la regle")]
        [Tooltip("ON = journalise chaque contact sur le haut d'un ennemi que la regle refuse de compter, avec la raison exacte.")]
        [SerializeField] private bool logRuleSkips = false;

        [Tooltip("Delai minimal entre deux journaux de refus pour un meme ennemi.")]
        [SerializeField, Min(0.05f)] private float ruleSkipThrottleSeconds = 0.25f;

        [Header("Diagnostic - Traversee de plateforme de haut en bas")]
        [Tooltip("ON = journalise tout franchissement vers le bas du sommet d'une plateforme pendant un pas physique.")]
        [SerializeField] private bool logPlatformDownwardCrossing = false;

        [Tooltip("Delai minimal entre deux journaux pour la meme plateforme.")]
        [SerializeField, Min(0.05f)] private float platformCrossingThrottleSeconds = 0.4f;

        #endregion

        #region Etat interne

        private readonly Collider2D[] _diagTopHits = new Collider2D[16];

        private Enemy _diagEnemyUnderFeet;
        private int _diagGeometricTopContacts;
        private int _diagRuleCountAtLastTopContact;
        private float _diagLastTopContactTime = -999f;

        private string _diagLastSkipReason = "aucune";
        private float _diagLastSkipTime = -999f;
        private int _diagLastSkipEnemyId;

        private readonly System.Collections.Generic.Dictionary<int, float> _diagLastSkipLogByEnemy =
            new System.Collections.Generic.Dictionary<int, float>();

        private bool _hasDiagCrossingSample;
        private Vector2 _diagPreviousFeet;
        private readonly RaycastHit2D[] _diagCrossingHits = new RaycastHit2D[12];

        private readonly System.Collections.Generic.Dictionary<int, float> _diagLastCrossingLogByPlatform =
            new System.Collections.Generic.Dictionary<int, float>();

        #endregion

        #region Point d'entree unique (appele depuis FixedUpdate)

        private void TrackRuleMissDiagnostics()
        {
            TrackGeometricEnemyTopBounces();
            TrackPlatformDownwardCrossing();
        }

        #endregion

        #region 1. Rebonds reels vs rebonds comptes

        /// <summary>
        /// Detecte, sans dependre d'OnCollisionEnter, chaque transition "pas sur une tete" ->
        /// "sur une tete". C'est la mesure de verite: si ces transitions depassent le compteur de
        /// la regle, des rebonds ne sont pas comptes, et le journal dit pourquoi (derniere raison
        /// de refus enregistree).
        /// </summary>
        private void TrackGeometricEnemyTopBounces()
        {
            if (!logRealVsCountedTopBounces || collider2 == null || enemyLayer.value == 0)
                return;

            Enemy enemyUnderFeet = FindEnemyUnderFeet();

            bool wasOnTop = _diagEnemyUnderFeet != null;
            bool isOnTop = enemyUnderFeet != null;

            if (isOnTop && !wasOnTop)
            {
                _diagGeometricTopContacts++;

                bool counted = _middleLandingBounceCount > _diagRuleCountAtLastTopContact;
                bool skipFresh = Time.time - _diagLastSkipTime <= 0.4f;

                Bounds enemyBounds = GetDiagEnemyBounds(enemyUnderFeet);

                Debug.Log(
                    "[TOPDET] pose REELLE #" + _diagGeometricTopContacts + " sur " + enemyUnderFeet.name +
                    " | compte par la regle: " + (counted ? "OUI" : "NON") +
                    " | compteur global=" + _middleLandingBounceCount + "/" + enemyTopMiddleLandingBounceLimit +
                    " | meme ennemi=" + DescribeSameEnemyCount(enemyUnderFeet) +
                    " | pointsSol=" + CountGroundPoints() +
                    " vy=" + (rigidbody2 != null ? rigidbody2.linearVelocity.y : 0f).ToString("F2") +
                    " | pieds-sommet=" + (collider2.bounds.min.y - enemyBounds.max.y).ToString("F2") +
                    " ecartX=" + (enemyBounds.center.x - transform.position.x).ToString("F2") +
                    " | depuis la pose precedente=" + (Time.time - _diagLastTopContactTime).ToString("F2") + "s" +
                    " | derniere raison de refus=" + (skipFresh ? _diagLastSkipReason : "aucune") +
                    " | descenteForcee=" + (_middleLandingIgnoreRoutine != null) +
                    " garde=" + (Time.time < _middleLandingSettleUntil) +
                    " failsafe=" + _absoluteFailsafeActive +
                    " sprint=" + _sprintActive +
                    " arc=" + (activesJumpCoroutine != null), this);

                if (!counted)
                    TryCountGeometricBounceIntoRule(enemyUnderFeet);

                _diagRuleCountAtLastTopContact = _middleLandingBounceCount;
                _diagLastTopContactTime = Time.time;
            }
            else if (!isOnTop && wasOnTop)
            {
                Debug.Log(
                    "[TOPDET]   quitte la tete de " + _diagEnemyUnderFeet.name +
                    " apres " + (Time.time - _diagLastTopContactTime).ToString("F2") + "s" +
                    " | vy=" + (rigidbody2 != null ? rigidbody2.linearVelocity.y : 0f).ToString("F2") +
                    " | arc=" + (activesJumpCoroutine != null) +
                    " pointsSol=" + CountGroundPoints(), this);
            }

            // Un vrai contact avec le sol termine la chaine: on realigne la reference pour ne pas
            // signaler un faux "non compte" apres une remise a zero legitime du compteur.
            if (CountGroundPoints() > 0)
                _diagRuleCountAtLastTopContact = _middleLandingBounceCount;

            _diagEnemyUnderFeet = enemyUnderFeet;
        }

        /// <summary>
        /// Fait compter par la regle un rebond que le chemin de collision a laisse passer. C'est
        /// la correction du defaut principal mesure: sur 49 poses REELLES sur des tetes d'ennemis,
        /// 10 seulement etaient comptees. Les 39 autres arrivaient pendant un rebond SCRIPTE
        /// (arc=True sur presque toutes les lignes): le corps est deplace par position, la vitesse
        /// est nulle, aucun OnCollisionEnter exploitable n'est emis et les trois tests
        /// "est-il sur la tete" repondent tous faux alors que les pieds sont au niveau du sommet.
        /// La geometrie, elle, ne se trompe pas. Les memes gardes que le chemin de collision sont
        /// appliquees, et le compteur est protege du double comptage par la temporisation de
        /// meme-contact.
        /// </summary>
        private void TryCountGeometricBounceIntoRule(Enemy enemy)
        {
            if (!countTopBouncesGeometrically || enemy == null)
                return;

            // Le failsafe absolu possede deja la resolution.
            if (_absoluteFailsafeActive)
                return;

            // Descente forcee en cours ou garde anti-relance: aucun rebond ne doit repartir.
            if (Time.time < _middleLandingSettleUntil || _middleLandingIgnoreRoutine != null)
                return;

            // Un vrai sol sous les pieds termine la chaine: ce n'est pas un rebond.
            if (CountGroundPoints() > 0)
                return;

            if (!CanEnemyParticipateInTopPingPong(enemy))
                return;

            if (logMiddleLandingDecisions)
                Debug.Log("[TOPDET] -> rebond rattrape par le detecteur geometrique sur " + enemy.name, this);

            UpdateAndCheckEnemyTopMiddleLanding(enemy, registerBounceContact: true);
        }

        /// <summary>
        /// Cherche un corps d'ennemi dont le SOMMET se trouve juste sous les pieds du Warrior.
        /// Geometrie pure: aucun filtre d'eligibilite, aucune collision — c'est precisement ce qui
        /// permet de voir les rebonds que la regle laisse passer.
        /// </summary>
        private Enemy FindEnemyUnderFeet()
        {
            Bounds body = collider2.bounds;

            Vector2 boxCenter = new Vector2(body.center.x, body.min.y - topDetectionBand * 0.5f);
            Vector2 boxSize = new Vector2(Mathf.Max(0.1f, body.size.x * 0.9f), topDetectionBand);

            int count = Physics2D.OverlapBoxNonAlloc(boxCenter, boxSize, 0f, _diagTopHits, enemyLayer);

            Enemy best = null;
            float bestTop = float.MinValue;

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _diagTopHits[i];

                if (hit == null || hit.isTrigger)
                    continue;

                Enemy enemy = hit.GetComponentInParent<Enemy>();

                if (enemy == null || enemy.gameObject == gameObject)
                    continue;

                float top = hit.bounds.max.y;

                // Le Warrior doit etre AU-DESSUS du sommet (a la tolerance pres), sinon c'est un
                // contact lateral ou un chevauchement de corps, pas une pose sur la tete.
                if (body.min.y < top - topDetectionBand)
                    continue;

                if (top > bestTop)
                {
                    bestTop = top;
                    best = enemy;
                }
            }

            return best;
        }

        private Bounds GetDiagEnemyBounds(Enemy enemy)
        {
            if (enemy == null)
                return new Bounds(transform.position, Vector3.zero);

            Collider2D enemyCollider = enemy.collider2 != null
                ? enemy.collider2
                : enemy.GetComponentInChildren<Collider2D>();

            return enemyCollider != null
                ? enemyCollider.bounds
                : new Bounds(enemy.transform.position, Vector3.zero);
        }

        private string DescribeSameEnemyCount(Enemy enemy)
        {
            if (enemy == null)
                return "?";

            _middleLandingSameEnemyBounces.TryGetValue(enemy.GetInstanceID(), out int bounces);

            return bounces + "/" + enemyTopMiddleLandingSameEnemyBounceLimit;
        }

        #endregion

        #region 2. Raisons de refus de la regle

        /// <summary>
        /// Enregistre et journalise le fait qu'un contact sur le haut d'un ennemi n'ira PAS
        /// jusqu'au compteur de la regle. Appele depuis chaque sortie anticipee du chemin de
        /// ping-pong. Volontairement silencieux quand le Warrior n'est pas au-dessus de l'ennemi:
        /// un contact lateral n'a jamais vocation a compter.
        /// </summary>
        private void NoteRuleSkip(Enemy enemy, string reason)
        {
            if (!logRuleSkips)
                return;

            _diagLastSkipReason = reason;
            _diagLastSkipTime = Time.time;
            _diagLastSkipEnemyId = enemy != null ? enemy.GetInstanceID() : 0;

            if (!IsDiagAboveEnemyTop(enemy))
                return;

            if (_diagLastSkipLogByEnemy.TryGetValue(_diagLastSkipEnemyId, out float lastLog) &&
                Time.time - lastLog < ruleSkipThrottleSeconds)
                return;

            _diagLastSkipLogByEnemy[_diagLastSkipEnemyId] = Time.time;

            Bounds enemyBounds = GetDiagEnemyBounds(enemy);

            Debug.Log(
                "[MIDLAND-SKIP] contact NON compte sur " + (enemy != null ? enemy.name : "?") +
                " | raison: " + reason +
                " | compteur global=" + _middleLandingBounceCount + "/" + enemyTopMiddleLandingBounceLimit +
                " | pointsSol=" + CountGroundPoints() +
                " | pieds-sommet=" +
                (collider2 != null ? collider2.bounds.min.y - enemyBounds.max.y : 0f).ToString("F2") +
                " | vy=" + (rigidbody2 != null ? rigidbody2.linearVelocity.y : 0f).ToString("F2") +
                " | pos=(" + transform.position.x.ToString("F2") + ", " +
                transform.position.y.ToString("F2") + ")", this);
        }

        private bool IsDiagAboveEnemyTop(Enemy enemy)
        {
            if (enemy == null || collider2 == null)
                return false;

            Bounds enemyBounds = GetDiagEnemyBounds(enemy);

            if (enemyBounds.size == Vector3.zero)
                return false;

            Bounds body = collider2.bounds;

            bool horizontallyOverlapping =
                body.max.x > enemyBounds.min.x && body.min.x < enemyBounds.max.x;

            return horizontallyOverlapping && body.min.y >= enemyBounds.max.y - topDetectionBand;
        }

        /// <summary>
        /// Detail de l'eligibilite d'un ennemi au ping-pong, pour que le journal dise QUEL critere
        /// a echoue (case du prefab, mort en cours, collider desactive, objet culle...).
        /// </summary>
        private string DescribeEnemyParticipation(Enemy enemy)
        {
            if (enemy == null)
                return "ennemi nul";

            if (!enemy.gameObject.activeInHierarchy)
                return "objet desactive (culling de zone ?)";

            if (!enemy.CanCauseWarriorTopPingPongTrap)
                return "CanCauseWarriorTopPingPongTrap=false (mourant=" + enemy.IsDeadOrDying +
                       ", vie=" + enemy.CurrentHealth.ToString("F1") + ", case du prefab ?)";

            Collider2D enemyCollider = GetEnemyTopTrapCollider(enemy);

            if (enemyCollider == null)
                return "aucun collider de ping-pong";

            if (!enemyCollider.enabled)
                return "collider " + enemyCollider.name + " desactive";

            if (enemyCollider.isTrigger)
                return "collider " + enemyCollider.name + " en trigger";

            return "eligible";
        }

        #endregion

        #region 3. Traversee de plateforme de haut en bas

        /// <summary>
        /// Signale tout pas physique pendant lequel les pieds passent SOUS le sommet d'une
        /// plateforme. Le journal dit si la collision avec cette plateforme etait ignoree (donc
        /// une traversee autorisee par la machinerie one-way) ou non (donc un tunneling), et dans
        /// quel etat de la regle de ping-pong le Warrior se trouvait.
        /// </summary>
        private void TrackPlatformDownwardCrossing()
        {
            if (!logPlatformDownwardCrossing || collider2 == null || PlatformLayer.value == 0)
            {
                _hasDiagCrossingSample = false;
                return;
            }

            Bounds body = collider2.bounds;
            Vector2 feet = new Vector2(body.center.x, body.min.y);

            if (_hasDiagCrossingSample && feet.y < _diagPreviousFeet.y)
            {
                int count = Physics2D.LinecastNonAlloc(_diagPreviousFeet, feet, _diagCrossingHits, PlatformLayer);

                for (int i = 0; i < count; i++)
                {
                    Collider2D platform = _diagCrossingHits[i].collider;

                    if (platform == null)
                        continue;

                    float top = platform.bounds.max.y;

                    // Franchissement du sommet pendant ce pas: pieds au-dessus avant, en dessous
                    // apres. Tout le reste (passer devant, sortir par le cote) est ignore.
                    if (_diagPreviousFeet.y < top - 0.02f || feet.y >= top - 0.02f)
                        continue;

                    ReportPlatformDownwardCrossing(platform, top, feet);
                }
            }

            _diagPreviousFeet = feet;
            _hasDiagCrossingSample = true;
        }

        private void ReportPlatformDownwardCrossing(Collider2D platform, float top, Vector2 feet)
        {
            int platformId = platform.GetInstanceID();

            if (_diagLastCrossingLogByPlatform.TryGetValue(platformId, out float lastLog) &&
                Time.time - lastLog < platformCrossingThrottleSeconds)
                return;

            _diagLastCrossingLogByPlatform[platformId] = Time.time;

            bool ignoredWithBody = Physics2D.GetIgnoreCollision(platform, collider2);

            PlatFormColliderTrigger trigger = platform.GetComponentInParent<PlatFormColliderTrigger>();

            bool ignoredWithPlatformBody =
                trigger != null && trigger.platformCollider != null &&
                Physics2D.GetIgnoreCollision(trigger.platformCollider, collider2);

            Debug.LogWarning(
                "[PLF-CROSS] TRAVERSEE HAUT->BAS de " + platform.name +
                " | sommet=" + top.ToString("F2") +
                " pieds " + _diagPreviousFeet.y.ToString("F2") + " -> " + feet.y.ToString("F2") +
                " (descente de " + (_diagPreviousFeet.y - feet.y).ToString("F2") + " en un pas)" +
                " | collision ignoree: corps=" + ignoredWithBody + " plateforme=" + ignoredWithPlatformBody +
                " | vy=" + (rigidbody2 != null ? rigidbody2.linearVelocity.y : 0f).ToString("F2") +
                " vx=" + (rigidbody2 != null ? rigidbody2.linearVelocity.x : 0f).ToString("F2") +
                " | descenteForcee=" + (_middleLandingIgnoreRoutine != null) +
                " garde=" + (Time.time < _middleLandingSettleUntil) +
                " failsafe=" + _absoluteFailsafeActive +
                " | arc=" + (activesJumpCoroutine != null) +
                " sprint=" + _sprintActive +
                " postRebond=" + _postBounceActive +
                " | plateforme courante=" + (CurrentplatForm != null ? CurrentplatForm.name : "aucune") +
                " | pointsSol=" + CountGroundPoints() +
                " | sur une tete=" + (_diagEnemyUnderFeet != null ? _diagEnemyUnderFeet.name : "non"), this);
        }

        #endregion
    }
}

using System.Collections;
using System.Collections.Generic;
using Assets.Scripts.Characteres.EnemyContoller;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    /// <summary>
    /// Fenetre de survie a l'ecrasement lateral.
    ///
    /// La mort par ecrasement entre deux ennemis de faces opposees est VOULUE (elle reste). Ce qui
    /// manquait, c'est la chance de s'en sortir: le sursis servi par WarriorBehaveEnemyAttack ne
    /// faisait que retarder la mort, sans jamais rendre le saut possible. Or pendant l'ecrasement
    /// le Warrior est justement dans l'etat ou les deux chemins de saut le refusent: `CanMove`
    /// tombe a false quand un systeme de poussee prend la main, et surtout il perd ses points de
    /// sol des que les deux corps le soulevent, ce qui coupe la condition `CountGroundPoints() > 0`.
    /// Le joueur appuyait donc sur Saut sans effet, puis mourait.
    ///
    /// Pendant la fenetre: l'action est deverrouillee, le saut est accepte meme sans point de sol,
    /// et le saut declenche traverse les corps qui l'ecrasent le temps de s'extraire.
    /// </summary>
    public partial class Warrior
    {
        #region Reglages

        [Header("Ecrasement lateral - fenetre de survie")]
        [Tooltip("ON = pendant le sursis accorde par l'ecrasement lateral, le Warrior peut reellement sauter: l'action est deverrouillee et le saut est accepte meme sans point de sol. OFF = sursis passif, le joueur subit.")]
        [SerializeField] private bool enableSqueezeEscapeWindow = true;

        [Tooltip("Duree pendant laquelle le saut d'echappement traverse les corps qui ecrasent le Warrior, pour qu'il puisse vraiment s'extraire au lieu d'etre retenu entre eux.")]
        [SerializeField, Min(0.1f)] private float squeezeEscapeJumpPassThroughSeconds = 0.6f;

        [Tooltip("Journalise l'ouverture de la fenetre, le saut d'echappement et la sortie.")]
        [SerializeField] private bool logSqueezeEscapeWindow = false;

        #endregion

        #region Etat

        private float _squeezeEscapeWindowUntil = -999f;
        private readonly List<Enemy> _squeezeEscapeEnemies = new List<Enemy>();
        private Coroutine _squeezeEscapePassThroughRoutine;
        private bool _squeezeEscapeJumpStarted;

        /// <summary>Le sursis est en cours: le joueur a encore le droit de placer son saut.</summary>
        public bool IsInSqueezeEscapeWindow =>
            enableSqueezeEscapeWindow && !IsDeadOrDying && Time.time < _squeezeEscapeWindowUntil;

        /// <summary>
        /// Un saut d'echappement est engage: la traversee des corps est active et le Warrior est en
        /// train de sortir. L'ecrasement ne doit pas le tuer pendant ce temps, sinon le sursis
        /// n'aurait servi a rien.
        /// </summary>
        public bool IsEscapingSqueezeByJump =>
            _squeezeEscapePassThroughRoutine != null || (_squeezeEscapeJumpStarted && activesJumpCoroutine != null);

        #endregion

        /// <summary>
        /// Ouvre (ou prolonge) la fenetre de survie. Appelee a chaque verification d'ecrasement
        /// tant que le sursis court. Deverrouille l'action: sans cela le saut est refuse et le
        /// sursis ne sert a rien.
        /// </summary>
        public void BeginOrRefreshSqueezeEscapeWindow(float secondsLeft, List<Enemy> squeezingEnemies)
        {
            if (!enableSqueezeEscapeWindow || IsDeadOrDying)
                return;

            bool opening = !IsInSqueezeEscapeWindow;

            _squeezeEscapeWindowUntil = Time.time + Mathf.Max(0f, secondsLeft);

            _squeezeEscapeEnemies.Clear();

            if (squeezingEnemies != null)
            {
                for (int i = 0; i < squeezingEnemies.Count; i++)
                {
                    Enemy enemy = squeezingEnemies[i];

                    if (enemy != null)
                        _squeezeEscapeEnemies.Add(enemy);
                }
            }

            // Rien ne doit empecher le saut pendant le sursis.
            CanMove = true;
            _blockAction = false;
            _blockedByEnemyContact = false;
            _blockingEnemy = null;

            if (opening)
            {
                _squeezeEscapeJumpStarted = false;

                if (logSqueezeEscapeWindow)
                    Debug.Log($"[SQUEEZE-ESCAPE] fenetre ouverte {secondsLeft:F2}s " +
                              $"| {_squeezeEscapeEnemies.Count} ennemi(s) " +
                              $"| pointsSol={CountGroundPoints()} " +
                              $"| pos=({transform.position.x:F2}, {transform.position.y:F2})", this);
            }
        }

        /// <summary>Ferme la fenetre: l'ecrasement a cesse, ou il a fait son office.</summary>
        public void EndSqueezeEscapeWindow()
        {
            if (!IsInSqueezeEscapeWindow)
            {
                _squeezeEscapeWindowUntil = -999f;
                return;
            }

            if (logSqueezeEscapeWindow)
                Debug.Log($"[SQUEEZE-ESCAPE] fenetre fermee | saut place={_squeezeEscapeJumpStarted} " +
                          $"| pointsSol={CountGroundPoints()}", this);

            _squeezeEscapeWindowUntil = -999f;
        }

        /// <summary>
        /// Vrai quand un saut doit etre accepte malgre les conditions habituelles (sol sous les
        /// pieds, deplacement autorise, chute de bord en cours). C'est le coeur de la fenetre:
        /// coince entre deux corps, le Warrior n'a plus de point de sol, donc le saut normal est
        /// refuse alors que c'est precisement la sortie qu'on veut lui offrir.
        /// </summary>
        public bool IsSqueezeEscapeJumpAllowed => IsInSqueezeEscapeWindow && activesJumpCoroutine == null;

        /// <summary>
        /// Appelee par les deux chemins de saut au moment ou le saut part pendant la fenetre.
        /// Accorde la traversee des corps qui ecrasent, sinon le saut se fait contre eux et le
        /// Warrior retombe entre les deux.
        /// </summary>
        public void NotifySqueezeEscapeJumpStarted()
        {
            if (!IsInSqueezeEscapeWindow)
                return;

            _squeezeEscapeJumpStarted = true;

            if (logSqueezeEscapeWindow)
                Debug.Log($"[SQUEEZE-ESCAPE] saut d'echappement place " +
                          $"| traversee de {_squeezeEscapeEnemies.Count} corps pendant " +
                          $"{squeezeEscapeJumpPassThroughSeconds:F2}s", this);

            if (_squeezeEscapePassThroughRoutine != null)
                StopCoroutine(_squeezeEscapePassThroughRoutine);

            _squeezeEscapePassThroughRoutine = StartCoroutine(SqueezeEscapePassThrough());
        }

        private float _squeezeTapLogThrottleUntil;

        /// <summary>
        /// Diagnostic du schema TAP (celui reellement actif: control_scheme = Tap, le HUD joystick
        /// n'est meme pas construit). Ici le saut exige un appui AU-DESSUS de la tete (`CanJump`);
        /// un appui ailleurs part en attaque. Cette trace dit si le joueur a touche l'ecran pendant
        /// le sursis et si son appui etait assez haut pour valoir un saut.
        /// </summary>
        private void NoteSqueezeTapDuringWindow(bool canJump, float touchY)
        {
            if (!logSqueezeEscapeWindow || !IsInSqueezeEscapeWindow)
                return;

            if (Time.time < _squeezeTapLogThrottleUntil)
                return;

            _squeezeTapLogThrottleUntil = Time.time + 0.2f;

            float headY = collider2 != null ? collider2.bounds.max.y : transform.position.y;

            Debug.Log($"[SQUEEZE-ESCAPE] appui ecran pendant le sursis " +
                      $"| assezHautPourSauter={canJump} (appui y={touchY:F2}, tete y={headY:F2}) " +
                      $"| restant={(_squeezeEscapeWindowUntil - Time.time):F2}s " +
                      $"| pointsSol={CountGroundPoints()} CanMove={CanMove}", this);
        }

        /// <summary>
        /// Diagnostic: le joueur a bien demande un saut pendant le sursis. Avec la ligne de refus
        /// juste apres (ou son absence, suivie du saut place), le journal dit exactement ou ca
        /// casse.
        /// </summary>
        private void NoteSqueezeJumpRequestedIfWindowOpen()
        {
            if (!logSqueezeEscapeWindow || !IsInSqueezeEscapeWindow)
                return;

            Debug.Log($"[SQUEEZE-ESCAPE] saut DEMANDE pendant le sursis " +
                      $"| restant={(_squeezeEscapeWindowUntil - Time.time):F2}s " +
                      $"| pointsSol={CountGroundPoints()} CanMove={CanMove} " +
                      $"sautEnCours={(activesJumpCoroutine != null)}", this);
        }

        /// <summary>
        /// Diagnostic: dit qu'un saut a ete refuse ALORS QUE le sursis courait. C'est le seul cas
        /// qui compte vraiment: si cette ligne apparait, le joueur a appuye et le verrou qui l'a
        /// bloque est nomme, donc la fenetre est a elargir sur ce point precis.
        /// </summary>
        private void NoteSqueezeJumpRefusedIfWindowOpen(string reason)
        {
            if (!logSqueezeEscapeWindow || !IsInSqueezeEscapeWindow)
                return;

            Debug.Log($"[SQUEEZE-ESCAPE] saut REFUSE pendant le sursis | verrou: {reason} " +
                      $"| pointsSol={CountGroundPoints()} CanMove={CanMove}", this);
        }

        private IEnumerator SqueezeEscapePassThrough()
        {
            List<Enemy> ignored = new List<Enemy>(_squeezeEscapeEnemies);

            SetIgnoreNearbyEnemies(ignored, true);

            float until = Time.time + squeezeEscapeJumpPassThroughSeconds;

            // L'ignore est reaffirme a chaque pas: Unity le perd des qu'un collider est desactive
            // puis reactive, et un ennemi qui rejoint le groupe pendant la sortie n'y serait pas.
            while (Time.time < until && !IsDeadOrDying)
            {
                SetIgnoreNearbyEnemies(ignored, true);
                yield return new WaitForFixedUpdate();
            }

            SetIgnoreNearbyEnemies(ignored, false);

            _squeezeEscapePassThroughRoutine = null;

            if (logSqueezeEscapeWindow)
                Debug.Log($"[SQUEEZE-ESCAPE] fin de traversee | pointsSol={CountGroundPoints()} " +
                          $"| pos=({transform.position.x:F2}, {transform.position.y:F2})", this);
        }
    }
}

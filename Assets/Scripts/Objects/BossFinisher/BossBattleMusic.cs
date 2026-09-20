using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Objects.BossFinisherFx
{
    /// <summary>
    /// Swaps the level track for a boss track when a boss fight actually starts, and hands the
    /// level track back when that boss dies.
    ///
    /// Two different triggers on purpose. A normal boss starts its track when the Warrior steps
    /// onto the platform it is standing on, because that is the moment the fight becomes
    /// unavoidable. Zort starts his the moment he is on screen: he teleports and blinks around his
    /// arena, so "same platform" would fire late and erratically for him.
    ///
    /// The trigger LATCHES. Once a boss track is playing it runs until that boss dies, which is
    /// what the design asks for and also sidesteps a real problem: platform ownership flickers
    /// between two abutting pieces, and a non-latching trigger would restart the track on every
    /// flicker.
    ///
    /// Self-installing, so none of the four gameplay scenes needed wiring. Playback itself belongs
    /// to GameMgr, which owns the single music AudioSource and knows how to resume the level track
    /// at the right playhead.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class BossBattleMusic : MonoBehaviour
    {
        private static BossBattleMusicSettings _settings;
        private static bool _settingsLookupDone;

        private static BossBattleMusicSettings Settings
        {
            get
            {
                if (!_settingsLookupDone)
                {
                    _settingsLookupDone = true;
                    _settings = Resources.Load<BossBattleMusicSettings>(BossBattleMusicSettings.ResourcePath);
                }

                return _settings;
            }
        }

        private Enemy _trackedBoss;
        private bool _fightActive;
        private float _nextCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Settings == null || !Settings.enabled)
                return;

            var go = new GameObject("BossBattleMusic");
            DontDestroyOnLoad(go);
            go.AddComponent<BossBattleMusic>();
        }

        private void Update()
        {
            BossBattleMusicSettings s = Settings;
            if (s == null || !s.enabled) return;

            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + Mathf.Max(0.02f, s.checkInterval);

            // A destroyed UnityEngine.Object compares equal to null, so the tracked reference alone
            // cannot say whether a fight was running: a boss destroyed rather than killed would
            // read as "no fight" and the boss track would keep playing forever. Hence the flag.
            if (_fightActive)
            {
                bool over = _trackedBoss == null || _trackedBoss.IsDeadOrDying;
                if (!over)
                    return;   // latched: one fight owns the music until it ends

                _fightActive = false;
                _trackedBoss = null;
                FindGameMgr()?.StopBossMusic();
            }

            TryStartFight(s);
        }

        private void TryStartFight(BossBattleMusicSettings s)
        {
            var bosses = Enemy.ActiveEnemies;

            for (int i = bosses.Count - 1; i >= 0; i--)
            {
                Enemy boss = bosses[i];
                if (boss == null || !boss.IsBoss || boss.IsDeadOrDying)
                    continue;

                bool isZort = boss is ZortBoss;

                AudioClip clip = isZort ? s.zortTrack : s.generalBossTrack;
                if (clip == null)
                    continue;

                bool triggered = isZort
                    ? IsOnScreen(boss, s.zortVisibilityMargin)
                    : SharesPlatformWithWarrior(boss);

                if (!triggered)
                    continue;

                _trackedBoss = boss;
                _fightActive = true;
                FindGameMgr()?.PlayBossMusic(clip);
                return;
            }
        }

        /// <summary>
        /// GameMgr.Instance can be null while a GameMgr exists in the scene, observed in play mode,
        /// exactly like Warrior.Instance. Going through the singleton alone made the music silently
        /// never stop, because the null-conditional call simply did nothing.
        /// </summary>
        private static GameMgr FindGameMgr()
        {
            return GameMgr.Instance != null ? GameMgr.Instance : FindFirstObjectByType<GameMgr>();
        }

        /// <summary>
        /// Platform identity rather than proximity: the fight starts when the Warrior is standing
        /// on the same piece of ground as the boss.
        /// </summary>
        private static bool SharesPlatformWithWarrior(Enemy boss)
        {
            Warrior warrior = Warrior.Instance;
            if (warrior == null)
                warrior = FindFirstObjectByType<Warrior>();

            if (warrior == null || warrior.CurrentplatForm == null || boss.CurrentplatForm == null)
                return false;

            return warrior.CurrentplatForm == boss.CurrentplatForm;
        }

        /// <summary>
        /// Explicit rectangle test against the camera, NOT Renderer.isVisible: that one also counts
        /// the Scene view camera in the Editor, so it reads true while the boss is nowhere near the
        /// player's screen.
        /// </summary>
        private static bool IsOnScreen(Enemy boss, float margin)
        {
            Camera cam = Camera.main;
            if (cam == null || !cam.orthographic)
                return false;

            Renderer renderer = boss.GetComponentInChildren<Renderer>();
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            float halfHeight = cam.orthographicSize + margin;
            float halfWidth = cam.orthographicSize * cam.aspect + margin;
            if (halfHeight <= 0f || halfWidth <= 0f)
                return false;

            Vector3 camPos = cam.transform.position;
            Bounds view = new Bounds(
                new Vector3(camPos.x, camPos.y, 0f),
                new Vector3(halfWidth * 2f, halfHeight * 2f, 1000f));

            Bounds bossBounds = renderer.bounds;
            bossBounds.center = new Vector3(bossBounds.center.x, bossBounds.center.y, 0f);

            return view.Intersects(bossBounds);
        }
    }
}

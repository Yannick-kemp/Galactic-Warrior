using System.Collections;
using System.Collections.Generic;
using Assets.Scripts.Characteres.EnemyContoller;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Assets.Scripts.Objects.BossFinisherFx
{
    /// <summary>
    /// Cinematic take-over for the last hits of a boss fight: hard zoom onto the duel, slow motion,
    /// a freeze frame on every landed hit, screen shake and a post-processing punch.
    ///
    /// Self-installing. Nothing to wire in any scene: NotifyBossDamaged creates the runner on first
    /// use and it survives scene loads. Tuning lives in a BossFinisherSettings asset under
    /// Resources, and built-in defaults are used when that asset is absent, so a missing asset
    /// degrades to "still works" rather than "throws".
    ///
    /// Two things it deliberately does NOT do. It never edits the scene's Global Volume profile:
    /// that profile is a project asset, and writing to sharedProfile from play mode would persist
    /// into the file on disk. Instead it layers its own runtime Volume at a higher priority and
    /// blends its weight. And it never fights the pause menu: while PauseButtonUI reports a pause it
    /// stops touching Time.timeScale entirely and lets the menu own it.
    ///
    /// Camera control works by disabling CameraFollow for the duration and driving the transform
    /// here, then handing it back. CameraFollow smooths its way home on its own, so the return
    /// needs no special casing.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class BossFinisher : MonoBehaviour
    {
        private static BossFinisher _instance;
        private static BossFinisherSettings _settings;
        private static bool _settingsLookupDone;

        private static BossFinisherSettings Settings
        {
            get
            {
                if (!_settingsLookupDone)
                {
                    _settingsLookupDone = true;
                    _settings = Resources.Load<BossFinisherSettings>(BossFinisherSettings.ResourcePath);

                    if (_settings == null)
                        _settings = ScriptableObject.CreateInstance<BossFinisherSettings>();
                }

                return _settings;
            }
        }

        private Enemy _boss;
        private Transform _warrior;
        private Camera _camera;
        private CameraFollow _cameraFollow;

        private bool _active;
        private float _currentSize;
        private float _sizeOnEntry;
        private Vector3 _lastBossPosition;
        private float _holdUntil;

        private Volume _volume;
        private float _volumeWeight;
        private GameObject _flashGo;

        private float _shakeAmplitude;
        private float _shakeStartedAt;
        private float _shakeEndsAt;

        private float _timeScaleTarget = 1f;
        private float _hitStopUntil;

        // Captured when the shot starts, not hard-coded: the game may legitimately run at a scale
        // other than 1 (a slow-motion power, or an Editor left at 0.5), and restoring to a constant
        // would silently change the game speed. The settings are read as a MULTIPLIER of this.
        private float _timeScaleOnEntry = 1f;
        private float _fixedDeltaOnEntry = 0.02f;

        // Set on the killing blow once the freeze frame is over. GameMgr already owns boss-death
        // slow motion (PlayBossDeathSlowMotion, 0.15 for 0.6s) and the level-complete flow waits on
        // it, so this runner must stop writing Time.timeScale rather than fight it every frame.
        private bool _killing;
        private bool _timeReleased;

        /// <summary>
        /// True while the cinematic owns the camera, from the brink until the camera is handed
        /// back. GameMgr waits on this before raising the boss memory relic so the two do not
        /// compete for the same frame.
        /// </summary>
        public static bool IsRunning
        {
            get { return _instance != null && _instance._active; }
        }

        /// <summary>
        /// Called from Enemy.TakeDamageAndReturnKilled for every hit that lands on a boss. Cheap
        /// and silent until the boss is actually on its last hits.
        /// </summary>
        public static void NotifyBossDamaged(Enemy boss, bool killed)
        {
            if (boss == null) return;

            BossFinisherSettings settings = Settings;
            if (settings == null || !settings.enabled) return;

            float fraction = boss.maxHealth > 0f ? boss.CurrentHealth / boss.maxHealth : 0f;

            // Nothing happens until the boss crosses the brink, but once the shot is running every
            // further hit feeds it, including the one that finally lands the kill.
            bool alreadyRunning = _instance != null && _instance._active && _instance._boss == boss;
            if (!killed && !alreadyRunning && fraction > settings.brinkHealthFraction)
                return;

            Ensure();
            _instance.Handle(boss, killed);
        }

        private static void Ensure()
        {
            if (_instance != null) return;

            var go = new GameObject("BossFinisher");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BossFinisher>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (_instance == this)
                _instance = null;

            Restore();
        }

        private void OnDisable()
        {
            // A disabled runner must never leave the game in slow motion or the camera detached.
            Restore();
        }

        /// <summary>A scene change invalidates the camera and the boss, so drop the shot at once.</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Restore();
        }

        private void Handle(Enemy boss, bool killed)
        {
            BossFinisherSettings s = Settings;

            // First boss to reach the brink owns the shot; a second one does not steal the camera.
            if (_active && _boss != null && _boss != boss)
                return;

            if (!_active && !Begin(boss))
                return;

            _lastBossPosition = boss.transform.position;

            float now = Time.realtimeSinceStartup;

            if (killed)
            {
                // Hand time back at once. GameMgr.PlayBossDeathSlowMotion fires on this same
                // death and drops to 0.15, which already IS the punch; a freeze frame layered on
                // top would leave timeScale at 0 until GameMgr's routine ends, hanging the game.
                _killing = true;
                ReleaseTimeControl();
                _hitStopUntil = 0f;
                _holdUntil = now + s.killHoldDuration;
                Shake(s.killShakeAmplitude, s.shakeDuration * 1.5f);
                PlaySfx(s.killSfx);
                StartCoroutine(PlayDeathSpectacle(boss.transform.position));
            }
            else
            {
                _hitStopUntil = now + s.hitStopDuration;
                _holdUntil = now + s.holdDuration;
                Shake(s.hitShakeAmplitude, s.shakeDuration);
            }
        }

        private bool Begin(Enemy boss)
        {
            _camera = Camera.main;
            if (_camera == null || !_camera.orthographic)
                return false;

            _warrior = FindWarrior();

            _boss = boss;
            _active = true;

            _cameraFollow = _camera.GetComponent<CameraFollow>();
            if (_cameraFollow != null)
                _cameraFollow.enabled = false;

            _sizeOnEntry = _camera.orthographicSize;
            _currentSize = _sizeOnEntry;
            _lastBossPosition = boss.transform.position;

            _timeScaleOnEntry = Time.timeScale > 0f ? Time.timeScale : 1f;
            _fixedDeltaOnEntry = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;

            BossFinisherSettings s = Settings;
            _timeScaleTarget = _timeScaleOnEntry * s.slowMotionScale;

            BuildVolume(s);
            SpawnVfx(s.brinkVfxPrefab, boss.transform.position);
            PlaySfx(s.brinkSfx);

            StartCoroutine(RunShot());
            return true;
        }

        /// <summary>
        /// Same fallback chain CameraFollow uses. The singleton can legitimately be null while a
        /// Warrior exists in the scene, which was observed in play mode, and framing on the boss
        /// alone reads much worse than framing the duel.
        /// </summary>
        private static Transform FindWarrior()
        {
            var w = Assets.Scripts.Characteres.WarriorController.Warrior.Instance;
            if (w != null) return w.transform;

            w = FindFirstObjectByType<Assets.Scripts.Characteres.WarriorController.Warrior>();
            if (w != null) return w.transform;

            var go = GameObject.Find("Warrior");
            return go != null ? go.transform : null;
        }

        private IEnumerator RunShot()
        {
            BossFinisherSettings s = Settings;

            // Zoom in. Unscaled throughout: the whole point is that game time is crawling.
            yield return Blend(s.zoomInDuration, t =>
            {
                _currentSize = Mathf.Lerp(_sizeOnEntry, s.zoomOrthographicSize, Smooth(t));
                _volumeWeight = Smooth(t);
            });

            _currentSize = s.zoomOrthographicSize;
            _volumeWeight = 1f;
            // The kill has its own hold: without this the brink hold would always win, since it is
            // the longer of the two, and killHoldDuration would be a knob with no effect.
            float baseHold = _killing ? s.killHoldDuration : s.holdDuration;
            _holdUntil = Mathf.Max(_holdUntil, Time.realtimeSinceStartup + baseHold);

            // Hold. Every landed hit pushes _holdUntil further out, so the shot lasts exactly as
            // long as the player keeps swinging.
            while (Time.realtimeSinceStartup < _holdUntil)
                yield return null;

            float sizeAtExit = _currentSize;
            yield return Blend(s.zoomOutDuration, t =>
            {
                _currentSize = Mathf.Lerp(sizeAtExit, _sizeOnEntry, Smooth(t));
                _volumeWeight = 1f - Smooth(t);
            });

            Restore();
        }

        private IEnumerator Blend(float duration, System.Action<float> step)
        {
            if (duration <= 0f)
            {
                step(1f);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                step(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            step(1f);
        }

        private static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private void Update()
        {
            if (!_active) return;

            if (_volume != null)
                _volume.weight = _volumeWeight;

            // The pause menu owns Time.timeScale while it is up. Touching it here would silently
            // un-pause the game behind the menu.
            if (PauseButtonUI.IsPaused)
                return;

            if (_timeReleased)
                return;

            bool inHitStop = Time.realtimeSinceStartup < _hitStopUntil;
            ApplyTimeScale(inHitStop ? 0f : _timeScaleTarget);
        }

        private void LateUpdate()
        {
            if (!_active || _camera == null) return;

            if (_boss != null)
                _lastBossPosition = _boss.transform.position;

            Vector3 focus = _lastBossPosition;
            if (_warrior != null)
                focus = Vector3.Lerp(_warrior.position, _lastBossPosition, Settings.bossFraming);

            Vector3 offset = ShakeOffset();

            _camera.transform.position = new Vector3(
                focus.x + offset.x,
                focus.y + offset.y,
                _camera.transform.position.z);

            _camera.orthographicSize = _currentSize;
        }

        private void Shake(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f) return;

            float now = Time.realtimeSinceStartup;

            // A new impact never weakens an ongoing shake.
            _shakeAmplitude = Mathf.Max(_shakeAmplitude, amplitude);
            _shakeStartedAt = now;
            _shakeEndsAt = Mathf.Max(_shakeEndsAt, now + duration);
        }

        private Vector3 ShakeOffset()
        {
            float now = Time.realtimeSinceStartup;
            if (now >= _shakeEndsAt || _shakeAmplitude <= 0f)
            {
                _shakeAmplitude = 0f;
                return Vector3.zero;
            }

            float span = Mathf.Max(0.0001f, _shakeEndsAt - _shakeStartedAt);
            float decay = 1f - Mathf.Clamp01((now - _shakeStartedAt) / span);
            float a = _shakeAmplitude * decay;

            // Perlin rather than Random so successive frames stay correlated and it reads as a
            // shake instead of static.
            float x = (Mathf.PerlinNoise(now * 28f, 0f) - 0.5f) * 2f * a;
            float y = (Mathf.PerlinNoise(0f, now * 28f) - 0.5f) * 2f * a;
            return new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Stops writing Time.timeScale and gives the physics step back. Used on the killing blow,
        /// where GameMgr owns the slow motion and the level-complete flow waits on it.
        /// </summary>
        private void ReleaseTimeControl()
        {
            _timeReleased = true;
            Time.fixedDeltaTime = _fixedDeltaOnEntry;
        }

        private void ApplyTimeScale(float scale)
        {
            Time.timeScale = scale;

            // Keep physics steps proportional, otherwise slow motion makes collisions coarse.
            float ratio = _timeScaleOnEntry > 0f ? scale / _timeScaleOnEntry : scale;
            Time.fixedDeltaTime = _fixedDeltaOnEntry * Mathf.Max(ratio, 0.01f);
        }

        private void BuildVolume(BossFinisherSettings s)
        {
            if (_volume != null) return;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(s.bloomIntensity);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(s.vignetteIntensity);

            var chroma = profile.Add<ChromaticAberration>(true);
            chroma.intensity.Override(s.chromaticAberration);

            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(s.saturation);
            color.contrast.Override(s.contrast);

            var go = new GameObject("BossFinisherVolume");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(transform, false);

            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;   // above the scene's Global Volume
            _volume.profile = profile;
            _volume.weight = 0f;
            _volumeWeight = 0f;
        }

        private void SpawnVfx(GameObject prefab, Vector3 position, float scale = 1f)
        {
            if (prefab == null) return;

            GameObject spawned = VFXPool.Instance != null
                ? VFXPool.Instance.Spawn(prefab, position, Quaternion.identity)
                : Instantiate(prefab, position, Quaternion.identity);

            // The pool resets localScale from the prefab on every spawn, so scaling here is safe
            // and does not leak into the next user of the same pooled object.
            if (spawned != null && !Mathf.Approximately(scale, 1f))
                spawned.transform.localScale *= scale;
        }

        /// <summary>
        /// The death chain. One puff reads as a hit; a staggered sequence of bursts reads as a
        /// death. Every delay is in REAL seconds so the choreography keeps its rhythm while the
        /// game itself is crawling in slow motion.
        /// </summary>
        private IEnumerator PlayDeathSpectacle(Vector3 at)
        {
            BossFinisherSettings s = Settings;

            StartCoroutine(ScreenFlash(s));

            if (s.deathBursts == null || s.deathBursts.Length == 0)
                yield break;

            var ordered = new List<DeathBurst>(s.deathBursts);
            ordered.Sort((a, b) => a.delay.CompareTo(b.delay));

            float elapsed = 0f;
            for (int i = 0; i < ordered.Count; i++)
            {
                DeathBurst burst = ordered[i];
                if (burst == null || burst.prefab == null) continue;

                float wait = burst.delay - elapsed;
                if (wait > 0f)
                {
                    yield return new WaitForSecondsRealtime(wait);
                    elapsed = burst.delay;
                }

                int count = Mathf.Max(1, burst.count);
                for (int c = 0; c < count; c++)
                {
                    Vector2 jitter = burst.radius > 0f
                        ? Random.insideUnitCircle * burst.radius
                        : Vector2.zero;

                    SpawnVfx(burst.prefab, at + new Vector3(jitter.x, jitter.y, 0f), burst.scale);
                }
            }
        }

        /// <summary>
        /// Full-screen white-out on its own overlay canvas, above everything including the HUD.
        /// Squared falloff so it punches hard and leaves quickly rather than lingering as a haze.
        /// </summary>
        private IEnumerator ScreenFlash(BossFinisherSettings s)
        {
            if (s.screenFlashColor.a <= 0f || s.screenFlashDuration <= 0f)
                yield break;

            if (_flashGo != null)
                Destroy(_flashGo);

            _flashGo = new GameObject("BossFinisherFlash");
            _flashGo.hideFlags = HideFlags.HideAndDontSave;
            _flashGo.transform.SetParent(transform, false);

            var canvas = _flashGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            var imageGo = new GameObject("Flash");
            imageGo.transform.SetParent(_flashGo.transform, false);

            var image = imageGo.AddComponent<Image>();
            image.raycastTarget = false;   // must never eat a touch aimed at the game

            RectTransform rt = image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Color c = s.screenFlashColor;
            float t = 0f;
            while (t < s.screenFlashDuration && image != null)
            {
                t += Time.unscaledDeltaTime;
                float k = 1f - Mathf.Clamp01(t / s.screenFlashDuration);
                image.color = new Color(c.r, c.g, c.b, c.a * k * k);
                yield return null;
            }

            if (_flashGo != null)
            {
                Destroy(_flashGo);
                _flashGo = null;
            }
        }

        /// <summary>
        /// Plays on the runner itself, which is DontDestroyOnLoad, instead of through OneShotAudio.
        /// OneShotAudio spawns its source in the ACTIVE scene, and a boss death loads the menu
        /// roughly a second later while the death clip runs longer than that, so the most important
        /// sound of the fight would be cut off mid-clip. 2D on purpose: a boss dying is a global
        /// event, not a point in the world the player can walk away from.
        /// </summary>
        private void PlaySfx(AudioClip clip)
        {
            if (clip == null) return;

            var go = new GameObject("BossFinisherSfx_" + clip.name);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = Mathf.Clamp01(Settings.sfxVolume);
            source.spatialBlend = 0f;
            source.Play();

            StartCoroutine(DestroyAfterRealtime(go, clip.length + 0.1f));
        }

        /// <summary>
        /// Realtime, not scaled: the clip plays at normal speed regardless of slow motion, so a
        /// scaled wait would keep the dead source alive several times longer than the sound.
        /// </summary>
        private IEnumerator DestroyAfterRealtime(GameObject go, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);

            if (go != null)
                Destroy(go);
        }

        /// <summary>
        /// Puts everything back. Safe to call at any point and any number of times: it is the exit
        /// of the normal sequence and also the guard on disable, destroy and scene load.
        /// </summary>
        private void Restore()
        {
            if (_camera != null && _sizeOnEntry > 0f)
                _camera.orthographicSize = _sizeOnEntry;

            if (_cameraFollow != null)
                _cameraFollow.enabled = true;

            if (_volume != null)
            {
                _volume.weight = 0f;
                Destroy(_volume.gameObject);
                _volume = null;
            }

            if (_flashGo != null)
            {
                Destroy(_flashGo);
                _flashGo = null;
            }

            // Never restore time while the pause menu holds it at zero; it restores its own value.
            // And never restore it after releasing control on a kill: GameMgr is mid slow-motion
            // there and would be cut short. The physics step is ours either way.
            if (!PauseButtonUI.IsPaused && !_timeReleased)
                ApplyTimeScale(_timeScaleOnEntry);
            else
                Time.fixedDeltaTime = _fixedDeltaOnEntry;

            _active = false;
            _boss = null;
            _warrior = null;
            _camera = null;
            _cameraFollow = null;
            _volumeWeight = 0f;
            _shakeAmplitude = 0f;
            _hitStopUntil = 0f;
            _timeScaleTarget = _timeScaleOnEntry;
            _killing = false;
            _timeReleased = false;
        }
    }
}

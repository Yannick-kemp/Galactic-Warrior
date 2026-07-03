using UnityEngine;
using Assets.Scripts.Characteres.WarriorController;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Assets.Scripts.Objects.Game
{
    /// <summary>
    /// Desktop (Steam/Windows) pause trigger. The on-screen touch Pause/Mute buttons are a
    /// mobile affordance and are hidden on standalone, so here Escape (keyboard) or the
    /// Start/Menu button (gamepad ☰) toggles the shared <see cref="PauseMenu"/> — which shows
    /// the same PAUSE overlay (Resume + Settings) used on mobile.
    ///
    /// It bootstraps itself (a persistent object created at launch), so no scene wiring is
    /// required. Attaching it manually to a HUD prefab also works — the duplicate guard keeps
    /// a single active instance. Inert on mobile builds.
    /// </summary>
    public class DesktopPauseInput : MonoBehaviour
    {
        private static DesktopPauseInput _instance;

        // Auto-spawn a persistent trigger on desktop so pause works with zero scene wiring.
        // Skipped if a scene copy already registered itself in Awake.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_STANDALONE || UNITY_WSA || UNITY_GAMECORE || UNITY_EDITOR
            if (_instance != null)
                return;

            var go = new GameObject("DesktopPauseInput");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DesktopPauseInput>();
#endif
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
#if UNITY_STANDALONE || UNITY_WSA || UNITY_GAMECORE || UNITY_EDITOR
            if (_instance != this)
                return;

            if (!WantsToggle())
                return;

            // Only meaningful inside a level (a Warrior exists). Ignore in menus, where a
            // global pause would freeze the UI with no way back — but still allow RESUME.
            if (!PauseMenu.IsPaused && Warrior.Instance == null)
                return;

            PauseMenu.Toggle();
#endif
        }

#if UNITY_STANDALONE || UNITY_WSA || UNITY_GAMECORE || UNITY_EDITOR
        private static bool WantsToggle()
        {
            // Legacy Input (project uses the "Both" input backend) — same as DirectControlHud.
            if (Input.GetKeyDown(KeyCode.Escape))
                return true;

#if ENABLE_INPUT_SYSTEM
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.startButton.wasPressedThisFrame)  // Xbox: Menu ☰
                return true;
#endif
            return false;
        }
#endif
    }
}

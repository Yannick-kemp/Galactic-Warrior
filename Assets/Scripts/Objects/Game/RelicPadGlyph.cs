using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Assets.Scripts.Objects.Game
{
    /// <summary>
    /// Optional discoverability helper: shows a small button-glyph badge (e.g. "RB", "↑") on a relic
    /// button ONLY while a gamepad is connected, and hides it otherwise (mouse/touch users). Put this
    /// on the relic button and assign its badge child object; create one badge per relic with the
    /// matching label/sprite. Purely cosmetic — the actual input lives in RelicGamepadInput.
    /// </summary>
    public class RelicPadGlyph : MonoBehaviour
    {
        [Tooltip("Child object holding the glyph label/sprite (e.g. a small 'RB' badge).")]
        [SerializeField] private GameObject badge;

        private bool _shown;
        private bool _initialized;

        private void OnEnable()
        {
            _initialized = false; // force a state apply on (re)enable
        }

        private void Update()
        {
            bool connected;
#if ENABLE_INPUT_SYSTEM
            connected = Gamepad.current != null;
#else
            connected = false;
#endif
            if (_initialized && connected == _shown)
                return;

            if (badge != null)
                badge.SetActive(connected);

            _shown = connected;
            _initialized = true;
        }
    }
}

using Assets.Scripts.Relics.Core;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Assets.Scripts.Objects.Game
{
    /// <summary>
    /// Maps a physical Xbox/DualShock gamepad to the clickable relic buttons on desktop (Steam).
    /// Each mapped input calls RelicUIController.ActivateRelic(def), which reuses the exact same
    /// centralized flow as a mouse/touch click (consume / arm / disarm / refund / mutual exclusion
    /// / visuals). Mouse clicking the relic buttons keeps working in parallel.
    ///
    /// Default layout (movement is on the left stick, so the D-pad is free):
    ///   D-pad Up    → Health (heal)        D-pad Down  → Shield
    ///   D-pad Left  → Sprint               D-pad Right → Key relic (contextual)
    ///   RB          → arm Ice-Ball  (then aim with the stick, Y fires — see DirectControlHud)
    ///   LB          → arm PowerCombo (then the next attack / X triggers it)
    /// Pressing RB/LB again cancels the armed relic (handled by RelicUIController).
    ///
    /// Place this component on the in-game HUD (the _GameTools prefab): present in every gameplay
    /// scene, absent from the menu. Relic use is independent of the movement scheme (works in Tap
    /// and Direct), so this is intentionally NOT gated on ControlScheme.
    /// </summary>
    public class RelicGamepadInput : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private RelicUIController controller;

        [Header("Relic definitions mapped to pad inputs")]
        [SerializeField] private RelicDefinition healthRelic;     // D-pad Up
        [SerializeField] private RelicDefinition shieldRelic;     // D-pad Down
        [SerializeField] private RelicDefinition sprintRelic;     // D-pad Left
        [SerializeField] private RelicDefinition keyRelic;        // D-pad Right (contextual)
        [SerializeField] private RelicDefinition iceBallRelic;    // RB → arm, then stick + Y to fire
        [SerializeField] private RelicDefinition powerComboRelic; // LB → arm, then attack triggers

        private void Awake()
        {
            if (controller == null)
                controller = FindFirstObjectByType<RelicUIController>();
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (controller == null)
                return;

            // Never consume relics while paused or a menu overlay is open.
            if (PauseMenu.IsPaused)
                return;

            var gp = Gamepad.current;
            if (gp == null)
                return;

            // Edge-triggered (wasPressedThisFrame) so a held button fires only once.
            // ActivateRelic(null) is a safe no-op, so unassigned mappings simply do nothing.
            if (gp.dpad.up.wasPressedThisFrame) controller.ActivateRelic(healthRelic);
            if (gp.dpad.down.wasPressedThisFrame) controller.ActivateRelic(shieldRelic);
            if (gp.dpad.left.wasPressedThisFrame) controller.ActivateRelic(sprintRelic);
            if (gp.dpad.right.wasPressedThisFrame) controller.ActivateRelic(keyRelic);
            if (gp.leftShoulder.wasPressedThisFrame) controller.ActivateRelic(powerComboRelic);
            if (gp.rightShoulder.wasPressedThisFrame) controller.ActivateRelic(iceBallRelic);
#endif
        }
    }
}

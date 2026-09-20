using Assets.Scripts.Relics.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Objects.Game
{
    /// <summary>
    /// Gamepad shortcuts for the relic buttons, same layout as the DevXbox build:
    ///   D-pad ↓ Shield   ← Sprint   → Key (contextual)
    /// Health and Memory are not mapped: they trigger on their own, the player never uses them.
    ///   LB Power Combo (arm, the next attack triggers it)   RB Ice-Ball (arm, aim with the stick, Y fires)
    /// Pressing LB/RB again cancels the armed relic (RelicUIController handles it).
    ///
    /// Each press goes through the exact flow of a click (RelicUIController.ActivateRelicById, or the
    /// slot's own button for a standalone slot like the Key), so touch and mouse keep working
    /// in parallel. Relics are found by id at runtime: no Inspector
    /// wiring, the component installs itself. While a controller is connected, a small badge on
    /// each relic button shows which button uses it.
    ///
    /// Web build only (GamepadSupport.Enabled); inert on the Android build.
    /// </summary>
    public class RelicGamepadInput : MonoBehaviour
    {
        private enum Glyph { Up, Down, Left, Right, LB, RB }

        private static readonly (string relicId, Glyph glyph)[] Mapping =
        {
            ("relic_shield", Glyph.Down),
            ("relic_sprint", Glyph.Left),
            ("relic_key", Glyph.Right),
            ("relic_power_combo", Glyph.LB),
            ("relic_IceBall", Glyph.RB),
        };

        private RelicUIController _controller;
        private readonly GameObject[] _badges = new GameObject[Mapping.Length];
        private bool _badgesShown;
        private float _nextSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!GamepadSupport.Enabled)
                return;

            var go = new GameObject("RelicGamepadInput");
            DontDestroyOnLoad(go);
            go.AddComponent<RelicGamepadInput>();
        }

        private void Update()
        {
            if (_controller == null)
            {
                // Present in gameplay scenes only (and may appear after the scene loads):
                // looked for once a second rather than every frame.
                if (Time.unscaledTime < _nextSearch)
                    return;
                _nextSearch = Time.unscaledTime + 1f;

                _controller = FindFirstObjectByType<RelicUIController>();
                _badgesShown = false;
                for (int i = 0; i < _badges.Length; i++)
                    _badges[i] = null;
                if (_controller == null)
                    return;
            }

            bool connected = GamepadSupport.Connected;
            if (connected != _badgesShown)
                ShowBadges(connected);

#if ENABLE_INPUT_SYSTEM
            var pad = GamepadSupport.Pad;
            if (pad == null)
                return;

            // Never consume relics while paused, frozen (demo end, tutorial gate) or input-locked.
            if (PauseButtonUI.IsPaused || Time.timeScale <= 0f)
                return;
            if (InputMgr.Instance != null && InputMgr.Instance.InputLocked)
                return;

            // Edge-triggered so a held button fires once. Unknown ids are a safe no-op.
            if (pad.dpad.down.wasPressedThisFrame) Activate("relic_shield");
            if (pad.dpad.left.wasPressedThisFrame) Activate("relic_sprint");
            if (pad.dpad.right.wasPressedThisFrame) Activate("relic_key");
            if (pad.leftShoulder.wasPressedThisFrame) Activate("relic_power_combo");
            if (pad.rightShoulder.wasPressedThisFrame) Activate("relic_IceBall");
#endif
        }

        // Relics listed in RelicUIController go through its click flow. The others (in the demo
        // HUD: the Key) are standalone RelicUIButton slots that handle their own click,
        // so the pad presses their button — the exact path of a tap. A non-interactable button
        // (e.g. the Key outside a lock's window) ignores it, like a real click would.
        private void Activate(string relicId)
        {
            if (_controller.ActivateRelicById(relicId))
                return;

            Button button = FindStandaloneButton(relicId);
            if (button != null && button.isActiveAndEnabled && button.interactable)
                button.onClick.Invoke();
        }

        private Button ResolveButton(string relicId)
        {
            Button button = _controller.GetRelicButton(relicId);
            return button != null ? button : FindStandaloneButton(relicId);
        }

        private static Button FindStandaloneButton(string relicId)
        {
            foreach (RelicUIButton slot in FindObjectsByType<RelicUIButton>(FindObjectsSortMode.None))
            {
                if (slot.Definition != null && slot.Definition.relicId == relicId)
                    return slot.GetComponent<Button>();
            }
            return null;
        }

        // ── Button badges (discoverability, purely visual) ───────────────────────────

        private void ShowBadges(bool show)
        {
            _badgesShown = show;

            for (int i = 0; i < Mapping.Length; i++)
            {
                if (_badges[i] == null && show)
                {
                    Button button = ResolveButton(Mapping[i].relicId);
                    if (button != null)
                        _badges[i] = BuildBadge(button.transform as RectTransform, Mapping[i].glyph);
                }

                if (_badges[i] != null)
                    _badges[i].SetActive(show);
            }
        }

        private static GameObject BuildBadge(RectTransform parent, Glyph glyph)
        {
            var badge = new GameObject("PadGlyph", typeof(RectTransform), typeof(Image));
            var rt = badge.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); // top-right corner of the relic button
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-6f, -6f);
            rt.sizeDelta = new Vector2(34f, 34f);

            var bg = badge.GetComponent<Image>();
            bg.sprite = RuntimeSprites.Circle();
            bg.color = new Color(0f, 0f, 0f, 0.8f);
            bg.raycastTarget = false; // never steals taps/clicks from the relic button

            if (glyph == Glyph.LB || glyph == Glyph.RB)
            {
                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var lrt = label.GetComponent<RectTransform>();
                lrt.SetParent(rt, false);
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = lrt.offsetMax = Vector2.zero;

                var tmp = label.GetComponent<TextMeshProUGUI>();
                tmp.text = glyph == Glyph.LB ? "LB" : "RB";
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontSize = 15f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color = Color.white;
                tmp.raycastTarget = false;
            }
            else
            {
                // A small D-pad cross (the game font has no arrow characters): grey cross with
                // the arm to press drawn in white.
                const float length = 24f;
                const float thickness = 8f;
                var grey = new Color(0.55f, 0.55f, 0.55f, 1f);

                AddBar(rt, "CrossH", new Vector2(length, thickness), Vector2.zero, grey);
                AddBar(rt, "CrossV", new Vector2(thickness, length), Vector2.zero, grey);

                float arm = (length - thickness) * 0.5f;              // length of one arm
                float offset = thickness * 0.5f + arm * 0.5f;         // arm centre from the middle
                Vector2 dir = glyph == Glyph.Up ? Vector2.up
                            : glyph == Glyph.Down ? Vector2.down
                            : glyph == Glyph.Left ? Vector2.left
                            : Vector2.right;
                Vector2 size = dir.x != 0f ? new Vector2(arm, thickness) : new Vector2(thickness, arm);
                AddBar(rt, "Pressed", size, dir * offset, Color.white);
            }

            return badge;
        }

        private static void AddBar(RectTransform parent, string name, Vector2 size, Vector2 position, Color color)
        {
            var bar = new GameObject(name, typeof(RectTransform), typeof(Image));
            var brt = bar.GetComponent<RectTransform>();
            brt.SetParent(parent, false);
            brt.sizeDelta = size;
            brt.anchoredPosition = position;

            var img = bar.GetComponent<Image>();
            img.sprite = RuntimeSprites.Solid();
            img.color = color;
            img.raycastTarget = false;
        }
    }
}

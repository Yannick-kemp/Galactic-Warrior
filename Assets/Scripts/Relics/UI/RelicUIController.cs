// Assets/Scripts/Relics/UI/RelicUIController.cs
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Definitions; // ShieldRelic
using Assets.Scripts.Relics.UI;
using Assets.Scripts.Relics.World;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class RelicUIController : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxUsesPerFramePerRelic = 1;

    // relicId -> frame / count
    private readonly Dictionary<string, int> _useFrameByRelic = new();
    private readonly Dictionary<string, int> _useCountInFrameByRelic = new();

    // store delegates so RemoveListener actually removes the same instance
    private readonly Dictionary<Button, UnityAction> _clickHandlers = new();
    public enum RelicUseEffect
    {
        None,
        Heal,
        ShieldTimed
    }

    [System.Serializable]
    public class RelicButtonRule
    {
        [Tooltip("Optional label for your own readability")]
        public string key;

        [Tooltip("Assign slot object (RelicHealth, RelicPowerComBo, ...)")]
        public RelicUIButton slot;

        [Tooltip("Optional: if null, Button is taken from slot")]
        public Button button;

        [Tooltip("Optional override if slot/definition is missing")]
        public string relicIdOverride;

        [Tooltip("How many stacks to consume per click")]
        public int consumeStacks = 1;

        [Tooltip("Use for inventory/resource relics that should never be clickable.")]
        public bool passiveDisplayOnly = false;

        [Header("Key Platform Motion")]
        [Tooltip("For KeyRelic: blink this slot while Warrior is in front of a disabled moving/rotating platform lock.")]
        public bool blinkWhenPlatformMotionLockAvailable = true;
        public Color keyBlinkColor = Color.yellow;
        [Min(0.1f)] public float keyBlinkSpeed = 7f;

        [Header("Built-in effect")]
        public RelicUseEffect effect = RelicUseEffect.None;
        public int effectValue = 20; // heal amount if Heal, fallback duration if ShieldTimed

        [Header("Timed Visual (for ShieldTimed)")]
        public Color activeColor = Color.green;
        [Tooltip("Optional radial/filled image. fillAmount will go 1 -> 0.")]
        public Image cooldownFill;
        public bool hideFillWhenDone = true;

        [Header("Optional custom callback after successful consume")]
        public UnityEvent onUsed;
    }

    [Header("Refs")]
    [SerializeField] private Warrior warrior;
    [SerializeField] private RelicManager relicManager;

    [Header("Rules (one per button)")]
    [SerializeField] private List<RelicButtonRule> rules = new();

    // Visual state
    private readonly Dictionary<Button, Color> _defaultGraphicColors = new();
    private readonly Dictionary<Button, Coroutine> _runningFx = new();
    private readonly Dictionary<Button, Selectable.Transition> _savedTransitions = new();
    private readonly HashSet<Button> _fxActive = new();
    private readonly HashSet<Button> _keyBlinkingButtons = new();

    // IceBallRelic can be canceled/refunded inside the same click frame.
    // Keep a tiny state cache so the controller can re-apply count/interactable/
    // armed visuals on the next frame if Unity UI re-applies a selected/highlighted
    // Button state after the click callback.
    private readonly Dictionary<Button, bool> _lastArmedStateByButton = new();
    private readonly Dictionary<string, int> _lastKnownCountByRelicId = new();

    private void Awake()
    {
        if (warrior == null) warrior = FindFirstObjectByType<Warrior>();
        if (relicManager == null && warrior != null) relicManager = warrior.GetComponent<RelicManager>();

        AutoWireButtons();
        CacheDefaultColors();
        BindClicksOnce();
    }

    private void OnEnable()
    {
        if (relicManager != null)
            relicManager.OnRelicCountChanged += OnRelicCountChanged;

        KeyRelicLock.OnActivePlatformMotionLockChanged += OnActivePlatformMotionLockChanged;

        RefreshAllButtons();
    }

    private void OnDisable()
    {
        if (relicManager != null)
            relicManager.OnRelicCountChanged -= OnRelicCountChanged;

        KeyRelicLock.OnActivePlatformMotionLockChanged -= OnActivePlatformMotionLockChanged;

        StopAllRunningFx();

        // Optional: clear per-frame gates
        _useFrameByRelic.Clear();
        _useCountInFrameByRelic.Clear();

        // remove click handlers safely
        foreach (var kv in _clickHandlers)
        {
            if (kv.Key != null && kv.Value != null)
                kv.Key.onClick.RemoveListener(kv.Value);
        }
        _clickHandlers.Clear();
    }

    private void Update()
    {
        UpdateKeyBlinkingButtons();
        RefreshArmableRelicButtonsWhenStateChanged();
    }

    private void LateUpdate()
    {
        // Final visual authority for cancelable armable relics.
        // This runs after normal Update/event handling so a selected/highlighted
        // Button state or another refresh pass cannot leave IceBallRelic green
        // after it has been disarmed/refunded.
        StabilizeControllerOwnedArmableButtonVisuals();
    }

    private void AutoWireButtons()
    {
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r == null) continue;

            if (r.button == null && r.slot != null)
                r.button = r.slot.GetComponent<Button>();

            // Centralize clicks here. This prevents the standalone RelicUIButton
            // handler from re-arming immediately after this controller disarms/refunds.
            if (r.slot != null)
                r.slot.SetControlledByRelicUIController(true);

            // Optional: avoid keyboard/gamepad selected-color sticking.
            if (r.button != null)
            {
                var nav = r.button.navigation;
                nav.mode = Navigation.Mode.None;
                r.button.navigation = nav;

                // IMPORTANT for click-again-to-cancel relics:
                // IceBall/PowerCombo visuals are owned by this controller.
                // Do not let Unity Button ColorTint re-apply Highlighted/Selected/Pressed
                // colors after onClick, otherwise the button can turn green again until
                // the player clicks somewhere else.
                if (IsControllerOwnedArmableRule(r))
                {
                    if (!_savedTransitions.ContainsKey(r.button))
                        _savedTransitions[r.button] = r.button.transition;

                    r.button.transition = Selectable.Transition.None;
                }
            }
        }
    }

    private void CacheDefaultColors()
    {
        _defaultGraphicColors.Clear();

        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r?.button == null) continue;

            var g = r.button.targetGraphic;
            if (g != null)
            {
                // IMPORTANT:
                // Cache the actual visible color from the image/icon.
                // Do NOT use r.button.colors.normalColor here.
                _defaultGraphicColors[r.button] = g.color;
            }

            if (r.cooldownFill != null)
            {
                r.cooldownFill.fillAmount = 0f;
                if (r.hideFillWhenDone)
                    r.cooldownFill.gameObject.SetActive(false);
            }
        }
    }

    private void BindClicksOnce()
    {
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r == null || r.button == null) continue;

            // remove previous bound handler for this button (if any)
            if (_clickHandlers.TryGetValue(r.button, out var oldHandler) && oldHandler != null)
                r.button.onClick.RemoveListener(oldHandler);

            int idx = i; // capture
            UnityAction handler = () => OnClickRule(idx);

            _clickHandlers[r.button] = handler;
            r.button.onClick.AddListener(handler);
        }
    }

    private bool TryEnterUseGate(string relicId)
    {
        int frame = Time.frameCount;

        if (!_useFrameByRelic.TryGetValue(relicId, out int seenFrame) || seenFrame != frame)
        {
            _useFrameByRelic[relicId] = frame;
            _useCountInFrameByRelic[relicId] = 0;
        }

        int used = _useCountInFrameByRelic[relicId];
        if (used >= maxUsesPerFramePerRelic)
            return false;

        _useCountInFrameByRelic[relicId] = used + 1;
        return true;
    }
    private void OnClickRule(int index)
    {
        if (index < 0 || index >= rules.Count) return;
        if (relicManager == null) return;
        if (IsWarriorUnavailable()) return;

        var r = rules[index];
        if (r == null) return;

        string relicId = ResolveRelicId(r);
        if (string.IsNullOrEmpty(relicId)) return;

        // KEY RELIC
        if (IsKeyRelicRule(r))
        {
            TryActivateKeyPlatformMotion(r);
            RefreshButton(r);
            return;
        }

        if (IsPassiveDisplayOnlyRelic(r))
        {
            RefreshButton(r);
            return;
        }

        // Click-again-to-disarm must run before any consume/arm logic.
        // This keeps the armed state reversible without spending another stack.
        if (TryDisarmAlreadyArmedRelic(r))
            return;

        if (IsBlockedByMutualExclusion(r))
        {
            RefreshButton(r);
            return;
        }

        if (!TryEnterUseGate(relicId))
            return;

        int consume = Mathf.Max(1, r.consumeStacks);

        if (relicManager.GetCountById(relicId) < consume)
        {
            RefreshButton(r);
            return;
        }

        // ICE BALL: first click consumes one stack and arms the waiting stage.
        // The actual projectile is fired later by the world-touch flow.
        if (r.slot != null && r.slot.Definition is IceBallRelic iceDef)
        {
            bool consumed = relicManager.TryConsumeById(relicId, consume);
            if (!consumed)
            {
                RefreshButton(r);
                return;
            }

            bool armed = warrior != null && warrior.TryArmIceBallRelic(iceDef, consumeOnCast: false);
            if (!armed)
            {
                RefundStacks(iceDef, consume);
                RefreshButton(r);
                return;
            }

            r.onUsed?.Invoke();
            RefreshButton(r);
            return;
        }

        // SPRINT
        if (r.slot != null && r.slot.Definition is SprintRelic sprintDef)
        {
            bool consumed = relicManager.TryConsumeById(relicId, consume);
            if (!consumed)
            {
                RefreshButton(r);
                return;
            }

            float durationToAdd = sprintDef.sprintDuration * consume;
            bool used = warrior != null && warrior.TryExtendOrQueueSprintRelic(
                relicId: relicId,
                speedMultiplier: sprintDef.speedMultiplier,
                duration: durationToAdd,
                cooldown: sprintDef.sprintCooldown,
                consumeOnUse: false);

            if (!used)
            {
                for (int i = 0; i < consume; i++)
                    relicManager.Collect(sprintDef, bypassFrameCap: true);
            }

            RefreshButton(r);
            return;
        }

        // POWER COMBO: first click consumes one stack and arms Attack2 as a waiting stage.
        // Do not trigger Attack2 here; the real action happens later from the attack button/input.
        if (r.slot != null && r.slot.Definition is PowerComboRelic powerDef)
        {
            bool consumed = relicManager.TryConsumeById(relicId, consume);
            if (!consumed)
            {
                RefreshButton(r);
                return;
            }

            bool armed = warrior != null && warrior.TryUseRelicAttack2(
                powerDef.attack2UseDuration,
                powerDef.attack2Cooldown,
                triggerNow: false);

            if (!armed)
            {
                RefundStacks(powerDef, consume);
                RefreshButton(r);
                return;
            }

            r.onUsed?.Invoke();
            RefreshButton(r);
            return;
        }

        // SHIELD: activate first, consume only if activation succeeds.
        // Handled by definition type OR by the ShieldTimed effect enum (Inspector fallback).
        bool isShieldRule = (r.slot != null && r.slot.Definition is ShieldRelic) ||
                            r.effect == RelicUseEffect.ShieldTimed;

        if (isShieldRule)
        {
            float duration = ResolveShieldDuration(r);
            float cooldown = ResolveShieldCooldown(r);

            bool used = warrior != null && warrior.TryUseShieldRelic(duration, cooldown);
            if (!used)
            {
                RefreshButton(r);
                return;
            }

            relicManager.TryConsumeById(relicId, consume);
            StartTimedVisual(r, duration);
            r.onUsed?.Invoke();
            RefreshButton(r);
            return;
        }

        // Other relics: instant consume + effect
        bool consumedDefault = relicManager.TryConsumeById(relicId, consume);
        if (!consumedDefault)
        {
            RefreshButton(r);
            return;
        }

        ApplyImmediateEffect(r);
        r.onUsed?.Invoke();
        RefreshButton(r);
    }
    private bool TryDisarmAlreadyArmedRelic(RelicButtonRule r)
    {
        if (r == null || r.slot == null || r.slot.Definition == null || warrior == null || relicManager == null)
            return false;

        RelicDefinition def = r.slot.Definition;
        int refundStacks = Mathf.Max(1, r.consumeStacks);

        if (def is IceBallRelic iceDef && warrior.IsIceBallArmed)
        {
            Debug.Log($"[IceBall] DISARM START frame={Time.frameCount} armed={warrior.IsIceBallArmed}");
            bool disarmed = warrior.DisarmIceBallRelic();
            Debug.Log($"[IceBall] After DisarmIceBallRelic: disarmed={disarmed} armed={warrior.IsIceBallArmed}");

            if (disarmed)
                RefundStacks(iceDef, refundStacks);

            Debug.Log($"[IceBall] After RefundStacks: armed={warrior.IsIceBallArmed} count={relicManager.GetCountById(ResolveRelicId(r))}");

            if (r.slot != null)
                r.slot.RefreshFromRelicManager();

            ForceImmediateUIRefreshAfterRelicStateChange(r);
            Debug.Log($"[IceBall] DISARM END frame={Time.frameCount} armed={warrior.IsIceBallArmed}");
            return true;
        }

        if (def is PowerComboRelic && warrior.IsPowerComboArmed)
        {
            bool disarmed = warrior.DisarmPowerComboRelic();
            if (disarmed)
                RefundStacks(def, refundStacks);

            RefreshButton(r);
            return true;
        }

        return false;
    }

    private bool IsControllerOwnedArmableRule(RelicButtonRule r)
    {
        return r != null &&
               r.slot != null &&
               (r.slot.Definition is IceBallRelic || r.slot.Definition is PowerComboRelic);
    }

    private bool IsControllerOwnedArmableButton(Button button)
    {
        if (button == null)
            return false;

        for (int i = 0; i < rules.Count; i++)
        {
            RelicButtonRule r = rules[i];
            if (r != null && r.button == button && IsControllerOwnedArmableRule(r))
                return true;
        }

        return false;
    }

    private void RefundStacks(RelicDefinition def, int amount)
    {
        if (def == null || relicManager == null)
            return;

        int safeAmount = Mathf.Max(1, amount);
        for (int i = 0; i < safeAmount; i++)
            relicManager.Collect(def, bypassFrameCap: true);
    }

    private void ForceImmediateUIRefreshAfterRelicStateChange(RelicButtonRule changedRule)
    {
        RefreshButton(changedRule);
        RefreshAllButtons();

        ForceButtonExitAndDeselect(changedRule != null ? changedRule.button : null);
        ForceButtonNormalVisual(changedRule);
        ForceLayoutRebuild(changedRule != null ? changedRule.button : null);
        Canvas.ForceUpdateCanvases();

        if (isActiveAndEnabled)
            StartCoroutine(CoRefreshAllButtonsNextFrame(changedRule));
    }

    private IEnumerator CoRefreshAllButtonsNextFrame(RelicButtonRule changedRule = null)
    {
        yield return null;

        RefreshAllButtons();

        // Unity's Button can apply its selected/highlighted color after onClick.
        // Re-apply once on the next frame so the Ice Ball cancel visual does not
        // wait for another pointer/screen event.
        ForceButtonExitAndDeselect(changedRule != null ? changedRule.button : null);
        ForceButtonNormalVisual(changedRule);
        ForceLayoutRebuild(changedRule != null ? changedRule.button : null);
        Canvas.ForceUpdateCanvases();

        yield return new WaitForEndOfFrame();

        RefreshAllButtons();
        ForceButtonNormalVisual(changedRule);
        ForceLayoutRebuild(changedRule != null ? changedRule.button : null);
        // Do not restore the Button transition for IceBall/PowerCombo.
        // Those buttons must stay controller-owned, otherwise Unity's highlighted
        // color can immediately tint the icon green again.
        RestoreButtonTransitionIfSafe(changedRule != null ? changedRule.button : null);
        Canvas.ForceUpdateCanvases();
    }

    private static void ForceButtonExitAndDeselect(Button button)
    {
        if (button == null || EventSystem.current == null)
            return;

        var pointerData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(button.gameObject, pointerData, ExecuteEvents.pointerExitHandler);

        var baseData = new BaseEventData(EventSystem.current);
        ExecuteEvents.Execute(button.gameObject, baseData, ExecuteEvents.deselectHandler);

        if (EventSystem.current.currentSelectedGameObject == button.gameObject)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void ForceButtonNormalVisual(RelicButtonRule r)
    {
        if (r == null || r.button == null || r.button.targetGraphic == null)
            return;

        if (IsThisRelicAlreadyArmed(r))
            return;

        if (_fxActive.Contains(r.button) || _keyBlinkingButtons.Contains(r.button))
            return;

        if (!_savedTransitions.ContainsKey(r.button))
            _savedTransitions[r.button] = r.button.transition;

        r.button.transition = Selectable.Transition.None;

        Color defaultColor = _defaultGraphicColors.TryGetValue(r.button, out var dc)
            ? dc
            : Color.white;

        // Flush pending CanvasRenderer tweens BEFORE the direct write.
        Canvas.ForceUpdateCanvases();
        r.button.targetGraphic.canvasRenderer.SetColor(defaultColor);
        r.button.targetGraphic.color = defaultColor;
    }

    private void RestoreButtonTransitionIfSafe(Button button)
    {
        if (button == null)
            return;

        // IceBallRelic / PowerComboRelic use controller-owned colors.
        // Keeping transition=None is intentional: it prevents Unity's Button
        // Highlighted/Selected/Pressed ColorTint from turning the icon green
        // again after the cancel/refund click.
        if (IsControllerOwnedArmableButton(button))
        {
            button.transition = Selectable.Transition.None;
            return;
        }

        if (_fxActive.Contains(button) || _keyBlinkingButtons.Contains(button))
            return;

        if (_savedTransitions.TryGetValue(button, out var transition))
            button.transition = transition;
    }

    private static void ForceLayoutRebuild(Button button)
    {
        if (button == null)
            return;

        RectTransform rect = button.transform as RectTransform;
        if (rect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

        if (button.transform.parent is RectTransform parentRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
    }

    private float ResolveShieldCooldown(RelicButtonRule r)
    {
        if (r.slot != null && r.slot.Definition is ShieldRelic shieldDef)
            return Mathf.Max(0f, shieldDef.shieldCooldown);

        // Inspector fallback: no dedicated cooldown field on the rule,
        // so reuse effectValue (same field already used for duration fallback).
        return Mathf.Max(0f, r.effectValue);
    }
    private void ApplyImmediateEffect(RelicButtonRule r)
    {
        switch (r.effect)
        {
            case RelicUseEffect.Heal:
                {
                    if (warrior != null)
                    {
                        int heal = Mathf.Max(0, r.effectValue);
                        warrior.Heal(heal);
                    }
                    break;
                }

            case RelicUseEffect.None:
            default:
                break;
        }
    }

    private float ResolveShieldDuration(RelicButtonRule r)
    {
        if (r.slot != null && r.slot.Definition is ShieldRelic shieldDef)
            return Mathf.Max(0.05f, shieldDef.shieldDuration);

        return Mathf.Max(0.05f, r.effectValue);
    }

    private void StartTimedVisual(RelicButtonRule r, float duration)
    {
        if (r.button == null) return;

        if (_runningFx.TryGetValue(r.button, out var running) && running != null)
            StopCoroutine(running);

        // ensure override is active even if called externally
        BeginVisualOverride(r.button);
        _runningFx[r.button] = StartCoroutine(CoTimedVisual(r, duration));
    }

    private IEnumerator CoTimedVisual(RelicButtonRule r, float duration)
    {
        var btn = r.button;
        if (btn == null) yield break;

        var g = btn.targetGraphic;
        if (g == null)
        {
            EndVisualOverride(btn);
            yield break;
        }

        Color defaultColor = _defaultGraphicColors.TryGetValue(btn, out var dc)
            ? dc
            : btn.colors.normalColor;

        g.color = r.activeColor;

        if (r.cooldownFill != null)
        {
            r.cooldownFill.fillAmount = 1f;
            r.cooldownFill.gameObject.SetActive(true);
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime; // smoother if timescale changes
            float p = Mathf.Clamp01(t / duration);

            g.color = Color.Lerp(r.activeColor, defaultColor, p);

            if (r.cooldownFill != null)
                r.cooldownFill.fillAmount = 1f - p;

            yield return null;
        }

        g.color = defaultColor;

        if (r.cooldownFill != null)
        {
            r.cooldownFill.fillAmount = 0f;
            if (r.hideFillWhenDone) r.cooldownFill.gameObject.SetActive(false);
        }

        EndVisualOverride(btn);
        _runningFx[btn] = null;

        // now we can safely re-apply interactable based on count
        RefreshButton(r);
    }

    private void BeginVisualOverride(Button btn)
    {
        if (btn == null) return;

        if (!_savedTransitions.ContainsKey(btn))
            _savedTransitions[btn] = btn.transition;

        btn.transition = Selectable.Transition.None; // stop ColorTint fighting us
        _fxActive.Add(btn);
    }

    private void EndVisualOverride(Button btn)
    {
        if (btn == null) return;

        if (IsControllerOwnedArmableButton(btn))
        {
            btn.transition = Selectable.Transition.None;
        }
        else if (_savedTransitions.TryGetValue(btn, out var tr))
        {
            btn.transition = tr;
        }

        _fxActive.Remove(btn);
    }

    private string ResolveRelicId(RelicButtonRule r)
    {
        if (r.slot != null && r.slot.Definition != null)
        {
            var def = r.slot.Definition;
            return !string.IsNullOrEmpty(def.relicId) ? def.relicId : def.name;
        }
        return r.relicIdOverride;
    }

    private void OnRelicCountChanged(RelicDefinition _, int __)
    {
        RefreshAllButtons();
        // No coroutine here � LateUpdate?StabilizeControllerOwnedArmableButtonVisuals
        // is the visual authority every frame. A coroutine with changedRule=null
        // cannot call ForceButtonNormalVisual on the correct button anyway.
    }

    private void RefreshAllButtons()
    {
        for (int i = 0; i < rules.Count; i++)
            RefreshButton(rules[i]);
    }

    private void RefreshButton(RelicButtonRule r)
    {
        if (r == null || r.button == null || relicManager == null)
            return;

        string relicId = ResolveRelicId(r);
        int count = !string.IsNullOrEmpty(relicId) ? relicManager.GetCountById(relicId) : 0;
        Color defaultColor = GetDefaultButtonColor(r.button);

        // Push the current count directly into the visible slot first without
        // touching interactable. RelicUIButton.RefreshFromRelicManager() is
        // count-only when this button is controlled by RelicUIController.
        if (r.slot != null)
            r.slot.RefreshFromRelicManager();

        if (IsWarriorUnavailable() || string.IsNullOrEmpty(relicId))
        {
            r.button.interactable = false;
            if (r.slot != null)
                r.slot.ApplyControllerState(count, false, false, defaultColor, r.activeColor, applyVisual: true);
            return;
        }

        if (IsKeyRelicRule(r))
        {
            RefreshKeyRelicButton(r, relicId);
            if (r.slot != null)
                r.slot.ApplyControllerState(count, r.button.interactable, false, defaultColor, r.activeColor, applyVisual: false);
            return;
        }

        if (IsPassiveDisplayOnlyRelic(r))
        {
            r.button.interactable = false;
            StopKeyBlink(r.button);
            if (r.slot != null)
                r.slot.ApplyControllerState(count, false, false, defaultColor, r.activeColor, applyVisual: true);
            return;
        }

        if (_fxActive.Contains(r.button))
        {
            if (r.slot != null)
                r.slot.ApplyControllerState(count, r.button.interactable, false, defaultColor, r.activeColor, applyVisual: false);
            return;
        }

        bool hasCount = count > 0;
        bool armedThisRelic = IsThisRelicAlreadyArmed(r);

        // Block interactable if mutually exclusive state is active, except when this
        // exact button is the armed relic; then it must stay clickable so it can cancel.
        bool blocked = !armedThisRelic && IsBlockedByMutualExclusion(r);
        bool interactable = (hasCount || armedThisRelic) && !blocked;

        r.button.interactable = interactable;

        if (IsControllerOwnedArmableRule(r))
            r.button.transition = Selectable.Transition.None;

        bool canApplyVisual = !_fxActive.Contains(r.button) && !_keyBlinkingButtons.Contains(r.button);
        if (r.slot != null)
            r.slot.ApplyControllerState(count, interactable, armedThisRelic, defaultColor, r.activeColor, canApplyVisual);
        else if (canApplyVisual)
            ApplyArmedVisual(r, armedThisRelic);

        RememberDynamicButtonState(r, relicId, count, armedThisRelic);
    }

    private Color GetDefaultButtonColor(Button button)
    {
        if (button == null)
            return Color.white;

        return _defaultGraphicColors.TryGetValue(button, out var dc)
            ? dc
            : button.colors.normalColor;
    }

    private void RememberDynamicButtonState(RelicButtonRule r, string relicId, int count, bool armed)
    {
        if (r == null || r.button == null || string.IsNullOrEmpty(relicId))
            return;

        _lastArmedStateByButton[r.button] = armed;
        _lastKnownCountByRelicId[relicId] = count;
    }

    private bool IsThisRelicAlreadyArmed(RelicButtonRule r)
    {
        if (r == null || r.slot == null || r.slot.Definition == null || warrior == null)
            return false;

        if (r.slot.Definition is IceBallRelic)
            return warrior.IsIceBallArmed;

        if (r.slot.Definition is PowerComboRelic)
            return warrior.IsPowerComboArmed;

        return false;
    }

    private void ApplyArmedVisual(RelicButtonRule r, bool armed)
    {
        if (r == null || r.button == null || r.button.targetGraphic == null)
            return;

        if (_fxActive.Contains(r.button) || _keyBlinkingButtons.Contains(r.button))
            return;

        Color defaultColor = _defaultGraphicColors.TryGetValue(r.button, out var dc)
            ? dc
            : Color.white;

        r.button.targetGraphic.color = armed ? r.activeColor : defaultColor;
    }

    private void StopAllRunningFx()
    {
        foreach (var kv in _runningFx)
        {
            if (kv.Value != null) StopCoroutine(kv.Value);
        }
        _runningFx.Clear();

        // restore transitions if needed
        var buttons = new List<Button>(_fxActive);
        for (int i = 0; i < buttons.Count; i++)
            EndVisualOverride(buttons[i]);

        var blinkingButtons = new List<Button>(_keyBlinkingButtons);
        for (int i = 0; i < blinkingButtons.Count; i++)
            StopKeyBlink(blinkingButtons[i]);
    }

    private bool IsWarriorDeadOrUnavailable()
    {
        return warrior == null || warrior.IsDead || warrior.CanDie;
    }
    private bool IsWarriorUnavailable()
    {
        return warrior == null || warrior.IsDead || warrior.CanDie;
    }

    private bool IsPassiveDisplayOnlyRelic(RelicButtonRule r)
    {
        return r != null && r.passiveDisplayOnly;
    }

    private bool IsKeyRelicRule(RelicButtonRule r)
    {
        return r != null && r.slot != null && r.slot.Definition is KeyRelic;
    }

    private void OnActivePlatformMotionLockChanged(KeyRelicLock _)
    {
        RefreshAllButtons();
    }

    private void TryActivateKeyPlatformMotion(RelicButtonRule r)
    {
        if (warrior == null || relicManager == null || r == null)
            return;

        KeyRelicLock activeLock = KeyRelicLock.ActivePlatformMotionLock;
        if (activeLock == null)
        {
            RefreshAllButtons();
            return;
        }

        if (!activeLock.CanActivateFromUI(warrior))
        {
            RefreshAllButtons();
            return;
        }

        bool activated = activeLock.TryActivateFromUI(warrior);

        // Force immediate UI refresh after count/platform state changes.
        RefreshAllButtons();

        if (activated && r.button != null)
        {
            r.button.interactable = false;
            StopKeyBlink(r.button);
        }

        // One extra refresh next frame avoids stale button state from UI selection / timing.
        StartCoroutine(CoRefreshAllButtonsNextFrame());
    }

    //private IEnumerator CoRefreshAllButtonsNextFrame()
    //{
    //    yield return null;
    //    RefreshAllButtons();
    //}
    private void RefreshKeyRelicButton(RelicButtonRule r, string relicId)
    {
        if (r == null || r.button == null || relicManager == null)
            return;

        int count = relicManager.GetCountById(relicId);
        bool hasKey = count > 0;

        KeyRelicLock activeLock = KeyRelicLock.ActivePlatformMotionLock;
        bool canActivatePlatform =
            hasKey &&
            activeLock != null &&
            warrior != null &&
            activeLock.CanActivateFromUI(warrior);

        r.button.interactable = canActivatePlatform;

        if (canActivatePlatform && r.blinkWhenPlatformMotionLockAvailable)
            StartKeyBlink(r.button);
        else
            StopKeyBlink(r.button);
    }

    private void RefreshArmableRelicButtonsWhenStateChanged()
    {
        if (relicManager == null)
            return;

        for (int i = 0; i < rules.Count; i++)
        {
            RelicButtonRule r = rules[i];
            if (r == null || r.button == null || r.slot == null || r.slot.Definition == null)
                continue;

            bool isDynamicIceBall =
                r.slot.Definition is IceBallRelic;

            if (!isDynamicIceBall)
                continue;

            string relicId = ResolveRelicId(r);
            if (string.IsNullOrEmpty(relicId))
                continue;

            int count = relicManager.GetCountById(relicId);
            bool armed = IsThisRelicAlreadyArmed(r);

            bool armedChanged =
                !_lastArmedStateByButton.TryGetValue(r.button, out bool previousArmed) ||
                previousArmed != armed;

            bool countChanged =
                !_lastKnownCountByRelicId.TryGetValue(relicId, out int previousCount) ||
                previousCount != count;

            if (!armedChanged && !countChanged)
                continue;

            RefreshButton(r);
            RememberDynamicButtonState(r, relicId, count, armed);

            if (!armed)
                ForceButtonNormalVisual(r);
        }
    }

    private void StabilizeControllerOwnedArmableButtonVisuals()
    {
        if (relicManager == null)
            return;

        for (int i = 0; i < rules.Count; i++)
        {
            RelicButtonRule r = rules[i];
            if (!IsControllerOwnedArmableRule(r) || r.button == null)
                continue;

            string relicId = ResolveRelicId(r);
            if (string.IsNullOrEmpty(relicId))
                continue;

            int count = relicManager.GetCountById(relicId);
            bool armed = IsThisRelicAlreadyArmed(r);
            bool hasCount = count > 0;
            bool blocked = !armed && IsBlockedByMutualExclusion(r);
            bool interactable = !IsWarriorUnavailable() && (hasCount || armed) && !blocked;
            Color defaultColor = GetDefaultButtonColor(r.button);
            
            
            //SO_Relic_Key
            
            
            // LOG: see what LateUpdate is actually deciding every frame
            if (r.slot != null && r.slot.Definition is IceBallRelic)
                //Debug.Log($"[IceBall][LateUpdate] frame={Time.frameCount} armed={armed} count={count} interactable={interactable} targetGraphicColor={r.button.targetGraphic?.color}");

            r.button.interactable = interactable;
            r.button.transition = Selectable.Transition.None;

            if (r.slot != null)
                r.slot.ApplyControllerState(count, interactable, armed, defaultColor, r.activeColor, applyVisual: true);
            else
                ApplyArmedVisual(r, armed);
        }
    }

    private void UpdateKeyBlinkingButtons()
    {
        if (_keyBlinkingButtons.Count == 0)
            return;

        for (int i = 0; i < rules.Count; i++)
        {
            RelicButtonRule r = rules[i];
            if (r == null || r.button == null)
                continue;

            if (!_keyBlinkingButtons.Contains(r.button))
                continue;

            if (!IsKeyRelicRule(r))
            {
                StopKeyBlink(r.button);
                continue;
            }

            string relicId = ResolveRelicId(r);
            int count = relicManager != null && !string.IsNullOrEmpty(relicId)
                ? relicManager.GetCountById(relicId)
                : 0;

            KeyRelicLock activeLock = KeyRelicLock.ActivePlatformMotionLock;
            bool canActivatePlatform =
                count > 0 &&
                activeLock != null &&
                warrior != null &&
                activeLock.CanActivateFromUI(warrior);

            if (!canActivatePlatform)
            {
                StopKeyBlink(r.button);
                r.button.interactable = false;
                continue;
            }

            ApplyKeyBlinkColor(r);
        }
    }

    private void StartKeyBlink(Button btn)
    {
        if (btn == null)
            return;

        if (!_savedTransitions.ContainsKey(btn))
            _savedTransitions[btn] = btn.transition;

        btn.transition = Selectable.Transition.None;
        _keyBlinkingButtons.Add(btn);
    }

    private void StopKeyBlink(Button btn)
    {
        if (btn == null)
            return;

        if (!_keyBlinkingButtons.Remove(btn))
            return;

        if (IsControllerOwnedArmableButton(btn))
            btn.transition = Selectable.Transition.None;
        else if (_savedTransitions.TryGetValue(btn, out var tr))
            btn.transition = tr;

        if (btn.targetGraphic != null)
        {
            Color defaultColor = _defaultGraphicColors.TryGetValue(btn, out var dc)
                ? dc
                : btn.colors.normalColor;

            btn.targetGraphic.color = defaultColor;
        }
    }

    private void ApplyKeyBlinkColor(RelicButtonRule r)
    {
        if (r == null || r.button == null || r.button.targetGraphic == null)
            return;

        Color defaultColor = _defaultGraphicColors.TryGetValue(r.button, out var dc)
            ? dc
            : Color.white;

        float pulse = (Mathf.Sin(Time.unscaledTime * Mathf.Max(0.1f, r.keyBlinkSpeed)) + 1f) * 0.5f;
        r.button.targetGraphic.color = Color.Lerp(defaultColor, r.keyBlinkColor, pulse);
    }

    private bool IsBlockedByMutualExclusion(RelicButtonRule r)
    {
        if (warrior == null || r == null) return true;

        // Shield button blocked while sprint is armed/active
        bool isShieldRule = r.effect == RelicUseEffect.ShieldTimed ||
                            (r.slot != null && r.slot.Definition is ShieldRelic);

        if (isShieldRule)
            return warrior.IsDodging || warrior.IsSprintArmed;

        // Sprint button is still allowed while sprint is armed/active,
        // because extra Sprint Relic stacks extend/queue duration.
        bool isSprintRule = (r.slot != null && r.slot.Definition is SprintRelic);
        if (isSprintRule)
            return warrior.ShieldIsUp;

        return false;
    }
}
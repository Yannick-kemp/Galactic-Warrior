// Assets/Scripts/Relics/UI/RelicUIController.cs
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Definitions; // ShieldRelic
using Assets.Scripts.Relics.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
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

        RefreshAllButtons();
    }

    private void OnDisable()
    {
        if (relicManager != null)
            relicManager.OnRelicCountChanged -= OnRelicCountChanged;

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

    private void AutoWireButtons()
    {
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r == null) continue;

            if (r.button == null && r.slot != null)
                r.button = r.slot.GetComponent<Button>();

            // Optional: avoid keyboard/gamepad selected-color sticking
            if (r.button != null)
            {
                var nav = r.button.navigation;
                nav.mode = Navigation.Mode.None;
                r.button.navigation = nav;
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
                // Use ColorBlock normalColor as canonical default
                _defaultGraphicColors[r.button] = r.button.colors.normalColor;
                g.color = r.button.colors.normalColor;
            }

            if (r.cooldownFill != null)
            {
                r.cooldownFill.fillAmount = 0f;
                if (r.hideFillWhenDone) r.cooldownFill.gameObject.SetActive(false);
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

        // hard block before any consume
        if (IsBlockedByMutualExclusion(r))
        {
            RefreshButton(r);
            return;
        }

        if (!TryEnterUseGate(relicId))
            return;

        int consume = Mathf.Max(1, r.consumeStacks);

        // pre-check resource
        if (relicManager.GetCountById(relicId) < consume)
        {
            RefreshButton(r);
            return;
        }

        // ICE BALL: arm now, consume later on world touch
        if (r.slot != null && r.slot.Definition is IceBallRelic iceDef)
        {
            bool armed = warrior != null && warrior.TryArmIceBallRelic(iceDef, consumeOnCast: true);
            RefreshButton(r);
            return;
        }

        // SPRINT: arm now, consume later on world touch / actual sprint start
        if (r.slot != null && r.slot.Definition is SprintRelic sprintDef)
        {
            // Consommer avant d'armer
            bool consumed = relicManager.TryConsumeById(relicId, consume);
            if (!consumed) { RefreshButton(r); return; }

            bool armed = warrior != null && warrior.TryArmSprintRelic(
                relicId: relicId,
                speedMultiplier: sprintDef.speedMultiplier,
                duration: sprintDef.sprintDuration,
                cooldown: sprintDef.sprintCooldown,
                consumeOnUse: false); // déjà consommé

            if (!armed)
            {
                // Rembourser si TryArm a refusé
                relicManager.Collect(sprintDef, bypassFrameCap: true);
            }

            RefreshButton(r);
            return;
        }

        // SHIELD: activate first, consume only if activation succeeds
        if (r.effect == RelicUseEffect.ShieldTimed)
        {
            float duration = ResolveShieldDuration(r);

            bool used = warrior != null && warrior.TryUseShieldRelic(duration);
            if (!used)
            {
                RefreshButton(r);
                return;
            }

            bool consumed = relicManager.TryConsumeById(relicId, consume);
            if (!consumed)
            {
                RefreshButton(r);
                return;
            }

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

        if (_savedTransitions.TryGetValue(btn, out var tr))
            btn.transition = tr;

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

    private void OnRelicCountChanged(RelicDefinition _, int __) => RefreshAllButtons();

    private void RefreshAllButtons()
    {
        for (int i = 0; i < rules.Count; i++)
            RefreshButton(rules[i]);
    }

    private void RefreshButton(RelicButtonRule r)
    {
        if (r == null || r.button == null || relicManager == null) return;

        if (IsWarriorUnavailable())
        {
            r.button.interactable = false;
            return;
        }

        string relicId = ResolveRelicId(r);
        if (string.IsNullOrEmpty(relicId))
        {
            r.button.interactable = false;
            return;
        }

        if (_fxActive.Contains(r.button)) return;

        int count = relicManager.GetCountById(relicId);
        bool hasCount = count > 0;

        // NEW: block interactable if mutually exclusive state is active
        bool blocked = IsBlockedByMutualExclusion(r);

        bool blockedByIceArmed =
    r.slot != null &&
    r.slot.Definition is IceBallRelic &&
    warrior != null &&
    warrior.IsIceBallArmed;

        r.button.interactable = hasCount && !blocked && !blockedByIceArmed;
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
    }

    private bool IsWarriorDeadOrUnavailable()
    {
        return warrior == null || warrior.IsDead || warrior.CanDie;
    }
    private bool IsWarriorUnavailable()
    {
        return warrior == null || warrior.IsDead || warrior.CanDie;
    }

    private bool IsBlockedByMutualExclusion(RelicButtonRule r)
    {
        if (warrior == null || r == null) return true;

        // Shield button blocked while sprint is armed/active
        bool isShieldRule = r.effect == RelicUseEffect.ShieldTimed ||
                            (r.slot != null && r.slot.Definition is ShieldRelic);

        if (isShieldRule)
            return warrior.IsDodging || warrior.IsSprintArmed;

        // Sprint button blocked while shield is up
        bool isSprintRule = (r.slot != null && r.slot.Definition is SprintRelic);
        if (isSprintRule)
            return warrior.ShieldIsUp || warrior.IsSprintArmed || warrior.IsDodging;

        return false;
    }
}
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Definitions;
using Assets.Scripts.Relics.World;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Assets.Scripts.Relics.UI
{
    [RequireComponent(typeof(Button))]
    public class RelicUIButton : MonoBehaviour, IPointerDownHandler
    {
        [Header("Relic")]
        [SerializeField] private RelicDefinition definition;
        public RelicDefinition Definition => definition;

        [Header("UI")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text countText;

        [Header("Click Handling")]
        [Tooltip(
            "OFF when this button is controlled by RelicUIController. " +
            "Recommended OFF for KeyRelic buttons. " +
            "Use ON only for old/standalone buttons that are not listed in RelicUIController rules."
        )]
        [SerializeField] private bool bindInternalClick = false;

        [Header("Generic Use Behavior")]
        [SerializeField] private bool consumeOneOnUse = true;
        [SerializeField] private float fallbackAttack2Duration = 1.0f;
        [SerializeField] private float fallbackAttack2Cooldown = 6f;
        [SerializeField] private float worldInputBlockSeconds = 0.12f;

        [Header("Refs")]
        [SerializeField] private Warrior warrior;

        private Button _button;
        private RelicManager _relicManager;
        private string _relicId;
        private bool _controlledByRelicUIController;
        private Image[] _cachedImages;
        private readonly Dictionary<Image, Color> _defaultImageColors = new Dictionary<Image, Color>();

        private void Awake()
        {
            _button = GetComponent<Button>();

            ResolveRelicId();
            AutoBindIcon();
            CacheDefaultImageColors();

            _button.onClick.RemoveListener(OnClicked);

            if (bindInternalClick)
                _button.onClick.AddListener(OnClicked);
        }

        /// <summary>
        /// Called by RelicUIController for buttons listed in its rules.
        /// This prevents the standalone RelicUIButton.OnClicked path from running
        /// after the controller has already consumed, disarmed, refunded, or armed.
        /// </summary>
        public void SetControlledByRelicUIController(bool controlled)
        {
            _controlledByRelicUIController = controlled;

            if (controlled)
                bindInternalClick = false;

            if (_button == null)
                _button = GetComponent<Button>();

            if (_button != null)
                _button.onClick.RemoveListener(OnClicked);
        }

        private void Start()
        {
            ResolveRefs();

            if (_relicManager != null)
            {
                _relicManager.OnRelicCountChanged += HandleCountChanged;
                SetCount(_relicManager.GetCount(definition));
            }

            RefreshInteractable();
        }

        private void OnEnable()
        {
            if (_controlledByRelicUIController)
                RefreshCountOnly();
            else
                RefreshInteractable();
        }

        public void RefreshFromRelicManager()
        {
            ResolveRefs();
            ResolveRelicId();
            RefreshCountOnly();

            // When RelicUIController owns the button, do not let this component
            // recompute interactable/visual state. The controller has extra rules
            // for armed/cancel states, mutual exclusion, key locks, and active FX.
            if (!_controlledByRelicUIController)
                RefreshInteractable();

            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// Called by RelicUIController after it has computed the real state for this rule.
        /// This avoids split ownership where RelicUIButton.Update() overwrites the
        /// controller's state and leaves IceBallRelic visually/count-stale until another click.
        /// </summary>
        public void ApplyControllerState(int count, bool interactable, bool armed, Color normalColor, Color armedColor, bool applyVisual)
        {
            ResolveRefs();
            ResolveRelicId();

            if (definition is IceBallRelic)
                Debug.Log($"[IceBall][ApplyControllerState] frame={Time.frameCount} armed={armed} count={count} applyVisual={applyVisual} caller={new System.Diagnostics.StackTrace().ToString().Substring(0, Mathf.Min(300, new System.Diagnostics.StackTrace().ToString().Length))}");

            SetCount(count);

            if (_button == null)
                _button = GetComponent<Button>();

            bool controllerOwnedArmable =
                _controlledByRelicUIController &&
                (definition is IceBallRelic || definition is PowerComboRelic);

            // Flush any pending CanvasRenderer tweens BEFORE writing new colors.
            // CrossFadeColor (called in older code paths or by Unity internally) submits
            // tweens that Canvas.ForceUpdateCanvases() flushes. If we flush after writing,
            // the old tween overwrites our direct color write � causing the green re-flash.
            Canvas.ForceUpdateCanvases();

            if (_button != null)
            {
                _button.interactable = interactable;

                if (controllerOwnedArmable)
                {
                    // Keep transition=None so Unity's ColorTint never fights us.
                    _button.transition = Selectable.Transition.None;

                    // Set all ColorBlock slots to the current state color so Unity's
                    // Button cannot re-apply a different tint on pointer events.
                    Color stateColor = armed ? armedColor : normalColor;
                    ColorBlock cb = _button.colors;
                    cb.normalColor = stateColor;
                    cb.highlightedColor = stateColor;
                    cb.pressedColor = stateColor;
                    cb.selectedColor = stateColor;
                    cb.disabledColor = stateColor;
                    _button.colors = cb;

                    // When disarming, also force the targetGraphic directly.
                    // The ColorBlock write alone is not enough because Unity may not
                    // re-evaluate it until the next pointer event.
                    if (!armed && _button.targetGraphic != null)
                    {
                        _button.targetGraphic.canvasRenderer.SetColor(normalColor);
                        _button.targetGraphic.color = normalColor;
                    }
                }
            }

            if (applyVisual)
            {
                if (controllerOwnedArmable)
                {
                    if (armed)
                        TintAllImages(armedColor);
                    else
                        RestoreDefaultImageColors();
                }
                else if (_button != null && _button.targetGraphic != null)
                {
                    Color c = armed ? armedColor : normalColor;
                    _button.targetGraphic.canvasRenderer.SetColor(c);
                    _button.targetGraphic.color = c;
                }
            }
        }

        private void RefreshCountOnly()
        {
            if (_relicManager != null && definition != null)
                SetCount(_relicManager.GetCount(definition));
        }

        private void Update()
        {
            if (_controlledByRelicUIController)
            {
                RefreshCountOnly();
                return;
            }

            RefreshInteractable();
        }

        private void OnDestroy()
        {
            if (_button != null)
                _button.onClick.RemoveListener(OnClicked);

            if (_relicManager != null)
                _relicManager.OnRelicCountChanged -= HandleCountChanged;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            ResolveRefs();

            if (warrior != null)
                warrior.NotifyUIConsumedInput(worldInputBlockSeconds);
        }

        private void ResolveRefs()
        {
            if (warrior == null)
                warrior = FindFirstObjectByType<Warrior>();

            if (_relicManager == null && warrior != null)
                _relicManager = warrior.GetComponent<RelicManager>();
        }

        private void ResolveRelicId()
        {
            _relicId = definition != null && !string.IsNullOrEmpty(definition.relicId)
                ? definition.relicId
                : definition != null ? definition.name : string.Empty;
        }

        private void AutoBindIcon()
        {
            if (iconImage == null)
                iconImage = GetComponent<Image>();

            if (definition != null && iconImage != null && definition.icon != null)
                iconImage.sprite = definition.icon;
        }

        private void CacheDefaultImageColors()
        {
            _cachedImages = GetComponentsInChildren<Image>(true);
            _defaultImageColors.Clear();

            if (_cachedImages == null)
                return;

            for (int i = 0; i < _cachedImages.Length; i++)
            {
                Image img = _cachedImages[i];
                if (img == null)
                    continue;

                _defaultImageColors[img] = img.color;
            }
        }

        private void RestoreDefaultImageColors()
        {
            if (_cachedImages == null || _cachedImages.Length == 0)
                CacheDefaultImageColors();

            if (_cachedImages == null)
                return;

            // Flush any pending CanvasRenderer tweens (e.g. from a previous TintAllImages
            // CrossFadeColor call) BEFORE writing our color. If we flush after, the old
            // tween overwrites the direct write.
            Canvas.ForceUpdateCanvases();

            for (int i = 0; i < _cachedImages.Length; i++)
            {
                Image img = _cachedImages[i];
                if (img == null)
                    continue;

                Color c = _defaultImageColors.TryGetValue(img, out var cached)
                    ? cached
                    : Color.white;

                // Direct assignment only � no CrossFadeColor. CrossFadeColor submits a
                // tween that Canvas.ForceUpdateCanvases() can flush back over us.
                img.canvasRenderer.SetColor(c);
                img.color = c;
            }
        }

        private void TintAllImages(Color color)
        {
            if (_cachedImages == null || _cachedImages.Length == 0)
                CacheDefaultImageColors();

            if (_cachedImages == null)
                return;

            Canvas.ForceUpdateCanvases();

            for (int i = 0; i < _cachedImages.Length; i++)
            {
                Image img = _cachedImages[i];
                if (img == null)
                    continue;

                img.canvasRenderer.SetColor(color);
                img.color = color;
            }
        }

        private void HandleCountChanged(RelicDefinition changedDefinition, int newCount)
        {
            if (changedDefinition == null)
                return;

            string changedId = !string.IsNullOrEmpty(changedDefinition.relicId)
                ? changedDefinition.relicId
                : changedDefinition.name;

            if (changedId != _relicId)
                return;

            SetCount(newCount);

            if (!_controlledByRelicUIController)
                RefreshInteractable();
        }

        private void SetCount(int count)
        {
            if (countText == null)
                return;

            int safeCount = Mathf.Max(0, count);
            countText.SetText("x{0}", safeCount);
            countText.ForceMeshUpdate();
        }

        private void RefreshInteractable()
        {
            if (_button == null)
                return;

            ResolveRefs();

            if (definition == null || warrior == null || _relicManager == null)
            {
                _button.interactable = false;
                return;
            }

            if (IsWarriorUnavailable())
            {
                _button.interactable = false;
                return;
            }

            int count = _relicManager.GetCount(definition);

            // Keep the visible count synchronized with the manager every time the
            // button state is evaluated. This makes the UI resilient even if the
            // count-change event happens during the same click frame as a disarm/refund.
            SetCount(count);

            bool hasResource = count > 0;

            // KeyRelic is contextual.
            // It must be consumed by KeyRelicLock through RelicUIController,
            // not by this generic button script.
            if (definition is KeyRelic)
            {
                KeyRelicLock activeLock = KeyRelicLock.ActivePlatformMotionLock;

                _button.interactable =
                    hasResource &&
                    activeLock != null &&
                    activeLock.CanActivateFromUI(warrior);

                return;
            }

            bool thisRelicIsArmedWaitingStage =
                (definition is IceBallRelic && warrior.IsIceBallArmed) ||
                (definition is PowerComboRelic && warrior.IsPowerComboArmed);

            // RelicUIController owns the actual click behavior.
            // Keep the button clickable while its own reversible waiting stage is armed
            // so the second click can cancel/disarm even when the count is already x0.
            _button.interactable = hasResource || thisRelicIsArmedWaitingStage;
        }

        private bool IsWarriorUnavailable()
        {
            return warrior == null ||
                   warrior.IsDead ||
                   warrior.CanDie ||
                   !warrior.CanAttackWarrior ||
                   warrior.IsFrozenByHivernox;
        }

        private void OnClicked()
        {
            ResolveRefs();

            if (_controlledByRelicUIController || !bindInternalClick)
                return;

            if (definition == null || warrior == null || _relicManager == null)
                return;

            if (IsWarriorUnavailable())
                return;

            warrior.NotifyUIConsumedInput(worldInputBlockSeconds);

            // Standalone fallback only.
            // RelicUIController normally owns this behavior, but this keeps old
            // inspector setups safe if bindInternalClick was left ON.
            if (TryCancelArmedWaitingStageFromStandaloneButton())
            {
                RefreshInteractable();
                return;
            }

            if (!HasResourceToUse())
                return;

            // IMPORTANT:
            // KeyRelic is not consumed here.
            // For platform motion, RelicUIController must call KeyRelicLock.TryActivateFromUI().
            if (definition is KeyRelic)
            {
                KeyRelicLock activeLock = KeyRelicLock.ActivePlatformMotionLock;

                if (activeLock == null)
                    return;

                activeLock.TryActivateFromUI(warrior);
                return;
            }

            if (definition is ShieldRelic shieldDef)
            {
                bool used = warrior.TryUseShieldRelic(
                    shieldDef.shieldDuration,
                    shieldDef.shieldCooldown
                );

                if (!used)
                    return;

                if (ShouldConsumeOnUse())
                    _relicManager.TryConsume(definition, 1);

                RefreshInteractable();
                return;
            }

            if (definition is SprintRelic sprintDef)
            {
                if (ShouldConsumeOnUse() && !_relicManager.TryConsume(definition, 1))
                    return;

                bool used = warrior.TryExtendOrQueueSprintRelic(
                    relicId: _relicId,
                    speedMultiplier: sprintDef.speedMultiplier,
                    duration: sprintDef.sprintDuration,
                    cooldown: sprintDef.sprintCooldown,
                    consumeOnUse: false
                );

                if (!used && ShouldConsumeOnUse())
                    _relicManager.Collect(definition, bypassFrameCap: true);

                RefreshInteractable();
                return;
            }

            if (definition is IceBallRelic iceDef)
            {
                bool shouldConsume = ShouldConsumeOnUse();

                if (shouldConsume && !_relicManager.TryConsume(definition, 1))
                    return;

                bool armed = warrior.TryArmIceBallRelic(
                    iceDef,
                    consumeOnCast: false
                );

                if (!armed)
                {
                    if (shouldConsume)
                        _relicManager.Collect(definition, bypassFrameCap: true);

                    RefreshInteractable();
                    return;
                }

                RefreshInteractable();
                return;
            }

            float duration = fallbackAttack2Duration;
            float cooldown = fallbackAttack2Cooldown;

            if (definition is PowerComboRelic powerCombo)
            {
                duration = powerCombo.attack2UseDuration;
                cooldown = powerCombo.attack2Cooldown;
            }

            bool shouldConsumeAttack2 = ShouldConsumeOnUse();

            if (shouldConsumeAttack2 && !_relicManager.TryConsume(definition, 1))
                return;

            bool attack2Used = warrior.TryUseRelicAttack2(
                duration,
                cooldown,
                triggerNow: false
            );

            if (!attack2Used)
            {
                if (shouldConsumeAttack2)
                    _relicManager.Collect(definition, bypassFrameCap: true);

                RefreshInteractable();
                return;
            }

            RefreshInteractable();
        }

        private bool TryCancelArmedWaitingStageFromStandaloneButton()
        {
            if (definition == null || warrior == null || _relicManager == null)
                return false;

            if (definition is IceBallRelic && warrior.IsIceBallArmed)
            {
                bool disarmed = warrior.DisarmIceBallRelic();
                if (disarmed && ShouldConsumeOnUse())
                    _relicManager.Collect(definition, bypassFrameCap: true);

                return true;
            }

            if (definition is PowerComboRelic && warrior.IsPowerComboArmed)
            {
                bool disarmed = warrior.DisarmPowerComboRelic();
                if (disarmed && ShouldConsumeOnUse())
                    _relicManager.Collect(definition, bypassFrameCap: true);

                return true;
            }

            return false;
        }

        private bool HasResourceToUse()
        {
            if (_relicManager == null || definition == null)
                return false;

            return _relicManager.GetCount(definition) > 0;
        }

        private bool ShouldConsumeOnUse()
        {
            if (definition == null)
                return false;

            if (!definition.isConsumable)
                return false;

            // Shield is unlock-style in your current design.
            if (definition is ShieldRelic)
                return false;

            // KeyRelic is consumed only by KeyRelicLock / gates.
            if (definition is KeyRelic)
                return false;

            return consumeOneOnUse;
        }
    }
}
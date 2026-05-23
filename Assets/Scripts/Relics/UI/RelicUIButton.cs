using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Definitions;
using Assets.Scripts.Relics.World;
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

        private void Awake()
        {
            _button = GetComponent<Button>();

            ResolveRelicId();
            AutoBindIcon();

            _button.onClick.RemoveListener(OnClicked);

            if (bindInternalClick)
                _button.onClick.AddListener(OnClicked);
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
            RefreshInteractable();
        }

        private void Update()
        {
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
            RefreshInteractable();
        }

        private void SetCount(int count)
        {
            if (countText != null)
                countText.text = "x" + Mathf.Max(0, count);
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

            bool blockedByIceAlreadyArmed =
                definition is IceBallRelic &&
                warrior.IsIceBallArmed;

            _button.interactable =
                hasResource &&
                !blockedByIceAlreadyArmed;
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

            if (!bindInternalClick)
                return;

            if (definition == null || warrior == null || _relicManager == null)
                return;

            if (IsWarriorUnavailable())
                return;

            warrior.NotifyUIConsumedInput(worldInputBlockSeconds);

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
                bool armed = warrior.TryArmIceBallRelic(
                    iceDef,
                    consumeOnCast: ShouldConsumeOnUse()
                );

                if (!armed)
                    return;

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

            bool attack2Used = warrior.TryUseRelicAttack2(
                duration,
                cooldown,
                triggerNow: false
            );

            if (!attack2Used)
                return;

            if (ShouldConsumeOnUse())
                _relicManager.TryConsume(definition, 1);

            RefreshInteractable();
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
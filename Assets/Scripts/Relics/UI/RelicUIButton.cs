using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Definitions;
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

        [Header("Use Behavior")]
        [SerializeField] private bool consumeOneOnUse = true;
        [SerializeField] private float fallbackAttack2Duration = 1.0f;
        [SerializeField] private float fallbackAttack2Cooldown = 6f;
        [SerializeField] private bool triggerAttackImmediately = true;
        [SerializeField] private float worldInputBlockSeconds = 0.12f;

        [Header("Refs")]
        [SerializeField] private Warrior warrior;

        [SerializeField] private bool bindInternalClick = true;

        private Button _btn;
        private RelicManager _rm;
        private string _id;

        private void Awake()
        {
            _btn = GetComponent<Button>();

            _btn.onClick.RemoveListener(OnClicked);

            if (bindInternalClick) // <- respect flag
                _btn.onClick.AddListener(OnClicked);

            if (iconImage == null) iconImage = GetComponent<Image>();

            _id = (definition != null && !string.IsNullOrEmpty(definition.relicId))
                ? definition.relicId
                : (definition != null ? definition.name : "");

            if (definition != null && iconImage != null && definition.icon != null)
                iconImage.sprite = definition.icon;
        }

        private void Start()
        {
            if (warrior == null) warrior = FindFirstObjectByType<Warrior>();
            if (warrior == null) return;

            _rm = warrior.GetComponent<RelicManager>();
            if (_rm == null) return;

            _rm.OnRelicCountChanged += HandleCountChanged;
            SetCount(_rm.GetCount(definition));
        }

        private void Update()
        {
            if (_btn == null || _rm == null || definition == null || warrior == null) return;

            bool hasResource = _rm.GetCount(definition) > 0;
            bool canUseNow =
                !warrior.IsDead &&
                !warrior.CanDie &&
                warrior.CanAttackWarrior &&
                !warrior.IsFrozenByHivernox;

            bool blockedByIceArmed = definition is IceBallRelic && warrior != null && warrior.IsIceBallArmed;
            _btn.interactable = hasResource && canUseNow && !blockedByIceArmed;
        }

        private void OnDestroy()
        {
            if (_btn != null) _btn.onClick.RemoveListener(OnClicked);
            if (_rm != null) _rm.OnRelicCountChanged -= HandleCountChanged;
        }

        private int _lastHandledFrame = -1;
        private int _callsThisFrame = 0;

        //private void HandleCountChanged(RelicDefinition def, int newCount)
        //{
        //    if (def == null) return;

        //    string id = !string.IsNullOrEmpty(def.relicId) ? def.relicId : def.name;
        //    if (id != _id) return;

        //    if (Time.frameCount != _lastHandledFrame)
        //    {
        //        _lastHandledFrame = Time.frameCount;
        //        _callsThisFrame = 0;
        //    }

        //    if (_callsThisFrame >= 1) return; // set to 2 if you truly want max 2
        //    _callsThisFrame++;
        //    Debug.Log($"Updating count for {id} to {newCount} (calls this frame: {_callsThisFrame})");
        //    SetCount(newCount);
        //}
        private void HandleCountChanged(RelicDefinition def, int newCount)
        {
            if (def == null) return;

            string id = !string.IsNullOrEmpty(def.relicId) ? def.relicId : def.name;
            if (id != _id) return;

            SetCount(newCount);
        }
        private void SetCount(int c)
        {
            if (countText != null) countText.text = "x" + c;
        }
        public void OnPointerDown(PointerEventData eventData)
        {
            if (warrior == null) warrior = FindFirstObjectByType<Warrior>();
            if (warrior == null) return;

            // Keep your existing UI-world input block
            warrior.NotifyUIConsumedInput(worldInputBlockSeconds);

        }
        private void OnClicked()
        {
            if (definition == null || warrior == null || _rm == null) return;
            // Hard guard: do not arm or use relics while dead, frozen, or action-locked by Hivernox.
            if (warrior.IsDead || warrior.CanDie || !warrior.CanAttackWarrior || warrior.IsFrozenByHivernox)
                return;

            warrior.NotifyUIConsumedInput(worldInputBlockSeconds);

            if (!HasResourceToUse())
                return;

            // 1) Shield relic branch
            if (definition is ShieldRelic shieldDef)
            {
                bool used = warrior.TryUseShieldRelic(shieldDef.shieldDuration, shieldDef.shieldCooldown);
                if (!used)
                    return;

                if (ShouldConsumeOnUse())
                    _rm.TryConsume(definition, 1);

                return;
            }
            // 2) Sprint relic branch (ARM only; actual consume/use happens when movement starts)
            if (definition is SprintRelic sprintDef)
            {
                // Consommer d'abord
                if (ShouldConsumeOnUse() && !_rm.TryConsume(definition, 1))
                    return; // plus de stack

                bool armed = warrior.TryArmSprintRelic(
                    relicId: _id,
                    speedMultiplier: sprintDef.speedMultiplier,
                    duration: sprintDef.sprintDuration,
                    cooldown: sprintDef.sprintCooldown,
                    consumeOnUse: false); // Warrior ne reconsomme pas

                if (!armed)
                {
                    // Annulé (shield up, déjà armé, etc.) ? rembourser
                    _rm.Collect(definition); // ou une méthode Refund() dédiée
                }
                return;
            }

            // Ice Ball relic branch (arm now, consume on next world touch)
            if (definition is IceBallRelic iceDef)
            {
                bool armed = warrior.TryArmIceBallRelic(
                    iceDef,
                    consumeOnCast: ShouldConsumeOnUse());

                if (!armed)
                    return;

                return;
            }
            // 2) Attack2 relic branch
            float duration = fallbackAttack2Duration;
            float cooldown = fallbackAttack2Cooldown;

            if (definition is PowerComboRelic p)
            {
                duration = p.attack2UseDuration;
                cooldown = p.attack2Cooldown;
            }

            bool ok = warrior.TryUseRelicAttack2(duration, cooldown, triggerNow: false);
            if (!ok) return;

            if (ShouldConsumeOnUse())
                _rm.TryConsume(definition, 1);
        }

        private bool ShouldConsumeOnUse()
        {
            if (definition == null) return false;

            // Unlock-style relics use cooldown/ownership only.
            if (!definition.isConsumable) return false;

            // Shield remains unlock-style in your current design.
            if (definition is ShieldRelic) return false;

            return consumeOneOnUse;
        }

        private bool HasResourceToUse()
        {
            if (_rm == null || definition == null) return false;

            return ShouldConsumeOnUse()
                ? _rm.GetCount(definition) > 0
                : _rm.IsOwned(definition);
        }
    }
}
using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Runtime;
using UnityEngine;

namespace Assets.Scripts.Relics.Definitions
{
    [CreateAssetMenu(menuName = "Relics/Common/Power Combo", fileName = "SO_Relic_PowerCombo")]
    public class PowerComboRelic : RelicDefinition
    {
        [Header("Power Combo Settings")]
        public float bonusDamageMultiplier = 1.25f;


        public int healPerKill = 1;

        [Header("Active Use (UI relic click -> Attack2)")]
        public float attack2UseDuration = 1.0f;
        public float attack2Cooldown = 6f;
        public bool triggerAttackImmediately = false;
        public override IRelicRuntime CreateRuntime() => new Runtime(this);

        private sealed class Runtime : RelicRuntimeBase
        {
            private readonly PowerComboRelic _def;
            public Runtime(PowerComboRelic def) => _def = def;

            public override void OnKill(KillEvent e)
            {
                if (Ctx?.Warrior == null) return;
                Ctx.Warrior.Heal(_def.healPerKill);
            }
        }
    }
}
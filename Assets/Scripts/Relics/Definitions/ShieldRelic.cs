using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Runtime;
using UnityEngine;

namespace Assets.Scripts.Relics.Definitions
{
    [CreateAssetMenu(menuName = "Relics/Common/Shield", fileName = "SO_Relic_Shield")]
    public class ShieldRelic : RelicDefinition
    {
        [Header("Shield Active Use")]
        public float shieldDuration = 5f;
        public float shieldCooldown = 8f;

        [Header("Passive")]
        public int healPerKill = 2;

        public override IRelicRuntime CreateRuntime() => new Runtime(this);

        private sealed class Runtime : RelicRuntimeBase
        {
            private readonly ShieldRelic _def;
            public Runtime(ShieldRelic def) => _def = def;

            public override void OnKill(KillEvent e)
            {
                if (Ctx?.Warrior == null) return;
                Ctx.Warrior.Heal(_def.healPerKill);
            }
        }
    }
}
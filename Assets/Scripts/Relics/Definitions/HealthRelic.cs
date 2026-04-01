using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Runtime;
using UnityEngine;

namespace Assets.Scripts.Relics.Definitions
{
    [CreateAssetMenu(menuName = "Relics/Survival/Leech")]
    public class HealthRelic : RelicDefinition
    {
        public int healPerKill = 2;

        public override IRelicRuntime CreateRuntime() => new Runtime(this);

        private sealed class Runtime : RelicRuntimeBase
        {
            private readonly HealthRelic _def;
            public Runtime(HealthRelic def) => _def = def;

            public override void OnKill(KillEvent e)
            {
                if (Ctx?.Warrior == null) return;
                Ctx.Warrior.Heal(_def.healPerKill);
            }
        }
    }
}

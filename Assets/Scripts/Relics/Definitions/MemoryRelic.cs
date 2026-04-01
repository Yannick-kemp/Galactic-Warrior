using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Runtime;
using UnityEngine;

namespace Assets.Scripts.Relics.Definitions
{
    [CreateAssetMenu(menuName = "Relics/Common/Memory", fileName = "SO_Relic_Memory")]
    public class MemoryRelic : RelicDefinition
    {
        [Header("Memory Settings")]
        public int memoryValue = 1; // Optional for future systems

        public int healPerKill = 2;
        public override IRelicRuntime CreateRuntime() => new Runtime(this);

        private sealed class Runtime : RelicRuntimeBase
        {
            private readonly MemoryRelic _def;
            public Runtime(MemoryRelic def) => _def = def;

            public override void OnKill(KillEvent e)
            {
                if (Ctx?.Warrior == null) return;
                Ctx.Warrior.Heal(_def.healPerKill);
            }
        }
    }
}

// Assets/Scripts/Relics/Definitions/SprintRelic.cs
using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Runtime;
using UnityEngine;

namespace Assets.Scripts.Relics.Definitions
{
    [CreateAssetMenu(menuName = "Relics/Common/Sprint", fileName = "SO_Relic_Sprint")]
    public class SprintRelic : RelicDefinition
    {
        [Header("Sprint Settings")]
        [Min(1f)] public float speedMultiplier = 1.35f;
        [Min(0.05f)] public float sprintDuration = 1.5f;
        [Min(0.1f)] public float sprintCooldown = 4.0f;

        public override IRelicRuntime CreateRuntime() => new Runtime(this);

        private sealed class Runtime : RelicRuntimeBase
        {
            private readonly SprintRelic _def;
            public Runtime(SprintRelic def) => _def = def;
            // Intentionally passive runtime: activation is UI + Warrior-driven.
        }
    }
}
using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Runtime;
using UnityEngine;

namespace Assets.Scripts.Relics.Definitions
{
    [CreateAssetMenu(menuName = "Relics/Utility/Key", fileName = "SO_Relic_Key")]
    public class KeyRelic : RelicDefinition
    {
        public const string DefaultRelicId = "relic_key";

        [Header("Key")]
        [Tooltip("Only used as editor/default information. One pickup still adds one stack in RelicManager.")]
        [Min(1)] public int displayPickupValue = 1;

        private void Reset() => ApplyDefaultIdentity();

        private void OnValidate() => ApplyDefaultIdentity();

        private void ApplyDefaultIdentity()
        {
            if (string.IsNullOrWhiteSpace(relicId))
                relicId = DefaultRelicId;

            if (string.IsNullOrWhiteSpace(relicName))
                relicName = "Key";

            if (string.IsNullOrWhiteSpace(description))
                description = "A key relic used to unlock keyed gates, doors, chests, or level blockers.";

            isConsumable = true;
            showCountInUI = true;
        }

        public override IRelicRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : RelicRuntimeBase
        {
            // Passive inventory relic.
            // Collection/count/consumption is handled by RelicManager and KeyRelicLock.
        }
    }
}

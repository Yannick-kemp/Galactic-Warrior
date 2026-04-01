using Assets.Scripts.Relics.Runtime;
using UnityEngine;

namespace Assets.Scripts.Relics.Core
{
    public abstract class RelicDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string relicId;
        public string relicName;
        [TextArea] public string description;
        public Sprite icon;

        [Header("Usage")]
        [Tooltip("If true: each use consumes stack. If false: unlock-style relic (cooldown only).")]
        public bool isConsumable = true;

        [Tooltip("Optional: show count in UI for this relic.")]
        public bool showCountInUI = true;

        public abstract IRelicRuntime CreateRuntime();
    }
}
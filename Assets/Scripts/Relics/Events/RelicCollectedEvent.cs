using Assets.Scripts.Relics.Core;
using UnityEngine;

namespace Assets.Scripts.Relics.Events
{
    public readonly struct RelicCollectedEvent
    {
        public readonly RelicDefinition Definition;
        public readonly string RelicId;
        public readonly int NewCount;
        public readonly Vector3 WorldPosition;

        public RelicCollectedEvent(RelicDefinition def, string relicId, int newCount, Vector3 worldPos)
        {
            Definition = def;
            RelicId = relicId;
            NewCount = newCount;
            WorldPosition = worldPos;
        }
    }
}
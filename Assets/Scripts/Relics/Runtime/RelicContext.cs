// Assets/Scripts/Relics/Runtime/RelicContext.cs
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Events;

namespace Assets.Scripts.Relics.Runtime
{
    public sealed class RelicContext
    {
        public Warrior Warrior { get; }
        public PlayerEventHub Events { get; }

        public RelicContext(Warrior warrior, PlayerEventHub eventsHub)
        {
            Warrior = warrior;
            Events = eventsHub;
        }
    }
}

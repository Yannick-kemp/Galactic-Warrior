// Assets/Scripts/Relics/Core/WarriorRelicBootstrapper.cs
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Events;
using Assets.Scripts.Relics.Runtime;
using UnityEngine;

namespace Assets.Scripts.Relics.Core
{
    [RequireComponent(typeof(Warrior))]
    [RequireComponent(typeof(PlayerEventHub))]
    [RequireComponent(typeof(RelicManager))]
    public sealed class WarriorRelicBootstrapper : MonoBehaviour
    {
        private void Awake()
        {
            var w = GetComponent<Warrior>();
            var hub = GetComponent<PlayerEventHub>();
            var relics = GetComponent<RelicManager>();

            relics.Init(new RelicContext(w, hub));
        }
    }
}

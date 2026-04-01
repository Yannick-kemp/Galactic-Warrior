// Assets/Scripts/Relics/Events/PlayerEventHub.cs
using System;
using UnityEngine;

namespace Assets.Scripts.Relics.Events
{
    public sealed class PlayerEventHub : MonoBehaviour
    {
        public event Action<HitEvent> OnHit;
        public event Action<KillEvent> OnKill;

        public void RaiseHit(HitEvent e) => OnHit?.Invoke(e);
        public void RaiseKill(KillEvent e) => OnKill?.Invoke(e);
    }
}

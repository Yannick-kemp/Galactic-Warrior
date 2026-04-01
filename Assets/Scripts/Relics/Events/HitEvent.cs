// Assets/Scripts/Relics/Events/HitEvent.cs
using System;
using UnityEngine;

namespace Assets.Scripts.Relics.Events
{
    [Serializable]
    public struct HitEvent
    {
        public GameObject attacker;
        public GameObject target;
        public int damage;
        public Vector2 hitPoint;
    }
}

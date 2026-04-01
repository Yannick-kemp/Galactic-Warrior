using UnityEngine;

namespace Assets.Scripts.Services
{
    public interface IAttacker
    {
        Transform Transform { get; }
        Animator Animator { get; }
        AudioSource AudioSource { get; }
        string Name { get; }
        void OnRangeExecuted(Transform target, int damage);
        void OnWarriorDetectedInLaser();
        void OnWarriorLeftLaser();
        void OnLaserDeactivated();


    }
}

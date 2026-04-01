using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public interface ICollisionHandler
    {
        bool ShouldHandle(Collision2D collision);
        void HandleCollision(Collision2D collision);
    }
}

using System.Collections;

namespace Assets.Scripts.Characteres
{
    public interface ICharacterController
    {
        bool CanJump { get; set; }
        void WaitAnimationDisplay();
        void JumpAnimationDisplay();
        void RunAnimationDisplay();
        void AttackAnimationDisplay();
        IEnumerator MoveTowardPostionAction(float x);
        // IEnumerator JumpTowardPositionAction(UnityEngine.Vector2 target, float height, float duration);

    }
}

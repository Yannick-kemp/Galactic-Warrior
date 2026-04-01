using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {

        #region Gizmos

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
            {
                Vector2 right = (Vector2)transform.position + new Vector2(attackCenterOffset.x, attackCenterOffset.y);
                Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
                Gizmos.DrawSphere(right, attackRadius);
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(right, attackRadius);

                Vector2 left = (Vector2)transform.position + new Vector2(-attackCenterOffset.x, attackCenterOffset.y);
                Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
                Gizmos.DrawSphere(left, attackRadius);
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(left, attackRadius);
                return;
            }

            Vector2 center = GetAttackCenter();
            bool hasEnemy = HasEnemyInAttackRange();

            Gizmos.color = hasEnemy ? new Color(0f, 1f, 0f, 0.3f) : new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawSphere(center, attackRadius);
            Gizmos.color = hasEnemy ? Color.green : Color.red;
            Gizmos.DrawWireSphere(center, attackRadius);
        }

        #endregion
    }
}

using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

public class WarriorDeathZone2D : MonoBehaviour
{
    public enum AffectMode
    {
        WarriorOnly,
        EnemiesOnly,
        Both
    }

    [Header("Target Filtering")]
    [SerializeField] private AffectMode affectMode = AffectMode.Both;

    [Header("Enemy Handling")]
    [SerializeField] private bool killEnemiesInstantly = true;

    private void Reset()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryKill(other);
    }

    private void TryKill(Collider2D other)
    {
        if (other == null) return;

        Warrior warrior = other.GetComponentInParent<Warrior>();
        if (warrior != null &&
            (affectMode == AffectMode.Both || affectMode == AffectMode.WarriorOnly))
        {
            if (!warrior.IsDeadOrDying)
                warrior.ForceDeath();

            return;
        }

        Enemy enemy = other.GetComponentInParent<Enemy>();
        if (enemy != null &&
            (affectMode == AffectMode.Both || affectMode == AffectMode.EnemiesOnly))
        {
            if (!enemy.IsDeadOrDying)
            {
                if (killEnemiesInstantly)
                    enemy.ForceDeathImmediate();
                else
                    enemy.ForceDeath();
            }
        }
    }
}
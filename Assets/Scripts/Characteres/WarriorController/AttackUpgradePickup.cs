using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

public class AttackUpgradePickup : MonoBehaviour
{
    [SerializeField] private float attack2Duration = 7f;

    private void OnTriggerEnter2D(Collider2D other)
    {
        var warrior = other.GetComponent<Warrior>();
        if (warrior == null) return;

        // Switch to Attack2 now, then auto-revert to Attack1 after duration
        warrior.EnableAttack2Temporarily(attack2Duration);

        Destroy(gameObject);
    }
}

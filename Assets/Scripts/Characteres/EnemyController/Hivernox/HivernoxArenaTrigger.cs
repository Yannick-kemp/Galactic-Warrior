using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Place this on the boss-arena/platform trigger. When Warrior enters, Hivernox starts.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class HivernoxArenaTrigger : MonoBehaviour
    {
        [SerializeField] private HivernoxBoss hivernox;
        [SerializeField] private bool triggerOnlyOnce = true;
        private bool _triggered;

        private void Reset()
        {
            Collider2D c = GetComponent<Collider2D>();
            if (c != null)
                c.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (triggerOnlyOnce && _triggered)
                return;

            Warrior warrior = other.GetComponent<Warrior>() ?? other.GetComponentInParent<Warrior>();
            if (warrior == null)
                return;

            if (hivernox == null)
                hivernox = FindFirstObjectByType<HivernoxBoss>();

            if (hivernox == null)
                return;

            _triggered = true;
            hivernox.ActivateBoss();
        }
    }
}

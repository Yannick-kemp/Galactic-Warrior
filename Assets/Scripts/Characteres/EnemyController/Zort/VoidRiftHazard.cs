using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Persistent floor-rift damage zone used by Zort in Phase 3. ZortBoss toggles these
    /// GameObjects on/off via <c>SetActive</c> (see <c>riftHazardSlots</c>), so this only
    /// needs to deal ticking damage to the Warrior while it overlaps the zone.
    ///
    /// Requires a trigger <see cref="Collider2D"/> sized to the rift footprint.
    /// Damage path mirrors RakaFlameDamage: guarded by sprint (IsDodging) and shield (ShieldIsUp),
    /// then TakeDamage + ApplyHitReaction.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class VoidRiftHazard : MonoBehaviour
    {
        [Header("Damage")]
        [SerializeField] private int damage = 8;
        [SerializeField] private float tickInterval = 0.5f;
        [SerializeField] private float warriorHitStun = 0.08f;
        [SerializeField] private float warriorHitKnockback = 2.5f;
        [SerializeField] private HitKind hitKind = HitKind.Spark;

        [Header("Optional")]
        [Tooltip("Spawned once when the Warrior first enters the rift (e.g. a flare). Auto-destroyed after 2s.")]
        [SerializeField] private GameObject enterFxPrefab;

        private float _nextTickTime;

        private void OnEnable()
        {
            // Re-armed every time Zort activates/relocates this slot.
            _nextTickTime = 0f;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryDamage(other, isEnter: true);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryDamage(other, isEnter: false);
        }

        private void TryDamage(Collider2D other, bool isEnter)
        {
            if (other == null)
                return;

            Warrior warrior = other.GetComponent<Warrior>() ?? other.GetComponentInParent<Warrior>();
            if (warrior == null || warrior.IsDeadOrDying)
                return;

            if (Time.time < _nextTickTime)
                return;

            _nextTickTime = Time.time + tickInterval;

            // Sprint = invulnerable; shield = absorbed. Same rules as other Zort hits.
            if (warrior.IsDodging || warrior.ShieldIsUp)
                return;

            if (damage > 0)
            {
                warrior.TakeDamage(damage);
                warrior.ApplyHitReaction(hitKind, transform.position, warriorHitStun, warriorHitKnockback);
            }

            if (isEnter && enterFxPrefab != null)
            {
                GameObject fx = Instantiate(enterFxPrefab, other.ClosestPoint(transform.position), Quaternion.identity);
                Destroy(fx, 2f);
            }
        }
    }
}

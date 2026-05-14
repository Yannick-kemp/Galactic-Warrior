using System.Collections.Generic;
using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Simple arena portal for the Hivernox fight.
    /// Put this on a trigger collider. Link it to another HivernoxPortalGateway.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class HivernoxPortalGateway : MonoBehaviour
    {
        [SerializeField] private HivernoxPortalGateway linkedPortal;
        [SerializeField] private Vector3 exitOffset = new Vector3(0.75f, 0f, 0f);
        [SerializeField] private float reuseCooldown = 0.35f;
        [SerializeField] private bool keepVelocity = false;

        private static readonly Dictionary<Warrior, float> LastTeleportTime = new Dictionary<Warrior, float>();

        private void Reset()
        {
            Collider2D c = GetComponent<Collider2D>();
            if (c != null)
                c.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Warrior warrior = other.GetComponent<Warrior>() ?? other.GetComponentInParent<Warrior>();
            if (warrior == null || linkedPortal == null)
                return;

            float lastTime;
            if (LastTeleportTime.TryGetValue(warrior, out lastTime) && Time.time < lastTime + reuseCooldown)
                return;

            LastTeleportTime[warrior] = Time.time;

            Vector3 target = linkedPortal.transform.position + linkedPortal.exitOffset;
            warrior.StopMoveTowardCoroutine();
            warrior.StopJumpTowardCoroutine();
            warrior.transform.position = new Vector3(target.x, target.y, warrior.transform.position.z);

            if (!keepVelocity && warrior.rigidbody2 != null)
                warrior.rigidbody2.linearVelocity = Vector2.zero;

            warrior.CanMove = true;
            warrior.CanAttackWarrior = true;
        }
    }
}

using System;
using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// Spectral minion summoned by Zort. Inherits <see cref="Enemy"/> so the Warrior's
    /// normal attack scan finds and kills it, and so it reuses the shared health bar /
    /// death / dissolve flow. It floats toward the Warrior (no platform binding) and
    /// deals ticking contact damage. On death it reports its position via the callback
    /// passed in <see cref="Init"/> so Zort can drop a small HP pickup.
    /// </summary>
    public class VoidWraith : Enemy
    {
        [Header("Void Wraith")]
        [SerializeField] private float floatSpeed = 3.2f;
        [SerializeField] private float hoverAmplitude = 0.25f;
        [SerializeField] private float hoverFrequency = 4f;
        [SerializeField] private float contactRange = 0.75f;
        [SerializeField] private int contactDamage = 10;
        [SerializeField] private float contactTickInterval = 0.6f;
        [SerializeField] private float warriorHitStun = 0.1f;
        [SerializeField] private float warriorHitKnockback = 3f;

        private Action<Vector3> _onDeath;
        private float _nextContactTime;
        private float _hoverSeed;

        /// <summary>Called by ZortBoss right after spawning. <paramref name="onDeath"/> receives the death position.</summary>
        public void Init(Action<Vector3> onDeath)
        {
            _onDeath = onDeath;
        }

        protected override void Start()
        {
            base.Start();

            SetEnemyType(EnemyType.Wraith);
            _hoverSeed = UnityEngine.Random.value * 10f;

            // Wraiths float; never let gravity pull them off-screen.
            if (rigidbody2 != null)
                rigidbody2.gravityScale = 0f;
        }

        // Replace the platform-bound Enemy.Update with simple homing flight.
        protected override void Update()
        {
            if (IsDeadOrDying)
                return;

            Warrior warrior = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            if (warrior == null || warrior.IsDeadOrDying)
                return;

            MoveTowardWarrior(warrior);
            TryContactDamage(warrior);
        }

        private void MoveTowardWarrior(Warrior warrior)
        {
            Vector3 targetPos = warrior.collider2 != null
                ? warrior.collider2.bounds.center
                : warrior.transform.position;

            // Hover bob layered on top of the homing motion.
            float bob = Mathf.Sin((Time.time + _hoverSeed) * hoverFrequency) * hoverAmplitude;
            targetPos.y += bob;

            Vector3 next = Vector3.MoveTowards(transform.position, targetPos, floatSpeed * Time.deltaTime);

            // Scripted movement: keep the Warrior-body momentum guard from rolling it back.
            MarkIntentionalEnemyDisplacement(0.1f);

            if (rigidbody2 != null)
                rigidbody2.MovePosition(next);
            else
                transform.position = next;

            FlipCharacter(targetPos.x);
        }

        private void TryContactDamage(Warrior warrior)
        {
            if (Time.time < _nextContactTime)
                return;

            Vector2 myCenter = NormalCollider != null
                ? (Vector2)NormalCollider.bounds.center
                : (Vector2)transform.position;

            Vector2 warriorCenter = warrior.collider2 != null
                ? (Vector2)warrior.collider2.bounds.center
                : (Vector2)warrior.transform.position;

            if (Vector2.Distance(myCenter, warriorCenter) > contactRange)
                return;

            _nextContactTime = Time.time + contactTickInterval;

            if (warrior.IsDodging || warrior.ShieldIsUp)
                return;

            warrior.TakeDamage(contactDamage);
            warrior.ApplyHitReaction(HitKind.Spark, myCenter, warriorHitStun, warriorHitKnockback);
        }

        protected override void OnDeath()
        {
            // Fire the callback before the base flow destroys us.
            _onDeath?.Invoke(transform.position);
            _onDeath = null;

            base.OnDeath();
        }
    }
}

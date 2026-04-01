// Assets/Scripts/Pickups/StabilizationCorePickup.cs
using UnityEngine;

namespace Assets.Scripts.Pickups
{
    public sealed class StabilizationCorePickup : MonoBehaviour
    {
        public int value = 1;
        public float magnetRange = 3f;
        public float magnetSpeed = 8f;

        private Transform _target;

        private void Start()
        {
            var w = GameMgr.Instance != null ? GameMgr.Instance.WarriorInstance : null;
            _target = w != null ? w.transform : null;

            Destroy(gameObject, 12f);
        }

        private void Update()
        {
            if (_target == null) return;

            float d = Vector3.Distance(transform.position, _target.position);
            if (d <= magnetRange)
                transform.position = Vector3.MoveTowards(transform.position, _target.position, magnetSpeed * Time.deltaTime);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var w = other.GetComponentInParent<Assets.Scripts.Characteres.WarriorController.Warrior>();
            if (w == null) return;

            Debug.Log($"[Pickup] Stabilization Core +{value}");
            Destroy(gameObject);
        }
    }
}

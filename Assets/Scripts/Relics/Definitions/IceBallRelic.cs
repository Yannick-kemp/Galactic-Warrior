using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Runtime;
using UnityEngine;

namespace Assets.Scripts.Relics.Definitions
{
    [CreateAssetMenu(menuName = "Relics/Offense/Ice Ball Relic", fileName = "SO_IceBallRelic")]
    public class IceBallRelic : RelicDefinition
    {
        [Header("Projectile")]
        public GameObject projectilePrefab;

        [Min(0.1f)] public float projectileSpeed = 12f;
        [Min(1)] public int damage = 12;
        [Min(0f)] public float stunSeconds = 0.35f;
        [Min(0.1f)] public float lifeTime = 2.5f;

        [Header("Spawn")]
        public Vector3 spawnLocalOffset = new Vector3(0.65f, 0.45f, 0f);

        public override IRelicRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : RelicRuntimeBase
        {
            // No passive runtime behavior needed.
            // This relic is used actively from the UI + Warrior input flow.
        }
    }
}
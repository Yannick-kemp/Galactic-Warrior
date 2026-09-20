using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// How many melee hits each enemy survives, expressed as a hit count rather than as damage.
    ///
    /// Why a hit count: damage on its own says nothing without the enemy's health, and every enemy
    /// currently sits at the inherited default of 100. Tuning "3 hits" is the intent; the damage
    /// figure is just the arithmetic. The table converts at runtime with
    /// damage = ceil(maxHealth / hits), reading each enemy's own maxHealth, so a per-prefab health
    /// change or a spawn-point override still lands on the requested number of hits.
    ///
    /// Enemies are matched on their C# component type, NOT on the serialized EnemyType enum. That
    /// enum is unreliable here: bee, bee_eretic and P39 all leave it at 0, which reads as Hashagar,
    /// so keying on it would give three enemies Hashagar's row.
    ///
    /// Anything without its own field falls into <see cref="otherEnemies"/>: Arachnee, Bee,
    /// Morvex, Hivernox, Zort and Wraith.
    ///
    /// The two other ways to kill an enemy are already Inspector-editable and are deliberately not
    /// duplicated here: the shield stomp via stompDamage on the Warrior, and the ice bullet via
    /// iceBulletEnemyDamage on its projectile prefab.
    /// </summary>
    [CreateAssetMenu(menuName = "Galactic Warrior/Enemy Hit Count Table", fileName = "SO_EnemyHitCounts")]
    public class EnemyHitCountTable : ScriptableObject
    {
        [System.Serializable]
        public class HitCounts
        {
            [Min(1), Tooltip("Hits of the main attack needed to kill this enemy.")]
            public int attack1Hits = 10;

            [Min(1), Tooltip("Hits of the second attack needed to kill this enemy.")]
            public int attack2Hits = 10;
        }

        [Header("Per enemy")]
        public HitCounts hashagar = new HitCounts { attack1Hits = 50, attack2Hits = 50 };
        public HitCounts zalayty  = new HitCounts { attack1Hits = 10, attack2Hits = 20 };
        public HitCounts m97      = new HitCounts { attack1Hits = 17, attack2Hits = 17 };
        public HitCounts p39      = new HitCounts { attack1Hits = 5,  attack2Hits = 34 };
        public HitCounts raka     = new HitCounts { attack1Hits = 25, attack2Hits = 25 };
        public HitCounts crawling = new HitCounts { attack1Hits = 4,  attack2Hits = 13 };

        [Header("Everything else")]
        [Tooltip("Arachnee, Bee, Bee eretic, Morvex, Hivernox, Zort, Wraith.")]
        public HitCounts otherEnemies = new HitCounts { attack1Hits = 10, attack2Hits = 10 };

        public int Attack1Damage(Enemy enemy) => DamageFor(enemy, CountsFor(enemy).attack1Hits);

        public int Attack2Damage(Enemy enemy) => DamageFor(enemy, CountsFor(enemy).attack2Hits);

        public HitCounts CountsFor(Enemy enemy) => enemy switch
        {
            M97Monster => m97,
            CrawlingMonster => crawling,
            P39Monster_WithHealthBar => p39,
            RakaMonster => raka,
            ZalaytyMonster => zalayty,
            HashagarMonster => hashagar,
            _ => otherEnemies
        };

        /// <summary>
        /// Rounds up so the requested hit actually reaches zero: 100 health over 3 hits gives 34,
        /// and 3 x 34 clears 100. Never returns 0, which would make the enemy unkillable.
        /// </summary>
        private static int DamageFor(Enemy enemy, int hits)
        {
            if (hits < 1) hits = 1;

            float health = enemy != null ? enemy.maxHealth : 100f;

            return Mathf.Max(1, Mathf.CeilToInt(health / hits));
        }
    }
}

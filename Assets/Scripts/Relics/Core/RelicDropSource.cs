using UnityEngine;

namespace Assets.Scripts.Relics.Core
{
    public class RelicDropSource : MonoBehaviour
    {
        [Header("Override drop table for this enemy")]
        public RelicDropTable table;

        [Tooltip("Multiplies the table.dropChance (1 = normal, 2 = double chance, etc.)")]
        public float chanceMultiplier = 1f;
    }
}

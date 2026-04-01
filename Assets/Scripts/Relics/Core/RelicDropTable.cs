using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Relics.Core
{
    [System.Serializable]
    public class RelicDropEntry
    {
        public GameObject pickupPrefab;

        [Range(0f, 1f)]
        public float dropChance = 0.25f;
    }

    [CreateAssetMenu(menuName = "Relics/Drop Table", fileName = "SO_RelicDropTable")]
    public class RelicDropTable : ScriptableObject
    {
        [Header("Master chance for this table")]
        [Range(0f, 1f)]
        public float dropChance = 1f;

        [Header("Possible drops")]
        public List<RelicDropEntry> entries = new List<RelicDropEntry>();

        public List<GameObject> RollAll(float chanceMultiplier = 1f)
        {
            List<GameObject> results = new List<GameObject>();

            if (entries == null || entries.Count == 0)
                return results;

            float finalTableChance = Mathf.Clamp01(dropChance * chanceMultiplier);
            if (Random.value > finalTableChance)
                return results;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.pickupPrefab == null)
                    continue;

                float finalEntryChance = Mathf.Clamp01(entry.dropChance);
                if (Random.value <= finalEntryChance)
                    results.Add(entry.pickupPrefab);
            }

            return results;
        }
    }
}
using System.Collections.Generic;
using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Relics.Events;
using UnityEngine;

namespace Assets.Scripts.Relics.Core
{
    [RequireComponent(typeof(PlayerEventHub))]
    public class RelicRewardSpawner : MonoBehaviour
    {
        [SerializeField] private RelicDropTable table;
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.2f, 0f);

        [Header("Spread multiple drops")]
        [SerializeField] private float horizontalSpacing = 0.35f;

        private PlayerEventHub _hub;

        private void Awake()
        {
            _hub = GetComponent<PlayerEventHub>();
            _hub.OnKill += OnKill;
        }

        private void OnDestroy()
        {
            if (_hub != null) _hub.OnKill -= OnKill;
        }

        private void OnKill(KillEvent e)
        {
            if (e.victim == null) return;

            var enemy = e.victim.GetComponent<Enemy>() ?? e.victim.GetComponentInParent<Enemy>();

            RelicDropTable chosenTable = table;
            float multiplier = 1f;

            var dropSource = (enemy != null)
                ? enemy.GetComponent<RelicDropSource>()
                : e.victim.GetComponent<RelicDropSource>();

            if (dropSource != null && dropSource.table != null)
            {
                chosenTable = dropSource.table;
                multiplier = Mathf.Max(0f, dropSource.chanceMultiplier);
            }

            if (chosenTable == null) return;

            List<GameObject> prefabs = chosenTable.RollAll(multiplier);
            if (prefabs == null || prefabs.Count == 0) return;

            Vector3 basePos = enemy != null ? enemy.transform.position : e.victim.transform.position;

            float startX = -(prefabs.Count - 1) * 0.5f * horizontalSpacing;

            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                if (prefab == null) continue;

                Vector3 extraOffset = new Vector3(startX + i * horizontalSpacing, 0f, 0f);
                GameObject spawned = Instantiate(prefab, basePos + spawnOffset + extraOffset, Quaternion.identity);

                var rb = spawned.GetComponent<Rigidbody2D>();
                if (rb != null) rb.simulated = true;
            }
        }
    }
}
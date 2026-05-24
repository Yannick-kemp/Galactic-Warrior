using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Relics.Definitions;
using Assets.Scripts.Relics.Events;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Scripts.Relics.Core
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerEventHub))]
    [RequireComponent(typeof(RelicManager))]
    public sealed class ArachneeKillKeyRelicReward : MonoBehaviour
    {
        [Header("World Reward")]
        [Tooltip("Assign the visible KeyRelic PICKUP PREFAB here. Do NOT assign the ScriptableObject here.")]
        [SerializeField] private GameObject keyRelicPickupPrefab;

        [Tooltip("Optional fallback only. Used only if no pickup prefab is assigned.")]
        [SerializeField] private KeyRelic keyRelicDefinition;

        [Header("Rule")]
        [SerializeField, Min(1)] private int arachneeKillsPerKey = 5;
        [SerializeField, Min(1)] private int keyRelicsPerReward = 1;

        [Header("Spawn")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.25f, 0f);
        [SerializeField, Min(0f)] private float multiRewardHorizontalSpacing = 0.35f;

        [Header("Fallback")]
        [SerializeField] private bool fallbackToDirectCollectIfPrefabMissing = false;

        [Header("Progress")]
        [SerializeField] private bool resetProgressOnSceneLoad = false;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private PlayerEventHub _hub;
        private RelicManager _relicManager;

        private int _arachneeKillProgress;

        // Prevent double counting because IceBulletProjectile can raise KillEvent,
        // and Enemy.OnDeath can also raise KillEvent.
        private readonly HashSet<int> _countedArachneeVictims = new HashSet<int>();

        private void Awake()
        {
            _hub = GetComponent<PlayerEventHub>();
            _relicManager = GetComponent<RelicManager>();
        }

        private void OnEnable()
        {
            if (_hub != null)
            {
                _hub.OnKill += OnKill;

                if (debugLogs)
                    Debug.Log("[ArachneeKillKeyRelicReward] Subscribed to PlayerEventHub.OnKill.", this);
            }
            else
            {
                Debug.LogError("[ArachneeKillKeyRelicReward] Missing PlayerEventHub on Warrior.", this);
            }

            if (resetProgressOnSceneLoad)
                SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            if (_hub != null)
                _hub.OnKill -= OnKill;

            if (resetProgressOnSceneLoad)
                SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnValidate()
        {
            arachneeKillsPerKey = Mathf.Max(1, arachneeKillsPerKey);
            keyRelicsPerReward = Mathf.Max(1, keyRelicsPerReward);
            multiRewardHorizontalSpacing = Mathf.Max(0f, multiRewardHorizontalSpacing);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _arachneeKillProgress = 0;
            _countedArachneeVictims.Clear();

            if (debugLogs)
                Debug.Log("[ArachneeKillKeyRelicReward] Progress reset on scene load.", this);
        }

        private void OnKill(KillEvent e)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[ArachneeKillKeyRelicReward] OnKill received. " +
                    $"killer={(e.killer != null ? e.killer.name : "NULL")} | " +
                    $"victim={(e.victim != null ? e.victim.name : "NULL")}",
                    this
                );
            }

            // Only count kills credited to this Warrior.
            if (e.killer != null && e.killer != gameObject)
            {
                if (debugLogs)
                    Debug.Log("[ArachneeKillKeyRelicReward] Ignored kill because killer is not this Warrior.", this);

                return;
            }

            ArachneeMonster arachnee = GetArachneeVictim(e.victim);
            if (arachnee == null)
            {
                if (debugLogs)
                    Debug.Log("[ArachneeKillKeyRelicReward] Ignored kill because victim is not ArachneeMonster.", this);

                return;
            }

            int victimId = arachnee.GetInstanceID();

            if (!_countedArachneeVictims.Add(victimId))
            {
                if (debugLogs)
                    Debug.Log("[ArachneeKillKeyRelicReward] Ignored duplicate Arachnee kill event.", this);

                return;
            }

            Vector3 rewardSpawnPosition = GetRewardSpawnPosition(arachnee, e.victim);

            _arachneeKillProgress++;

            if (debugLogs)
            {
                Debug.Log(
                    $"[ArachneeKillKeyRelicReward] Arachnee kill progress: " +
                    $"{_arachneeKillProgress}/{arachneeKillsPerKey}",
                    this
                );
            }

            if (_arachneeKillProgress < arachneeKillsPerKey)
                return;

            int completedRewards = _arachneeKillProgress / arachneeKillsPerKey;
            _arachneeKillProgress %= arachneeKillsPerKey;

            int totalKeysToGive = completedRewards * keyRelicsPerReward;

            SpawnOrGrantKeyRelicRewards(totalKeysToGive, rewardSpawnPosition);

            if (debugLogs)
            {
                Debug.Log(
                    $"[ArachneeKillKeyRelicReward] Reward triggered. " +
                    $"Spawned/granted {totalKeysToGive} KeyRelic(s). " +
                    $"Remaining progress: {_arachneeKillProgress}/{arachneeKillsPerKey}",
                    this
                );
            }
        }

        private void SpawnOrGrantKeyRelicRewards(int amount, Vector3 basePosition)
        {
            amount = Mathf.Max(1, amount);

            if (keyRelicPickupPrefab != null)
            {
                SpawnKeyRelicPickups(amount, basePosition);
                return;
            }

            Debug.LogWarning(
                "[ArachneeKillKeyRelicReward] Reward reached, but Key Relic Pickup Prefab is not assigned. " +
                "Assign a visible KeyRelic pickup prefab in the Inspector.",
                this
            );

            if (!fallbackToDirectCollectIfPrefabMissing)
                return;

            if (_relicManager == null || keyRelicDefinition == null)
            {
                Debug.LogWarning(
                    "[ArachneeKillKeyRelicReward] Direct collect fallback failed. Missing RelicManager or KeyRelic definition.",
                    this
                );
                return;
            }

            for (int i = 0; i < amount; i++)
                _relicManager.Collect(keyRelicDefinition, bypassFrameCap: true);
        }

        private void SpawnKeyRelicPickups(int amount, Vector3 basePosition)
        {
            float startX = -(amount - 1) * 0.5f * multiRewardHorizontalSpacing;

            for (int i = 0; i < amount; i++)
            {
                Vector3 pos = basePosition + spawnOffset;
                pos.x += startX + i * multiRewardHorizontalSpacing;

                GameObject spawned = Instantiate(keyRelicPickupPrefab, pos, Quaternion.identity);

                if (debugLogs)
                {
                    Debug.Log(
                        $"[ArachneeKillKeyRelicReward] Spawned KeyRelic pickup '{spawned.name}' at {pos}.",
                        spawned
                    );
                }

                Rigidbody2D rb = spawned.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.simulated = true;
                    rb.WakeUp();
                }

                Collider2D[] cols = spawned.GetComponentsInChildren<Collider2D>(true);
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] != null)
                        cols[c].enabled = true;
                }
            }
        }

        private static Vector3 GetRewardSpawnPosition(ArachneeMonster arachnee, GameObject victim)
        {
            if (arachnee != null)
            {
                Collider2D col = arachnee.GetComponent<Collider2D>();

                if (col == null)
                    col = arachnee.GetComponentInChildren<Collider2D>();

                if (col != null)
                    return col.bounds.center;

                return arachnee.transform.position;
            }

            if (victim != null)
                return victim.transform.position;

            return Vector3.zero;
        }

        private static ArachneeMonster GetArachneeVictim(GameObject victim)
        {
            if (victim == null)
                return null;

            ArachneeMonster arachnee = victim.GetComponent<ArachneeMonster>();

            if (arachnee == null)
                arachnee = victim.GetComponentInParent<ArachneeMonster>();

            return arachnee;
        }
    }
}
using Assets.Scripts.Relics.Events;
using Assets.Scripts.Relics.Runtime;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Relics.Core
{
    public sealed class RelicManager : MonoBehaviour
    {
        [SerializeField] private List<RelicDefinition> startingRelics = new List<RelicDefinition>();

        [Header("Anti-Duplicate")]
        [SerializeField, Min(1)] private int maxCollectsPerFramePerRelic = 1;

        private readonly List<IRelicRuntime> _active = new List<IRelicRuntime>();
        private readonly HashSet<string> _ownedIds = new HashSet<string>();

        // Count per relic type (for UI xN)
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        private readonly Dictionary<string, RelicDefinition> _defsById = new Dictionary<string, RelicDefinition>();

        // UI listens to this to refresh xN
        public event Action<RelicDefinition, int> OnRelicCountChanged;

        // Dedupe by pickup instance
        private readonly HashSet<int> _processedPickupIds = new HashSet<int>();

        // Per-relic per-frame cap bookkeeping
        private readonly Dictionary<string, int> _collectFrameById = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _collectCountInFrameById = new Dictionary<string, int>();

        private PlayerEventHub _hub;
        private RelicContext _ctx;

        [SerializeField, Min(1)] private int maxConsumesPerFramePerRelic = 1;
        private readonly Dictionary<string, int> _consumeFrameById = new();
        private readonly Dictionary<string, int> _consumeCountInFrameById = new();

        public event Action<RelicCollectedEvent> OnRelicCollected;
        public void Init(RelicContext ctx)
        {
            _ctx = ctx;
            _hub = ctx.Events;

            _hub.OnHit += ForwardHit;
            _hub.OnKill += ForwardKill;

            // starting relics: bypass frame cap so setup is deterministic
            for (int i = 0; i < startingRelics.Count; i++)
                Collect(startingRelics[i], bypassFrameCap: true);
        }

        private static string GetId(RelicDefinition def)
        {
            if (def == null) return "";
            return !string.IsNullOrEmpty(def.relicId) ? def.relicId : def.name;
        }

        /// <summary>
        /// Preferred from pickup scripts. Prevents same pickup object from being counted twice.
        /// </summary>
        public bool CollectFromPickup(RelicDefinition def, int pickupInstanceId, Vector3 worldPos)
        {
            if (def == null) return false;

            // ensure we never process same pickup twice even in same frame
            if (_processedPickupIds.Contains(pickupInstanceId))
                return false;

            _processedPickupIds.Add(pickupInstanceId);

            // Bypass frame cap is safe here because pickupId already dedupes the exact object
            bool incremented = Collect(def, bypassFrameCap: true);
            if (!incremented) return false;

            string id = GetId(def);
            int newCount = _counts.TryGetValue(id, out int c) ? c : 0;

            OnRelicCollected?.Invoke(new RelicCollectedEvent(def, id, newCount, worldPos));
            return true;
        }

        // Ancienne signature conservée (compat)
        public bool CollectFromPickup(RelicDefinition def, int pickupInstanceId)
        {
            return CollectFromPickup(def, pickupInstanceId, Vector3.zero);
        }
        /// <summary>
        /// Called when pickup is collected.
        /// Increments count every time; equips runtime only first time.
        /// Returns true if increment happened.
        /// </summary>
        public bool Collect(RelicDefinition def, bool bypassFrameCap = false)
        {
            if (def == null) return false;

            string id = GetId(def);
            if (string.IsNullOrEmpty(id)) return false;

            if (!AllowCollectThisFrame(id, bypassFrameCap))
                return false;

            _defsById[id] = def;

            int newCount = _counts.TryGetValue(id, out int old) ? old + 1 : 1;
            _counts[id] = newCount;

            // Equip runtime only once
            if (_ownedIds.Add(id))
            {
                var rt = def.CreateRuntime();
                if (rt != null)
                {
                    _active.Add(rt);
                    rt.OnEquip(_ctx);
                }
            }

            OnRelicCountChanged?.Invoke(def, newCount);
            return true;
        }

        private bool AllowCollectThisFrame(string id, bool bypassFrameCap)
        {
            if (bypassFrameCap) return true;

            int frame = Time.frameCount;

            if (!_collectFrameById.TryGetValue(id, out int seenFrame) || seenFrame != frame)
            {
                _collectFrameById[id] = frame;
                _collectCountInFrameById[id] = 0;
            }

            int used = _collectCountInFrameById[id];
            if (used >= maxCollectsPerFramePerRelic)
                return false;

            _collectCountInFrameById[id] = used + 1;
            return true;
        }
        private bool AllowConsumeThisFrame(string relicId)
        {
            int frame = Time.frameCount;

            if (!_consumeFrameById.TryGetValue(relicId, out int seenFrame) || seenFrame != frame)
            {
                _consumeFrameById[relicId] = frame;
                _consumeCountInFrameById[relicId] = 0;
            }

            int used = _consumeCountInFrameById[relicId];
            if (used >= maxConsumesPerFramePerRelic)
                return false;

            _consumeCountInFrameById[relicId] = used + 1;
            return true;
        }

        /// <summary>
        /// Consume stack(s) from a relic type. Returns false if not enough count.
        /// </summary>
        public bool TryConsume(RelicDefinition def, int amount = 1)
        {
            if (def == null) return false;
            if (amount <= 0) return false;

            string id = GetId(def);
            if (string.IsNullOrEmpty(id)) return false;

            if (!AllowConsumeThisFrame(id))
                return false;

            if (!_counts.TryGetValue(id, out int current) || current < amount)
                return false;

            int newCount = current - amount;

            if (newCount <= 0)
                _counts.Remove(id);
            else
                _counts[id] = newCount;

            OnRelicCountChanged?.Invoke(def, Mathf.Max(newCount, 0));
            return true;
        }

        public int GetCount(RelicDefinition def)
        {
            string id = GetId(def);
            return _counts.TryGetValue(id, out int c) ? c : 0;
        }

        public int GetCountById(string relicId)
        {
            if (string.IsNullOrEmpty(relicId)) return 0;
            return _counts.TryGetValue(relicId, out int c) ? c : 0;
        }

        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            if (_hub == null) return;
            _hub.OnHit -= ForwardHit;
            _hub.OnKill -= ForwardKill;
            _hub = null;
        }

        private void ForwardHit(HitEvent e)
        {
            for (int i = 0; i < _active.Count; i++)
                _active[i].OnHit(e);
        }

        private void ForwardKill(KillEvent e)
        {
            for (int i = 0; i < _active.Count; i++)
                _active[i].OnKill(e);
        }

        public bool TryConsumeById(string relicId, int amount = 1)
        {
            if (!CanConsumeNow()) return false;

            if (string.IsNullOrEmpty(relicId)) return false;

            if (!AllowConsumeThisFrame(relicId))
                return false;
            if (amount <= 0) return false;


            if (!_counts.TryGetValue(relicId, out int current) || current < amount)
                return false;

            int newCount = current - amount;

            if (newCount <= 0) _counts.Remove(relicId);
            else _counts[relicId] = newCount;

            if (_defsById.TryGetValue(relicId, out var def) && def != null)
                OnRelicCountChanged?.Invoke(def, Mathf.Max(newCount, 0));

            return true;
        }

        private bool CanConsumeNow()
        {
            if (_ctx?.Warrior == null) return true; // or false if you prefer strict
            return !_ctx.Warrior.IsDead && !_ctx.Warrior.CanDie;
        }
        public bool IsOwned(RelicDefinition def)
        {
            string id = GetId(def);
            return !string.IsNullOrEmpty(id) && _ownedIds.Contains(id);
        }

        public bool IsOwnedById(string relicId)
        {
            return !string.IsNullOrEmpty(relicId) && _ownedIds.Contains(relicId);
        }
    }
}
using Assets.Scripts.Characteres.WarriorController;
using Assets.Scripts.Relics.Core;
using Assets.Scripts.Relics.Definitions;
using UnityEngine;

[DisallowMultipleComponent]
public class AutoHealthRelicConsumer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Warrior warrior;
    [SerializeField] private RelicManager relicManager;

    [Header("Relic Definition (SO) - consume this stack")]
    [SerializeField] private HealthRelic healthRelic;

    [Header("Auto Heal Thresholds")]
    [SerializeField] private bool triggerAt75 = true;
    [SerializeField] private bool triggerAt50 = true;
    [SerializeField] private bool triggerAt25 = true;

    [Header("Auto Heal Amount")]
    [Tooltip("If true, use % MaxHP heal. If false, use HealthRelic.healPerKill.")]
    [SerializeField] private bool usePercentHeal = true;

    [Range(0.01f, 1f)]
    [SerializeField] private float healPercent = 0.25f; // 25%

    [Header("Anti-retrigger")]
    [SerializeField, Range(0f, 0.2f)] private float resetBuffer = 0.03f;

    private bool _used75;
    private bool _used50;
    private bool _used25;

    private void Awake()
    {
        if (warrior == null)
            warrior = GetComponent<Warrior>() ?? Warrior.Instance ?? GameMgr.Instance?.WarriorInstance;

        if (relicManager == null)
            relicManager = GetComponent<RelicManager>() ?? warrior?.GetComponent<RelicManager>();

        if (healthRelic == null)
            Debug.LogWarning("[AutoHealthRelicConsumer] HealthRelic SO not assigned.");
    }

    private void LateUpdate()
    {
        // Late bind (safe if Warrior spawns later)
        if (warrior == null)
            warrior = GetComponent<Warrior>() ?? Warrior.Instance ?? GameMgr.Instance?.WarriorInstance;

        if (relicManager == null)
            relicManager = GetComponent<RelicManager>() ?? warrior?.GetComponent<RelicManager>();

        if (warrior == null || relicManager == null || healthRelic == null)
            return;

        if (warrior.MaxHealth <= 0f) return;
        if (warrior.IsDead) return; // you already expose IsDead

        float hpRatio = Mathf.Clamp01(warrior.CurrentHealth / warrior.MaxHealth);

        // Reset flags when healing above threshold + buffer
        if (_used75 && hpRatio > 0.75f + resetBuffer) _used75 = false;
        if (_used50 && hpRatio > 0.50f + resetBuffer) _used50 = false;
        if (_used25 && hpRatio > 0.25f + resetBuffer) _used25 = false;

        // Check lower thresholds first
        if (triggerAt25 && !_used25 && hpRatio <= 0.25f)
        {
            if (TryConsumeAndHeal())
                _used25 = true;
            return;
        }

        if (triggerAt50 && !_used50 && hpRatio <= 0.50f)
        {
            if (TryConsumeAndHeal())
                _used50 = true;
            return;
        }

        if (triggerAt75 && !_used75 && hpRatio <= 0.75f)
        {
            if (TryConsumeAndHeal())
                _used75 = true;
            return;
        }
    }

    private bool TryConsumeAndHeal()
    {
        int countBefore = relicManager.GetCount(healthRelic);

        bool consumed = relicManager.TryConsume(healthRelic, 1); // consume relic_health stack
        if (!consumed)
        {
            // Debug.Log($"[AutoHealthRelicConsumer] No HealthRelic stack to consume (count={countBefore}).");
            return false;
        }

        int healAmount = usePercentHeal
            ? Mathf.Max(1, Mathf.CeilToInt(warrior.MaxHealth * healPercent))
            : Mathf.Max(1, healthRelic.healPerKill);

        warrior.Heal(healAmount);

        Debug.Log($"[AutoHealthRelicConsumer] Consumed HealthRelic. Heal +{healAmount} HP.");
        return true;
    }

    public void ResetThresholdTriggers()
    {
        _used75 = false;
        _used50 = false;
        _used25 = false;
    }
}
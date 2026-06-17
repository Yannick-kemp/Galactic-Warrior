using UnityEngine;

[System.Serializable]
public class ZalaytySpawnOverrides
{
    public bool overrideSqueezeCheckInterval;
    [Min(0.01f)] public float squeezeCheckInterval = 0.12f;

    public bool overrideRepathInterval;
    [Min(0.01f)] public float repathInterval = 0.25f;
}

[System.Serializable]
public class EnemySpawnOverrides
{
    [Header("Common")]
    public bool overrideSpeed;
    public float speed = 2f;

    public bool overrideAttackRange;
    public float attackRange = 3f;

    public bool overrideAttackCooldown;
    public float attackCooldown = 1f;

    public bool overrideAttackDamage;
    public int attackDamage = 10;

    public bool overrideMaxHealth;
    public float maxHealth = 100f;

    // Bee-only: spark hit reaction. Kept flat in Common (per request) so it shows directly under
    // the spawn point; only the Bee's spark (SparkShieldAwareClamp2D) reads these values.
    public bool overrideSparkStun;
    [Min(0f)] public float sparkStunSeconds = 0.10f;

    public bool overrideSparkKnockback;
    [Min(0f)] public float sparkKnockbackVel = 0f;

    [Header("Zalayty")]
    public ZalaytySpawnOverrides zalayty = new ZalaytySpawnOverrides();
}
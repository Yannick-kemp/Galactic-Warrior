using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

public class RakaFlameDamage : MonoBehaviour
{
    public int damage = 8;
    public float tickInterval = 0.4f;

    private float nextTick;

    private int warriorLayer;
    private int shieldLayer;

    [SerializeField] private RakaMonster owner;

    [SerializeField] private GameObject hitVfxPrefab;
    [SerializeField] private float hitVfxDestroyAfter = 0.8f;
    [SerializeField] private Vector3 hitVfxScale = Vector3.one;

    private Collider2D myCol;
    private bool shieldBlocked;

    private void Awake()
    {
        myCol = GetComponent<Collider2D>();
        warriorLayer = LayerMask.NameToLayer("Hit Box");
        shieldLayer = LayerMask.NameToLayer("Shield Laser");

        if (owner == null)
            owner = GetComponentInParent<RakaMonster>();
    }
    private void Start()
    {
        myCol = GetComponent<Collider2D>();
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        Warrior warrior = GameMgr.Instance?.WarriorInstance;
        if (warrior == null) return;

        int otherLayer = other.gameObject.layer;

        // ----------------------------
        // SHIELD BLOCK
        // ----------------------------
        if (otherLayer == shieldLayer)
        {
            if (warrior.ShieldIsUp && !shieldBlocked)
            {
                shieldBlocked = true;

                SpawnHitVfx(other.ClosestPoint(transform.position));

                Destroy(gameObject);
            }

            return;
        }

        // ----------------------------
        // WARRIOR BODY
        // ----------------------------
        if (otherLayer == warriorLayer)
        {
            if (Time.time < nextTick)
                return;

            nextTick = Time.time + tickInterval;

            // Sprint relic = invulnerable
            if (warrior.IsDodging)
                return;

            // Shield active -> absorb instead of damage
            if (warrior.ShieldIsUp)
            {
                warrior.TryAbsorbSqueeze(3f);
                return;
            }

            warrior.TakeDamage(damage);
            Vector2 from = myCol != null ? (Vector2)myCol.bounds.center : (Vector2)transform.position;
            warrior.ApplyHitReaction(HitKind.Flamethrower, from, stunSeconds: 0.15f, knockbackVel: 2.2f);
            if (owner != null)
                warrior.SpawnBloodshedEffectFromEnemy(owner);

            SpawnHitVfx(other.ClosestPoint(transform.position));

            Destroy(gameObject);
        }
    }

    private void SpawnHitVfx(Vector2 pos)
    {
        if (hitVfxPrefab == null) return;

        GameObject fx = Instantiate(
            hitVfxPrefab,
            pos,
            Quaternion.identity);

        fx.transform.localScale = hitVfxScale;

        var systems = fx.GetComponentsInChildren<ParticleSystem>(true);

        foreach (var ps in systems)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        Destroy(fx, hitVfxDestroyAfter);
    }

    public void Initialize(RakaMonster raka)
    {
        owner = raka;
    }
}
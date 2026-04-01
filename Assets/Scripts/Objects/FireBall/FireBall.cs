using Assets.Scripts.Characteres.EnemyContoller;
using Assets.Scripts.Characteres.WarriorController;
using System.Collections;
using UnityEngine;

public class LaunchProjectile : MonoBehaviour

{
    [SerializeField]
    private float centerHitRadius = 0.35f;
    private float speed;

    //[SerializeField] private Vector2 centerBoxSize = new Vector2(0.4f, 0.6f);

    private Enemy owner;
    private int damage;
    private bool alreadyHit;

    private float lifetime; // Lifetime of the projectile
    private float timer; // Tracks time elapsed
    private ParticleSystem sphereParticleSystem;

    private bool deflected = false;
    private Collider2D myCol;
    // Layer IDs cached
    private int warriorLayer;
    private int shieldLayer;

    [SerializeField] private GameObject hitVfxPrefab;          // drag vfx_my_Explosion_01 here
    [SerializeField] private float hitVfxDestroyAfter = 1.2f;
    [SerializeField] private Vector3 hitVfxScale = Vector3.one;

    private void Awake()
    {
        myCol = GetComponent<Collider2D>();

        warriorLayer = LayerMask.NameToLayer("Hit Box");
        shieldLayer = LayerMask.NameToLayer("Shield Laser");

        // Safety: if you typo layer names, Unity returns -1
        if (warriorLayer < 0) Debug.LogError("Layer 'Hit Box' not found!");
        if (shieldLayer < 0) Debug.LogError("Layer 'Shield Laser' not found!");
    }


    private void Start()
    {
        ParticleSystem[] particleSystems = GetComponentsInChildren<ParticleSystem>();
        myCol = GetComponent<Collider2D>();
        // Retrieve the specific one by index (e.g., the 2nd one)
        if (particleSystems.Length > 1) // Ensure index is valid
        {
            sphereParticleSystem = particleSystems[0]; // Replace 1 with the correct index

            // Play the specific particle system
            if (sphereParticleSystem != null)
            {
                sphereParticleSystem.Play();
            }
        }
    }
    public void Initialize(float speed, float lifetime)
    {
        this.speed = speed;
        this.lifetime = lifetime;

        // Optionally, log the initialization
        //  UnityEngine.Debug.Log($"Projectile initialized with Speed: {speed}, Lifetime: {lifetime}");
    }
    // Called from Hashagar right before launching
    public void SetOwnerAndDamage(Enemy owner, int damage)
    {
        this.owner = owner;
        this.damage = damage;
    }

    private bool isInsideWarrior = false;

    private void OnTriggerEnter2D(Collider2D other)
    {
        var warrior = GameMgr.Instance.WarriorInstance;
        if (warrior == null) return;

        int otherLayer = other.gameObject.layer;

        //  1) SHIELD HIT => destroy projectile (and optionally consume durability)
        if (otherLayer == shieldLayer)
        {
            // Only if shield is actually up (recommended)
            if (warrior.ShieldIsUp)
            {
                SpawnHitVfx_OnShield(other);

                Destroy(gameObject);
            }
            else
            {
                // Shield collider got hit but shield isn't active (if that can happen)
                // You can either ignore or treat as body hit. Usually ignore.
            }
            return;
        }
        if (other != warrior.collider2)
            return;
        // 4) Not blocked -> apply damage once
        if (alreadyHit) return;          // <-- add this guard
        alreadyHit = true;
        isInsideWarrior = true;

        warrior.TakeDamage(damage);

        // CALL ONCE: projectile hit reaction (right after damage) 
        //stunSeconds: 0.15f
        Vector2 from = myCol != null ? (Vector2)myCol.bounds.center : (Vector2)transform.position;
        warrior.ApplyHitReaction(HitKind.Projectile, from, stunSeconds: 0.35f, knockbackVel: 5.2f);

        if (owner != null)
            warrior.SpawnBloodshedEffectFromEnemy(owner);

    }


    void Update()
    {
        // Move the projectile forward
        transform.Translate(Vector2.right * speed * Time.deltaTime);

        // AFTER we have hit the warrior, wait until we reach the center
        if (isInsideWarrior)
        {
            var warrior = GameMgr.Instance.WarriorInstance;
            if (warrior != null && warrior.collider2 != null)
            {
                Vector2 warriorCenter = warrior.collider2.bounds.center;

                var myCol = GetComponent<Collider2D>();
                if (myCol == null)
                    return;

                Vector2 fireballCenter = myCol.bounds.center;

                // Auto radius: about 40% of fireball size (tweak if needed)
                float autoRadius = Mathf.Min(myCol.bounds.extents.x, myCol.bounds.extents.y) * 0.4f;

                if (Vector2.Distance(fireballCenter, warriorCenter) <= 0.8f)
                {
                    SpawnHitVfx_At(fireballCenter);
                    Destroy(gameObject);
                    return;
                }
            }
        }

        timer += Time.deltaTime;
        // if (timer >= lifetime)
        if (timer >= 3f)
        {
            Destroy(gameObject);
        }
    }
    private void SpawnHitVfx_OnShield(Collider2D hitCol)
    {
        if (hitVfxPrefab == null || hitCol == null) return;

        // Best-effort impact point for trigger collisions
        Vector2 hitPoint = hitCol.ClosestPoint(transform.position);

        // Optional: rotate effect to face travel direction
        Vector2 dir = (Vector2)transform.right;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);

        GameObject fx = Instantiate(hitVfxPrefab, hitPoint, rot);
        fx.transform.localScale = hitVfxScale;

        // Ensure particle systems actually play (some prefabs are "Play On Awake" off)
        var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        Destroy(fx, hitVfxDestroyAfter);
    }
    private void SpawnHitVfx_At(Vector2 pos)
    {
        if (hitVfxPrefab == null) return;

        Vector3 spawnPos = new Vector3(pos.x, pos.y, transform.position.z); // keep Z
        GameObject fx = Instantiate(hitVfxPrefab, spawnPos, Quaternion.identity);
        fx.transform.localScale = hitVfxScale;

        var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        Destroy(fx, hitVfxDestroyAfter);
    }

    private void OnBecameInvisible()
    {
        if (!isInsideWarrior)
            Destroy(gameObject);
    }
}



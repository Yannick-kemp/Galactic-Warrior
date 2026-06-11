using UnityEngine;

// NOTE: crescentPrefab and riftProjectilePrefab are GameObject fields (the projectile
// behaviour components are added at runtime by ZortBoss.FireVoidProjectile).
namespace Assets.Scripts.Characteres.EnemyContoller
{
    /// <summary>
    /// All tunables, prefab references and audio clips used by <see cref="ZortBoss"/>.
    /// Scene-only references (arenaCenter, platforms, spawnPoints, projectileOrigin)
    /// stay on the boss component itself; everything here is a shared asset.
    /// Create via: Assets > Create > Game > Enemy > Zort Attack Data
    /// </summary>
    [CreateAssetMenu(
        fileName = "ZortAttackData",
        menuName = "Game/Enemy/Zort Attack Data")]
    public class ZortAttackData : ScriptableObject
    {
        // ─── Generic Warrior Hit ──────────────────────────────────────────────────

        [Header("Generic Warrior Hit")]
        [Tooltip("Control-lock applied to the Warrior on any successful Zort hit.")]
        public float warriorHitStun = 0.12f;

        [Tooltip("Knockback velocity applied to the Warrior on any successful Zort hit.")]
        public float warriorHitKnockback = 4f;

        [Tooltip("Layers a projectile treats as solid obstacles (walls / floor).")]
        public LayerMask projectileObstacleMask;

        [Tooltip("Seconds before a projectile self-destructs if it hits nothing.")]
        public float projectileLifetime = 4f;

        // ─── Void Crescent Slash ──────────────────────────────────────────────────

        [Header("Void Crescent Slash")]
        [Tooltip("Projectile prefab. VoidProjectile + Rigidbody2D are added at runtime.")]
        public GameObject crescentPrefab;

        [Tooltip("Blade-glow charge sound played during the telegraph window.")]
        public AudioClip crescentCharge;

        [Tooltip("Fire sound on projectile launch.")]
        public AudioClip crescentFire;

        [Tooltip("Seconds the blade glows cyan before the crescent fires (telegraph window).")]
        public float crescentTelegraphDuration = 1.2f;

        [Tooltip("Travel speed of the crescent projectile.")]
        public float crescentSpeed = 10f;

        [Tooltip("Delay between the high and low crescent in Twin Crescent (Phase 2+).")]
        public float twinCrescentDelay = 0.35f;

        [Tooltip("Damage dealt by each crescent projectile.")]
        public int crescentDamage = 12;

        // ─── Shadow Step Blitz ────────────────────────────────────────────────────

        [Header("Shadow Step Blitz")]
        [Tooltip("Purple particle burst instantiated at the teleport destination.")]
        public GameObject teleportVfxPrefab;

        [Tooltip("Sound played as Zort dissolves out.")]
        public AudioClip teleportOut;

        [Tooltip("Sound played as Zort reappears.")]
        public AudioClip teleportIn;

        [Tooltip("Impact sound for each melee strike.")]
        public AudioClip meleeHit;

        [Tooltip("Damage per Shadow Step strike.")]
        public int shadowStepDamage = 14;

        // ─── Spectral Summon ──────────────────────────────────────────────────────

        [Header("Spectral Summon")]
        [Tooltip("Prefab with VoidWraith component + standard Enemy setup.")]
        public GameObject wraithPrefab;

        [Tooltip("Small HP orb dropped at the wraith's death position.")]
        public GameObject wraithHealthPickupPrefab;

        [Tooltip("Charge sound played while Zort floats up and spreads his arms.")]
        public AudioClip summonCharge;

        // ─── Rift Barrage ─────────────────────────────────────────────────────────

        [Header("Rift Barrage")]
        [Tooltip("Projectile prefab for each rift shot. VoidProjectile added at runtime.")]
        public GameObject riftProjectilePrefab;

        [Tooltip("Charge sound played during the tilt-back telegraph.")]
        public AudioClip barrageCharge;

        [Tooltip("Seconds of tilt-back telegraph before the barrage fires.")]
        public float barrageTelegraphDuration = 0.8f;

        [Tooltip("Travel speed of each rift projectile.")]
        public float barrageProjectileSpeed = 9f;

        [Tooltip("Damage per rift projectile.")]
        public int riftDamage = 10;

        // ─── Earth Slash (close range) ────────────────────────────────────────────

        [Header("Earth Slash (close range)")]
        [Tooltip("Pure-VFX slash prefab (no scripts). Assign Slash_Earth_VFX here.")]
        public GameObject slashEarthVfxPrefab;

        [Tooltip("Horizontal sprint speed while charging toward the Warrior.")]
        public float earthSlashDashSpeed = 8f;

        [Tooltip("Maximum horizontal distance Zort will dash before committing to the slash.")]
        public float earthSlashMaxDashDistance = 4f;

        [Tooltip("Warrior must be within this distance when the slash lands to take damage.")]
        public float earthSlashContactRange = 1.5f;

        // ─── Gravity Well (Phase 2+) ──────────────────────────────────────────────

        [Header("Gravity Well (Phase 2+)")]
        [Tooltip("VFX instantiated at arena center during the pull. Destroyed when well ends.")]
        public GameObject gravityWellFxPrefab;

        [Tooltip("Sound on gravity well activation.")]
        public AudioClip gravityWellStart;

        [Tooltip("Sound when the gravity well ends or is cancelled.")]
        public AudioClip gravityWellEnd;

        [Tooltip("How many seconds the gravity pull lasts if not cancelled.")]
        public float gravityWellDuration = 5f;

        [Tooltip("Force magnitude added to the Warrior's Rigidbody2D each frame toward the center.")]
        public float gravityPullForce = 18f;

        [Tooltip("Fraction of Zort's max HP the Warrior must deal during the well to cancel it early (0.08 = 8%).")]
        [Range(0f, 1f)]
        public float gravityWellCancelPercent = 0.08f;

        // ─── Aerial Dive Slash (Phase 2+) ─────────────────────────────────────────

        [Header("Aerial Dive Slash (Phase 2+)")]
        [Tooltip("Shockwave VFX instantiated on landing.")]
        public GameObject diveShockwavePrefab;

        [Tooltip("Ascent sound as Zort flies off-screen.")]
        public AudioClip diveAscend;

        [Tooltip("Impact sound on landing.")]
        public AudioClip diveLand;

        [Tooltip("Damage dealt if the Warrior is not jumping when Zort lands.")]
        public int diveDamage = 18;

        [Tooltip("Radius around the landing point within which the shockwave hits.")]
        public float diveShockwaveRadius = 2.6f;

        // ─── Void Stare (Phase 3) ─────────────────────────────────────────────────

        [Header("Void Stare (Phase 3)")]
        [Tooltip("Prefab with a LineRenderer. VoidBeam is added and configured at runtime.")]
        public GameObject voidBeamPrefab;

        [Tooltip("1-second charge-up sound before the beam fires.")]
        public AudioClip stareCharge;

        [Tooltip("Damage per tick delivered to the Warrior while inside the beam.")]
        public int beamDamage = 12;

        [Tooltip("Seconds between each beam damage tick.")]
        public float beamTickInterval = 0.4f;

        [Tooltip("Total duration of the beam before it ends (or until lock-on breaks).")]
        public float beamDuration = 5f;

        [Tooltip("Maximum beam length in world units before it stops.")]
        public float beamMaxLength = 12f;

        [Tooltip("How fast the beam re-aims at the Warrior (degrees/second). Lower = easier to dash behind.")]
        public float beamTurnSpeed = 110f;

        [Tooltip("Beam width when Zort is near full Phase 3 HP (far from desperation).")]
        public float beamWidthFar = 0.45f;

        [Tooltip("Beam width when Zort is near 0 HP (full desperation).")]
        public float beamWidthNear = 0.9f;

        // ─── Desperation Nova (below 10% HP) ──────────────────────────────────────

        [Header("Desperation Nova (below 10% HP)")]
        [Tooltip("Expanding ring VFX instantiated at arena center. Visual only — damage is applied in code.")]
        public GameObject novaPrefab;

        [Tooltip("Wind-up charge sound.")]
        public AudioClip novaCharge;

        [Tooltip("Fire sound on nova release.")]
        public AudioClip novaFire;

        [Tooltip("Duration of the charge animation before the nova fires.")]
        public float novaChargeDuration = 1.5f;

        [Tooltip("Radius within which the nova hits a non-jumping Warrior.")]
        public float novaRadius = 8f;

        [Tooltip("Damage dealt by the nova if the Warrior is not jumping.")]
        public int novaDamage = 20;

        // ─── Transitions / Death ──────────────────────────────────────────────────

        [Header("Transitions / Death")]
        [Tooltip("Flash VFX instantiated at Zort's position on each phase transition.")]
        public GameObject transitionNovaPrefab;

        [Tooltip("Generic hit-spark prefab used as projectile impact FX.")]
        public GameObject genericImpactFxPrefab;

        [Tooltip("Sound played at the start of each phase transition.")]
        public AudioClip phaseTransition;

        [Tooltip("Sound played when Zort's death sequence begins.")]
        public AudioClip deathSound;
    }
}

using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public sealed class MeteorRainCollisionRouter : MonoBehaviour
{
    [Header("Meteor")]
    [SerializeField] private float meteorDamage = 8f;
    [SerializeField] private float meteorHitStunSeconds = 0.08f;
    [SerializeField] private float meteorShieldBlockCost = 5f;

    private int _platformLayer;

    private void Awake()
    {
        _platformLayer = LayerMask.NameToLayer("PlatformLayer");
        Debug.Log($"[MeteorRouter Awake] platform={_platformLayer}");
    }

    private void OnParticleCollision(GameObject other)
    {
        if (other == null) return;

        int otherLayer = other.layer;
        Debug.Log(
            $"[MeteorRouter] collision with {other.name} | " +
            $"layerIndex={otherLayer} | layerName={LayerMask.LayerToName(otherLayer)}"
        );

        if (otherLayer == _platformLayer)
            return;

        Warrior warrior = other.GetComponentInParent<Warrior>();
        if (warrior == null)
        {
            Debug.Log("[MeteorRouter] no Warrior in parent");
            return;
        }

        // Shield collider is enabled only while shield is truly active.
        if (warrior.MeteorShieldIsActive)
        {
            Debug.Log("[MeteorRouter] SHIELD BLOCK");
            warrior.TryBlockMeteorHit(meteorShieldBlockCost);
            return;
        }

        Debug.Log("[MeteorRouter] BODY HIT");
        warrior.TryTakeMeteorHit(
            meteorDamage,
            transform.position,
            meteorHitStunSeconds
        );
    }
}
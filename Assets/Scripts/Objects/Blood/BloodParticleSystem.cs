using UnityEngine;

/// <summary>
/// Controls blood particle effects for 2D combat system
/// Attach this to a GameObject with a ParticleSystem component
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class BloodParticleSystem : MonoBehaviour
{
    [Header("Particle System References")]
    [SerializeField] public ParticleSystem bloodParticles;

    [Header("Blood Effect Settings")]
    [SerializeField] public int particleCount = 20;
    [SerializeField] public float spreadAngle = 45f;
    [SerializeField] public Vector2 velocityRange = new Vector2(2f, 5f);
    [SerializeField] public bool useRandomDirection = false;

    [Header("Color Settings")]
    [SerializeField] public Color bloodColor = new Color(0.6f, 0.0f, 0.0f, 1.0f);
    [SerializeField] public Color fadeColor = new Color(0.3f, 0.0f, 0.0f, 0.5f);
    [SerializeField] public bool useColorOverLifetime = true;

    [Header("Audio (Optional)")]
    [SerializeField] public AudioClip bloodSplatSound;
    [SerializeField] public AudioSource audioSource;
    [SerializeField] public float audioVolume = 0.5f;

    private void Awake()
    {
        if (bloodParticles == null)
        {
            bloodParticles = GetComponent<ParticleSystem>();
        }

        InitializeParticleSystem();
    }

    private void InitializeParticleSystem()
    {
        if (bloodParticles == null) return;

        var main = bloodParticles.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(velocityRange.x, velocityRange.y);
        main.startColor = bloodColor;
        main.maxParticles = 100;
        main.playOnAwake = false;

        // Color over lifetime for fade effect
        if (useColorOverLifetime)
        {
            var colorOverLifetime = bloodParticles.colorOverLifetime;
            colorOverLifetime.enabled = true;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(bloodColor, 0.0f),
                    new GradientColorKey(fadeColor, 1.0f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(0.0f, 1.0f)
                }
            );
            colorOverLifetime.color = gradient;
        }
    }

    /// <summary>
    /// Triggers blood splatter effect at a specific position and direction
    /// </summary>
    /// <param name="position">World position for the effect</param>
    /// <param name="direction">Direction of blood spray (2D vector)</param>
    public void PlayBloodEffect(Vector3 position, Vector2 direction)
    {
        transform.position = position;

        if (!useRandomDirection)
        {
            // Calculate angle from direction
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);

            // Set emission shape to cone for directional spray
            var shape = bloodParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spreadAngle;
            shape.rotation = new Vector3(-90, 0, 0); // Point forward in 2D
        }
        else
        {
            // Random spray in all directions
            var shape = bloodParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
        }

        // Emit particles
        bloodParticles.Emit(particleCount);

        // Play sound if available
        if (bloodSplatSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(bloodSplatSound, audioVolume);
        }
    }

    /// <summary>
    /// Simple blood effect without specific direction
    /// </summary>
    /// <param name="position">World position for the effect</param>
    public void PlayBloodEffect(Vector3 position)
    {
        PlayBloodEffect(position, Vector2.right);
    }

    /// <summary>
    /// Create a blood trail effect (for continuous damage)
    /// </summary>
    public void StartBloodTrail()
    {
        if (!bloodParticles.isPlaying)
        {
            bloodParticles.Play();
        }
    }

    /// <summary>
    /// Stop continuous blood effect
    /// </summary>
    public void StopBloodTrail()
    {
        if (bloodParticles.isPlaying)
        {
            bloodParticles.Stop();
        }
    }

    /// <summary>
    /// Sets the intensity of the blood effect
    /// </summary>
    /// <param name="intensity">0.0 to 1.0 multiplier</param>
    public void SetIntensity(float intensity)
    {
        intensity = Mathf.Clamp01(intensity);

        var emission = bloodParticles.emission;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(10f * intensity, 30f * intensity);

        particleCount = Mathf.RoundToInt(20 * intensity);
    }
}

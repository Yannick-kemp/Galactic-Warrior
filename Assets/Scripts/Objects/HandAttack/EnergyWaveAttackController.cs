using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the Energy Wave Attack visual effect
/// Animates shader properties and triggers particle systems
/// </summary>
public class EnergyWaveAttackController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private ParticleSystem mainParticles;
    [SerializeField] private ParticleSystem trailParticles;
    [SerializeField] private AudioSource attackSound;

    [Header("Attack Settings")]
    [SerializeField] private float attackDuration = 0.8f;
    [SerializeField] private float chargeUpTime = 0.2f;
    [SerializeField] private AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0.5f, 1, 1.2f);

    [Header("Shader Properties")]
    [SerializeField] private float maxGlowIntensity = 10f;
    [SerializeField] private float maxDistortion = 0.5f;
    [SerializeField] private float maxWaveSpeed = 2f;

    [Header("Screen Shake")]
    [SerializeField] private bool enableScreenShake = true;
    [SerializeField] private float shakeIntensity = 0.1f;
    [SerializeField] private float shakeDuration = 0.3f;

    [Header("Damage Settings")]
    [SerializeField] private float damageAmount = 25f;
    [SerializeField] private LayerMask damageLayer;
    [SerializeField] private float damageRadius = 2f;

    private Material waveMaterial;
    private Vector3 originalScale;
    private bool isAttacking = false;

    // Shader property IDs (cached for performance)
    private static readonly int GlowIntensityID = Shader.PropertyToID("_GlowIntensity");
    private static readonly int DistortionStrengthID = Shader.PropertyToID("_DistortionStrength");
    private static readonly int WaveSpeedID = Shader.PropertyToID("_WaveSpeed");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int PulseSpeedID = Shader.PropertyToID("_PulseSpeed");

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        // Create instance of material to avoid modifying the shared material
        if (spriteRenderer != null)
        {
            waveMaterial = spriteRenderer.material;
            spriteRenderer.material = waveMaterial;
        }

        originalScale = transform.localScale;

        // Hide initially
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
    }

    /// <summary>
    /// Triggers the energy wave attack
    /// </summary>
    public void TriggerAttack()
    {
        if (isAttacking) return;

        StartCoroutine(AttackSequence());
    }

    /// <summary>
    /// Triggers attack towards a specific direction
    /// </summary>
    public void TriggerAttackTowards(Vector2 direction)
    {
        if (isAttacking) return;

        // Rotate towards direction
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);

        StartCoroutine(AttackSequence());
    }

    private IEnumerator AttackSequence()
    {
        isAttacking = true;

        // Show sprite
        if (spriteRenderer != null)
            spriteRenderer.enabled = true;

        // Reset properties
        ResetShaderProperties();

        // Charge up phase
        yield return StartCoroutine(ChargeUp());

        // Main attack phase
        yield return StartCoroutine(ExecuteAttack());

        // Fade out phase
        yield return StartCoroutine(FadeOut());

        // Hide sprite
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;

        isAttacking = false;
    }

    private IEnumerator ChargeUp()
    {
        float elapsed = 0f;

        while (elapsed < chargeUpTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / chargeUpTime;

            // Gradual glow increase
            waveMaterial.SetFloat(GlowIntensityID, Mathf.Lerp(0, maxGlowIntensity * 0.3f, t));
            waveMaterial.SetFloat(PulseSpeedID, Mathf.Lerp(5f, 2f, t));

            // Scale pulse
            transform.localScale = originalScale * Mathf.Lerp(0.5f, 0.8f, t);

            yield return null;
        }

        // Play sound at peak of charge
        if (attackSound != null)
            attackSound.Play();
    }

    private IEnumerator ExecuteAttack()
    {
        // Trigger particles
        if (mainParticles != null)
            mainParticles.Play();

        if (trailParticles != null)
            trailParticles.Play();

        // Screen shake
        if (enableScreenShake)
            StartCoroutine(ScreenShake());

        float elapsed = 0f;

        while (elapsed < attackDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / attackDuration;

            // Animate intensity
            float intensity = intensityCurve.Evaluate(t);
            waveMaterial.SetFloat(GlowIntensityID, intensity * maxGlowIntensity);

            // Animate distortion
            waveMaterial.SetFloat(DistortionStrengthID, intensity * maxDistortion);

            // Animate wave speed
            waveMaterial.SetFloat(WaveSpeedID, Mathf.Lerp(0.5f, maxWaveSpeed, intensity));

            // Scale animation
            float scale = scaleCurve.Evaluate(t);
            transform.localScale = originalScale * scale;

            // Color shift for impact moment (at peak)
            if (t > 0.4f && t < 0.6f)
            {
                float flashIntensity = Mathf.Sin((t - 0.4f) * Mathf.PI / 0.2f);
                Color brightColor = Color.white;
                waveMaterial.SetColor(BaseColorID, Color.Lerp(
                    new Color(0.11f, 0.83f, 1f, 1f),
                    brightColor,
                    flashIntensity * 0.5f
                ));
            }

            yield return null;
        }

        // Deal damage at peak
        DealDamage();
    }

    private IEnumerator FadeOut()
    {
        float fadeTime = 0.3f;
        float elapsed = 0f;

        Color originalColor = waveMaterial.GetColor(BaseColorID);

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeTime;

            // Fade glow
            waveMaterial.SetFloat(GlowIntensityID, Mathf.Lerp(maxGlowIntensity, 0, t));

            // Fade alpha
            Color fadeColor = originalColor;
            fadeColor.a = Mathf.Lerp(1f, 0f, t);
            waveMaterial.SetColor(BaseColorID, fadeColor);

            // Shrink
            transform.localScale = originalScale * Mathf.Lerp(1.2f, 0.5f, t);

            yield return null;
        }

        // Reset color
        waveMaterial.SetColor(BaseColorID, originalColor);
        transform.localScale = originalScale;
    }

    private IEnumerator ScreenShake()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) yield break;

        Vector3 originalPos = mainCam.transform.position;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
            float strength = shakeIntensity * (1f - elapsed / shakeDuration);

            float x = Random.Range(-1f, 1f) * strength;
            float y = Random.Range(-1f, 1f) * strength;

            mainCam.transform.position = originalPos + new Vector3(x, y, 0);

            yield return null;
        }

        mainCam.transform.position = originalPos;
    }

    private void DealDamage()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, damageRadius, damageLayer);

        foreach (Collider2D hit in hits)
        {
            // Try to find a health component
            var health = hit.GetComponent<IHealthSystem>();
            if (health != null)
            {
                health.TakeDamage(damageAmount);
            }

            // Alternative: Use SendMessage for flexibility
            hit.SendMessage("TakeDamage", damageAmount, SendMessageOptions.DontRequireReceiver);

            // Add knockback effect
            Rigidbody2D rb = hit.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                Vector2 knockbackDir = (hit.transform.position - transform.position).normalized;
                rb.AddForce(knockbackDir * 500f);
            }
        }
    }

    private void ResetShaderProperties()
    {
        if (waveMaterial == null) return;

        waveMaterial.SetFloat(GlowIntensityID, 0f);
        waveMaterial.SetFloat(DistortionStrengthID, 0f);
        waveMaterial.SetFloat(WaveSpeedID, 0.5f);
        waveMaterial.SetFloat(PulseSpeedID, 2f);
        waveMaterial.SetColor(BaseColorID, new Color(0.11f, 0.83f, 1f, 1f));
    }

    private void OnDrawGizmosSelected()
    {
        // Visualize damage radius
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, damageRadius);
    }

    /// <summary>
    /// Public method to set attack direction without triggering
    /// </summary>
    public void SetDirection(Vector2 direction)
    {
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }
}

/// <summary>
/// Interface for health systems - implement this on your enemy/player health scripts
/// </summary>
public interface IHealthSystem
{
    void TakeDamage(float damage);
}

using UnityEngine;

/// <summary>
/// Helper script to configure particle systems for Energy Wave Attack
/// Attach to GameObject with Particle System component
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class EnergyWaveParticleSetup : MonoBehaviour
{
    [Header("Particle Type")]
    [SerializeField] private ParticleType particleType = ParticleType.MainWave;

    [Header("Material")]
    [SerializeField] private Material particleMaterial;

    private ParticleSystem ps;

    public enum ParticleType
    {
        MainWave,
        TrailParticles,
        ImpactBurst,
        AmbientGlow
    }

    private void Start()
    {
        ps = GetComponent<ParticleSystem>();
        ConfigureParticleSystem();
    }

    [ContextMenu("Configure Particle System")]
    public void ConfigureParticleSystem()
    {
        if (ps == null)
            ps = GetComponent<ParticleSystem>();

        switch (particleType)
        {
            case ParticleType.MainWave:
                ConfigureMainWave();
                break;
            case ParticleType.TrailParticles:
                ConfigureTrailParticles();
                break;
            case ParticleType.ImpactBurst:
                ConfigureImpactBurst();
                break;
            case ParticleType.AmbientGlow:
                ConfigureAmbientGlow();
                break;
        }

        // Apply material if provided
        if (particleMaterial != null)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = particleMaterial;
        }
    }

    private void ConfigureMainWave()
    {
        // Main module
        var main = ps.main;
        main.startLifetime = 0.5f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.11f, 0.83f, 1f, 1f),
            new Color(0.49f, 1f, 1f, 1f)
        );
        main.maxParticles = 100;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Emission
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, 20, 30, 1, 0.1f)
        });

        // Shape
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.radius = 0.1f;
        shape.radiusThickness = 0f;

        // Velocity over lifetime
        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(0f);
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(0f);
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0f);

        // Color over lifetime
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.11f, 0.83f, 1f), 0.5f),
                new GradientColorKey(new Color(0.49f, 1f, 1f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.8f, 0.5f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        // Size over lifetime
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.5f);
        sizeCurve.AddKey(0.3f, 1.2f);
        sizeCurve.AddKey(1f, 0.2f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Rotation over lifetime
        var rotationOverLifetime = ps.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-180f, 180f);

        // Renderer
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = 10;
        renderer.sortingLayerName = "Effects";
    }

    private void ConfigureTrailParticles()
    {
        // Main module
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.49f, 1f, 1f, 0.8f)
        );
        main.maxParticles = 200;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Emission
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 50f;

        // Shape
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.2f;

        // Color over lifetime
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.49f, 1f, 1f), 0.0f),
                new GradientColorKey(new Color(0.11f, 0.83f, 1f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.8f, 0.0f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        // Size over lifetime
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 1f);
        sizeCurve.AddKey(1f, 0.2f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Trails
        var trails = ps.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.Ribbon;
        trails.ratio = 1f;
        trails.lifetime = 0.3f;
        trails.minVertexDistance = 0.1f;
        trails.worldSpace = false;
        trails.dieWithParticles = true;

        Gradient trailGradient = new Gradient();
        trailGradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.49f, 1f, 1f), 0.0f),
                new GradientColorKey(new Color(0.11f, 0.83f, 1f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(trailGradient);

        // Renderer
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.sortingOrder = 9;
        renderer.sortingLayerName = "Effects";
    }

    private void ConfigureImpactBurst()
    {
        // Main module
        var main = ps.main;
        main.startLifetime = 0.4f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
        main.maxParticles = 50;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Emission
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, 30, 40, 1, 0f)
        });

        // Shape
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;
        shape.radiusThickness = 1f;

        // Color over lifetime
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.49f, 1f, 1f), 0.5f),
                new GradientColorKey(new Color(0.11f, 0.83f, 1f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        // Size over lifetime
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.5f);
        sizeCurve.AddKey(0.2f, 1.5f);
        sizeCurve.AddKey(1f, 0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Velocity over lifetime (gravity effect)
        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
        AnimationCurve velocityCurve = AnimationCurve.Linear(0f, 0f, 1f, -2f);
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(1f, velocityCurve);

        // Renderer
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = 11;
        renderer.sortingLayerName = "Effects";
    }

    private void ConfigureAmbientGlow()
    {
        // Main module
        var main = ps.main;
        main.startLifetime = 1f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.11f, 0.83f, 1f, 0.3f)
        );
        main.maxParticles = 10;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        // Emission
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 5f;

        // Shape
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1f;

        // Color over lifetime
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.11f, 0.83f, 1f), 0.0f),
                new GradientColorKey(new Color(0.49f, 1f, 1f), 0.5f),
                new GradientColorKey(new Color(0.11f, 0.83f, 1f), 1.0f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.3f, 0.5f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        // Size over lifetime
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.5f);
        sizeCurve.AddKey(0.5f, 1.2f);
        sizeCurve.AddKey(1f, 0.5f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Rotation over lifetime
        var rotationOverLifetime = ps.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-45f, 45f);

        // Renderer
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = 8;
        renderer.sortingLayerName = "Effects";
    }
}

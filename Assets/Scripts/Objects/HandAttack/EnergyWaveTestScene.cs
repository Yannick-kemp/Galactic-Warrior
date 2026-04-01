using UnityEngine;

/// <summary>
/// Example setup script for testing the Energy Wave Attack
/// Attach to an empty GameObject in your scene
/// This script will create a test character and let you trigger the attack with spacebar
/// </summary>
public class EnergyWaveTestScene : MonoBehaviour
{
    [Header("Prefab References (Optional - will create if null)")]
    [SerializeField] private GameObject energyWavePrefab;
    [SerializeField] private Material energyWaveMaterial;

    [Header("Test Settings")]
    [SerializeField] private KeyCode attackKey = KeyCode.Space;
    [SerializeField] private bool useMouseDirection = true;
    [SerializeField] private float attackCooldown = 1f;

    [Header("Auto-Generated References")]
    [SerializeField] private GameObject testCharacter;
    [SerializeField] private EnergyWaveAttackController energyWaveController;
    [SerializeField] private Camera mainCamera;

    private float lastAttackTime;
    private SpriteRenderer characterRenderer;

#if UNITY_EDITOR
    [ContextMenu("Create Test Setup")]
    private void CreateTestSetup()
    {
        // Create camera if needed
        if (Camera.main == null)
        {
            GameObject camObj = new GameObject("Main Camera");
            mainCamera = camObj.AddComponent<Camera>();
            mainCamera.tag = "MainCamera";
            mainCamera.orthographic = true;
            mainCamera.orthographicSize = 5;
            mainCamera.backgroundColor = new Color(0.1f, 0.1f, 0.15f);
            camObj.AddComponent<AudioListener>();
        }
        else
        {
            mainCamera = Camera.main;
        }

        // Create test character
        CreateTestCharacter();

        // Create energy wave
        CreateEnergyWave();

        Debug.Log("✓ Test scene setup complete! Press Space to attack.");
    }

    private void CreateTestCharacter()
    {
        // Create character GameObject
        testCharacter = new GameObject("TestCharacter");
        testCharacter.transform.position = Vector3.zero;

        // Add sprite renderer
        characterRenderer = testCharacter.AddComponent<SpriteRenderer>();
        characterRenderer.sprite = CreateSquareSprite();
        characterRenderer.color = new Color(0.8f, 0.8f, 0.8f);
        characterRenderer.sortingLayerName = "Default";
        characterRenderer.sortingOrder = 0;

        // Add circle collider for visual reference
        CircleCollider2D collider = testCharacter.AddComponent<CircleCollider2D>();
        collider.radius = 0.5f;

        // Scale character
        testCharacter.transform.localScale = Vector3.one * 2f;

        Debug.Log("✓ Test character created at origin");
    }

    private void CreateEnergyWave()
    {
        // Create energy wave GameObject
        GameObject waveObj = new GameObject("EnergyWaveAttack");
        waveObj.transform.SetParent(testCharacter.transform);
        waveObj.transform.localPosition = Vector3.right * 1f; // Offset from character
        waveObj.transform.localScale = Vector3.one * 2f;

        // Add sprite renderer
        SpriteRenderer waveRenderer = waveObj.AddComponent<SpriteRenderer>();
        waveRenderer.sprite = CreateSquareSprite();
        waveRenderer.color = Color.white;
        waveRenderer.sortingLayerName = "Default";
        waveRenderer.sortingOrder = 10;

        // Create or assign material
        if (energyWaveMaterial == null)
        {
            // Try to find the material
            energyWaveMaterial = Resources.Load<Material>("Mat_EnergyWave");

            if (energyWaveMaterial == null)
            {
                Debug.LogWarning("⚠ Material 'Mat_EnergyWave' not found. Please assign manually or create it from the shader.");
                // Create a simple material as fallback
                energyWaveMaterial = new Material(Shader.Find("Sprites/Default"));
                energyWaveMaterial.color = new Color(0.11f, 0.83f, 1f);
            }
        }

        waveRenderer.material = energyWaveMaterial;

        // Add controller script
        energyWaveController = waveObj.AddComponent<EnergyWaveAttackController>();

        // Configure controller
        ConfigureController();

        // Add main particle system
        ParticleSystem mainPS = waveObj.AddComponent<ParticleSystem>();
        ConfigureMainParticles(mainPS);

        // Add particle setup script
        EnergyWaveParticleSetup particleSetup = waveObj.AddComponent<EnergyWaveParticleSetup>();
        particleSetup.enabled = false; // Disable to prevent auto-config

        // Create trail particles child
        GameObject trailObj = new GameObject("TrailParticles");
        trailObj.transform.SetParent(waveObj.transform);
        trailObj.transform.localPosition = Vector3.zero;

        ParticleSystem trailPS = trailObj.AddComponent<ParticleSystem>();
        ConfigureTrailParticles(trailPS);

        // Hide wave initially
        waveRenderer.enabled = false;

        Debug.Log("✓ Energy wave attack created and configured");
    }

    private void ConfigureController()
    {
        // Use reflection to set private fields if needed, or just configure public ones
        // For now, we'll just set what we can

        Debug.Log("✓ Controller configured with default settings");
    }

    private void ConfigureMainParticles(ParticleSystem ps)
    {
        var main = ps.main;
        main.startLifetime = 0.5f;
        main.startSpeed = 6f;
        main.startSize = 0.3f;
        main.startColor = new Color(0.11f, 0.83f, 1f);
        main.maxParticles = 100;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0f, 20)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        ps.Stop(); // Don't play on start
    }

    private void ConfigureTrailParticles(ParticleSystem ps)
    {
        var main = ps.main;
        main.startLifetime = 0.3f;
        main.startSpeed = 3f;
        main.startSize = 0.1f;
        main.startColor = new Color(0.49f, 1f, 1f, 0.8f);
        main.maxParticles = 150;

        var emission = ps.emission;
        emission.rateOverTime = 50f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;

        ps.Stop(); // Don't play on start
    }

    private Sprite CreateSquareSprite()
    {
        // Try to get Unity's built-in square sprite
        Sprite squareSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");

        if (squareSprite == null)
        {
            // Fallback: create a simple white texture
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            squareSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        }

        return squareSprite;
    }
#endif

    private void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (energyWaveController == null)
        {
            Debug.LogError("Energy Wave Controller not found! Use 'Create Test Setup' from the context menu.");
        }

        Debug.Log($"Energy Wave Test Scene loaded. Press {attackKey} to attack!");
    }

    private void Update()
    {
        if (energyWaveController == null) return;

        // Check for attack input
        if (Input.GetKeyDown(attackKey) && CanAttack())
        {
            PerformAttack();
        }

        // Visual feedback - show direction indicator
        if (useMouseDirection && mainCamera != null)
        {
            Vector2 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector2 direction = (mousePos - (Vector2)testCharacter.transform.position).normalized;

            // Rotate character sprite to face direction
            if (characterRenderer != null)
            {
                characterRenderer.flipX = direction.x < 0;
            }
        }
    }

    private bool CanAttack()
    {
        return Time.time >= lastAttackTime + attackCooldown;
    }

    private void PerformAttack()
    {
        lastAttackTime = Time.time;

        Vector2 attackDirection;

        if (useMouseDirection && mainCamera != null)
        {
            // Attack towards mouse
            Vector2 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            attackDirection = (mousePos - (Vector2)testCharacter.transform.position).normalized;
        }
        else
        {
            // Attack to the right
            attackDirection = Vector2.right;
        }

        // Trigger the attack
        energyWaveController.TriggerAttackTowards(attackDirection);

        Debug.Log($"Attack triggered in direction: {attackDirection}");
    }

    private void OnDrawGizmos()
    {
        if (testCharacter == null || mainCamera == null) return;

        // Draw attack direction line
        if (useMouseDirection)
        {
            Vector2 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector2 direction = (mousePos - (Vector2)testCharacter.transform.position).normalized;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(testCharacter.transform.position,
                          testCharacter.transform.position + (Vector3)(direction * 3f));

            Gizmos.DrawWireSphere(mousePos, 0.2f);
        }
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(EnergyWaveTestScene))]
public class EnergyWaveTestSceneEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        UnityEditor.EditorGUILayout.Space(10);

        EnergyWaveTestScene script = (EnergyWaveTestScene)target;

        // Add helpful buttons
        if (GUILayout.Button("Create Test Setup", GUILayout.Height(30)))
        {
            script.SendMessage("CreateTestSetup");
        }

        UnityEditor.EditorGUILayout.HelpBox(
            "Click 'Create Test Setup' to automatically create:\n" +
            "• Test Character\n" +
            "• Energy Wave Attack\n" +
            "• Camera (if needed)\n\n" +
            "Then press Play and hit SPACE to test!",
            UnityEditor.MessageType.Info
        );

        UnityEditor.EditorGUILayout.Space(5);

        if (GUILayout.Button("Open Setup Guide"))
        {
            Application.OpenURL("https://docs.unity3d.com/Manual/Sprites.html");
        }
    }
}
#endif

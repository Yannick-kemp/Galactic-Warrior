using UnityEngine;

public class CharacterEchoGenerator : MonoBehaviour
{
    [Header("Echo Settings")]
    [SerializeField] private GameObject WarriorEchoPrefab;
    [SerializeField] private float echoSpawnInterval = 0.1f;
    [SerializeField] private float echoLifetime = 0.5f;
    [SerializeField] private float echoFadeDuration = 0.5f;
    [SerializeField] private Color echoColor = new Color(1f, 1f, 1f, 0.5f);

    [Header("Activation Conditions")]
    [SerializeField] private bool isEchoActive = true;
    [SerializeField] private bool onlyWhenMoving = false;
    [SerializeField] private float minimumSpeed = 0.1f;

    private float echoSpawnTimer;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        if (!isEchoActive) return;

        // Check if we should spawn echo based on movement
        if (onlyWhenMoving && rb != null)
        {
            if (rb.linearVelocity.magnitude < minimumSpeed)
                return;
        }

        echoSpawnTimer += Time.deltaTime;

        if (echoSpawnTimer >= echoSpawnInterval)
        {
            SpawnEcho();
            echoSpawnTimer = 0f;
        }
    }

    private void SpawnEcho()
    {
        // OLD CODE (without pooling):
        // GameObject echo = Instantiate(echoPrefab, transform.position, Quaternion.identity);

        // NEW CODE (with pooling):
        GameObject echo = EchoPool.Instance.GetEcho();
        echo.transform.position = transform.position;
        echo.transform.rotation = Quaternion.identity;

        WarriorEcho echoScript = echo.GetComponent<WarriorEcho>();
        if (echoScript != null)
        {
            echoScript.Initialize(
                spriteRenderer.sprite,
                transform.position,
                transform.localScale,
                spriteRenderer.flipX,
                echoLifetime,
                echoColor,
                echoFadeDuration
            );
        }
    }

    public void SetEchoActive(bool active)
    {
        isEchoActive = active;
    }

    public void SetEchoColor(Color color)
    {
        echoColor = color;
    }
}
using UnityEngine;

public class WarriorEcho : MonoBehaviour
{
    private float timeToDestroy;
    private Color startColor;
    private Color endColor;
    private float fadeDuration;
    private float fadeTimer;
    private float lifetime;
    private float lifeTimer;

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void Initialize(Sprite sprite, Vector3 position, Vector3 scale,
                          bool flipX, float lifetimeValue, Color color, float fadeTime)
    {
        spriteRenderer.sprite = sprite;
        transform.position = position;
        transform.localScale = scale;
        spriteRenderer.flipX = flipX;

        lifetime = lifetimeValue;
        lifeTimer = 0f;
        fadeDuration = fadeTime;
        fadeTimer = 0f;

        startColor = color;
        endColor = new Color(color.r, color.g, color.b, 0f);

        spriteRenderer.color = startColor;

        // REMOVED: Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        fadeTimer += Time.deltaTime;
        lifeTimer += Time.deltaTime;

        float fadeProgress = fadeTimer / fadeDuration;
        spriteRenderer.color = Color.Lerp(startColor, endColor, fadeProgress);

        // Return to pool when lifetime expires
        if (lifeTimer >= lifetime)
        {
            EchoPool.Instance.ReturnEcho(gameObject);
        }
    }
}
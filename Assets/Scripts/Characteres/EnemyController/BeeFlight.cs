using UnityEngine;

public class BeeFlight : MonoBehaviour
{
    public float distance = 1f;          // How far the bee flies back and forth
    public float speed = 2f;             // How fast the bee moves
    public float verticalAmplitude = 0.1f;  // Slight up-down flutter
    public float verticalFrequency = 3f;   // Flutter speed

    private Vector3 startPosition;

    void Start()
    {
        startPosition = transform.position;
    }

    void Update()
    {
        // Horizontal back and forth movement
        float xOffset = Mathf.PingPong(Time.time * speed, distance) - distance / 2f;

        // Slight up-down flutter to look more natural
        float yOffset = Mathf.Sin(Time.time * verticalFrequency) * verticalAmplitude;

        // Apply the position
        transform.position = startPosition + new Vector3(xOffset, yOffset, 0f);
    }
}

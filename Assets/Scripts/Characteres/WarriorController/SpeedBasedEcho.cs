using UnityEngine;

public class SpeedBasedEcho : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterEchoGenerator echoGenerator;
    [SerializeField] private Rigidbody2D rigidBody;

    [Header("Speed Settings")]
    [SerializeField] private float speedThreshold = 5f;
    [SerializeField] private Color normalEchoColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private Color highSpeedEchoColor = new Color(0f, 0.5f, 1f, 0.5f);

    private void Update()
    {
        float currentSpeed = rigidBody.linearVelocity.magnitude;

        if (currentSpeed >= speedThreshold)
        {
            echoGenerator.SetEchoActive(true);
            echoGenerator.SetEchoColor(highSpeedEchoColor);
        }
        else
        {
            echoGenerator.SetEchoActive(false);
        }
    }
}
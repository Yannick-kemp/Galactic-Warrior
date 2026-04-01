using Assets.Scripts.Characteres.EnemyContoller;
using UnityEngine;

public class BeeMonster : Enemy
{
    [Header("Movement")]
    [SerializeField] private float distance = 1f;
    [SerializeField] private float speed = 2f;
    [SerializeField] private float verticalAmplitude = 0.1f;
    [SerializeField] private float verticalFrequency = 3f;

    private Vector3 startPosition;

    private float xOffset;
    private int xDir = 1;
    private bool blockedThisStep;

    protected override void Start()
    {
        startPosition = transform.position;
    }

    private void FixedUpdate()
    {
        float yOffset = Mathf.Sin(Time.time * verticalFrequency) * verticalAmplitude;

        if (!blockedThisStep)
            xOffset += xDir * speed * Time.fixedDeltaTime;

        float half = distance * 0.5f;
        if (Mathf.Abs(xOffset) >= half)
        {
            xOffset = Mathf.Sign(xOffset) * half;
            xDir *= -1;
        }

        transform.position = startPosition + new Vector3(xOffset, yOffset, 0f);

        blockedThisStep = false;
    }

    // Called from SparkProjectile when overlapping
    public void NudgeX(float worldPushX)
    {
        xOffset += worldPushX;

        float half = distance * 0.5f;
        xOffset = Mathf.Clamp(xOffset, -half, half);
    }

    // Optional: flip direction so it doesn't keep trying to go into the warrior
    public void BounceAwayFrom(Vector2 hitNormal)
    {
        blockedThisStep = true;

        // hitNormal points from spark -> warrior
        // If we're moving toward the warrior, reverse
        if (Mathf.Sign(hitNormal.x) == xDir)
            xDir *= -1;
    }
}

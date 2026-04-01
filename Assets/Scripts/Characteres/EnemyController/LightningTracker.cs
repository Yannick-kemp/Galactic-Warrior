using UnityEngine;

public class LightningTracker : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Vector3 localUp = Vector3.up;
    [SerializeField] float turnSpeed = 360f;

    void Update()
    {
        if (target == null) return;

        Vector3 dir = target.position - transform.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up) *
                         Quaternion.FromToRotation(Vector3.forward, localUp);

        // Hard snap or smooth-turn?
        if (turnSpeed <= 0)
            transform.rotation = rot;
        else
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                                                          rot,
                                                          turnSpeed * Time.deltaTime);

    }
}
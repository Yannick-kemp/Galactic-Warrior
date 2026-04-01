using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enemy/Hit Reaction Profile")]
public class EnemyHitReactionProfile : ScriptableObject
{
    public float stepBackMultiplier = 1f;
    public float min = 0.05f;
    public float max = 1.2f;
    public float whileAttackingMultiplier = 0.6f;
}
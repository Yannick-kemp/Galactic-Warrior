using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    public class StoneReserve : MonoBehaviour
    {
        [SerializeField] private int stoneCount = 4;
        [SerializeField] private bool infiniteStones = false;

        [SerializeField] private Transform grabPoint;
        [SerializeField] private Transform approachPoint;
        [SerializeField] private float defaultApproachHeight = 0.8f;

        public bool HasStones => infiniteStones || stoneCount > 0;

        public Vector3 GetGrabWorldPosition()
        {
            return grabPoint != null ? grabPoint.position : transform.position;
        }

        public Vector3 GetApproachWorldPosition()
        {
            if (approachPoint != null)
                return approachPoint.position;

            return GetGrabWorldPosition() + Vector3.up * defaultApproachHeight;
        }

        public bool TryTakeStone()
        {
            if (!HasStones)
                return false;

            if (!infiniteStones)
                stoneCount--;

            return true;
        }
    }
}
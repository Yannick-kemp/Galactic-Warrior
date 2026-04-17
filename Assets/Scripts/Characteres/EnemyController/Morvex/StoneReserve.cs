using UnityEngine;

namespace Assets.Scripts.Characteres.EnemyContoller
{
    public class StoneReserve : MonoBehaviour
    {
        [Header("Reserve")]
        [SerializeField] private int stoneCount = 4;
        [SerializeField] private bool infiniteStones;
        [SerializeField] private Transform grabPoint;

        public bool HasStones => infiniteStones || stoneCount > 0;
        public int CurrentStoneCount => infiniteStones ? int.MaxValue : stoneCount;

        public Vector3 GetGrabWorldPosition()
        {
            return grabPoint != null ? grabPoint.position : transform.position;
        }

        public bool TryTakeStone()
        {
            if (!HasStones)
                return false;

            if (!infiniteStones)
                stoneCount--;

            return true;
        }

        public void AddStones(int amount)
        {
            if (amount <= 0 || infiniteStones)
                return;

            stoneCount += amount;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = HasStones ? Color.green : Color.red;
            Gizmos.DrawWireSphere(GetGrabWorldPosition(), 0.2f);
        }
    }
}

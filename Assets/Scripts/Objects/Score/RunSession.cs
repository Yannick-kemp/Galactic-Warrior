using UnityEngine;

namespace Assets.Scripts.Core
{
    public class RunSession
    {
        public int Score;
        public int RetryCount;
        public float RunStartTime;

        public void Reset()
        {
            Score = 0;
            RetryCount = 0;
            RunStartTime = Time.time;
        }
    }
}

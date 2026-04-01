using System;
namespace Assets.GalaticfFileSys.TimerManager
{
    public class Timer
    {
        private float duration;
        private float timeRemaining;
        private bool isRunning;
        private bool isLooping;
        public Action OnTimerComplete;

        public Timer(float duration, bool isLooping = false)
        {
            this.duration = duration;
            this.isLooping = isLooping;
            this.timeRemaining = duration;
        }

        public void Start()
        {
            isRunning = true;
            timeRemaining = duration;
        }

        public void Stop()
        {
            isRunning = false;
        }

        public void Reset()
        {
            timeRemaining = duration;
        }

        public void Update(float deltaTime)
        {
            if (!isRunning) return;
            timeRemaining -= deltaTime;
            if (timeRemaining <= 0f)
            {
                OnTimerComplete?.Invoke();
                if (isLooping)
                {
                    timeRemaining = duration;
                }
                else
                {
                    isRunning = false;
                }
            }
        }

        public bool IsRunning => isRunning;
        public float TimeRemaining => timeRemaining;
        public float ElapsedTime => duration - timeRemaining;
        public float Duration => duration;
    }
}
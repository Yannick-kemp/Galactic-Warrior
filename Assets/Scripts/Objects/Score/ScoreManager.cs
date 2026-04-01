using Assets.Scripts.Core;
using System;
using UnityEngine;

namespace Assets.Scripts.Scoring
{
    public class ScoreManager : MonoBehaviour
    {
        public static ScoreManager Instance { get; private set; }

        private RunSession session = new RunSession();

        public int TotalPoints => session.Score;

        public event Action<int, int, string, Vector2> OnPointsAdded;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void StartNewRun()
        {
            session.Reset();
            OnPointsAdded?.Invoke(0, session.Score, "Reset", Vector2.zero);
        }

        public void Add(int points, string label, Vector2 worldPos)
        {
            if (points <= 0) return;

            session.Score += points;

            OnPointsAdded?.Invoke(points, session.Score, label, worldPos);
        }
    }
}
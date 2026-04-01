using System;
using UnityEngine;

namespace Assets.Scripts.Tools
{
    public static class Utilities
    {
        public static float sleepThreshold = 10f;
        public static float angularSleepThreshold = 10f;

        public static float AngleInRad(this Vector2 vec1, Vector2 vec2)
        {
            return Mathf.Atan2(vec2.y - vec1.y, vec2.x - vec1.x);
        }
        public static float AngleDegre(this Vector2 vec1, Vector3 vec2)
        {
            return AngleInRad(vec1, vec2) * 180 / Mathf.PI;
        }

        public static double DecimalWithoutRound(this double val)
        {
            return Math.Truncate(100 * val) / 100;
        }

        public static double DecimalWithoutRound(this float val)
        {
            return Math.Truncate(100 * val) / 100;
        }

        public static float CalculatePercentage(this float number, float percentage)
        {
            if (percentage < 0 || percentage > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(percentage), "Percentage must be between 0 and 100.");
            }

            return number * (percentage / 100);
        }

        public static float GetDistanceXAxis(this float start, float end)
        {
            return Mathf.Abs(start - end);
        }
    }
}

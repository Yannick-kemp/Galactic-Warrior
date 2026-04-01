using System;
using System.Collections;
using UnityEngine;

public class TimerMgr : MonoBehaviour
{
    public static TimerMgr Instance { get; private set; }

    public Coroutine timerCoroutine; // Reference to the coroutine
    private float elapsedTime;
    private Action onTimerEnd;

    public bool IsStarted
    {
        get { return timerCoroutine != null; }
    }
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Persist through scene changes
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Starts the timer with a specific duration and optional callback.
    /// </summary>
    /// <param name="duration">Duration in seconds to run the timer.</param>
    /// <param name="onEndCallback">Callback invoked when the timer ends.</param>
    public void StartTimer(float duration, Action onEndCallback = null)
    {
        if (timerCoroutine != null)
        {
            StopCoroutine(timerCoroutine); // Stop any running timer coroutine
        }
        onTimerEnd = onEndCallback;
        timerCoroutine = StartCoroutine(TimerCoroutine(duration));
    }

    /// <summary>
    /// Stops the timer manually.
    /// </summary>
    public void StopTimer()
    {
        if (timerCoroutine != null)
        {
            StopCoroutine(timerCoroutine);
            timerCoroutine = null;
        }
    }

    /// <summary>
    /// Coroutine that handles the timer logic.
    /// </summary>
    private IEnumerator TimerCoroutine(float duration)
    {
        elapsedTime = 0f;
        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            yield return null; // WaitAnimationDisplay for the next frame
        }
        StopTimer();
        onTimerEnd?.Invoke(); // Call the callback method
        ResetTimer();

    }

    /// <summary>
    /// Resets the timer values.
    /// </summary>
    public void ResetTimer()
    {
        elapsedTime = 0f;
        onTimerEnd = null;
    }

    public float GetElapsedTime()
    {
        return elapsedTime;
    }
    public void Initialize()
    {
        Debug.Log("TimerManager Initialized");
    }
}

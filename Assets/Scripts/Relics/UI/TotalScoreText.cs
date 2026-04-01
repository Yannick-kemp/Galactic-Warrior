using Assets.Scripts.Scoring;
using System.Collections;
using TMPro;
using UnityEngine;

public class TotalScoreText : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    private bool _bound;

    private void Awake()
    {
        if (text == null) text = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        _bound = false;
        StartCoroutine(BindWhenReady());
    }

    private IEnumerator BindWhenReady()
    {
        while (ScoreManager.Instance == null)
            yield return null;

        if (!_bound)
        {
            ScoreManager.Instance.OnPointsAdded += OnPointsAdded;
            _bound = true;
        }

        Refresh();
    }

    private void OnDisable()
    {
        if (_bound && ScoreManager.Instance != null)
            ScoreManager.Instance.OnPointsAdded -= OnPointsAdded;

        _bound = false;
    }

    private void OnPointsAdded(int added, int total, string label, Vector2 pos) => Refresh();

    private void Refresh()
    {
        int total = ScoreManager.Instance != null ? ScoreManager.Instance.TotalPoints : 0;
        text.text = $"SCORE:{total}";
    }
}
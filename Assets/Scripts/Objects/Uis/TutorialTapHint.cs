using System.Collections;
using UnityEngine;

public class TutorialTapHint : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform hand;

    [Header("Target UI")]
    [SerializeField] private RectTransform target;

    [Header("Offset")]
    [SerializeField] private Vector2 offset = new Vector2(80, 80);
    [SerializeField] private float startDown = 55f;

    [Header("Animation")]
    [SerializeField] private float moveDistance = 40f;
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float pause = 0.3f;

    Vector2 startPos, endPos;
    Coroutine loop;

    void OnEnable()
    {
        StopAllCoroutines();
        StartCoroutine(BeginRoutine());
    }

    void OnDisable()
    {
        StopTutorial();
        StopAllCoroutines();
    }

    IEnumerator BeginRoutine()
    {
        if (hand == null || target == null)
        {
            Debug.LogWarning("TutorialTapHint missing references");
            yield break;
        }

        hand.gameObject.SetActive(true);
        hand.SetAsLastSibling();

        // wait for UI layout
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();

        PositionHand();

        loop = StartCoroutine(TapLoop());
    }

    void PositionHand()
    {
        RectTransform parent = hand.parent as RectTransform;

        Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, target);
        Vector2 localCenter = (Vector2)b.center;

        Rect r = parent.rect;
        float nx = Mathf.InverseLerp(r.xMin, r.xMax, localCenter.x);
        float signedX = (nx < 0.5f) ? Mathf.Abs(offset.x) : -Mathf.Abs(offset.x);
        Vector2 finalOffset = new Vector2(signedX, offset.y);

        startPos = localCenter + finalOffset;
        startPos.y -= startDown;

        endPos = startPos + Vector2.down * moveDistance;
        hand.anchoredPosition = startPos;
    }

    IEnumerator TapLoop()
    {
        while (true)
        {
            yield return Move(startPos, endPos);
            yield return new WaitForSeconds(pause);
            yield return Move(endPos, startPos);
            yield return new WaitForSeconds(pause);
        }
    }

    IEnumerator Move(Vector2 from, Vector2 to)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * moveSpeed;
            hand.anchoredPosition = Vector2.Lerp(from, to, t);
            yield return null;
        }
    }

    public void StopTutorial()
    {
        if (loop != null) StopCoroutine(loop);
        loop = null;
        if (hand != null) hand.gameObject.SetActive(false);
    }
}
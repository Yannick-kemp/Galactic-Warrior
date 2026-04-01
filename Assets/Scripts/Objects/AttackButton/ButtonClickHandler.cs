using Assets.Scripts.Characteres.WarriorController;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ButtonClickHandler : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Button Reference")]
    [SerializeField] private Button attackButton;
    [SerializeField] private RectTransform buttonRectTransform;
    [SerializeField] private Vector2 anchoredPos = new Vector2(2f, -180.5f); // used only if followWarrior = false

    [SerializeField] private float extraDownPixels = 24f;   // try 16..40
    [SerializeField] private float extraDownWorld = 0.08f;  // try 0.05..0.20
    [SerializeField] private bool clampY = false;           // IMPORTANT: false lets it go lower

    [Header("Follow Warrior (NEW)")]
    [SerializeField] private bool followWarrior = true;
    [SerializeField] private Canvas rootCanvas;          // canvas containing this button
    [SerializeField] private Camera worldCamera;         // gameplay camera (MainCamera)
    [SerializeField] private Vector3 worldOffsetFromFeet = new Vector3(0f, -0.55f, 0f);
    [SerializeField] private bool clampInsideCanvas = true;
    [SerializeField] private Vector2 canvasPadding = new Vector2(16f, 16f);
    [SerializeField, Min(0f)] private float followLerpSpeed = 25f; // 0 = snap instantly

    [Header("Hold Attack (optional)")]
    [SerializeField] private bool repeatWhileHeld = false;
    [SerializeField] private float repeatInterval = 0.08f;

    [Header("UI Input Block (important)")]
    [SerializeField] private float uiBlockOnPress = 0.20f;
    [SerializeField] private float uiBlockPerRepeat = 0.12f;

    [Header("Refs")]
    [SerializeField] private Warrior warrior;

    private Coroutine _holdRoutine;
    private bool _pressed;

    private RectTransform _canvasRect;
    private Collider2D _warriorCollider;

    [SerializeField] private RectTransform followSpace; // parent space where anchoredPosition is applied

    private void Awake()
    {
        if (attackButton == null) attackButton = GetComponent<Button>();
        if (buttonRectTransform == null) buttonRectTransform = GetComponent<RectTransform>();

        if (warrior == null)
            warrior = Warrior.Instance != null ? Warrior.Instance : FindFirstObjectByType<Warrior>();

        if (rootCanvas == null)
            rootCanvas = GetComponentInParent<Canvas>();

        if (rootCanvas != null)
            _canvasRect = rootCanvas.GetComponent<RectTransform>();

        if (worldCamera == null)
            worldCamera = Camera.main;

        if (warrior != null)
            _warriorCollider = warrior.GetComponent<Collider2D>();

        if (buttonRectTransform == null) buttonRectTransform = GetComponent<RectTransform>();
        if (rootCanvas == null) rootCanvas = GetComponentInParent<Canvas>();
        if (worldCamera == null) worldCamera = Camera.main;

        // IMPORTANT: anchoredPosition is relative to parent, so use parent rect
        if (followSpace == null && buttonRectTransform != null)
            followSpace = buttonRectTransform.parent as RectTransform;
    }

    private void Start()
    {
        if (!followWarrior && buttonRectTransform != null)
            buttonRectTransform.anchoredPosition = anchoredPos;
    }

    private void LateUpdate()
    {
        if (!followWarrior) return;
        FollowButtonUnderWarrior();
    }

    private void FollowButtonUnderWarrior()
    {
        if (warrior == null || buttonRectTransform == null || followSpace == null) return;

        // Reacquire collider if needed
        if (_warriorCollider == null)
            _warriorCollider = warrior.GetComponent<Collider2D>();

        // 1) World target near feet
        Vector3 worldTarget;
        if (_warriorCollider != null)
        {
            Bounds b = _warriorCollider.bounds;
            worldTarget = new Vector3(b.center.x, b.min.y, 0f) + worldOffsetFromFeet;
        }
        else
        {
            worldTarget = warrior.transform.position + worldOffsetFromFeet;
        }

        // Extra world down
        worldTarget += Vector3.down * extraDownWorld;

        // 2) World -> Screen (always world camera)
        Camera camWorld = worldCamera != null ? worldCamera : Camera.main;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camWorld, worldTarget);

        // Extra screen/UI down (more visible than -2 px)
        screenPoint.y -= extraDownPixels;

        // 3) Screen -> Local (Overlay => null camera)
        Camera uiCam = null;
        if (rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCam = rootCanvas.worldCamera != null ? rootCanvas.worldCamera : camWorld;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(followSpace, screenPoint, uiCam, out Vector2 localPoint))
            return;

        // 4) Clamp (X always optional, Y optional)
        if (clampInsideCanvas)
        {
            Rect r = followSpace.rect;
            Vector2 half = r.size * 0.5f;
            Vector2 halfBtn = buttonRectTransform.rect.size * 0.5f;

            float minX = -half.x + halfBtn.x + canvasPadding.x;
            float maxX = half.x - halfBtn.x - canvasPadding.x;
            localPoint.x = Mathf.Clamp(localPoint.x, minX, maxX);

            if (clampY)
            {
                float minY = -half.y + halfBtn.y + canvasPadding.y;
                float maxY = half.y - halfBtn.y - canvasPadding.y;
                localPoint.y = Mathf.Clamp(localPoint.y, minY, maxY);
            }
        }

        // 5) Apply
        if (followLerpSpeed <= 0f)
        {
            buttonRectTransform.anchoredPosition = localPoint;
        }
        else
        {
            float t = 1f - Mathf.Exp(-followLerpSpeed * Time.unscaledDeltaTime);
            buttonRectTransform.anchoredPosition =
                Vector2.Lerp(buttonRectTransform.anchoredPosition, localPoint, t);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pressed = true;

        ConsumeUiAndBlockWorld(eventData, uiBlockOnPress);
        AttackOnce();

        if (repeatWhileHeld && _holdRoutine == null)
            _holdRoutine = StartCoroutine(HoldAttackRoutine());
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ReleasePress(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ReleasePress(eventData);
    }

    private void OnDisable()
    {
        if (_holdRoutine != null)
        {
            StopCoroutine(_holdRoutine);
            _holdRoutine = null;
        }
        _pressed = false;
    }

    private void ReleasePress(PointerEventData eventData)
    {
        _pressed = false;

        if (_holdRoutine != null)
        {
            StopCoroutine(_holdRoutine);
            _holdRoutine = null;
        }

        ConsumeUiAndBlockWorld(eventData, uiBlockPerRepeat);
    }

    private void ConsumeUiAndBlockWorld(PointerEventData eventData, float blockDuration)
    {
        if (warrior != null)
            warrior.NotifyUIConsumedInput(blockDuration);

        eventData?.Use();
    }

    private void AttackOnce()
    {
        if (warrior == null) return;

        warrior.NotifyUIConsumedInput(uiBlockPerRepeat);
        warrior.RequestPrimaryAttackFromUIButton();
    }

    private IEnumerator HoldAttackRoutine()
    {
        while (_pressed)
        {
            yield return new WaitForSeconds(repeatInterval);
            if (!_pressed) break;
            AttackOnce();
        }

        _holdRoutine = null;
    }
}
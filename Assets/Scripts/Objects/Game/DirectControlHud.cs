using Assets.Scripts.Characteres.WarriorController;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Assets.Scripts.Objects.Game
{
    /// <summary>
    /// On-screen "Call of Duty Mobile" style controller: a virtual joystick (left)
    /// plus Jump / Attack buttons (right). It shows itself ONLY when the player has
    /// selected the Direct control scheme in the Settings popup (ControlScheme.IsDirect);
    /// in Tap mode it hides and the original tap-to-navigate flow runs untouched.
    ///
    /// Place this component on the in-game HUD (the _GameTools prefab): it reuses the
    /// HUD Canvas/EventSystem already there and is therefore present in every gameplay
    /// scene and absent from the main menu. It drives the Warrior through the additive
    /// DirectMove / DirectJump / DirectAttack API (see Warrior.DirectControl.cs).
    ///
    /// PC testing: A/D or Left/Right to move, Space to jump, J to attack.
    /// </summary>
    public class DirectControlHud : MonoBehaviour
    {
        [Header("Tuning")]
        [SerializeField, Range(0f, 0.6f)] private float deadZone = 0.2f;
        [SerializeField] private float joystickRadius = 130f;

        [Header("Ice-Ball aim indicator (world space)")]
        [SerializeField] private float aimIndicatorLength = 1.6f;   // pointer length (world units)
        [SerializeField] private float aimIndicatorThickness = 0.08f;
        [SerializeField] private float aimIndicatorDotSize = 0.35f;
        [SerializeField] private float aimIndicatorYOffset = 0.9f;  // above the Warrior origin
        [SerializeField] private Color aimIndicatorColor = new Color(0.4f, 0.9f, 1f, 0.9f);
        [SerializeField] private int aimIndicatorSortingOrder = 1000;

        private Transform _aimRoot;

        private static DirectControlHud _instance;

        private Warrior _warrior;
        private SimpleJoystick _joystick;
        private GameObject _controlsRoot;
        private bool _built;
        private bool _lastActive;

        // Layout element rects (positioned by ApplyLayout for left/right-handed).
        private RectTransform _joystickBase;
        private RectTransform _jumpBtn;
        private RectTransform _atkBtn;
        private bool _appliedLeftHanded;

        // Ice-Ball aim state: while a relic is armed the joystick aims; releasing fires.
        private Vector2 _iceAimDir;
        private bool _iceAiming;

        // Right-handed anchor+offset (from the screen corner). Left-handed mirrors these.
        private static readonly Vector2 JoystickAnchor = new Vector2(0f, 0f);
        private static readonly Vector2 JoystickOffset = new Vector2(230f, 230f);
        private static readonly Vector2 JumpAnchor = new Vector2(1f, 0f);
        private static readonly Vector2 JumpOffset = new Vector2(-140f, 190f);
        private static readonly Vector2 AtkAnchor = new Vector2(1f, 0f);
        private static readonly Vector2 AtkOffset = new Vector2(-360f, 130f);

        private void Awake()
        {
            // Guard against duplicates (e.g. a persisted _GameTools + a scene copy).
            // Two HUDs would fight over the animation: the untouched joystick would
            // DirectStop every frame and cancel the touched one's Run.
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_aimRoot != null)
                Destroy(_aimRoot.gameObject);

            if (_instance == this)
                _instance = null;
        }

        private void Start()
        {
            if (_instance != this)
                return;
            AcquireWarrior();
        }

        private void Update()
        {
            if (_instance != this)
                return;

            bool direct = ControlScheme.IsDirect;

            if (!direct)
            {
                if (_lastActive)
                    SetActiveState(false);
                return;
            }

            if (!_built)
                BuildUI();

            if (_warrior == null)
                AcquireWarrior();

            if (!_lastActive)
                SetActiveState(true);

            // Re-apply the layout live if the handedness setting changed (e.g. toggled
            // in the pause settings) without needing a scene reload.
            if (ControlHandedness.IsLeft != _appliedLeftHanded)
                ApplyLayout();

            if (_warrior == null)
                return;

            // Ice-Ball: while a relic is armed the joystick AIMS (no movement); releasing
            // the joystick fires the projectile in the last aimed direction.
            if (_warrior.IsIceBallReadyToCastDirect)
            {
                HandleIceBallAiming();
                return;
            }
            _iceAiming = false;
            _iceAimDir = Vector2.zero;
            HideAimIndicator();

            // Joystick + keyboard (PC) combined.
            float x = _joystick != null ? _joystick.Value.x : 0f;
            float kb = Input.GetAxisRaw("Horizontal");
            if (Mathf.Abs(kb) > Mathf.Abs(x)) x = kb;

            if (Mathf.Abs(x) > deadZone)
                _warrior.DirectMove(Mathf.Sign(x));
            else
                _warrior.DirectStop();

            if (Input.GetKeyDown(KeyCode.Space))
                _warrior.DirectJump(x);
            if (Input.GetKeyDown(KeyCode.J))
                _warrior.DirectAttack();
        }

        // While an Ice-Ball relic is armed: the joystick aims (the Warrior stays put), and
        // RELEASING the joystick fires in the last aimed direction. K fires on PC. A 360°
        // aim indicator above the Warrior follows the joystick tilt while held.
        private void HandleIceBallAiming()
        {
            _warrior.DirectStop(); // no movement while aiming

            bool pressed = _joystick != null && _joystick.IsPressed;
            Vector2 joyVec = _joystick != null ? _joystick.Value : Vector2.zero;

            if (pressed)
            {
                if (joyVec.sqrMagnitude > deadZone * deadZone)
                    _iceAimDir = joyVec;

                if (_iceAimDir.sqrMagnitude > 0.0001f)
                    UpdateAimIndicator(_iceAimDir);
                else
                    HideAimIndicator();
            }
            else
            {
                HideAimIndicator();
            }

            // Release edge = fire (in the last aimed direction; facing if never aimed).
            if (_iceAiming && !pressed)
            {
                _warrior.DirectCastIceBall(_iceAimDir);
                _iceAimDir = Vector2.zero;
                HideAimIndicator();
            }

            _iceAiming = pressed;

            // PC fallback.
            if (Input.GetKeyDown(KeyCode.K))
            {
                _warrior.DirectCastIceBall(_iceAimDir);
                _iceAimDir = Vector2.zero;
                HideAimIndicator();
            }
        }

        // ── Ice-Ball aim indicator (world-space pointer, not parented to the Warrior so
        //    its rotation is never mirrored when the Warrior flips) ────────────────────

        private void EnsureAimIndicator()
        {
            if (_aimRoot != null)
                return;

            var root = new GameObject("IceAimIndicator");
            _aimRoot = root.transform;

            // Pointer line: a solid quad from the origin extending along local +X.
            var line = new GameObject("Pointer").AddComponent<SpriteRenderer>();
            line.transform.SetParent(_aimRoot, false);
            line.sprite = RuntimeSprites.Solid();
            line.color = aimIndicatorColor;
            line.sortingOrder = aimIndicatorSortingOrder;
            float solidUnit = line.sprite.bounds.size.x; // world size of the solid sprite
            line.transform.localScale = new Vector3(
                aimIndicatorLength / solidUnit, aimIndicatorThickness / solidUnit, 1f);
            line.transform.localPosition = new Vector3(aimIndicatorLength * 0.5f, 0f, 0f);

            // Tip dot.
            var dot = new GameObject("Dot").AddComponent<SpriteRenderer>();
            dot.transform.SetParent(_aimRoot, false);
            dot.sprite = RuntimeSprites.Circle();
            dot.color = aimIndicatorColor;
            dot.sortingOrder = aimIndicatorSortingOrder + 1;
            float circleUnit = dot.sprite.bounds.size.x;
            dot.transform.localScale = Vector3.one * (aimIndicatorDotSize / circleUnit);
            dot.transform.localPosition = new Vector3(aimIndicatorLength, 0f, 0f);

            _aimRoot.gameObject.SetActive(false);
        }

        private void UpdateAimIndicator(Vector2 dir)
        {
            EnsureAimIndicator();
            if (_warrior == null || _aimRoot == null)
                return;

            _aimRoot.position = _warrior.transform.position + Vector3.up * aimIndicatorYOffset;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            _aimRoot.rotation = Quaternion.Euler(0f, 0f, angle);

            if (!_aimRoot.gameObject.activeSelf)
                _aimRoot.gameObject.SetActive(true);
        }

        private void HideAimIndicator()
        {
            if (_aimRoot != null && _aimRoot.gameObject.activeSelf)
                _aimRoot.gameObject.SetActive(false);
        }

        private void AcquireWarrior()
        {
            _warrior = Warrior.Instance != null ? Warrior.Instance : FindFirstObjectByType<Warrior>();
        }

        private void SetActiveState(bool active)
        {
            _lastActive = active;

            if (_controlsRoot != null)
                _controlsRoot.SetActive(active);

            if (_warrior != null)
                _warrior.SetDirectControlActive(active);

            if (!active)
                HideAimIndicator();
        }

        private float CurrentDirX() => _joystick != null ? _joystick.Value.x : 0f;

        // ── UI construction ──────────────────────────────────────────────────

        private void BuildUI()
        {
            _built = true;

            EnsureEventSystem();

            // Reuse the HUD canvas if this component lives under one (e.g. _GameTools),
            // otherwise create a dedicated overlay canvas.
            Canvas canvas = GetComponentInParent<Canvas>();
            Transform canvasTransform;

            if (canvas != null)
            {
                canvasTransform = canvas.transform;
            }
            else
            {
                var canvasGo = new GameObject("DirectControlCanvas",
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);

                var c = canvasGo.GetComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.sortingOrder = 400;

                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 1920f);
                scaler.matchWidthOrHeight = 0.5f;

                canvasTransform = canvasGo.transform;
            }

            _controlsRoot = new GameObject("DirectControls", typeof(RectTransform));
            var rootRt = _controlsRoot.GetComponent<RectTransform>();
            rootRt.SetParent(canvasTransform, false);
            Stretch(rootRt);

            _joystickBase = BuildJoystick(rootRt);
            _jumpBtn = BuildActionButton(rootRt, "JUMP", 220f, () => _warrior?.DirectJump(CurrentDirX()));
            _atkBtn = BuildActionButton(rootRt, "ATK", 180f, () => _warrior?.DirectAttack());

            ApplyLayout();

            _controlsRoot.SetActive(false);
        }

        // Positions joystick + buttons for the current handedness. Right-handed uses the
        // constant anchors/offsets; left-handed mirrors them horizontally (anchor.x and
        // offset.x flipped), so the joystick moves to the right and the buttons to the left.
        private void ApplyLayout()
        {
            bool left = ControlHandedness.IsLeft;
            _appliedLeftHanded = left;

            Place(_joystickBase, JoystickAnchor, JoystickOffset, left);
            Place(_jumpBtn, JumpAnchor, JumpOffset, left);
            Place(_atkBtn, AtkAnchor, AtkOffset, left);
        }

        private static void Place(RectTransform rt, Vector2 rightAnchor, Vector2 rightOffset, bool left)
        {
            if (rt == null) return;

            Vector2 anchor = rightAnchor;
            Vector2 offset = rightOffset;
            if (left)
            {
                anchor.x = 1f - rightAnchor.x;
                offset.x = -rightOffset.x;
            }

            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
        }

        private RectTransform BuildJoystick(RectTransform parent)
        {
            var bg = CreateImage(parent, "JoystickBase", Knob(), new Color(1f, 1f, 1f, 0.25f));
            var bgRt = bg.rectTransform;
            bgRt.sizeDelta = new Vector2(joystickRadius * 2.2f, joystickRadius * 2.2f);

            var handle = CreateImage(bgRt, "JoystickHandle", Knob(), new Color(1f, 1f, 1f, 0.6f));
            var hRt = handle.rectTransform;
            hRt.anchorMin = hRt.anchorMax = hRt.pivot = new Vector2(0.5f, 0.5f);
            hRt.sizeDelta = new Vector2(joystickRadius, joystickRadius);
            hRt.anchoredPosition = Vector2.zero;

            _joystick = bg.gameObject.AddComponent<SimpleJoystick>();
            _joystick.Init(bgRt, hRt, joystickRadius);
            return bgRt;
        }

        private RectTransform BuildActionButton(RectTransform parent, string label,
            float size, UnityEngine.Events.UnityAction onDown)
        {
            var img = CreateImage(parent, "Btn_" + label, Knob(), new Color(1f, 1f, 1f, 0.35f));
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(size, size);

            AddLabel(rt, label, Mathf.RoundToInt(size * 0.28f));

            // Pointer-DOWN for snappy response (Button.onClick fires on release).
            var trigger = img.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            entry.callback.AddListener(_ => onDown());
            trigger.triggers.Add(entry);
            return rt;
        }

        // ── UI helpers ───────────────────────────────────────────────────────

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem",
                    typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }
        }

        private static Image CreateImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.GetComponent<RectTransform>().SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        private static Text AddLabel(RectTransform parent, string text, int fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.fontSize = fontSize;
            t.raycastTarget = false;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Sprite Knob() => RuntimeSprites.Circle();

        // ── Nested minimal joystick ──────────────────────────────────────────

        private class SimpleJoystick : MonoBehaviour,
            IPointerDownHandler, IPointerUpHandler
        {
            private RectTransform _bg;
            private RectTransform _handle;
            private float _radius;

            private bool _pressed;
            private bool _usingTouch;      // press was a finger (track by raw fingerId)
            private int _touchFingerId = -1;
            private Camera _pressCamera;

            public Vector2 Value { get; private set; }
            public bool IsPressed => _pressed;

            public void Init(RectTransform bg, RectTransform handle, float radius)
            {
                _bg = bg;
                _handle = handle;
                _radius = radius;
            }

            public void OnPointerDown(PointerEventData e)
            {
                _pressed = true;
                _pressCamera = e.pressEventCamera;

                // Lock onto the RAW touch fingerId (the one whose position is closest to the
                // press point), independent of the UI module's pointerId. This is what makes
                // multi-touch robust: while this finger is held, a SECOND finger (pressing
                // JUMP/ATK) is a different fingerId and is ignored — and we never fall back to
                // Input.mousePosition, which on touch devices (simulateMouseWithTouches) jumps
                // to the most recent finger and would flip the stick left/right.
                _usingTouch = false;
                _touchFingerId = -1;

                if (Input.touchCount > 0)
                {
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < Input.touchCount; i++)
                    {
                        Touch t = Input.GetTouch(i);
                        float d = ((Vector2)t.position - e.position).sqrMagnitude;
                        if (d < bestDist)
                        {
                            bestDist = d;
                            _touchFingerId = t.fingerId;
                        }
                    }
                    _usingTouch = _touchFingerId >= 0;
                }

                Recompute(e.position);
            }

            public void OnPointerUp(PointerEventData e)
            {
                Reset();
            }

            // Poll the live pointer position every frame while held, so the joystick
            // stays active even when the pointer is not moving (no OnDrag needed).
            private void Update()
            {
                if (!_pressed) return;

                if (!TryGetPointerScreenPosition(out Vector2 screenPos))
                {
                    Reset();
                    return;
                }

                Recompute(screenPos);
            }

            private bool TryGetPointerScreenPosition(out Vector2 screenPos)
            {
                screenPos = Vector2.zero;

                // Touch press: track ONLY our own finger. Never fall back to the mouse while
                // a finger press is active (that fallback grabs whichever finger moved last).
                if (_usingTouch)
                {
                    for (int i = 0; i < Input.touchCount; i++)
                    {
                        Touch t = Input.GetTouch(i);
                        if (t.fingerId == _touchFingerId)
                        {
                            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                                return false;
                            screenPos = t.position;
                            return true;
                        }
                    }
                    return false; // our finger lifted / no longer reported
                }

                // Mouse (editor / PC): only when the press was NOT a finger.
                if (Input.GetMouseButton(0))
                {
                    screenPos = Input.mousePosition;
                    return true;
                }

                return false;
            }

            private void Recompute(Vector2 screenPos)
            {
                if (_bg == null) return;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _bg, screenPos, _pressCamera, out Vector2 local))
                    return;

                Vector2 clamped = Vector2.ClampMagnitude(local, _radius);
                Value = clamped / _radius;
                if (_handle != null) _handle.anchoredPosition = clamped;
            }

            private void Reset()
            {
                _pressed = false;
                _usingTouch = false;
                _touchFingerId = -1;
                Value = Vector2.zero;
                if (_handle != null) _handle.anchoredPosition = Vector2.zero;
            }
        }
    }
}

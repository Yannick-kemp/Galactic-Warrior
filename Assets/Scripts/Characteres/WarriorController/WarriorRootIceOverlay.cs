using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    [DisallowMultipleComponent]
    public class WarriorRootIceOverlay : MonoBehaviour
    {
        [Header("Renderers")]
        [SerializeField] private SpriteRenderer sourceRenderer;
        [SerializeField] private SpriteRenderer overlayRenderer;

        [Header("Overlay Mode")]
        [SerializeField] private bool useOverlaySpriteAsAssigned = true;

        [Header("Hide Normal Warrior While Frozen")]
        [SerializeField] private bool hideSourceRendererWhileFrozen = true;

        [Header("Ice Look")]
        [SerializeField] private Material iceMaterial;
        [SerializeField] private Color iceTint = Color.white;
        [SerializeField] private int sortingOrderOffset = 20;

        private bool _isShowing;
        private bool _sourceRendererWasEnabled;
        private bool _hasSourceRendererSnapshot;

        private void Reset()
        {
            TryAutoAssign();
        }

        private void Awake()
        {
            TryAutoAssign();

            // Important:
            // Do NOT call Hide() here, because Hide() restores the source renderer
            // from a runtime snapshot. At Awake there is no valid snapshot yet.
            HideOverlayOnly();
        }

        private void OnEnable()
        {
            if (!_isShowing)
                HideOverlayOnly();
        }

        private void OnDisable()
        {
            // If the object is disabled while frozen, restore the source renderer safely.
            Hide();
        }

        private void LateUpdate()
        {
            if (!_isShowing)
                return;

            SyncOverlay();
        }

        public void Show()
        {
            TryAutoAssign();

            if (!_isShowing && sourceRenderer != null)
            {
                _sourceRendererWasEnabled = sourceRenderer.enabled;
                _hasSourceRendererSnapshot = true;

                if (hideSourceRendererWhileFrozen)
                    sourceRenderer.enabled = false;
            }

            _isShowing = true;
            SyncOverlay();

            if (overlayRenderer != null)
                overlayRenderer.enabled = true;
        }

        public void Hide()
        {
            bool shouldRestoreSource =
                _isShowing &&
                _hasSourceRendererSnapshot &&
                sourceRenderer != null &&
                hideSourceRendererWhileFrozen;

            _isShowing = false;
            HideOverlayOnly();

            if (shouldRestoreSource)
                sourceRenderer.enabled = _sourceRendererWasEnabled;

            _hasSourceRendererSnapshot = false;
        }

        private void HideOverlayOnly()
        {
            if (overlayRenderer != null)
                overlayRenderer.enabled = false;
        }

        private void SyncOverlay()
        {
            if (overlayRenderer == null)
                return;

            if (sourceRenderer != null)
            {
                if (!useOverlaySpriteAsAssigned)
                    overlayRenderer.sprite = sourceRenderer.sprite;

                overlayRenderer.flipX = sourceRenderer.flipX;
                overlayRenderer.flipY = sourceRenderer.flipY;

                overlayRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
                overlayRenderer.sortingOrder = sourceRenderer.sortingOrder + sortingOrderOffset;
            }

            overlayRenderer.color = iceTint;

            if (iceMaterial != null)
                overlayRenderer.sharedMaterial = iceMaterial;
        }

        private void TryAutoAssign()
        {
            if (sourceRenderer == null)
                sourceRenderer = GetComponent<SpriteRenderer>();

            if (overlayRenderer == null)
            {
                Transform overlay = transform.Find("WarriorSprite_IceOverlay");

                // Allows the script to be placed on BodyFixedRoot while
                // WarriorSprite_IceOverlay is a sibling under Warrior.
                if (overlay == null && transform.parent != null)
                    overlay = transform.parent.Find("WarriorSprite_IceOverlay");

                if (overlay != null)
                    overlayRenderer = overlay.GetComponent<SpriteRenderer>();
            }
        }
    }
}
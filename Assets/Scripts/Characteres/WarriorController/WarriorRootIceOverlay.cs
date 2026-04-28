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

        private void Reset()
        {
            TryAutoAssign();
        }

        private void Awake()
        {
            TryAutoAssign();
            Hide();
        }

        private void OnEnable()
        {
            if (!_isShowing)
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

            if (sourceRenderer != null)
            {
                _sourceRendererWasEnabled = sourceRenderer.enabled;

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
            _isShowing = false;

            if (overlayRenderer != null)
                overlayRenderer.enabled = false;

            if (sourceRenderer != null && hideSourceRendererWhileFrozen)
                sourceRenderer.enabled = _sourceRendererWasEnabled;
        }

        private void SyncOverlay()
        {
            if (sourceRenderer == null || overlayRenderer == null)
                return;

            if (!useOverlaySpriteAsAssigned)
                overlayRenderer.sprite = sourceRenderer.sprite;

            overlayRenderer.flipX = sourceRenderer.flipX;
            overlayRenderer.flipY = sourceRenderer.flipY;

            overlayRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            overlayRenderer.sortingOrder = sourceRenderer.sortingOrder + sortingOrderOffset;

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

                if (overlay != null)
                    overlayRenderer = overlay.GetComponent<SpriteRenderer>();
            }
        }
    }
}
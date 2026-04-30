using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    /// <summary>
    /// Hivernox freeze visual controller.
    /// While frozen, ONLY WarriorSprite_IceOverlay is allowed to be visible.
    /// After freeze, the normal/default Warrior sprite is forced back ON.
    /// This prevents the bug where freezing during Attack3 restores the snapshot
    /// where the default sprite was OFF.
    /// </summary>
    [DisallowMultipleComponent]
    public class WarriorRootIceOverlay : MonoBehaviour
    {
        [Header("Required")]
        [SerializeField] private Transform warriorRoot;
        [SerializeField] private SpriteRenderer overlayRenderer;

        [Header("Normal Warrior Renderer To Restore")]
        [SerializeField] private SpriteRenderer defaultWarriorSpriteRenderer;
        [SerializeField] private bool forceDefaultRendererEnabledAfterFreeze = true;

        [Header("Optional References")]
        [SerializeField] private SpriteRenderer sortingReferenceRenderer;
        [SerializeField] private GameObject bodyFixedRoot;

        [Header("Frozen Visual Rule")]
        [SerializeField] private bool hideEveryOtherSpriteRendererWhileFrozen = true;
        [SerializeField] private bool forceBodyFixedRootInactiveWhileFrozen = true;
        [SerializeField] private bool forceBodyFixedRootInactiveAfterFreeze = true;

        [Header("Overlay Look")]
        [SerializeField] private bool useOverlaySpriteAsAssigned = true;
        [SerializeField] private Material iceMaterial;
        [SerializeField] private Color iceTint = Color.white;
        [SerializeField] private int sortingOrderOffset = 50;

        private struct RendererSnapshot
        {
            public SpriteRenderer Renderer;
            public bool Enabled;
        }

        private readonly List<RendererSnapshot> _rendererSnapshots = new List<RendererSnapshot>(32);

        private bool _isShowing;
        private bool _bodyFixedRootWasActive;
        private bool _hasBodyFixedRootSnapshot;

        private void Reset()
        {
            TryAutoAssign();
        }

        private void Awake()
        {
            TryAutoAssign();
            HideOverlayOnly();
        }

        private void OnEnable()
        {
            if (!_isShowing)
                HideOverlayOnly();
        }

        private void OnDisable()
        {
            Hide();
        }

        private void LateUpdate()
        {
            if (!_isShowing)
                return;

            EnforceFrozenVisualRule();
        }

        public void Show()
        {
            TryAutoAssign();

            if (!_isShowing)
                TakeSnapshotsBeforeHiding();

            _isShowing = true;
            EnforceFrozenVisualRule();
        }

        public void Hide()
        {
            if (!_isShowing)
            {
                HideOverlayOnly();
                return;
            }

            _isShowing = false;

            RestoreRendererSnapshots();

            if (overlayRenderer != null)
                overlayRenderer.enabled = false;

            if (forceBodyFixedRootInactiveAfterFreeze && bodyFixedRoot != null)
                bodyFixedRoot.SetActive(false);
            else if (_hasBodyFixedRootSnapshot && bodyFixedRoot != null)
                bodyFixedRoot.SetActive(_bodyFixedRootWasActive);

            // Important fix:
            // If freeze started during Attack3, the snapshot says the default Warrior
            // sprite was disabled. Restoring that snapshot would make the Warrior invisible.
            // So after restoring snapshots, force the real default sprite ON again.
            ForceRestoreDefaultRendererAfterFreeze();

            _rendererSnapshots.Clear();
            _hasBodyFixedRootSnapshot = false;
        }

        public void ForceRestoreDefaultRendererAfterFreeze()
        {
            TryAutoAssign();

            if (!forceDefaultRendererEnabledAfterFreeze)
                return;

            if (defaultWarriorSpriteRenderer == null)
                return;

            if (!defaultWarriorSpriteRenderer.gameObject.activeSelf)
                defaultWarriorSpriteRenderer.gameObject.SetActive(true);

            defaultWarriorSpriteRenderer.enabled = true;
        }

        private void EnforceFrozenVisualRule()
        {
            TryAutoAssign();

            if (forceBodyFixedRootInactiveWhileFrozen && bodyFixedRoot != null && bodyFixedRoot.activeSelf)
                bodyFixedRoot.SetActive(false);

            if (hideEveryOtherSpriteRendererWhileFrozen && warriorRoot != null)
            {
                SpriteRenderer[] allRenderers = warriorRoot.GetComponentsInChildren<SpriteRenderer>(true);
                for (int i = 0; i < allRenderers.Length; i++)
                {
                    SpriteRenderer r = allRenderers[i];
                    if (r == null || r == overlayRenderer)
                        continue;

                    r.enabled = false;
                }
            }

            SyncOverlayRenderer();

            if (overlayRenderer != null)
            {
                if (!overlayRenderer.gameObject.activeSelf)
                    overlayRenderer.gameObject.SetActive(true);

                overlayRenderer.enabled = true;
            }
        }

        private void SyncOverlayRenderer()
        {
            if (overlayRenderer == null)
                return;

            SpriteRenderer reference = sortingReferenceRenderer != null
                ? sortingReferenceRenderer
                : defaultWarriorSpriteRenderer;

            if (reference == null && warriorRoot != null)
            {
                SpriteRenderer[] allRenderers = warriorRoot.GetComponentsInChildren<SpriteRenderer>(true);
                for (int i = 0; i < allRenderers.Length; i++)
                {
                    SpriteRenderer candidate = allRenderers[i];
                    if (candidate == null || candidate == overlayRenderer)
                        continue;

                    reference = candidate;
                    sortingReferenceRenderer = candidate;
                    break;
                }
            }

            if (reference != null)
            {
                if (!useOverlaySpriteAsAssigned)
                    overlayRenderer.sprite = reference.sprite;

                overlayRenderer.flipX = reference.flipX;
                overlayRenderer.flipY = reference.flipY;
                overlayRenderer.sortingLayerID = reference.sortingLayerID;
                overlayRenderer.sortingOrder = reference.sortingOrder + sortingOrderOffset;
            }

            overlayRenderer.color = iceTint;

            if (iceMaterial != null)
                overlayRenderer.sharedMaterial = iceMaterial;
        }

        private void TakeSnapshotsBeforeHiding()
        {
            _rendererSnapshots.Clear();

            if (warriorRoot != null)
            {
                SpriteRenderer[] allRenderers = warriorRoot.GetComponentsInChildren<SpriteRenderer>(true);
                for (int i = 0; i < allRenderers.Length; i++)
                {
                    SpriteRenderer r = allRenderers[i];
                    if (r == null || r == overlayRenderer)
                        continue;

                    _rendererSnapshots.Add(new RendererSnapshot
                    {
                        Renderer = r,
                        Enabled = r.enabled
                    });
                }
            }

            if (bodyFixedRoot != null)
            {
                _bodyFixedRootWasActive = bodyFixedRoot.activeSelf;
                _hasBodyFixedRootSnapshot = true;
            }
        }

        private void RestoreRendererSnapshots()
        {
            for (int i = 0; i < _rendererSnapshots.Count; i++)
            {
                SpriteRenderer r = _rendererSnapshots[i].Renderer;
                if (r == null)
                    continue;

                r.enabled = _rendererSnapshots[i].Enabled;
            }
        }

        private void HideOverlayOnly()
        {
            if (overlayRenderer != null)
                overlayRenderer.enabled = false;
        }

        private void TryAutoAssign()
        {
            Warrior warrior = GetComponentInParent<Warrior>();
            if (warriorRoot == null && warrior != null)
                warriorRoot = warrior.transform;

            if (warriorRoot == null)
                warriorRoot = transform.parent != null ? transform.parent : transform;

            if (overlayRenderer == null)
            {
                if (name == "WarriorSprite_IceOverlay")
                    overlayRenderer = GetComponent<SpriteRenderer>();

                if (overlayRenderer == null && warriorRoot != null)
                {
                    Transform overlay = warriorRoot.Find("WarriorSprite_IceOverlay");
                    if (overlay != null)
                        overlayRenderer = overlay.GetComponent<SpriteRenderer>();
                }

                if (overlayRenderer == null)
                    overlayRenderer = GetComponent<SpriteRenderer>();
            }

            if (bodyFixedRoot == null && warriorRoot != null)
            {
                Transform body = warriorRoot.Find("BodyFixedRoot");
                if (body != null)
                    bodyFixedRoot = body.gameObject;
            }

            if (defaultWarriorSpriteRenderer == null && warriorRoot != null)
            {
                // Most likely: the normal Warrior SpriteRenderer is on the Warrior root.
                SpriteRenderer rootRenderer = warriorRoot.GetComponent<SpriteRenderer>();
                if (rootRenderer != null && rootRenderer != overlayRenderer)
                    defaultWarriorSpriteRenderer = rootRenderer;
            }

            if (defaultWarriorSpriteRenderer == null && warriorRoot != null)
            {
                SpriteRenderer[] allRenderers = warriorRoot.GetComponentsInChildren<SpriteRenderer>(true);
                for (int i = 0; i < allRenderers.Length; i++)
                {
                    SpriteRenderer candidate = allRenderers[i];
                    if (candidate == null || candidate == overlayRenderer)
                        continue;

                    if (bodyFixedRoot != null && candidate.transform.IsChildOf(bodyFixedRoot.transform))
                        continue;

                    defaultWarriorSpriteRenderer = candidate;
                    break;
                }
            }

            if (sortingReferenceRenderer == null)
                sortingReferenceRenderer = defaultWarriorSpriteRenderer;
        }
    }
}

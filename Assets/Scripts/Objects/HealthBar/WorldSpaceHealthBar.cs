using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UI
{
    /// <summary>
    /// World-space health bar that follows an enemy and displays current health
    /// </summary>
    public class WorldSpaceHealthBar : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image fillImage;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Canvas canvas;

        [Header("Colors")]
        [SerializeField] private Color fullHealthColor = new Color(0.2f, 0.8f, 0.2f); // Green
        [SerializeField] private Color midHealthColor = new Color(1f, 0.8f, 0f);      // Yellow
        [SerializeField] private Color lowHealthColor = new Color(0.9f, 0.2f, 0.2f);  // Red

        [Header("Thresholds")]
        [SerializeField] private float midHealthThreshold = 0.5f;
        [SerializeField] private float lowHealthThreshold = 0.25f;

        [Header("Behavior")]
        [SerializeField] private bool hideWhenFull = false;
        [SerializeField] private bool smoothTransition = true;
        [SerializeField] private float transitionSpeed = 5f;

        [Header("Offset")]
        [SerializeField] private Vector3 offset = new Vector3(0, 1.5f, 0);

        private Transform target;
        private float currentFillAmount = 1f;
        private float targetFillAmount = 1f;
        private Camera mainCamera;

        private void Awake()
        {
            // Ensure canvas is set up for world space
            if (canvas == null)
            {
                canvas = GetComponent<Canvas>();
            }

            if (canvas != null)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = Camera.main;
            }

            mainCamera = Camera.main;

            // Auto-find fill image if not assigned
            if (fillImage == null)
            {
                fillImage = transform.Find("Fill")?.GetComponent<Image>();
                if (fillImage == null)
                {
                    Debug.LogWarning($"WorldSpaceHealthBar on {gameObject.name}: Fill Image not found!");
                }
            }

            // Auto-find background image if not assigned
            if (backgroundImage == null)
            {
                backgroundImage = transform.Find("Background")?.GetComponent<Image>();
            }
        }

        private void Start()
        {
            if (fillImage != null)
            {
                fillImage.fillAmount = 1f;
                fillImage.color = fullHealthColor;
            }

            if (hideWhenFull)
            {
                SetVisibility(false);
            }
        }

        private void LateUpdate()
        {
            // Follow target
            if (target != null)
            {
                transform.position = target.position + offset;

                // Make health bar face camera
                if (mainCamera != null)
                {
                    transform.rotation = mainCamera.transform.rotation;
                }
            }

            // Smooth fill animation
            if (smoothTransition && fillImage != null)
            {
                currentFillAmount = Mathf.Lerp(currentFillAmount, targetFillAmount, Time.deltaTime * transitionSpeed);
                fillImage.fillAmount = currentFillAmount;
            }
        }

        /// <summary>
        /// Set the target transform to follow
        /// </summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (target != null)
            {
                transform.position = target.position + offset;
            }
        }

        /// <summary>
        /// Update the health bar display
        /// </summary>
        public void UpdateHealth(float currentHealth, float maxHealth)
        {
            if (fillImage == null) return;

            float healthPercent = Mathf.Clamp01(currentHealth / maxHealth);

            targetFillAmount = healthPercent;

            if (!smoothTransition)
            {
                currentFillAmount = targetFillAmount;
                fillImage.fillAmount = currentFillAmount;
            }

            // Update color based on health percentage
            UpdateHealthColor(healthPercent);

            // Show/hide based on health
            if (hideWhenFull)
            {
                SetVisibility(healthPercent < 1f);
            }
        }

        /// <summary>
        /// Update the color based on current health percentage
        /// </summary>
        private void UpdateHealthColor(float healthPercent)
        {
            if (fillImage == null) return;

            Color targetColor;

            if (healthPercent <= lowHealthThreshold)
            {
                targetColor = lowHealthColor;
            }
            else if (healthPercent <= midHealthThreshold)
            {
                // Interpolate between low and mid health colors
                float t = (healthPercent - lowHealthThreshold) / (midHealthThreshold - lowHealthThreshold);
                targetColor = Color.Lerp(lowHealthColor, midHealthColor, t);
            }
            else
            {
                // Interpolate between mid and full health colors
                float t = (healthPercent - midHealthThreshold) / (1f - midHealthThreshold);
                targetColor = Color.Lerp(midHealthColor, fullHealthColor, t);
            }

            fillImage.color = targetColor;
        }

        /// <summary>
        /// Set the position offset from the target
        /// </summary>
        public void SetOffset(Vector3 newOffset)
        {
            offset = newOffset;
        }

        /// <summary>
        /// Set visibility of the health bar
        /// </summary>
        public void SetVisibility(bool visible)
        {
            if (canvas != null)
            {
                canvas.enabled = visible;
            }
            else
            {
                gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// Force immediate update without smooth transition
        /// </summary>
        public void ForceUpdate(float currentHealth, float maxHealth)
        {
            if (fillImage == null) return;

            float healthPercent = Mathf.Clamp01(currentHealth / maxHealth);
            currentFillAmount = healthPercent;
            targetFillAmount = healthPercent;
            fillImage.fillAmount = healthPercent;
            UpdateHealthColor(healthPercent);
        }
    }
}

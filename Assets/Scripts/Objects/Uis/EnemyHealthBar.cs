using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UI
{
    public class EnemyHealthBar : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private Image healthBarFill;
        [SerializeField] private Image healthBarBackground;

        [Header("Colors")]
        [SerializeField] private Color fullHealthColor = Color.green;
        [SerializeField] private Color mediumHealthColor = Color.yellow;
        [SerializeField] private Color lowHealthColor = Color.red;
        [SerializeField] private float mediumHealthThreshold = 0.6f;
        [SerializeField] private float lowHealthThreshold = 0.3f;

        [Header("Settings")]
        [SerializeField] private bool smoothTransition = true;
        [SerializeField] private float transitionSpeed = 5f;
        [SerializeField] private Vector3 offset = new Vector3(0, 1.5f, 0);
        [SerializeField] private bool hideWhenFull = true;
        [SerializeField] private bool hideWhenDead = true;

        private Transform targetEnemy;
        private Camera mainCamera;
        private float targetFillAmount;
        private CanvasGroup canvasGroup;

        private void Awake()
        {
            mainCamera = Camera.main;

            // Add CanvasGroup for fading
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        public void Initialize(Transform enemy, float maxHealth)
        {
            targetEnemy = enemy;
            targetFillAmount = 1f;

            if (healthBarFill != null)
            {
                healthBarFill.fillAmount = 1f;
                healthBarFill.color = fullHealthColor;
            }

            if (hideWhenFull)
            {
                canvasGroup.alpha = 0f;
            }
        }

        public void UpdateHealth(float currentHealth, float maxHealth)
        {
            if (maxHealth <= 0) return;

            targetFillAmount = Mathf.Clamp01(currentHealth / maxHealth);

            // Update color based on health percentage
            UpdateHealthColor(targetFillAmount);

            // Show/hide health bar
            if (hideWhenFull && targetFillAmount >= 0.99f)
            {
                canvasGroup.alpha = 0f;
            }
            else if (hideWhenDead && targetFillAmount <= 0.01f)
            {
                canvasGroup.alpha = 0f;
            }
            else
            {
                canvasGroup.alpha = 1f;
            }
        }

        private void Update()
        {
            if (targetEnemy == null) return;

            // Follow enemy position
            Vector3 screenPos = mainCamera.WorldToScreenPoint(targetEnemy.position + offset);
            transform.position = screenPos;

            // Smooth fill amount transition
            if (healthBarFill != null)
            {
                if (smoothTransition)
                {
                    healthBarFill.fillAmount = Mathf.Lerp(
                        healthBarFill.fillAmount,
                        targetFillAmount,
                        Time.deltaTime * transitionSpeed
                    );
                }
                else
                {
                    healthBarFill.fillAmount = targetFillAmount;
                }
            }
        }

        private void UpdateHealthColor(float healthPercent)
        {
            if (healthBarFill == null) return;

            if (healthPercent > mediumHealthThreshold)
            {
                healthBarFill.color = fullHealthColor;
            }
            else if (healthPercent > lowHealthThreshold)
            {
                // Lerp between full and medium
                float t = (healthPercent - lowHealthThreshold) / (mediumHealthThreshold - lowHealthThreshold);
                healthBarFill.color = Color.Lerp(mediumHealthColor, fullHealthColor, t);
            }
            else
            {
                // Lerp between low and medium
                float t = healthPercent / lowHealthThreshold;
                healthBarFill.color = Color.Lerp(lowHealthColor, mediumHealthColor, t);
            }
        }

        public void SetVisible(bool visible)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
        }
    }
}
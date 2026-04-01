using UnityEngine;

namespace Assets.Scripts.UI
{
    /// <summary>
    /// Factory class to create and manage health bars for enemies
    /// </summary>
    public static class HealthBarFactory
    {
        private static GameObject healthBarPrefab;

        /// <summary>
        /// Create a health bar for an enemy at runtime
        /// </summary>
        public static WorldSpaceHealthBar CreateHealthBar(Transform parent, Vector3 offset)
        {
            // Create health bar GameObject
            GameObject healthBarObj = new GameObject("HealthBar");
            healthBarObj.transform.SetParent(parent);
            healthBarObj.transform.localPosition = offset;

            // Add Canvas
            Canvas canvas = healthBarObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            // Set canvas size
            RectTransform canvasRect = healthBarObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1f, 0.15f);
            canvasRect.localScale = Vector3.one * 0.01f; // Scale down for world space

            // Create Background
            GameObject backgroundObj = new GameObject("Background");
            backgroundObj.transform.SetParent(healthBarObj.transform);

            RectTransform bgRect = backgroundObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            bgRect.anchoredPosition = Vector2.zero;

            UnityEngine.UI.Image bgImage = backgroundObj.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

            // Create Fill
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(healthBarObj.transform);

            RectTransform fillRect = fillObj.AddComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0, 0);
            fillRect.anchorMax = new Vector2(1, 1);
            fillRect.sizeDelta = new Vector2(-4, -4); // Padding
            fillRect.anchoredPosition = Vector2.zero;

            UnityEngine.UI.Image fillImage = fillObj.AddComponent<UnityEngine.UI.Image>();
            fillImage.color = new Color(0.2f, 0.8f, 0.2f);
            fillImage.type = UnityEngine.UI.Image.Type.Filled;
            fillImage.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;

            // Add WorldSpaceHealthBar component
            WorldSpaceHealthBar healthBar = healthBarObj.AddComponent<WorldSpaceHealthBar>();

            // Use reflection to set private fields (since they're serialized)
            var fillImageField = typeof(WorldSpaceHealthBar).GetField("fillImage",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fillImageField?.SetValue(healthBar, fillImage);

            var backgroundImageField = typeof(WorldSpaceHealthBar).GetField("backgroundImage",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            backgroundImageField?.SetValue(healthBar, bgImage);

            var canvasField = typeof(WorldSpaceHealthBar).GetField("canvas",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            canvasField?.SetValue(healthBar, canvas);

            healthBar.SetTarget(parent);
            healthBar.SetOffset(offset);

            return healthBar;
        }

        /// <summary>
        /// Create a health bar using a prefab
        /// </summary>
        public static WorldSpaceHealthBar CreateHealthBarFromPrefab(GameObject prefab, Transform parent, Vector3 offset)
        {
            if (prefab == null)
            {
                Debug.LogWarning("Health bar prefab is null, creating default health bar");
                return CreateHealthBar(parent, offset);
            }

            GameObject healthBarObj = Object.Instantiate(prefab, parent);
            healthBarObj.transform.localPosition = offset;

            WorldSpaceHealthBar healthBar = healthBarObj.GetComponent<WorldSpaceHealthBar>();

            if (healthBar == null)
            {
                Debug.LogError("Prefab does not have WorldSpaceHealthBar component!");
                return null;
            }

            healthBar.SetTarget(parent);
            healthBar.SetOffset(offset);

            return healthBar;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
/// <summary>
/// Creates an infinite scrolling background with multi-layer parallax effect and various placement patterns.
/// </summary>
public class BackGroundLoop : MonoBehaviour
{
    private bool _isBuilding;
    private bool _initialized;
    public enum PatternType
    {
        Full,
        Checkerboard,
        HorizontalStripes,
        VerticalStripes,
        SparseGrid,
        RandomSparse,
        Diagonal,
        OffsetRows,
        BorderOnly,
        DenseCenter
    }

    [System.Serializable]
    public class ParallaxLayer
    {
        [Tooltip("Background prefab to be repeated")]
        public GameObject prefab;

        [Tooltip("Parallax speed (0 = static, 0.5 = half camera speed, 1 = full camera speed)")]
        [UnityEngine.Range(0f, 1f)]
        public float parallaxSpeed = 1f;

        [Tooltip("Z-order for layer sorting (higher = closer to camera)")]
        public float zPosition = 0f;

        [Tooltip("Pattern type for object placement")]
        public PatternType patternType = PatternType.Full;

        [Tooltip("Density for RandomSparse pattern (0.1 = 10%, 0.5 = 50%, 1 = 100%)")]
        [UnityEngine.Range(0.1f, 1f)]
        public float randomDensity = 0.3f;

        [Tooltip("Spacing for SparseGrid pattern (2 = one over two, 3 = one over three, etc.)")]
        [UnityEngine.Range(2, 5)]
        public int gridSpacing = 3;

        [Tooltip("Radius for DenseCenter pattern")]
        [UnityEngine.Range(1f, 10f)]
        public float centerRadius = 3f;

        [Tooltip("Random seed for consistent RandomSparse pattern (0 = random each time)")]
        public int randomSeed = 12345;
    }

    // Class to store grid coordinates for each object
    private class GridCoordinates
    {
        public int x;
        public int y;

        public GridCoordinates(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [Tooltip("Parallax layers (bottom to top = far to near)")]
    public ParallaxLayer[] layers;

    [Tooltip("Main camera reference")]
    public Camera mainCamera;

    private Vector2 screenBounds;
    private Dictionary<GameObject, List<Transform>[]> rowsDict = new Dictionary<GameObject, List<Transform>[]>();
    private Dictionary<GameObject, List<Transform>[]> columnsDict = new Dictionary<GameObject, List<Transform>[]>();
    private Dictionary<GameObject, float> parallaxSpeedsDict = new Dictionary<GameObject, float>();
    private Dictionary<GameObject, ParallaxLayer> layerByContainer = new Dictionary<GameObject, ParallaxLayer>();
    private Dictionary<Transform, GridCoordinates> gridCoordinates = new Dictionary<Transform, GridCoordinates>();
    private List<GameObject> instancedLevels = new List<GameObject>();
    private Vector3 lastCameraPosition;

    void Start()
    {
        StartCoroutine(InitNextFrame());
    }

    void LateUpdate()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
            lastCameraPosition = mainCamera.transform.position;
            RefreshScreenBounds();
            return;
        }
        if (!_isBuilding && !HasAnyValidContainer())
        {
            // Clean stale null entries/dictionaries before rebuilding
          //  Debug.Log("[BackGroundLoop] No valid containers found -> rebuilding");
            ClearGeneratedParallax();
            StartCoroutine(InitNextFrame());
            return;
        }

        Vector3 cameraDelta = mainCamera.transform.position - lastCameraPosition;

        // Optional: if camera teleported a lot in one frame, still okay with while-wrap,
        // but this avoids weird first-frame spikes if needed.
        // if (cameraDelta.sqrMagnitude > 2500f) { lastCameraPosition = mainCamera.transform.position; return; }

        for (int i = 0; i < instancedLevels.Count; i++)
        {
            GameObject obj = instancedLevels[i];
            if (obj == null) continue;

            float parallaxSpeed = parallaxSpeedsDict.ContainsKey(obj) ? parallaxSpeedsDict[obj] : 1f;

            obj.transform.position += new Vector3(
                cameraDelta.x * parallaxSpeed,
                cameraDelta.y * parallaxSpeed,
                0f
            );

            RepositionBackgroundObjects(obj, parallaxSpeed);
        }

        lastCameraPosition = mainCamera.transform.position;
    }
    private IEnumerator InitNextFrame()
    {
        if (_isBuilding) yield break;
        _isBuilding = true;

        // Wait one frame so CameraFollow / hero registration can settle the camera first
        yield return null;

        if (mainCamera == null)
        {
            mainCamera = GetComponent<Camera>();
            if (mainCamera == null)
                mainCamera = Camera.main;

            if (mainCamera == null)
            {
                Debug.LogError("No camera found! Please assign a camera reference.");
                enabled = false;
                _isBuilding = false;
                yield break;
            }
        }

        // If already built and still valid, skip
        if (_initialized && HasAnyValidContainer())
        {
            _isBuilding = false;
            yield break;
        }

        lastCameraPosition = mainCamera.transform.position;
        RefreshScreenBounds();

        foreach (ParallaxLayer layer in layers)
        {
            if (layer.prefab != null)
            {
                GameObject levelContainer = new GameObject(layer.prefab.name + "_Container");
                levelContainer.transform.position = new Vector3(0, 0, layer.zPosition);
                instancedLevels.Add(levelContainer);

                parallaxSpeedsDict[levelContainer] = layer.parallaxSpeed;
                layerByContainer[levelContainer] = layer;

                LoadBackgroundObjects(layer, levelContainer);
            }
            else
            {
                Debug.LogWarning("Null prefab found in layers array!");
            }
        }

        _initialized = true;
        _isBuilding = false;
    }
    void RepositionBackgroundObjects(GameObject obj, float parallaxSpeed)
    {
        Vector3 effectiveCameraPos = new Vector3(
            mainCamera.transform.position.x,
            mainCamera.transform.position.y,
            obj.transform.position.z
        );

        // Get the layer configuration for pattern checking
        ParallaxLayer layer = layerByContainer.ContainsKey(obj) ? layerByContainer[obj] : null;

        // ===== HORIZONTAL SCROLLING =====
        if (!rowsDict.TryGetValue(obj, out List<Transform>[] rows) || rows == null)
        {
            SortRowsIntoGroups(obj);
            if (!rowsDict.TryGetValue(obj, out rows) || rows == null || rows.Length < 1)
            {
                return;
            }
        }

        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            List<Transform> row = rows[rowIndex];
            if (row == null || row.Count < 2) continue;

            Transform firstChild = row[0];
            Transform lastChild = row[row.Count - 1];

            if (firstChild == null || lastChild == null) continue;

            SpriteRenderer firstRenderer = firstChild.GetComponent<SpriteRenderer>();
            if (firstRenderer == null) continue;

            float objectWidth = firstRenderer.bounds.extents.x;

            // Scrolling RIGHT
            while (row.Count >= 2)
            {
                firstChild = row[0];
                lastChild = row[row.Count - 1];

                if (firstChild == null || lastChild == null) break;

                SpriteRenderer firstRendererLoop = firstChild.GetComponent<SpriteRenderer>();
                if (firstRendererLoop == null) break;

                objectWidth = firstRendererLoop.bounds.extents.x;

                bool moved = false;

                // Scrolling RIGHT
                if (effectiveCameraPos.x + screenBounds.x > lastChild.position.x + objectWidth)
                {
                    firstChild.SetAsLastSibling();
                    firstChild.position = new Vector3(
                        lastChild.position.x + objectWidth * 2f,
                        lastChild.position.y,
                        lastChild.position.z
                    );

                    if (gridCoordinates.ContainsKey(firstChild))
                    {
                        gridCoordinates[firstChild].x += row.Count;

                        if (layer != null)
                        {
                            bool shouldBeActive = ShouldPlaceObjectAtGrid(layer, gridCoordinates[firstChild].x, gridCoordinates[firstChild].y);
                            firstChild.gameObject.SetActive(shouldBeActive);
                        }
                    }

                    row.RemoveAt(0);
                    row.Add(firstChild);
                    moved = true;
                }

                // Scrolling LEFT
                else if (effectiveCameraPos.x - screenBounds.x < firstChild.position.x - objectWidth)
                {
                    lastChild.SetAsFirstSibling();
                    lastChild.position = new Vector3(
                        firstChild.position.x - objectWidth * 2f,
                        lastChild.position.y,
                        lastChild.position.z
                    );

                    if (gridCoordinates.ContainsKey(lastChild))
                    {
                        gridCoordinates[lastChild].x -= row.Count;

                        if (layer != null)
                        {
                            bool shouldBeActive = ShouldPlaceObjectAtGrid(layer, gridCoordinates[lastChild].x, gridCoordinates[lastChild].y);
                            lastChild.gameObject.SetActive(shouldBeActive);
                        }
                    }

                    row.RemoveAt(row.Count - 1);
                    row.Insert(0, lastChild);
                    moved = true;
                }

                if (!moved) break;
            }
        }

        // ===== VERTICAL SCROLLING =====
        if (!columnsDict.TryGetValue(obj, out List<Transform>[] columns) || columns == null)
        {
            SortColumnsIntoGroups(obj);
            if (!columnsDict.TryGetValue(obj, out columns) || columns == null || columns.Length < 1)
            {
                return;
            }
        }

        for (int colIndex = 0; colIndex < columns.Length; colIndex++)
        {
            List<Transform> column = columns[colIndex];
            if (column == null || column.Count < 2) continue;

            Transform bottomChild = column[0];
            Transform topChild = column[column.Count - 1];

            if (bottomChild == null || topChild == null) continue;

            SpriteRenderer bottomRenderer = bottomChild.GetComponent<SpriteRenderer>();
            if (bottomRenderer == null) continue;

            float objectHeight = bottomRenderer.bounds.extents.y;

            // Scrolling UP
            while (column.Count >= 2)
            {
                bottomChild = column[0];
                topChild = column[column.Count - 1];

                if (bottomChild == null || topChild == null) break;

                SpriteRenderer bottomRendererLoop = bottomChild.GetComponent<SpriteRenderer>();
                if (bottomRendererLoop == null) break;

                float h = bottomRendererLoop.bounds.extents.y;
                bool moved = false;

                // Scrolling UP
                if (effectiveCameraPos.y + screenBounds.y > topChild.position.y + h)
                {
                    bottomChild.position = new Vector3(
                        bottomChild.position.x,
                        topChild.position.y + h * 2f,
                        bottomChild.position.z
                    );

                    if (gridCoordinates.ContainsKey(bottomChild))
                    {
                        gridCoordinates[bottomChild].y += column.Count;

                        if (layer != null)
                        {
                            bool shouldBeActive = ShouldPlaceObjectAtGrid(
                                layer,
                                gridCoordinates[bottomChild].x,
                                gridCoordinates[bottomChild].y
                            );
                            bottomChild.gameObject.SetActive(shouldBeActive);
                        }
                    }

                    column.RemoveAt(0);
                    column.Add(bottomChild);
                    moved = true;
                }
                // Scrolling DOWN
                else if (effectiveCameraPos.y - screenBounds.y < bottomChild.position.y - h)
                {
                    topChild.position = new Vector3(
                        topChild.position.x,
                        bottomChild.position.y - h * 2f,
                        topChild.position.z
                    );

                    if (gridCoordinates.ContainsKey(topChild))
                    {
                        gridCoordinates[topChild].y -= column.Count;

                        if (layer != null)
                        {
                            bool shouldBeActive = ShouldPlaceObjectAtGrid(
                                layer,
                                gridCoordinates[topChild].x,
                                gridCoordinates[topChild].y
                            );
                            topChild.gameObject.SetActive(shouldBeActive);
                        }
                    }

                    column.RemoveAt(column.Count - 1);
                    column.Insert(0, topChild);
                    moved = true;
                }

                if (!moved) break;
            }
        }
    }

    void LoadBackgroundObjects(ParallaxLayer layer, GameObject container)
    {
        SpriteRenderer renderer = layer.prefab.GetComponent<SpriteRenderer>();
        if (renderer == null)
        {
            Debug.LogError($"Object {layer.prefab.name} has no SpriteRenderer component!");
            return;
        }

        float objectWidth = renderer.bounds.size.x;
        float objectHeight = renderer.bounds.size.y;

        int childsNeededX = Mathf.CeilToInt(screenBounds.x * 2 / objectWidth) + 3;
        int childsNeededY = Mathf.CeilToInt(screenBounds.y * 2 / objectHeight) + 3;

        Vector3 cameraPos = mainCamera.transform.position;
        float startX = cameraPos.x - (childsNeededX * objectWidth / 2f);
        float startY = cameraPos.y - (childsNeededY * objectHeight / 2f);

        // Set random seed for consistent pattern
        if (layer.patternType == PatternType.RandomSparse && layer.randomSeed != 0)
        {
            Random.InitState(layer.randomSeed);
        }

        // Create ALL objects but activate only those matching the pattern
        for (int y = 0; y < childsNeededY; y++)
        {
            for (int x = 0; x < childsNeededX; x++)
            {
                GameObject child = Instantiate(layer.prefab);
                child.transform.SetParent(container.transform);
                child.transform.position = new Vector3(
                    startX + (objectWidth * x),
                    startY + (objectHeight * y),
                    container.transform.position.z
                );
                child.name = $"{layer.prefab.name}_{x}_{y}";

                // Store grid coordinates
                gridCoordinates[child.transform] = new GridCoordinates(x, y);

                // Check if should be active based on pattern
                bool shouldBeActive = ShouldPlaceObjectAtGrid(layer, x, y);
                child.SetActive(shouldBeActive);
            }
        }

        SortRowsIntoGroups(container);
        SortColumnsIntoGroups(container);
    }

    /// <summary>
    /// Determines if object should be active at given grid coordinates (for RandomSparse uses seeded random)
    /// </summary>
    bool ShouldPlaceObjectAtGrid(ParallaxLayer layer, int x, int y)
    {
        switch (layer.patternType)
        {
            case PatternType.Full:
                return true;

            case PatternType.Checkerboard:
                return (x + y) % 2 != 0;

            case PatternType.HorizontalStripes:
                return y % 2 != 0;

            case PatternType.VerticalStripes:
                return x % 2 != 0;

            case PatternType.SparseGrid:
                return (x % layer.gridSpacing == 0) && (y % layer.gridSpacing == 0);

            case PatternType.RandomSparse:
                // Use deterministic random based on position
                int seed = layer.randomSeed + x * 73856093 + y * 19349663;
                Random.InitState(seed);
                return Random.value <= layer.randomDensity;

            case PatternType.Diagonal:
                return (x - y) % 3 == 0;

            case PatternType.OffsetRows:
                if (y % 2 == 0)
                    return x % 2 != 0;
                else
                    return x % 2 == 0;

            case PatternType.BorderOnly:
                // Always show border (this pattern doesn't work well with infinite scrolling)
                return false;

            case PatternType.DenseCenter:
                // Center pattern doesn't work well with infinite scrolling
                return false;

            default:
                return true;
        }
    }

    bool ShouldPlaceObject(ParallaxLayer layer, int x, int y, int maxX, int maxY)
    {
        switch (layer.patternType)
        {
            case PatternType.Full:
                return true;

            case PatternType.Checkerboard:
                return (x + y) % 2 != 0;

            case PatternType.HorizontalStripes:
                return y % 2 != 0;

            case PatternType.VerticalStripes:
                return x % 2 != 0;

            case PatternType.SparseGrid:
                return (x % layer.gridSpacing == 0) && (y % layer.gridSpacing == 0);

            case PatternType.RandomSparse:
                return Random.value <= layer.randomDensity;

            case PatternType.Diagonal:
                return (x - y) % 3 == 0;

            case PatternType.OffsetRows:
                if (y % 2 == 0)
                    return x % 2 != 0;
                else
                    return x % 2 == 0;

            case PatternType.BorderOnly:
                return (x == 0 || x == maxX - 1 || y == 0 || y == maxY - 1);

            case PatternType.DenseCenter:
                int centerX = maxX / 2;
                int centerY = maxY / 2;
                float distance = Mathf.Sqrt(Mathf.Pow(x - centerX, 2) + Mathf.Pow(y - centerY, 2));
                return distance <= layer.centerRadius;

            default:
                return true;
        }
    }

    void SortRowsIntoGroups(GameObject parent)
    {
        Transform[] children = parent.GetComponentsInChildren<Transform>(true); // Include inactive
        if (children.Length <= 1) return;

        Dictionary<float, List<Transform>> rowsByY = new Dictionary<float, List<Transform>>();

        for (int i = 1; i < children.Length; i++)
        {
            Transform child = children[i];
            float yPos = Mathf.Round(child.position.y * 10) / 10f;

            if (!rowsByY.ContainsKey(yPos))
            {
                rowsByY[yPos] = new List<Transform>();
            }
            rowsByY[yPos].Add(child);
        }

        foreach (var row in rowsByY.Values)
        {
            row.Sort((a, b) => a.position.x.CompareTo(b.position.x));
        }

        float[] yPositions = new float[rowsByY.Count];
        rowsByY.Keys.CopyTo(yPositions, 0);
        System.Array.Sort(yPositions);

        List<Transform>[] sortedRows = new List<Transform>[yPositions.Length];
        for (int i = 0; i < yPositions.Length; i++)
        {
            sortedRows[i] = rowsByY[yPositions[i]];
        }

        rowsDict[parent] = sortedRows;
    }

    void SortColumnsIntoGroups(GameObject parent)
    {
        Transform[] children = parent.GetComponentsInChildren<Transform>(true); // Include inactive
        if (children.Length <= 1) return;

        Dictionary<float, List<Transform>> columnsByX = new Dictionary<float, List<Transform>>();

        for (int i = 1; i < children.Length; i++)
        {
            Transform child = children[i];
            float xPos = Mathf.Round(child.position.x * 10) / 10f;

            if (!columnsByX.ContainsKey(xPos))
            {
                columnsByX[xPos] = new List<Transform>();
            }
            columnsByX[xPos].Add(child);
        }

        foreach (var column in columnsByX.Values)
        {
            column.Sort((a, b) => a.position.y.CompareTo(b.position.y));
        }

        float[] xPositions = new float[columnsByX.Count];
        columnsByX.Keys.CopyTo(xPositions, 0);
        System.Array.Sort(xPositions);

        List<Transform>[] sortedColumns = new List<Transform>[xPositions.Length];
        for (int i = 0; i < xPositions.Length; i++)
        {
            sortedColumns[i] = columnsByX[xPositions[i]];
        }

        columnsDict[parent] = sortedColumns;
    }

    private void RefreshScreenBounds()
    {
        if (mainCamera == null) return;

        if (mainCamera.orthographic)
        {
            float halfHeight = mainCamera.orthographicSize;
            float halfWidth = halfHeight * mainCamera.aspect;
            screenBounds = new Vector2(halfWidth, halfHeight);
        }
        else
        {
            // For perspective (less likely in your 2D setup)
            float distance = Mathf.Abs(mainCamera.transform.position.z);
            Vector3 topRight = mainCamera.ScreenToWorldPoint(
                new Vector3(Screen.width, Screen.height, distance)
            );

            screenBounds = new Vector2(
                Mathf.Abs(topRight.x - mainCamera.transform.position.x),
                Mathf.Abs(topRight.y - mainCamera.transform.position.y)
            );
        }
    }
    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // If this object survives scene reload (persistent camera, etc.),
        // rebuild generated parallax objects for the new scene.
        StartCoroutine(RebuildAfterSceneLoad());
    }

    private IEnumerator RebuildAfterSceneLoad()
    {
        // Wait a frame so CameraFollow / hero registration can settle camera position
        yield return null;

        ClearGeneratedParallax();
        yield return InitNextFrame();
    }
    private void ClearGeneratedParallax()
    {
        // Destroy generated container GameObjects (if they still exist)
        for (int i = 0; i < instancedLevels.Count; i++)
        {
            if (instancedLevels[i] != null)
                Destroy(instancedLevels[i]);
        }

        instancedLevels.Clear();
        rowsDict.Clear();
        columnsDict.Clear();
        parallaxSpeedsDict.Clear();
        layerByContainer.Clear();
        gridCoordinates.Clear();

        _initialized = false;

        if (mainCamera != null)
            lastCameraPosition = mainCamera.transform.position;
    }
    private bool HasAnyValidContainer()
    {
        if (instancedLevels == null || instancedLevels.Count == 0)
            return false;

        for (int i = 0; i < instancedLevels.Count; i++)
        {
            if (instancedLevels[i] != null)
                return true;
        }

        return false;
    }
}
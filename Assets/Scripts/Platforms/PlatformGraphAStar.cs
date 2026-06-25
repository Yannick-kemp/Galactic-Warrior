using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Platform graph + A* with agent-specific reachability settings.
/// </summary>
public class PlatformGraphAStar : MonoBehaviour
{
    public static PlatformGraphAStar Instance { get; private set; }

    [System.Serializable]
    public struct ReachabilitySettings
    {
        [Header("Reachability Settings")]
        [Tooltip("Maximum height the agent can jump upwards.")]
        public float maxJumpUp;

        [Tooltip("Maximum distance the agent can drop down.")]
        public float maxDropDown;

        [Tooltip("Maximum horizontal distance between platform edges.")]
        public float maxHorizontalGap;

        [Header("Physics Checks")]
        [Tooltip("Layers that block movement (e.g., Walls, Ground). Used to prevent jumping through terrain.")]
        public LayerMask obstacleMask;

        [Tooltip("Radius of the check line to ensure the agent body fits.")]
        public float bodyWidthCheck;

        public static ReachabilitySettings Default(float jumpUp, float dropDown, float gap, LayerMask mask, float bodyWidth)
        {
            return new ReachabilitySettings
            {
                maxJumpUp = jumpUp,
                maxDropDown = dropDown,
                maxHorizontalGap = gap,
                obstacleMask = mask,
                bodyWidthCheck = bodyWidth
            };
        }
    }

    [Header("DEFAULT Reachability Settings (used for graph links preview)")]
    public float maxJumpUp = 7f;
    public float maxDropDown = 12f;
    public float maxHorizontalGap = 16f;

    [Header("DEFAULT Physics Checks")]
    public LayerMask obstacleMask;
    public float bodyWidthCheck = 0.5f;

    [Header("Debug")]
    public bool drawGraph = true;
    public bool drawPath = true;

    private readonly List<PlatformNode> _nodes = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start() => RebuildGraph();

    [ContextMenu("Rebuild Graph")]
    public void RebuildGraph()
    {
        _nodes.Clear();

        // Find all triggers
        var triggers = FindObjectsByType<PlatFormColliderTrigger>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        // Create Nodes
        foreach (var trigger in triggers)
        {
            if (trigger != null && trigger.platformCollider != null)
                _nodes.Add(new PlatformNode(trigger));
        }

        // Connect nodes with DEFAULT settings (for debug visualization).
        // Pathfinding can override reachability per-agent.
        var defaultSettings = ReachabilitySettings.Default(maxJumpUp, maxDropDown, maxHorizontalGap, obstacleMask, bodyWidthCheck);

        foreach (var nodeA in _nodes)
        {
            foreach (var nodeB in _nodes)
            {
                if (nodeA == nodeB) continue;

                if (IsReachable(nodeA, nodeB, defaultSettings))
                    nodeA.Neighbors.Add(nodeB);
            }
        }

        Debug.Log($"[A* GRAPH] Rebuilt with {_nodes.Count} nodes and {_nodes.Sum(n => n.Neighbors.Count)} edges.");
    }

    /// <summary>
    /// Agent-specific reachability test (used by A* when expanding neighbors).
    /// </summary>
    private bool IsReachable(PlatformNode a, PlatformNode b, ReachabilitySettings settings)
    {
        if (a == null || b == null) return false;

        a.RefreshGeometry();
        b.RefreshGeometry();

        // Use the swept-travel envelope (motion platforms widen this). For static
        // platforms it equals the current collider bounds, so this is a no-op there.
        // This keeps edges to/from moving platforms stable across the whole motion
        // cycle instead of flickering with the platform's instantaneous position.
        Bounds A = a.ReachabilityBounds;
        Bounds B = b.ReachabilityBounds;

        // Horizontal gap between platform edges
        float gapX = 0f;
        if (B.min.x > A.max.x) gapX = B.min.x - A.max.x;      // B is right
        else if (A.min.x > B.max.x) gapX = A.min.x - B.max.x; // B is left

        if (gapX > settings.maxHorizontalGap) return false;

        // Vertical delta between tops
        float dy = B.max.y - A.max.y;
        if (dy > settings.maxJumpUp) return false;
        if (-dy > settings.maxDropDown) return false;

        // Physics LOS check (avoid walls)
        if (settings.obstacleMask != 0)
        {
            Vector2 start = a.TopCenter + Vector2.up * 0.5f;
            Vector2 end = b.TopCenter + Vector2.up * 0.5f;

            var dir = (end - start);
            float dist = dir.magnitude;
            if (dist > 0.001f)
            {
                var hit = Physics2D.CircleCast(start, settings.bodyWidthCheck, dir.normalized, dist, settings.obstacleMask);
                if (hit.collider != null)
                {
                    // If we hit something that isn't A or B platform objects, block
                    if (hit.collider.gameObject != b.Platform.gameObject && hit.collider.gameObject != a.Platform.gameObject)
                        return false;
                }
            }
        }

        return true;
    }

    public PlatformNode GetNode(PlatFormColliderTrigger trigger)
    {
        return _nodes.FirstOrDefault(n => n.Platform == trigger);
    }

    public List<PlatformNode> FindPath(PlatformNode startNode, PlatformNode targetNode, ReachabilitySettings? overrideSettings = null)
    {
        if (startNode == null || targetNode == null) return null;
        if (startNode == targetNode) return new List<PlatformNode> { startNode };

        var settings = overrideSettings ?? ReachabilitySettings.Default(maxJumpUp, maxDropDown, maxHorizontalGap, obstacleMask, bodyWidthCheck);

        List<PlatformNode> openSet = new List<PlatformNode>();
        HashSet<PlatformNode> closedSet = new HashSet<PlatformNode>();

        openSet.Add(startNode);

        foreach (var n in _nodes)
        {
            n.gCost = float.MaxValue;
            n.hCost = 0;
            n.Parent = null;
        }

        startNode.gCost = 0;
        startNode.hCost = Vector2.Distance(startNode.TopCenter, targetNode.TopCenter);

        while (openSet.Count > 0)
        {
            PlatformNode current = openSet[0];
            for (int i = 1; i < openSet.Count; i++)
            {
                if (openSet[i].fCost < current.fCost ||
                    (openSet[i].fCost == current.fCost && openSet[i].hCost < current.hCost))
                {
                    current = openSet[i];
                }
            }

            openSet.Remove(current);
            closedSet.Add(current);

            if (current == targetNode)
                return RetracePath(startNode, targetNode);

            // IMPORTANT: expand neighbors using reachability for THIS AGENT
            foreach (var neighbor in _nodes)
            {
                if (neighbor == null || neighbor == current) continue;
                if (closedSet.Contains(neighbor)) continue;

                if (!IsReachable(current, neighbor, settings))
                    continue;

                float moveCost = Vector2.Distance(current.TopCenter, neighbor.TopCenter);
                float newMovementCostToNeighbor = current.gCost + moveCost;

                if (newMovementCostToNeighbor < neighbor.gCost || !openSet.Contains(neighbor))
                {
                    neighbor.gCost = newMovementCostToNeighbor;
                    neighbor.hCost = Vector2.Distance(neighbor.TopCenter, targetNode.TopCenter);
                    neighbor.Parent = current;

                    if (!openSet.Contains(neighbor))
                        openSet.Add(neighbor);
                }
            }
        }

        return null;
    }

    public List<PlatformNode> FindPath(PlatFormColliderTrigger startTrigger, PlatFormColliderTrigger targetTrigger, ReachabilitySettings? overrideSettings = null)
    {
        if (startTrigger == null || targetTrigger == null) return null;
        var startNode = GetNode(startTrigger);
        var targetNode = GetNode(targetTrigger);
        return FindPath(startNode, targetNode, overrideSettings);
    }

    private List<PlatformNode> RetracePath(PlatformNode start, PlatformNode end)
    {
        List<PlatformNode> path = new List<PlatformNode>();
        PlatformNode currentNode = end;

        while (currentNode != null && currentNode != start)
        {
            path.Add(currentNode);
            currentNode = currentNode.Parent;
        }

        path.Add(start);
        path.Reverse();
        return path;
    }

    private void OnDrawGizmos()
    {
        if (!drawGraph || _nodes == null) return;

        Gizmos.color = new Color(1, 1, 1, 0.3f);
        foreach (var node in _nodes)
        {
            if (node == null) continue;
            foreach (var neighbor in node.Neighbors)
            {
                if (neighbor == null) continue;
                Gizmos.DrawLine(node.TopCenter, neighbor.TopCenter);
            }
        }
    }
}

[System.Serializable]
public class PlatformNode
{
    public PlatFormColliderTrigger Platform;
    public Bounds Bounds;
    public Vector2 TopCenter;

    // Bounds used only by the reachability test. For motion platforms this is the full
    // swept envelope of their travel; for static platforms it equals Bounds.
    public Bounds ReachabilityBounds;

    public List<PlatformNode> Neighbors = new List<PlatformNode>();

    public PlatformNode Parent;
    public float gCost;
    public float hCost;
    public float fCost => gCost + hCost;

    public PlatformNode(PlatFormColliderTrigger platform)
    {
        Platform = platform;
        RefreshGeometry();
    }

    public void RefreshGeometry()
    {
        if (Platform != null && Platform.platformCollider != null)
        {
            Bounds = Platform.platformCollider.bounds;
            TopCenter = new Vector2(Bounds.center.x, Bounds.max.y);
            ReachabilityBounds = Platform.GetReachabilityBounds();
        }
    }
}

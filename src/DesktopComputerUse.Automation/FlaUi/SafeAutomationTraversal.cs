using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.FlaUi;

public sealed record AutomationNodeCapture<T>(
    T Element,
    string CandidateId,
    string? ParentCandidateId,
    int Depth,
    SafeAutomationElementReader Reader)
{
    public bool Truncated { get; init; }
}

public sealed record AutomationTraversalResult<T>(
    IReadOnlyList<AutomationNodeCapture<T>> Nodes,
    bool Truncated,
    IReadOnlyList<AutomationDiagnostic> Failures)
{
    public bool Partial => Failures.Count > 0;
}

public static class SafeAutomationTraversal
{
    public static AutomationTraversalResult<T> Capture<T>(
        T root,
        Func<T, T[]> getChildren,
        int maxDepth,
        int maxResults,
        AutomationDiagnostic context,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        var nodes = new List<AutomationNodeCapture<T>>();
        var failures = new List<AutomationDiagnostic>();
        var queue = new Queue<(T Element, string? ParentId, int Depth)>();
        queue.Enqueue((root, null, 0));
        var truncated = false;
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            truncated |= CaptureNode(queue, nodes, getChildren, failures, context, maxDepth, maxResults);
        }
        return new(nodes, truncated, failures);
    }

    private static bool CaptureNode<T>(
        Queue<(T Element, string? ParentId, int Depth)> queue,
        List<AutomationNodeCapture<T>> nodes,
        Func<T, T[]> getChildren,
        ICollection<AutomationDiagnostic> failures,
        AutomationDiagnostic context,
        int maxDepth,
        int maxResults)
    {
        var item = queue.Dequeue();
        var id = $"node-{nodes.Count + 1:0000}";
        var reader = new SafeAutomationElementReader(
            context with { CandidateId = id, Depth = item.Depth }, failures);
        nodes.Add(new(item.Element, id, item.ParentId, item.Depth, reader));
        var children = reader.ReadChildren(() => getChildren(item.Element));
        var truncated = item.Depth >= maxDepth
            ? children.Length > 0
            : QueueChildren(queue, children, id, item.Depth + 1, maxResults - nodes.Count - queue.Count);
        nodes[^1] = nodes[^1] with { Truncated = truncated };
        return truncated;
    }

    private static bool QueueChildren<T>(
        Queue<(T Element, string? ParentId, int Depth)> queue,
        T[] children,
        string parentId,
        int depth,
        int capacity)
    {
        foreach (var child in children.Take(capacity))
            queue.Enqueue((child, parentId, depth));
        return children.Length > capacity;
    }
}

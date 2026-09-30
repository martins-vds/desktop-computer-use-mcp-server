using System.Security.Cryptography;
using System.Text;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;
using FlaUI.Core.AutomationElements;

namespace DesktopComputerUse.Automation.Discovery;

public sealed class ApplicationSnapshotBuilder
{
    private readonly ControlObserver _observer;

    public ApplicationSnapshotBuilder(ControlObserver observer)
    {
        _observer = observer;
    }

    internal ApplicationSnapshot Build(
        AutomationSession session,
        int maxDepth,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var nodes = CaptureNodes(
            session.MainWindow,
            maxDepth,
            maxResults,
            cancellationToken,
            out var isComplete);
        var runtimeLookup = nodes
            .Where(node => node.RuntimeKey is not null)
            .GroupBy(node => node.RuntimeKey!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().CandidateId,
                StringComparer.Ordinal);

        var snapshots = nodes
            .Select(node => BuildSnapshot(
                node,
                nodes,
                runtimeLookup,
                session.Profile,
                session.MainWindow.BoundingRectangle))
            .ToArray();

        var windowSummary = snapshots[0];
        var view = BuildViewSignature(session.MainWindow, snapshots);

        return new ApplicationSnapshot(
            session.Profile.Id,
            session.Application.ProcessId,
            session.Profile.ExecutablePath,
            session.Profile.Backend,
            DateTimeOffset.UtcNow,
            new WindowSnapshot(
                windowSummary.CandidateId,
                session.MainWindow.Title,
                windowSummary.AutomationId,
                windowSummary.ClassName,
                windowSummary.Bounds,
                view,
                snapshots,
                isComplete));
    }

    private static IReadOnlyList<NodeCapture> CaptureNodes(
        AutomationElement root,
        int maxDepth,
        int maxResults,
        CancellationToken cancellationToken,
        out bool isComplete)
    {
        var nodes = new List<NodeCapture>(Math.Min(maxResults, 256));
        var queue = new Queue<(AutomationElement Element, string? ParentId, int Depth, IReadOnlyList<string> Path)>();
        queue.Enqueue((root, null, 0, []));
        var complete = true;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nodes.Count >= maxResults)
            {
                complete = false;
                break;
            }

            complete &= CaptureNode(
                queue.Dequeue(),
                queue,
                nodes,
                maxDepth);
        }

        isComplete = complete;
        return nodes;
    }

    private static bool CaptureNode(
        (AutomationElement Element, string? ParentId, int Depth, IReadOnlyList<string> Path) item,
        Queue<(AutomationElement Element, string? ParentId, int Depth, IReadOnlyList<string> Path)> queue,
        ICollection<NodeCapture> nodes,
        int maxDepth)
    {
        var candidateId = $"node-{nodes.Count + 1:0000}";
        var path = item.Path.Append(DescribeForPath(item.Element)).ToArray();
        nodes.Add(new NodeCapture(
            candidateId,
            item.ParentId,
            item.Element,
            item.Depth,
            path,
            GetRuntimeKey(item.Element)));
        var children = SafeGet(item.Element.FindAllChildren, []);
        if (item.Depth >= maxDepth)
        {
            return children.Length == 0;
        }

        foreach (var child in children)
        {
            queue.Enqueue((child, candidateId, item.Depth + 1, path));
        }

        return true;
    }

    private ControlSnapshot BuildSnapshot(
        NodeCapture node,
        IReadOnlyList<NodeCapture> nodes,
        IReadOnlyDictionary<string, string> runtimeLookup,
        ApplicationProfile profile,
        System.Drawing.Rectangle windowBounds)
    {
        var summary = _observer.Observe(node.Element, profile);
        var children = GetChildIds(node, nodes);
        var siblings = GetSiblingIds(node, nodes);
        var labeledById = GetLabeledById(node.Element, runtimeLookup);
        var isPassword = SafeGet(
            () => node.Element.Properties.IsPassword.ValueOrDefault,
            false);
        var valueState = GetValueState(summary, isPassword);

        return new ControlSnapshot
        {
            CandidateId = node.CandidateId,
            ParentCandidateId = node.ParentCandidateId,
            ChildCandidateIds = children,
            SiblingCandidateIds = siblings,
            Name = summary.Name,
            AutomationId = summary.AutomationId,
            ControlType = summary.ControlType,
            LocalizedControlType = NullIfEmpty(SafeGet(
                () => node.Element.Properties.LocalizedControlType.ValueOrDefault,
                string.Empty)),
            ClassName = summary.ClassName,
            FrameworkId = NullIfEmpty(SafeGet(
                () => node.Element.FrameworkType.ToString(),
                string.Empty)),
            HelpText = NullIfEmpty(SafeGet(() => node.Element.HelpText, string.Empty)),
            AccessKey = NullIfEmpty(SafeGet(
                () => node.Element.Properties.AccessKey.ValueOrDefault,
                string.Empty)),
            AcceleratorKey = NullIfEmpty(SafeGet(
                () => node.Element.Properties.AcceleratorKey.ValueOrDefault,
                string.Empty)),
            IsEnabled = summary.IsEnabled,
            IsOffscreen = summary.IsOffscreen,
            IsKeyboardFocusable = SafeGet(
                () => node.Element.Properties.IsKeyboardFocusable.ValueOrDefault,
                false),
            IsPassword = isPassword,
            IsValueRedacted = valueState.IsRedacted,
            Value = valueState.Value,
            Bounds = summary.Bounds,
            RelativeBounds = RelativeBounds(summary.Bounds, windowBounds),
            SupportedPatterns = summary.SupportedPatterns,
            LabeledByCandidateId = labeledById,
            NearbyLabels = FindNearbyLabels(node, nodes, labeledById),
            TreePath = node.TreePath
        };
    }

    private static (bool IsRedacted, string? Value) GetValueState(
        ControlSummary summary,
        bool isPassword)
    {
        var redacted = summary.IsValueRedacted || isPassword;
        return (redacted, redacted ? null : summary.Value);
    }

    private static IReadOnlyList<string> GetChildIds(
        NodeCapture node,
        IReadOnlyList<NodeCapture> nodes)
        => nodes
            .Where(candidate => candidate.ParentCandidateId == node.CandidateId)
            .Select(candidate => candidate.CandidateId)
            .ToArray();

    private static IReadOnlyList<string> GetSiblingIds(
        NodeCapture node,
        IReadOnlyList<NodeCapture> nodes)
        => node.ParentCandidateId is null
            ? []
            : nodes
                .Where(candidate =>
                    candidate.ParentCandidateId == node.ParentCandidateId &&
                    candidate.CandidateId != node.CandidateId)
                .Select(candidate => candidate.CandidateId)
                .ToArray();

    private static string? GetLabeledById(
        AutomationElement element,
        IReadOnlyDictionary<string, string> runtimeLookup)
    {
        var labeledBy = SafeGet(
            () => element.Properties.LabeledBy.ValueOrDefault,
            null);
        return labeledBy is null
            ? null
            : runtimeLookup.GetValueOrDefault(GetRuntimeKey(labeledBy) ?? string.Empty);
    }

    private static IReadOnlyList<NearbyLabel> FindNearbyLabels(
        NodeCapture node,
        IReadOnlyList<NodeCapture> nodes,
        string? labeledById)
    {
        var labels = CreateExplicitLabels(nodes, labeledById);
        MergeLabels(labels, FindGeometricLabels(node, nodes));
        return labels;
    }

    private static IReadOnlyList<NearbyLabel> FindGeometricLabels(
        NodeCapture node,
        IReadOnlyList<NodeCapture> nodes)
    {
        var target = SafeGet(
            () => node.Element.BoundingRectangle,
            System.Drawing.Rectangle.Empty);
        if (node.ParentCandidateId is null || target.IsEmpty)
        {
            return [];
        }

        return nodes
            .Where(candidate => IsSiblingText(candidate, node))
            .Select(candidate => CreateGeometricLabel(candidate, target))
            .Where(label => label is not null)
            .Cast<NearbyLabel>()
            .OrderByDescending(label => label.Confidence)
            .ThenBy(label => label.Distance)
            .Take(3)
            .ToArray();
    }

    private static bool IsSiblingText(NodeCapture candidate, NodeCapture node)
        => candidate.ParentCandidateId == node.ParentCandidateId &&
            candidate.CandidateId != node.CandidateId &&
            SafeGet(() => candidate.Element.ControlType.ToString(), string.Empty) == "Text";

    private static void MergeLabels(
        ICollection<NearbyLabel> labels,
        IEnumerable<NearbyLabel> additions)
    {
        foreach (var label in additions.Where(label =>
                     labels.All(existing => existing.CandidateId != label.CandidateId)))
        {
            labels.Add(label);
        }
    }

    private static List<NearbyLabel> CreateExplicitLabels(
        IReadOnlyList<NodeCapture> nodes,
        string? labeledById)
    {
        var text = GetExplicitLabelText(nodes, labeledById);
        return text is null
            ? []
            : [new NearbyLabel(labeledById!, text, "labeledBy", 0, 1)];
    }

    private static string? GetExplicitLabelText(
        IReadOnlyList<NodeCapture> nodes,
        string? labeledById)
        => labeledById is null
            ? null
            : NullIfEmpty(SafeGet(
                () => nodes.FirstOrDefault(candidate =>
                    candidate.CandidateId == labeledById)?.Element.Name ?? string.Empty,
                string.Empty));

    private static NearbyLabel? CreateGeometricLabel(
        NodeCapture candidate,
        System.Drawing.Rectangle target)
    {
        var text = NullIfEmpty(SafeGet(() => candidate.Element.Name, string.Empty));
        var bounds = SafeGet(
            () => candidate.Element.BoundingRectangle,
            System.Drawing.Rectangle.Empty);
        if (text is null || bounds.IsEmpty)
        {
            return null;
        }

        return NearbyLabelGeometry.Create(
            candidate.CandidateId,
            text,
            new RectangleInfo(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            new RectangleInfo(target.X, target.Y, target.Width, target.Height));
    }

    private static ViewSignature BuildViewSignature(
        Window window,
        IReadOnlyList<ControlSnapshot> controls)
    {
        var automationIds = controls
            .Where(control => !control.IsOffscreen)
            .Select(control => control.AutomationId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(100)
            .ToArray();
        var names = controls
            .Where(control => !control.IsOffscreen && !control.IsPassword)
            .Select(control => control.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(100)
            .ToArray();

        var signatureText = string.Join(
            "\n",
            new[] { window.Title ?? string.Empty, window.ClassName ?? string.Empty }
                .Concat(automationIds)
                .Concat(names));
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(signatureText))).ToLowerInvariant();

        return new ViewSignature(
            $"view-{hash[..12]}",
            hash,
            TextNormalizer.Tokens(window.Title),
            automationIds,
            names);
    }

    private static RectangleInfo RelativeBounds(
        RectangleInfo bounds,
        System.Drawing.Rectangle window)
    {
        if (window.Width <= 0 || window.Height <= 0)
        {
            return new RectangleInfo(0, 0, 0, 0);
        }

        return new RectangleInfo(
            (bounds.X - window.X) / window.Width,
            (bounds.Y - window.Y) / window.Height,
            bounds.Width / window.Width,
            bounds.Height / window.Height);
    }

    private static string DescribeForPath(AutomationElement element)
        => NullIfEmpty(SafeGet(() => element.AutomationId, string.Empty))
            ?? NullIfEmpty(SafeGet(() => element.Name, string.Empty))
            ?? SafeGet(() => element.ControlType.ToString(), "Unknown");

    private static string? GetRuntimeKey(AutomationElement element)
    {
        var runtimeId = SafeGet(
            () => element.Properties.RuntimeId.ValueOrDefault,
            []);
        return runtimeId is null || runtimeId.Length == 0
            ? null
            : string.Join(".", runtimeId);
    }

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record NodeCapture(
        string CandidateId,
        string? ParentCandidateId,
        AutomationElement Element,
        int Depth,
        IReadOnlyList<string> TreePath,
        string? RuntimeKey);
}

using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using FlaUI.Core.AutomationElements;

namespace DesktopComputerUse.Automation.FlaUi;

public sealed class ControlObserver
{
    public ControlSummary Observe(AutomationElement element, ApplicationProfile profile)
        => Observe(element, profile, new SafeAutomationElementReader(
            new AutomationDiagnostic
            {
                Operation = "observeControl", ProfileId = profile.Id,
                ProfileRevision = profile.Metadata?.Revision
            }));

    public ControlSummary Observe(
        AutomationElement element,
        ApplicationProfile profile,
        SafeAutomationElementReader reader)
        => Observe(new ControlObservationSource(
            () => element.AutomationId,
            () => element.Properties.IsPassword.Value,
            () => element.Name,
            () => element.ControlType.ToString(),
            () => element.ClassName,
            () => element.IsEnabled,
            () => element.IsOffscreen,
            () => element.BoundingRectangle,
            () => element.Patterns.Value.TryGetPattern(out var pattern)
                ? NullIfEmpty(pattern.Value.Value) : null,
            new Dictionary<string, Func<bool>>
            {
                ["Invoke"] = () => element.Patterns.Invoke.IsSupported,
                ["Value"] = () => element.Patterns.Value.IsSupported,
                ["SelectionItem"] = () => element.Patterns.SelectionItem.IsSupported,
                ["ExpandCollapse"] = () => element.Patterns.ExpandCollapse.IsSupported,
                ["Scroll"] = () => element.Patterns.Scroll.IsSupported
            }), profile, reader);

    internal static ControlSummary Observe(
        ControlObservationSource source,
        ApplicationProfile profile,
        SafeAutomationElementReader reader)
    {
        var start = reader.Failures.Count;
        var (id, password, sensitive) = ReadSensitivity(reader, profile,
            source.AutomationId, source.IsPassword);
        var name = reader.Read("Name", source.Name, string.Empty);
        var controlType = reader.Read("ControlType", source.ControlType, "Unknown");
        var className = reader.Read("ClassName", source.ClassName, string.Empty);
        var enabled = reader.Read("IsEnabled", source.IsEnabled, false);
        var offscreen = reader.Read("IsOffscreen", source.IsOffscreen, true);
        var bounds = reader.Read("BoundingRectangle", source.Bounds,
            System.Drawing.Rectangle.Empty);
        var patterns = new List<string>();
        foreach (var (pattern, supported) in source.Patterns)
        {
            if (reader.Read($"Patterns.{pattern}", supported, false))
                patterns.Add(pattern);
        }
        var value = sensitive ? null : NullIfEmpty(reader.Read("Value", source.Value, null));
        return new ControlSummary(
            NullIfEmpty(name), NullIfEmpty(id), controlType, NullIfEmpty(className),
            enabled, offscreen,
            new RectangleInfo(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            value, sensitive, patterns)
        {
            PrivacyMode = profile.PrivacyMode,
            Failures = reader.Failures.Skip(start).ToArray(),
            IsPassword = password
        };
    }

    internal static (string? AutomationId, bool IsPassword, bool IsSensitive) ReadSensitivity(
        SafeAutomationElementReader reader,
        ApplicationProfile profile,
        Func<string?> getAutomationId,
        Func<bool> getPassword)
    {
        var hasId = reader.TryRead("AutomationId", getAutomationId, out var id);
        var hasPassword = reader.TryRead("IsPassword", getPassword, out var password);
        // Failed sensitivity checks fail closed unless privacy is explicitly disabled.
        return (id, !hasPassword || password,
            profile.PrivacyMode && (!hasId || !hasPassword || password ||
            (!string.IsNullOrWhiteSpace(id) &&
             profile.SensitiveAutomationIds.Contains(id, StringComparer.OrdinalIgnoreCase))));
    }

    public ControlTreeNode Inspect(
        AutomationElement root,
        ApplicationProfile profile,
        int maxDepth,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var capture = SafeAutomationTraversal.Capture(
            root, element => element.FindAllChildren(), maxDepth, maxResults,
            new AutomationDiagnostic
            {
                Operation = "inspectControls", ProfileId = profile.Id,
                ProfileRevision = profile.Metadata?.Revision
            },
            cancellationToken);
        return BuildTree(capture, node => Observe(node.Element, profile, node.Reader), cancellationToken);
    }

    internal static ControlTreeNode BuildTree<T>(
        AutomationTraversalResult<T> capture,
        Func<AutomationNodeCapture<T>, ControlSummary> observe,
        CancellationToken cancellationToken = default)
    {
        var lookup = new Dictionary<string, ControlTreeNode>();
        foreach (var node in capture.Nodes.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summary = observe(node);
            var children = capture.Nodes
                .Where(child => child.ParentCandidateId == node.CandidateId)
                .Select(child => lookup[child.CandidateId]).ToArray();
            var failures = capture.Failures
                .Where(failure => failure.CandidateId == node.CandidateId)
                .Concat(children.SelectMany(child => child.Failures)).ToArray();
            lookup[node.CandidateId] = new(summary, children)
            {
                CandidateId = node.CandidateId,
                Depth = node.Depth,
                Failures = failures,
                Partial = failures.Length > 0,
                Truncated = node.Truncated || children.Any(child => child.Truncated)
            };
        }
        return lookup[capture.Nodes[0].CandidateId];
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrEmpty(value) ? null : value;
}

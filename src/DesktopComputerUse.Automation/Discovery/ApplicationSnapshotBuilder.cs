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

public sealed class ApplicationSnapshotBuilder(ControlObserver observer)
{
    internal ApplicationSnapshot Build(
        AutomationSession session,
        int maxDepth,
        int maxResults,
        CancellationToken cancellationToken)
        => Build(session.MainWindow, session.Profile, session.Application.ProcessId,
            maxDepth, maxResults, cancellationToken);

    public ApplicationSnapshot Build(
        AutomationElement root,
        ApplicationProfile profile,
        int processId,
        int maxDepth,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var capture = SafeAutomationTraversal.Capture(
            root, element => element.FindAllChildren(), maxDepth, maxResults,
            new AutomationDiagnostic
            {
                Operation = "snapshotApplicationSchema", ProfileId = profile.Id, ProcessId = processId,
                ProfileRevision = profile.Metadata?.Revision
            }, cancellationToken);
        var summaries = capture.Nodes.ToDictionary(
            node => node.CandidateId,
            node => observer.Observe(node.Element, profile, node.Reader));
        var windowBounds = summaries[capture.Nodes[0].CandidateId].Bounds;
        var runtimeLookup = BuildRuntimeLookup(capture.Nodes, cancellationToken);
        var controls = capture.Nodes.Select(node =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BuildSnapshot(node, summaries[node.CandidateId], capture.Nodes,
                runtimeLookup, windowBounds);
        }).ToArray();
        EnrichControls(controls, capture.Failures);
        var window = controls[0];
        return new ApplicationSnapshot(
            profile.Id, processId, profile.ExecutablePath, profile.Backend, DateTimeOffset.UtcNow,
            new WindowSnapshot(window.CandidateId, window.Name, window.AutomationId,
                window.ClassName, window.Bounds, BuildViewSignature(window, controls),
                controls, !capture.Partial && !capture.Truncated)
            {
                Partial = capture.Partial,
                Truncated = capture.Truncated,
                Failures = capture.Failures.ToArray()
            });
    }

    private static IReadOnlyDictionary<string, string> BuildRuntimeLookup(
        IReadOnlyList<AutomationNodeCapture<AutomationElement>> nodes,
        CancellationToken cancellationToken)
    {
        var runtimeLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = ReadRuntimeKey(node.Element, node.Reader);
            AddRuntimeKey(runtimeLookup, key, node.CandidateId);
        }
        return runtimeLookup;
    }

    internal static void AddRuntimeKey(IDictionary<string, string> lookup, string? key, string candidateId)
    {
        if (key is not null)
            lookup.TryAdd(key, candidateId);
    }

    internal static void EnrichControls(
        ControlSnapshot[] controls, IReadOnlyList<AutomationDiagnostic> failures)
    {
        var lookup = controls.ToDictionary(control => control.CandidateId, StringComparer.Ordinal);
        for (var index = 0; index < controls.Length; index++)
        {
            var control = controls[index];
            controls[index] = control with
            {
                TreePath = BuildPath(control, lookup),
                NearbyLabels = FindNearbyLabels(control, controls, lookup),
                Failures = failures.Where(failure =>
                    failure.CandidateId == control.CandidateId).ToArray()
            };
        }
    }

    private static ControlSnapshot BuildSnapshot(
        AutomationNodeCapture<AutomationElement> node,
        ControlSummary summary,
        IReadOnlyList<AutomationNodeCapture<AutomationElement>> nodes,
        IReadOnlyDictionary<string, string> runtimeLookup,
        RectangleInfo windowBounds)
    {
        var reader = node.Reader;
        var element = node.Element;
        var labeledById = GetLabeledById(element, reader, runtimeLookup);
        // Observe has already applied fail-closed password/profile redaction.
        var password = summary.IsPassword;
        var redacted = summary.IsValueRedacted || password;
        return new ControlSnapshot
        {
            CandidateId = node.CandidateId,
            ParentCandidateId = node.ParentCandidateId,
            Depth = node.Depth,
            Truncated = node.Truncated,
            ChildCandidateIds = nodes.Where(child => child.ParentCandidateId == node.CandidateId)
                .Select(child => child.CandidateId).ToArray(),
            SiblingCandidateIds = GetSiblingIds(node, nodes),
            Name = summary.Name,
            AutomationId = summary.AutomationId,
            ControlType = summary.ControlType,
            ClassName = summary.ClassName,
            LocalizedControlType = NullIfEmpty(reader.Read("LocalizedControlType",
                () => element.Properties.LocalizedControlType.Value, string.Empty)),
            FrameworkId = NullIfEmpty(reader.Read("FrameworkId",
                () => element.FrameworkType.ToString(), string.Empty)),
            HelpText = NullIfEmpty(reader.Read("HelpText", () => element.HelpText, string.Empty)),
            AccessKey = NullIfEmpty(reader.Read("AccessKey",
                () => element.Properties.AccessKey.Value, string.Empty)),
            AcceleratorKey = NullIfEmpty(reader.Read("AcceleratorKey",
                () => element.Properties.AcceleratorKey.Value, string.Empty)),
            IsEnabled = summary.IsEnabled,
            IsOffscreen = summary.IsOffscreen,
            IsKeyboardFocusable = reader.Read("IsKeyboardFocusable",
                () => element.Properties.IsKeyboardFocusable.Value, false),
            IsPassword = password,
            IsValueRedacted = redacted,
            Value = redacted ? null : summary.Value,
            Bounds = summary.Bounds,
            RelativeBounds = RelativeBounds(summary.Bounds, windowBounds),
            SupportedPatterns = summary.SupportedPatterns,
            LabeledByCandidateId = labeledById
        };
    }

    private static IReadOnlyList<string> GetSiblingIds(
        AutomationNodeCapture<AutomationElement> node,
        IReadOnlyList<AutomationNodeCapture<AutomationElement>> nodes)
        => node.ParentCandidateId is null ? [] : nodes
            .Where(sibling => sibling.ParentCandidateId == node.ParentCandidateId &&
                sibling.CandidateId != node.CandidateId)
            .Select(sibling => sibling.CandidateId).ToArray();

    private static string? GetLabeledById(
        AutomationElement element,
        SafeAutomationElementReader reader,
        IReadOnlyDictionary<string, string> lookup)
    {
        var labeledBy = reader.Read("LabeledBy", () => element.Properties.LabeledBy.Value, null);
        var key = labeledBy is null ? null : ReadRuntimeKey(labeledBy, reader, "LabeledBy.RuntimeId");
        return key is null ? null : lookup.GetValueOrDefault(key);
    }

    private static string? ReadRuntimeKey(
        AutomationElement element,
        SafeAutomationElementReader reader,
        string property = "RuntimeId")
    {
        var id = reader.Read(property, () => element.Properties.RuntimeId.Value, []);
        return id is null || id.Length == 0 ? null : string.Join(".", id);
    }

    internal static IReadOnlyList<string> BuildPath(
        ControlSnapshot control,
        IReadOnlyDictionary<string, ControlSnapshot> lookup)
    {
        var path = new List<string>();
        for (var current = control; current is not null;
             current = current.ParentCandidateId is null ? null : lookup.GetValueOrDefault(current.ParentCandidateId))
        {
            path.Add(current.AutomationId ?? current.Name ?? current.ControlType);
        }
        path.Reverse();
        return path;
    }

    internal static IReadOnlyList<NearbyLabel> FindNearbyLabels(
        ControlSnapshot control,
        IReadOnlyList<ControlSnapshot> controls,
        IReadOnlyDictionary<string, ControlSnapshot> lookup)
    {
        var labels = new List<NearbyLabel>();
        if (control.LabeledByCandidateId is string id &&
            lookup.TryGetValue(id, out var explicitLabel) &&
            !string.IsNullOrWhiteSpace(explicitLabel.Name))
            labels.Add(new NearbyLabel(id, explicitLabel.Name, "labeledBy", 0, 1));
        if (control.ParentCandidateId is null ||
            control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            return labels;
        labels.AddRange(controls.Where(candidate =>
                candidate.ParentCandidateId == control.ParentCandidateId &&
                candidate.CandidateId != control.CandidateId && candidate.ControlType == "Text" &&
                !string.IsNullOrWhiteSpace(candidate.Name) &&
                candidate.Bounds.Width > 0 && candidate.Bounds.Height > 0 &&
                candidate.CandidateId != control.LabeledByCandidateId)
            .Select(candidate => NearbyLabelGeometry.Create(
                candidate.CandidateId, candidate.Name!, candidate.Bounds, control.Bounds))
            .Where(label => label is not null).Cast<NearbyLabel>()
            .OrderByDescending(label => label.Confidence).ThenBy(label => label.Distance).Take(3));
        return labels;
    }

    internal static ViewSignature BuildViewSignature(
        ControlSnapshot window,
        IReadOnlyList<ControlSnapshot> controls)
    {
        var ids = controls.Where(control => !control.IsOffscreen)
            .Select(control => control.AutomationId).Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>().Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)
            .Take(100).ToArray();
        var names = controls.Where(control => !control.IsOffscreen && !control.IsPassword)
            .Select(control => control.Name).Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>().Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)
            .Take(100).ToArray();
        var text = string.Join("\n", new[] { window.Name ?? string.Empty, window.ClassName ?? string.Empty }
            .Concat(ids).Concat(names));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return new ViewSignature($"view-{hash[..12]}", hash, TextNormalizer.Tokens(window.Name), ids, names);
    }

    internal static RectangleInfo RelativeBounds(RectangleInfo bounds, RectangleInfo window)
        => (window.Width <= 0 || window.Height <= 0
            ? new RectangleInfo(0, 0, 0, 0)
            : new RectangleInfo((bounds.X - window.X) / window.Width,
                (bounds.Y - window.Y) / window.Height, bounds.Width / window.Width,
                bounds.Height / window.Height)) with
        {
            CoordinateSpace = "windowRelative", Units = "normalized"
        };

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}

using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Contracts.Discovery;

public sealed record ControlSnapshot
{
    public required string CandidateId { get; init; }

    public string? ParentCandidateId { get; init; }

    public IReadOnlyList<string> ChildCandidateIds { get; init; } = [];

    public IReadOnlyList<string> SiblingCandidateIds { get; init; } = [];

    public string? Name { get; init; }

    public string? AutomationId { get; init; }

    public required string ControlType { get; init; }

    public string? LocalizedControlType { get; init; }

    public string? ClassName { get; init; }

    public string? FrameworkId { get; init; }

    public string? HelpText { get; init; }

    public string? AccessKey { get; init; }

    public string? AcceleratorKey { get; init; }

    public bool IsEnabled { get; init; }

    public bool IsOffscreen { get; init; }

    public bool IsKeyboardFocusable { get; init; }

    public bool IsPassword { get; init; }

    public bool IsValueRedacted { get; init; }

    public string? Value { get; init; }

    public RectangleInfo Bounds { get; init; } = new(0, 0, 0, 0);

    public RectangleInfo RelativeBounds { get; init; } = new(0, 0, 0, 0);

    public IReadOnlyList<string> SupportedPatterns { get; init; } = [];

    public string? LabeledByCandidateId { get; init; }

    public IReadOnlyList<NearbyLabel> NearbyLabels { get; init; } = [];

    public IReadOnlyList<string> TreePath { get; init; } = [];
}

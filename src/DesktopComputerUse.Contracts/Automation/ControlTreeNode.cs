namespace DesktopComputerUse.Contracts.Automation;

public sealed record ControlTreeNode(
    ControlSummary Control,
    IReadOnlyList<ControlTreeNode> Children)
{
    public string? CandidateId { get; init; }
    public int Depth { get; init; }
    public bool Partial { get; init; }
    public bool Truncated { get; init; }
    public IReadOnlyList<AutomationDiagnostic> Failures { get; init; } = [];
}

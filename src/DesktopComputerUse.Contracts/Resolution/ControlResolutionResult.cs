namespace DesktopComputerUse.Contracts.Resolution;

public sealed record ControlResolutionResult(
    ResolutionStatus Status,
    string SemanticKey,
    string Intent,
    string ViewKey,
    string? SelectedCandidateId,
    double? Score,
    double? Margin,
    string Reason,
    IReadOnlyList<ControlCandidateScore> Candidates)
{
    public IReadOnlyList<DesktopComputerUse.Contracts.Automation.AutomationDiagnostic> Failures { get; init; } = [];
    public bool Partial => Failures.Count > 0;
    public bool TraversalComplete { get; init; } = true;
}

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
    IReadOnlyList<ControlCandidateScore> Candidates);

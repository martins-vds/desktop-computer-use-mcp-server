namespace DesktopComputerUse.Contracts.Resolution;

public sealed record IntentResolutionSuggestion(
    string? CandidateId,
    double? ReportedConfidence,
    IReadOnlyList<string> Evidence);

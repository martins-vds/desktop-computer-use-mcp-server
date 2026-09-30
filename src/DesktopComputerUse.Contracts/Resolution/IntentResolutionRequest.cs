using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Contracts.Resolution;

public sealed record IntentResolutionRequest(
    string SemanticKey,
    SemanticTargetDefinition Target,
    string ViewKey,
    IReadOnlyList<ControlCandidateScore> Candidates);

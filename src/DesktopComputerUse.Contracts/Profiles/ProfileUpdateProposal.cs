using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.Contracts.Profiles;

public sealed record ProfileUpdateProposal(
    string ProposalId,
    string ProfileId,
    string SemanticKey,
    string ViewKey,
    string CandidateId,
    double Score,
    double Margin,
    SemanticTargetDefinition ProposedTarget,
    IReadOnlyList<ResolutionFeatureScore> Evidence,
    DateTimeOffset CreatedAt);

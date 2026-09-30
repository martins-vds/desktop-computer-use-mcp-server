using DesktopComputerUse.Contracts.Discovery;

namespace DesktopComputerUse.Contracts.Resolution;

public sealed record ControlCandidateScore(
    string CandidateId,
    double Score,
    ControlSnapshot Candidate,
    IReadOnlyList<ResolutionFeatureScore> Features);

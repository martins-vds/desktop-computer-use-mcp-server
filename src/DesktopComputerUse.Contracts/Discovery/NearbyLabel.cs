namespace DesktopComputerUse.Contracts.Discovery;

public sealed record NearbyLabel(
    string CandidateId,
    string Text,
    string Relation,
    double Distance,
    double Confidence);

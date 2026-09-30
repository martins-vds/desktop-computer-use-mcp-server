namespace DesktopComputerUse.Contracts.Resolution;

public sealed record ResolutionFeatureScore(
    string Feature,
    double Score,
    double Weight,
    string Evidence);

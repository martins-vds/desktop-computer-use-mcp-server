namespace DesktopComputerUse.Contracts.Profiles;

public sealed record ResolutionThresholds
{
    public double MinimumConfidence { get; init; } = 0.85;

    public double MinimumMargin { get; init; } = 0.12;
}
